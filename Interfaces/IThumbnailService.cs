using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface IThumbnailService
    {
        Task<string?> GenerateThumbnailAsync(MontageItem package, string? workingDir = null);
    }
}
