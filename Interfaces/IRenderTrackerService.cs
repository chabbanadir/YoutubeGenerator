using System.Collections.Generic;
using System.Threading;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface IRenderTrackerService
    {
        void StartSession(string montageId, string kindKey, string subKindKey, int totalClips, double expectedDurationSeconds = 0);
        void SetStep(int stepIndex, string? detail = null);
        void UpdateStepProgress(int stepIndex, double progressPercent, string? detail = null);
        void UpdateFfmpegProgress(double fps, string speed, string timeEncoded, double stepProgress, double overallProgress, string bitrate = "");
        void SetBatchInfo(int currentBatch, int totalBatches, int batchClipsCount, string batchTarget);
        void AddLog(string message, string level = "info");
        void CompleteSession(string finalVideoPath);
        void FailSession(string errorMessage);
        void StartShortProgress(string streamer, string kindKey, string subKindKey, string step = "Starting");
        void UpdateShortProgress(double progressPercent, string? step = null);
        void CompleteShortProgress(string? message = null);
        RenderProgressState GetSnapshot();
        IAsyncEnumerable<RenderProgressState> SubscribeAsync(CancellationToken ct = default);
    }
}
