using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface ILlmService
    {
        Task<MontageLLMResult> GenerateMetadataAsync(
            MontageItem package,
            MontageProfile profile
            );
    }
}
