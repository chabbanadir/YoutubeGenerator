using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Controllers
{
    /// <summary>
    /// Dashboard controller delegating operations to IDashboardService and IProfileManagementService.
    /// Supports public read-only monitoring and authenticated administration.
    /// </summary>
    [ApiController]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;
        private readonly IProfileManagementService _profileService;
        private readonly IRenderTrackerService _renderTracker;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IDashboardService dashboardService,
            IProfileManagementService profileService,
            IRenderTrackerService renderTracker,
            ILogger<DashboardController> logger)
        {
            _dashboardService = dashboardService;
            _profileService = profileService;
            _renderTracker = renderTracker;
            _logger = logger;
        }

        // ============================================================================
        // 1. OVERVIEW & WORKER STATUS
        // ============================================================================
        [HttpGet("overview")]
        public IActionResult GetOverview()
        {
            bool isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var result = _dashboardService.GetOverview(isAuthenticated);
            return Ok(result);
        }

        // ============================================================================
        // 1.1 LIVE MONTAGE RENDER TRACKING & STREAMING
        // ============================================================================
        [HttpGet("render-status")]
        public IActionResult GetRenderStatus()
        {
            var snapshot = _renderTracker.GetSnapshot();
            return Ok(snapshot);
        }

        [HttpGet("render-stream")]
        public async Task GetRenderStream(CancellationToken cancellationToken)
        {
            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            try
            {
                await foreach (var state in _renderTracker.SubscribeAsync(cancellationToken))
                {
                    var json = JsonSerializer.Serialize(state, serializerOptions);
                    await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected gracefully
            }
        }

        [HttpPost("trigger-montage")]
        public IActionResult TriggerMontage(
            [FromServices] IServiceScopeFactory scopeFactory,
            [FromServices] IWebHostEnvironment env)
        {
            // 1. Guard against overlapping active render sessions
            var currentStatus = _renderTracker.GetSnapshot();
            if (currentStatus.IsActive)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "A render process is already actively running. Please wait for it to complete."
                });
            }

            // 2. Inspect bag_montage.json - strictly require Completed == true
            string bagMontagePath = Path.Combine(env.ContentRootPath, "workspace", "bags", "bag_montage.json");
            if (!System.IO.File.Exists(bagMontagePath))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Montage bag file (bag_montage.json) not found."
                });
            }

            List<Models.MontageItem>? montageBag;
            try
            {
                montageBag = Services.JsonFileService.Load<List<Models.MontageItem>>(bagMontagePath);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Failed to read bag_montage.json: {ex.Message}"
                });
            }

            if (montageBag == null || montageBag.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "bag_montage.json is empty. No packages available."
                });
            }

            var readyPackage = montageBag.FirstOrDefault(p => p.Completed);
            if (readyPackage == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Cannot trigger render: No montage package has 'Completed: true'. Render only executes when CollectWorker completes the target duration."
                });
            }

            // 3. Launch MontageWorker for the verified completed package
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var montageWorker = scope.ServiceProvider.GetRequiredService<YoutubeGenerator.workers.MontageWorker>();
                var youtubeWorker = scope.ServiceProvider.GetRequiredService<YoutubeGenerator.workers.YoutubeWorker>();
                await montageWorker.RunAsync(CancellationToken.None);
                await youtubeWorker.RunAsync();
            });

            return Ok(new
            {
                success = true,
                message = $"Rendering launched for verified completed package #{readyPackage.MontageId.Substring(0, 8)}... ({readyPackage.Clips.Count} clips, {readyPackage.TotalDuration}).",
                montageId = readyPackage.MontageId
            });
        }

        [HttpPost("trigger-upload")]
        public IActionResult TriggerUpload(
            [FromServices] IServiceScopeFactory scopeFactory,
            [FromServices] IWebHostEnvironment env)
        {
            string bagYoutubePath = Path.Combine(env.ContentRootPath, "workspace", "bags", "bag_youtube.json");
            string bagYoutubeFailedPath = Path.Combine(env.ContentRootPath, "workspace", "bags", "bag_youtube_failed.json");

            var youtubeBag = System.IO.File.Exists(bagYoutubePath)
                ? Services.JsonFileService.Load<List<Models.MontageItem>>(bagYoutubePath)
                : new List<Models.MontageItem>();

            var failedBag = System.IO.File.Exists(bagYoutubeFailedPath)
                ? Services.JsonFileService.Load<List<Models.MontageItem>>(bagYoutubeFailedPath)
                : new List<Models.MontageItem>();

            // Recover failed packages whose video still exists on disk if active queue is empty
            if (youtubeBag.Count == 0 && failedBag.Count > 0)
            {
                var recoverable = failedBag
                    .Where(p => !string.IsNullOrEmpty(p.FinalVideoPath) && System.IO.File.Exists(p.FinalVideoPath))
                    .ToList();

                if (recoverable.Count > 0)
                {
                    foreach (var rec in recoverable)
                    {
                        failedBag.Remove(rec);
                        youtubeBag.Add(rec);
                    }
                    Services.JsonFileService.Save(bagYoutubePath, youtubeBag);
                    Services.JsonFileService.Save(bagYoutubeFailedPath, failedBag);
                }
            }

            if (youtubeBag.Count == 0)
            {
                return BadRequest(new { success = false, message = "No pending or recoverable packages found for upload." });
            }

            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var youtubeWorker = scope.ServiceProvider.GetRequiredService<YoutubeGenerator.workers.YoutubeWorker>();
                await youtubeWorker.RunAsync();
            });

            return Ok(new
            {
                success = true,
                message = $"Upload worker triggered for {youtubeBag.Count} pending package(s).",
                packagesCount = youtubeBag.Count
            });
        }

        [HttpPost("shorts/{shortId}/upload-now")]
        public async Task<IActionResult> ForceUploadShortNow(
            [FromRoute] string shortId,
            [FromServices] IShortsSchedulerService shortsScheduler)
        {
            if (string.IsNullOrWhiteSpace(shortId))
                return BadRequest(new { success = false, message = "Short ID is required." });

            bool success = await shortsScheduler.ForceUploadShortImmediatelyAsync(shortId);
            if (success)
            {
                return Ok(new { success = true, message = $"Short '{shortId}' uploaded successfully." });
            }
            return StatusCode(500, new { success = false, message = $"Failed to upload short '{shortId}'. Check worker logs or quota status." });
        }

        [HttpPost("montages/{montageId}/upload-now")]
        public IActionResult ForceUploadMontageNow(
            [FromRoute] string montageId,
            [FromServices] IServiceScopeFactory scopeFactory)
        {
            if (string.IsNullOrWhiteSpace(montageId))
                return BadRequest(new { success = false, message = "Montage ID is required." });

            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var youtubeWorker = scope.ServiceProvider.GetRequiredService<YoutubeGenerator.workers.YoutubeWorker>();
                await youtubeWorker.RunAsync(montageId, forceImmediate: true);
            });

            return Ok(new { success = true, message = $"Immediate upload triggered for montage '{montageId}'." });
        }

        [HttpPost("trigger-immediate-upload")]
        public async Task<IActionResult> TriggerImmediateUpload(
            [FromServices] IServiceScopeFactory scopeFactory,
            [FromServices] IShortsSchedulerService shortsScheduler)
        {
            bool shortUploaded = await shortsScheduler.ProcessNextScheduledShortAsync(forceImmediate: true);

            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var youtubeWorker = scope.ServiceProvider.GetRequiredService<YoutubeGenerator.workers.YoutubeWorker>();
                await youtubeWorker.RunAsync(forceImmediate: true);
            });

            return Ok(new
            {
                success = true,
                message = "Immediate upload triggered for pending queues.",
                shortUploaded
            });
        }

        // ============================================================================
        // 2. BAGS LIST
        // ============================================================================
        [HttpGet("bags")]
        public IActionResult GetBags()
        {
            bool isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var result = _dashboardService.GetBags(isAuthenticated);
            return Ok(result);
        }

        // ============================================================================
        // 3. READ BAG CONTENT
        // ============================================================================
        [HttpGet("bag")]
        [HttpGet("bags/{bagName}")]
        public IActionResult GetBagContent([FromQuery] string? name, [FromRoute] string? bagName)
        {
            bool isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var targetName = bagName ?? name;

            if (string.IsNullOrWhiteSpace(targetName))
            {
                return BadRequest(new { error = "Bag name must be specified." });
            }

            try
            {
                var result = _dashboardService.GetBagContent(targetName, isAuthenticated);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new
                {
                    error = ex.Message,
                    requiresAuth = true
                });
            }
        }

        // ============================================================================
        // 4. SAVE / EDIT BAG CONTENT (REQUIRES AUTHENTICATION)
        // ============================================================================
        [Authorize]
        [HttpPost("bags/{bagName}")]
        [HttpPut("bags/{bagName}")]
        public IActionResult SaveBagContent([FromRoute] string bagName, [FromBody] JsonElement content)
        {
            if (string.IsNullOrWhiteSpace(bagName))
            {
                return BadRequest(new { success = false, message = "Bag name must be specified." });
            }

            try
            {
                _dashboardService.SaveBagContent(bagName, content);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' updated bag file '{bagName}'.");

                return Ok(new
                {
                    success = true,
                    message = $"Bag '{bagName}' updated successfully.",
                    fileName = bagName,
                    itemCount = content.ValueKind == JsonValueKind.Array ? content.GetArrayLength() : 1
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to write bag '{bagName}'");
                return StatusCode(500, new { success = false, message = $"Failed to save bag content: {ex.Message}" });
            }
        }

        // ============================================================================
        // 5. PROFILES MANAGEMENT (KINDS & SUBKINDS)
        // ============================================================================
        [HttpGet("profiles")]
        public IActionResult GetProfiles()
        {
            bool isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var result = _profileService.GetProfiles(isAuthenticated);
            return Ok(result);
        }

        [HttpGet("resources")]
        public IActionResult GetResources()
        {
            var resources = _profileService.GetResourceFiles();
            return Ok(resources);
        }

        [Authorize]
        [HttpPost("profiles/subkinds")]
        public IActionResult CreateSubKindProfile([FromBody] JsonElement content)
        {
            try
            {
                var result = _profileService.CreateSubKindProfile(content);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' created subkind profile.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create subkind profile");
                return StatusCode(500, new { success = false, message = $"Failed to create subkind profile: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpPut("profiles/subkinds/{fileName}")]
        [HttpPost("profiles/subkinds/{fileName}")]
        public IActionResult UpdateSubKindProfile([FromRoute] string fileName, [FromBody] JsonElement content)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { success = false, message = "File name must be specified." });
            }

            try
            {
                var result = _profileService.UpdateSubKindProfile(fileName, content);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' updated subkind profile '{fileName}'.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update subkind profile '{fileName}'");
                return StatusCode(500, new { success = false, message = $"Failed to update subkind profile: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpDelete("profiles/subkinds/{fileName}")]
        public IActionResult DeleteSubKindProfile([FromRoute] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { success = false, message = "File name must be specified." });
            }

            try
            {
                _profileService.DeleteSubKindProfile(fileName);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' deleted subkind profile '{fileName}'.");

                return Ok(new
                {
                    success = true,
                    message = $"SubKind profile '{fileName}' deleted successfully."
                });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to delete subkind profile '{fileName}'");
                return StatusCode(500, new { success = false, message = $"Failed to delete subkind profile: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpPost("profiles/kinds")]
        public IActionResult CreateKindProfile([FromBody] JsonElement content)
        {
            try
            {
                var result = _profileService.CreateKindProfile(content);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' created kind profile.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create kind profile");
                return StatusCode(500, new { success = false, message = $"Failed to create kind profile: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpPut("profiles/kinds/{fileName}")]
        [HttpPost("profiles/kinds/{fileName}")]
        public IActionResult UpdateKindProfile([FromRoute] string fileName, [FromBody] JsonElement content)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { success = false, message = "File name must be specified." });
            }

            try
            {
                var result = _profileService.UpdateKindProfile(fileName, content);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' updated kind profile '{fileName}'.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update kind profile '{fileName}'");
                return StatusCode(500, new { success = false, message = $"Failed to update kind profile: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpPatch("profiles/kinds/{fileName}/min-delay")]
        [HttpPost("profiles/kinds/{fileName}/min-delay")]
        public IActionResult UpdateKindMinDelay([FromRoute] string fileName, [FromBody] JsonElement body)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { success = false, message = "File name must be specified." });
            }

            try
            {
                var result = _profileService.UpdateKindMinDelay(fileName, body);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' updated min delay for '{fileName}'.");
                return Ok(result);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update min delay for '{fileName}'");
                return StatusCode(500, new { success = false, message = $"Failed to update min delay: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpPost("profiles/kinds/{fileName}/google-credentials")]
        public async Task<IActionResult> UploadGoogleCredentials([FromRoute] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { success = false, message = "File name must be specified." });
            }

            try
            {
                JsonElement jsonDoc;

                if (Request.HasFormContentType && Request.Form.Files.Count > 0)
                {
                    var file = Request.Form.Files[0];
                    using var stream = file.OpenReadStream();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    jsonDoc = doc.RootElement.Clone();
                }
                else
                {
                    using var doc = await JsonDocument.ParseAsync(Request.Body);
                    jsonDoc = doc.RootElement.Clone();
                }

                var result = _profileService.UploadGoogleClientJson(fileName, jsonDoc);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' uploaded Google credentials for kind '{fileName}'.");
                return Ok(result);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to upload Google credentials for '{fileName}'");
                return StatusCode(500, new { success = false, message = $"Failed to upload Google credentials: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpDelete("profiles/kinds/{fileName}")]
        public IActionResult DeleteKindProfile([FromRoute] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { success = false, message = "File name must be specified." });
            }

            try
            {
                _profileService.DeleteKindProfile(fileName);
                _logger.LogInformation($"Admin '{User.Identity?.Name}' deleted kind profile '{fileName}'.");

                return Ok(new
                {
                    success = true,
                    message = $"Kind profile '{fileName}' deleted successfully."
                });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to delete kind profile '{fileName}'");
                return StatusCode(500, new { success = false, message = $"Failed to delete kind profile: {ex.Message}" });
            }
        }
    }
}
