using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface ILlmService
    {
        Task<MontageLLMResult> GenerateMetadataAsync(
            MontageItem package,
            MontageProfile profile
            );

        Task<MontageLLMResult> GenerateShortMetadataAsync(
            string kindKey,
            string subKindKey,
            string streamerName,
            string? clipTitle = null,
            MontageProfile? profile = null);
    }
}
