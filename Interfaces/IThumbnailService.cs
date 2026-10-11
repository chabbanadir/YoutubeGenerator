using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface IThumbnailService
    {
        Task<string?> GenerateThumbnailAsync(MontageItem package, string? workingDir = null);

        Task<string?> GenerateShortThumbnailAsync(
            string shortVideoPath,
            string kindKey,
            string? streamerName = null,
            string? subKindKey = null,
            string? workingDir = null);
    }
}
