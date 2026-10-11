using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class YoutubeUploader : IYoutubeUploader
    {
        private readonly ILogger<YoutubeUploader> _logger;

        public YoutubeUploader(ILogger<YoutubeUploader> logger)
        {
            _logger = logger;
        }

        private YouTubeService CreateYouTubeService(YoutubeCredentials creds)
        {
            var token = new TokenResponse
            {
                RefreshToken = creds.YoutubeRefreshToken
            };

            var flow = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = creds.YoutubeClientId,
                        ClientSecret = creds.YoutubeClientSecret
                    },
                    Scopes = new[] { YouTubeService.Scope.YoutubeUpload, YouTubeService.Scope.Youtube }
                });

            var credential = new UserCredential(flow, "user", token);

            return new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeGenerator"
            });
        }

        public async Task<string> UploadAsync(
            string filePath,
            string title,
            string description,
            YoutubeCredentials creds,
            DateTime publishTimeUtc,
            IList<string>? tags = null)
        {
            var youtube = CreateYouTubeService(creds);

            var safeDescription = TextFormatter.FixDescription(description ?? "", 4800);

            var video = new Video
            {
                Snippet = new VideoSnippet
                {
                    Title = title,
                    Description = safeDescription,
                    CategoryId = "20", // 20 = Gaming (24 was Entertainment)
                    DefaultLanguage = "en",
                    DefaultAudioLanguage = "en",
                    Tags = tags != null && tags.Count > 0 ? tags : null
                },
                Status = new VideoStatus
                {
                    PrivacyStatus = "private",   // MUST be private for scheduling
                    PublishAt = publishTimeUtc   // Auto–publish time (UTC)
                }
            };

            using var stream = new FileStream(filePath, FileMode.Open);

            var request = youtube.Videos.Insert(video, "snippet,status", stream, "video/*");

            var response = await request.UploadAsync();

            if (response.Status != UploadStatus.Completed)
                throw new Exception($"Upload failed: {response.Exception?.Message}");

            return request.ResponseBody.Id!;
        }

        public async Task<bool> SetThumbnailAsync(
            string videoId,
            string thumbnailPath,
            YoutubeCredentials creds)
        {
            if (string.IsNullOrEmpty(thumbnailPath) || !File.Exists(thumbnailPath))
            {
                _logger.LogWarning($"Thumbnail file not found: {thumbnailPath}");
                return false;
            }

            try
            {
                _logger.LogInformation($"🖼️ Setting custom thumbnail for video {videoId} from {thumbnailPath}");
                var youtube = CreateYouTubeService(creds);

                using var stream = new FileStream(thumbnailPath, FileMode.Open, FileAccess.Read);
                var request = youtube.Thumbnails.Set(videoId, stream, "image/jpeg");
                var response = await request.UploadAsync();

                if (response.Status == UploadStatus.Completed)
                {
                    _logger.LogInformation($"✅ Custom thumbnail uploaded successfully for video {videoId}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"⚠️ Custom thumbnail upload failed for {videoId}: {response.Exception?.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to upload custom thumbnail for video {videoId}");
                return false;
            }
        }
    }
}
