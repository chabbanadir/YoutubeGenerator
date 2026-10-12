using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Services
{
    public class TikTokUploader : ITikTokUploader
    {
        private readonly ITikTokAuthService _authService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TikTokUploader> _logger;

        public TikTokUploader(
            ITikTokAuthService authService,
            IHttpClientFactory httpClientFactory,
            ILogger<TikTokUploader> logger)
        {
            _authService = authService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<(bool Success, string PublishId, string Message)> UploadVideoAsync(
            string kindKey,
            string videoPath,
            string title,
            string privacyLevel = "SELF_ONLY")
        {
            if (!File.Exists(videoPath))
            {
                return (false, "", $"Video file does not exist: {videoPath}");
            }

            var accessToken = await _authService.GetValidAccessTokenAsync(kindKey);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, "", $"TikTok account for '{kindKey}' is not connected or authorization expired.");
            }

            var client = _httpClientFactory.CreateClient();
            var fileInfo = new FileInfo(videoPath);
            long videoSize = fileInfo.Length;

            if (videoSize <= 0)
            {
                return (false, "", "Video file is empty (0 bytes).");
            }

            // Cap title to 2200 characters as per TikTok constraints
            if (!string.IsNullOrEmpty(title) && title.Length > 2000)
            {
                title = title.Substring(0, 1997) + "...";
            }

            // 1. Initialize Direct Post Upload
            _logger.LogInformation($"[TikTokUpload] Initializing Direct Post for '{kindKey}' (file: {Path.GetFileName(videoPath)}, size: {videoSize} bytes)");

            var initPayload = new JsonObject
            {
                ["post_info"] = new JsonObject
                {
                    ["title"] = title ?? "New Gaming Clip",
                    ["privacy_level"] = privacyLevel,
                    ["disable_duet"] = false,
                    ["disable_stitch"] = false,
                    ["disable_comment"] = false
                },
                ["source_info"] = new JsonObject
                {
                    ["source"] = "FILE_UPLOAD",
                    ["video_size"] = videoSize,
                    ["chunk_size"] = videoSize,
                    ["total_chunk_count"] = 1
                }
            };

            var initReq = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/post/publish/video/init/");
            initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            initReq.Content = new StringContent(initPayload.ToJsonString(), Encoding.UTF8, "application/json");

            var initResp = await client.SendAsync(initReq);
            var initBody = await initResp.Content.ReadAsStringAsync();

            if (!initResp.IsSuccessStatusCode)
            {
                _logger.LogError($"[TikTokUpload] Init failed ({initResp.StatusCode}): {initBody}");
                return (false, "", $"TikTok init failed: {initBody}");
            }

            var initJson = JsonNode.Parse(initBody);
            var errNode = initJson?["error"];
            if (errNode != null && errNode["code"]?.ToString() != "ok" && errNode["code"]?.ToString() != "0")
            {
                string errMsg = errNode["message"]?.ToString() ?? initBody;
                _logger.LogError($"[TikTokUpload] TikTok API Error in Init: {errMsg}");
                return (false, "", $"TikTok API error: {errMsg}");
            }

            var data = initJson?["data"];
            string publishId = data?["publish_id"]?.ToString() ?? "";
            string uploadUrl = data?["upload_url"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(uploadUrl))
            {
                _logger.LogError($"[TikTokUpload] No upload_url returned: {initBody}");
                return (false, publishId, "No upload_url received from TikTok API.");
            }

            _logger.LogInformation($"[TikTokUpload] Init successful. PublishId: {publishId}. Uploading {videoSize} bytes to TikTok server...");

            // 2. Upload Video Binary via PUT
            try
            {
                await using var fileStream = File.OpenRead(videoPath);
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                streamContent.Headers.ContentRange = new ContentRangeHeaderValue(0, videoSize - 1, videoSize);
                streamContent.Headers.ContentLength = videoSize;

                var putReq = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
                {
                    Content = streamContent
                };

                var putResp = await client.SendAsync(putReq);
                if (!putResp.IsSuccessStatusCode)
                {
                    var putBody = await putResp.Content.ReadAsStringAsync();
                    _logger.LogError($"[TikTokUpload] Video chunk upload failed ({putResp.StatusCode}): {putBody}");
                    return (false, publishId, $"Chunk upload failed: {putResp.StatusCode} - {putBody}");
                }

                _logger.LogInformation($"[TikTokUpload] Binary upload succeeded for PublishId: {publishId}");
            }
            catch (Exception exUpload)
            {
                _logger.LogError(exUpload, $"[TikTokUpload] Exception during binary upload for PublishId: {publishId}");
                return (false, publishId, $"Upload exception: {exUpload.Message}");
            }

            // 3. Check Initial Publish Status
            var (status, failReason) = await CheckPublishStatusAsync(kindKey, publishId);
            _logger.LogInformation($"[TikTokUpload] Post status for '{publishId}': {status} {failReason}");

            return (true, publishId, $"Video uploaded successfully (status: {status})");
        }

        public async Task<(string Status, string FailReason)> CheckPublishStatusAsync(string kindKey, string publishId)
        {
            if (string.IsNullOrWhiteSpace(publishId)) return ("UNKNOWN", "Empty publish_id");

            var accessToken = await _authService.GetValidAccessTokenAsync(kindKey);
            if (string.IsNullOrWhiteSpace(accessToken)) return ("UNKNOWN", "Not authorized");

            var client = _httpClientFactory.CreateClient();
            var statusReq = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/post/publish/status/fetch/");
            statusReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            statusReq.Content = new StringContent(
                JsonSerializer.Serialize(new { publish_id = publishId }),
                Encoding.UTF8,
                "application/json");

            var resp = await client.SendAsync(statusReq);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning($"[TikTokUpload] Status check failed ({resp.StatusCode}): {body}");
                return ("ERROR", body);
            }

            try
            {
                var json = JsonNode.Parse(body);
                var data = json?["data"];
                string status = data?["status"]?.ToString() ?? "PROCESSING";
                string failReason = data?["fail_reason"]?.ToString() ?? "";
                return (status, failReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[TikTokUpload] Failed to parse status response: {body}");
                return ("PROCESSING", "");
            }
        }
    }
}
