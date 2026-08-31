using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Workers;

namespace YoutubeGenerator.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class TestController : ControllerBase
    {
        private readonly ILlmService _llmService;
        private readonly IMontageProfileService _montageProfileService;
        private readonly IServiceScopeFactory _scopeFactory;

        public TestController(ILlmService llmService, IMontageProfileService montageProfileService, IServiceScopeFactory scopeFactory)
        {
            _llmService = llmService;
            _montageProfileService = montageProfileService;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Manually triggers the CollectWorker pipeline (downloads CSV, clips, trims, packages montage).
        /// Route: POST /api/admin/trigger-collect
        /// </summary>
        [HttpPost("trigger-collect")]
        public async Task<IActionResult> TriggerCollect()
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var collectWorker = scope.ServiceProvider.GetRequiredService<CollectWorker>();
                await collectWorker.RunAsync(System.Threading.CancellationToken.None);
            });

            return Ok(new { message = "CollectWorker triggered successfully in background." });
        }

        /// <summary>
        /// Tests the LLM metadata generation with a provided MontageItem package.
        /// Route: POST /api/admin/test-llm-generation
        /// </summary>
        [HttpPost("test-llm-generation")]
        [ProducesResponseType(typeof(MontageLLMResult), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> TestLlmGeneration([FromBody] MontageItem package)
        {
            if (package == null)
            {
                return BadRequest("MontageItem package data is required.");
            }

            var profile = _montageProfileService.GetProfile(package.KindKey, package.SubKindKey);

            if (profile == null)
            {
                return BadRequest($"Montage Profile not found for KindKey: {package.KindKey}, SubKindKey: {package.SubKindKey}");
            }

            MontageLLMResult result;
            try
            {
                result = await _llmService.GenerateMetadataAsync(package, profile);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, $"LLM generation failed: {ex.Message}");
            }

            return Ok(result);
        }
    }
}