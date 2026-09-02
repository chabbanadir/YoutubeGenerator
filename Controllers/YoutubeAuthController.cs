using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Controllers
{
    [ApiController]
    [Route("api/youtube")]
    public class YoutubeAuthController : ControllerBase
    {
        private readonly IYoutubeAuthService _authService;
        private readonly ILogger<YoutubeAuthController> _logger;

        public YoutubeAuthController(
            IYoutubeAuthService authService,
            ILogger<YoutubeAuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            var (clientId, _) = _authService.GetGlobalCredentials();
            return Ok(new
            {
                configured = !string.IsNullOrWhiteSpace(clientId),
                clientId = clientId
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
                var redirectUri = Url.Action("Callback", "YoutubeAuth", null, Request.Scheme) 
                                 ?? $"{Request.Scheme}://{Request.Host}/api/youtube/callback";

                var url = _authService.GetAuthorizationUrl(kind.Trim().ToLowerInvariant(), redirectUri);
                return Ok(new { url, kind = kind.Trim().ToLowerInvariant() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate YouTube authorization URL.");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning($"YouTube OAuth callback returned error: {error}");
                return Redirect($"/?youtube_error={Uri.EscapeDataString(error)}#profiles");
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return Redirect("/?youtube_error=InvalidCallbackState#profiles");
            }

            try
            {
                var redirectUri = Url.Action("Callback", "YoutubeAuth", null, Request.Scheme) 
                                 ?? $"{Request.Scheme}://{Request.Host}/api/youtube/callback";

                var (refreshToken, accessToken, channelId, channelTitle) = 
                    await _authService.ExchangeCodeAndFetchChannelAsync(code, redirectUri);

                _authService.SaveChannelConnection(state, channelId, refreshToken);

                return Redirect($"/?connected={Uri.EscapeDataString(state)}&channel={Uri.EscapeDataString(channelTitle.Length > 0 ? channelTitle : channelId)}#profiles");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "YouTube OAuth callback exchange failed.");
                return Redirect($"/?youtube_error={Uri.EscapeDataString(ex.Message)}#profiles");
            }
        }

        [HttpPost("disconnect/{kindKey}")]
        [Authorize]
        public IActionResult Disconnect([FromRoute] string kindKey)
        {
            if (string.IsNullOrWhiteSpace(kindKey))
            {
                return BadRequest(new { error = "Parameter 'kindKey' is required." });
            }

            try
            {
                _authService.DisconnectChannel(kindKey);
                return Ok(new
                {
                    success = true,
                    message = $"Disconnected YouTube channel for '{kindKey}' successfully."
                });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to disconnect YouTube channel for '{kindKey}'.");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
