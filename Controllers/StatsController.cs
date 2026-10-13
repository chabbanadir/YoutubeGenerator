using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Controllers
{
    /// <summary>
    /// Public stats endpoint for external portfolio and status badges.
    /// Returns aggregated metrics: channels handled, clips downloaded, montages created, shorts published, and total runtime.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [EnableCors("AllowAll")]
    public class StatsController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public StatsController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        /// <summary>
        /// GET /api/stats or /api/public/stats
        /// Returns public recap stats for portfolio integration.
        /// </summary>
        [HttpGet("api/stats")]
        [HttpGet("api/public/stats")]
        public IActionResult GetStats()
        {
            var stats = _dashboardService.GetPublicStats();
            return Ok(stats);
        }
    }
}
