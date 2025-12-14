using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models; // Assuming MontageItem and MontageLLMResult are in this namespace

namespace YoutubeGenerator.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class TestController : ControllerBase
    {
        // Inject the services via the constructor
        private readonly ILlmService _llmService;
        private readonly IMontageProfileService _montageProfileService;

        public TestController(ILlmService llmService, IMontageProfileService montageProfileService)
        {
            _llmService = llmService;
            _montageProfileService = montageProfileService;
        }

        /// <summary>
        /// Tests the LLM metadata generation with a provided MontageItem package.
        /// Route: POST /api/admin/test-llm-generation
        /// </summary>
        /// <param name="package">The MontageItem data to generate metadata for.</param>
        /// <returns>The generated MontageLLMResult (Title and Description).</returns>
        [HttpPost("test-llm-generation")]
        [ProducesResponseType(typeof(MontageLLMResult), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> TestLlmGeneration([FromBody] MontageItem package)
        {
            if (package == null)
            {
                return BadRequest("MontageItem package data is required.");
            }

            // 1. Get the Profile based on the package keys, as specified in your setup
            // Note: Error handling for null profile might be needed here in a real app
            var profile = _montageProfileService.GetProfile(package.KindKey, package.SubKindKey);

            if (profile == null)
            {
                return BadRequest($"Montage Profile not found for KindKey: {package.KindKey}, SubKindKey: {package.SubKindKey}");
            }

            // 2. Generate the Title and Description using the LlmService
            MontageLLMResult result;
            try
            {
                result = await _llmService.GenerateMetadataAsync(package, profile);
            }
            catch (System.Exception ex)
            {
                // Catch any exceptions (e.g., API key errors, LLM response parsing failure)
                return StatusCode(500, $"LLM generation failed: {ex.Message}");
            }

            // 3. Return the generated result
            return Ok(result);
        }
    }
}