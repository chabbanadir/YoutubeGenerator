using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface ICsvService
    {
        Task<List<ClipInfo>> ParseCsvAsync(Stream csvStream);

    }
}
