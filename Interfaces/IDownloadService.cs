using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface IDownloadService
    {
        Task<DownloadResult> DownloadAsync(ClipInfo clip, string outputDir, int increment);
    }
}
