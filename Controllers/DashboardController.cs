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
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IDashboardService dashboardService,
            IProfileManagementService profileService,
            ILogger<DashboardController> logger)
        {
            _dashboardService = dashboardService;
            _profileService = profileService;
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
