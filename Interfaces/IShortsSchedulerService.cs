using System.Threading.Tasks;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface IShortsSchedulerService
    {
        Task<ShortItem?> QueueShortAsync(string shortFilePath, ClipInfo clip, double durationSeconds);
        Task<bool> ProcessNextScheduledShortAsync();
    }
}
