using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Controllers
{
    [ApiController]
    [Route("api/tiktok")]
    public class TikTokAuthController : ControllerBase
    {
        private readonly ITikTokAuthService _authService;
        private readonly ITikTokUploader _uploader;
        private readonly ILogger<TikTokAuthController> _logger;
        private readonly IConfiguration _config;

        public TikTokAuthController(
            ITikTokAuthService authService,
            ITikTokUploader uploader,
            ILogger<TikTokAuthController> logger,
            IConfiguration config)
        {
            _authService = authService;
            _uploader = uploader;
            _logger = logger;
            _config = config;
        }

        private string GetRedirectUri()
        {
            var overrideUri = _config["TikTok:RedirectUri"]
                           ?? _config["TIKTOK_REDIRECT_URI"]
                           ?? Environment.GetEnvironmentVariable("TIKTOK_REDIRECT_URI");

            if (!string.IsNullOrWhiteSpace(overrideUri))
            {
                return overrideUri.Trim();
            }

            var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
            var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;

            if (host.Contains("nip.io", StringComparison.OrdinalIgnoreCase) || 
                host.Contains("ngrok", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(Request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https";
            }

            return $"{scheme}://{host}/api/tiktok/callback";
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            var (clientKey, _) = _authService.GetGlobalCredentials();
            return Ok(new
            {
                configured = !string.IsNullOrWhiteSpace(clientKey),
                clientKey = clientKey,
                redirectUri = GetRedirectUri()
            });
        }

        [HttpGet("auth-url")]
        public IActionResult GetAuthUrl([FromQuery] string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return BadRequest(new { error = "Parameter 'kind' is required." });
            }

            try
            {
                var redirectUri = GetRedirectUri();
                var url = _authService.GetAuthorizationUrl(kind.Trim().ToLowerInvariant(), redirectUri);
                return Ok(new { url, kind = kind.Trim().ToLowerInvariant() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate TikTok authorization URL.");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, [FromQuery] string? error_description)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning($"TikTok OAuth callback error: {error} - {error_description}");
                return Redirect($"/?tiktok_error={Uri.EscapeDataString(error_description ?? error)}#profiles");
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return Redirect("/?tiktok_error=InvalidCallbackState#profiles");
            }

            try
            {
                var redirectUri = GetRedirectUri();
                var kindKey = state.Trim().ToLowerInvariant();

                var (accessToken, refreshToken, openId, displayName, avatarUrl, expiresIn, refreshExpiresIn) =
                    await _authService.ExchangeCodeAndFetchUserAsync(code, redirectUri);

                _authService.SaveConnection(kindKey, openId, accessToken, refreshToken, displayName, avatarUrl, expiresIn, refreshExpiresIn);

                return Redirect($"/?connected_tiktok={Uri.EscapeDataString(kindKey)}&account={Uri.EscapeDataString(displayName)}#profiles");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process TikTok OAuth callback.");
                return Redirect($"/?tiktok_error={Uri.EscapeDataString(ex.Message)}#profiles");
            }
        }

        [HttpPost("disconnect/{kind}")]
        public IActionResult Disconnect(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return BadRequest(new { error = "Kind parameter is required." });
            }

            try
            {
                _authService.Disconnect(kind.Trim().ToLowerInvariant());
                return Ok(new { success = true, message = $"Disconnected TikTok for '{kind}'." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to disconnect TikTok for {kind}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("refresh/{kind}")]
        public async Task<IActionResult> RefreshToken(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return BadRequest(new { error = "Kind parameter is required." });
            }

            try
            {
                var newToken = await _authService.RefreshTokenAsync(kind.Trim().ToLowerInvariant());
                if (string.IsNullOrEmpty(newToken))
                {
                    return BadRequest(new { success = false, message = "Token refresh failed. Account might not be connected." });
                }

                return Ok(new { success = true, message = "Token refreshed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to refresh TikTok token for {kind}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("test-upload/{kind}")]
        public async Task<IActionResult> TestUpload(string kind, [FromBody] System.Text.Json.Nodes.JsonObject? payload)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return BadRequest(new { error = "Kind parameter is required." });
            }

            string kindKey = kind.Trim().ToLowerInvariant();
            if (!_authService.IsConnected(kindKey))
            {
                return BadRequest(new { error = $"TikTok is not connected for '{kindKey}'." });
            }

            string? videoPath = payload?["videoPath"]?.ToString();
            string title = payload?["title"]?.ToString() ?? "Test Video #gaming #shorts";
            string privacyLevel = payload?["privacyLevel"]?.ToString() ?? "SELF_ONLY";

            // If no video path provided, find a short video from workspace
            if (string.IsNullOrWhiteSpace(videoPath) || !System.IO.File.Exists(videoPath))
            {
                var candidatePaths = new[]
                {
                    "/home/chabba/Projects/Projects/YoutubeGenerator/workspace/ressources/intro_gaming.mp4",
                    "/home/chabba/Projects/Projects/YoutubeGenerator/workspace/ressources/intro_valo.mp4",
                    "/home/chabba/Projects/Projects/YoutubeGenerator/workspace/ressources/intro_valorant.mp4"
                };

                videoPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);
            }

            if (string.IsNullOrWhiteSpace(videoPath) || !System.IO.File.Exists(videoPath))
            {
                return BadRequest(new { error = "No valid test video file found on server to upload." });
            }

            try
            {
                var (success, publishId, message) = await _uploader.UploadVideoAsync(kindKey, videoPath, title, privacyLevel);
                return Ok(new
                {
                    success,
                    publishId,
                    message,
                    videoUsed = System.IO.Path.GetFileName(videoPath)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute test upload for {kindKey}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("publish-status/{kind}")]
        public async Task<IActionResult> GetPublishStatus(string kind, [FromQuery] string publishId)
        {
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(publishId))
            {
                return BadRequest(new { error = "Parameters 'kind' and 'publishId' are required." });
            }

            try
            {
                var (status, failReason) = await _uploader.CheckPublishStatusAsync(kind.Trim().ToLowerInvariant(), publishId);
                return Ok(new { status, failReason });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to check publish status for {publishId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
