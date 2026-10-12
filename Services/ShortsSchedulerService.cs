using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class ShortsSchedulerService : IShortsSchedulerService
    {
        private readonly ILogger<ShortsSchedulerService> _logger;
        private readonly IMontageProfileService _profileService;
        private readonly IYoutubeUploader _uploader;
        private readonly ILlmService _llmService;
        private readonly IThumbnailService _thumbnailService;
        private readonly IRenderTrackerService? _renderTracker;
        private readonly string _bagShorts;
        private readonly string _backoffFile;
        private static readonly object _fileLock = new();
        private static readonly ConcurrentDictionary<string, DateTime> _channelBackoff = new();

        public ShortsSchedulerService(
            IWebHostEnvironment env,
            ILogger<ShortsSchedulerService> logger,
            IMontageProfileService profileService,
            IYoutubeUploader uploader,
            ILlmService llmService,
            IThumbnailService thumbnailService,
            IRenderTrackerService? renderTracker = null)
        {
            _logger = logger;
            _profileService = profileService;
            _uploader = uploader;
            _llmService = llmService;
            _thumbnailService = thumbnailService;
            _renderTracker = renderTracker;

            string bagsDir = Path.Combine(env.ContentRootPath, "workspace", "bags");
            Directory.CreateDirectory(bagsDir);
            _bagShorts = Path.Combine(bagsDir, "bag_shorts.json");
            _backoffFile = Path.Combine(bagsDir, "channel_backoff.json");
        }

        private bool IsChannelBackedOff(string channelKey, out DateTime backoffUntil)
        {
            backoffUntil = DateTime.MinValue;
            if (string.IsNullOrEmpty(channelKey)) return false;

            if (_channelBackoff.TryGetValue(channelKey, out backoffUntil) && DateTime.UtcNow < backoffUntil)
                return true;

            try
            {
                if (File.Exists(_backoffFile))
                {
                    var fileBackoff = JsonFileService.Load<Dictionary<string, DateTime>>(_backoffFile);
                    if (fileBackoff != null && fileBackoff.TryGetValue(channelKey, out var fileUntil) && DateTime.UtcNow < fileUntil)
                    {
                        backoffUntil = fileUntil;
                        _channelBackoff[channelKey] = fileUntil;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void SetChannelBackoff(string channelKey, DateTime until)
        {
            if (string.IsNullOrEmpty(channelKey)) return;
            _channelBackoff[channelKey] = until;
            try
            {
                lock (_fileLock)
                {
                    var fileBackoff = File.Exists(_backoffFile)
                        ? JsonFileService.Load<Dictionary<string, DateTime>>(_backoffFile) ?? new()
                        : new Dictionary<string, DateTime>();
                    fileBackoff[channelKey] = until;
                    JsonFileService.Save(_backoffFile, fileBackoff);
                }
            }
            catch { }
        }

        public async Task<ShortItem?> QueueShortAsync(string shortFilePath, ClipInfo clip, double durationSeconds)
        {
            if (string.IsNullOrEmpty(shortFilePath) || !File.Exists(shortFilePath))
                return null;

            var kind = (clip.KindKey ?? "lol").Trim().ToLowerInvariant();
            var subKind = (clip.SubKindKey ?? "normal").Trim().ToLowerInvariant();
            var streamer = (clip.Description ?? "Streamer").Trim().TrimStart('@');

            // Generate viral Short title, description, and tags via LLM
            _renderTracker?.UpdateShortProgress(85, "AI Metadata (LLM)");
            _renderTracker?.AddLog("ai", $"🤖 Prompting LLM for Short metadata: @{streamer} ({subKind})");
            _logger.LogInformation($"🤖 Generating AI metadata via LLM for Short ({kind}/{subKind}, Streamer: @{streamer})");
            var meta = await _llmService.GenerateShortMetadataAsync(kind, subKind, streamer, $"{subKind} highlight by @{streamer}");

            string title = meta.Title;
            string description = meta.Description;
            var tags = meta.Tags ?? new List<string> { "Shorts", "YouTube Shorts", $"{kind} Shorts", streamer, "Gaming" };
            _renderTracker?.AddLog("ai", $"✨ AI Title: \"{title}\"");

            // Ensure 9:16 Short thumbnail exists
            string dir = Path.GetDirectoryName(shortFilePath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(shortFilePath);
            string thumbPath = Path.Combine(dir, $"thumb_{baseName}.jpg");

            if (!File.Exists(thumbPath))
            {
                try
                {
                    await _thumbnailService.GenerateShortThumbnailAsync(shortFilePath, kind, streamer, subKind, dir);
                }
                catch (Exception exThumb)
                {
                    _logger.LogWarning(exThumb, $"[ShortsScheduler] Could not generate thumbnail for {shortFilePath}");
                }
            }

            lock (_fileLock)
            {
                var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);

                // Calculate 3-hour rolling cadence per game channel to respect YouTube limits and maximize algorithmic performance
                DateTime nextSlot;
                var latestPending = bag
                    .Where(s => !s.IsUploaded && s.KindKey == kind)
                    .OrderByDescending(s => s.ScheduledPublishTime)
                    .FirstOrDefault();

                if (latestPending != null && latestPending.ScheduledPublishTime > DateTime.UtcNow)
                {
                    nextSlot = latestPending.ScheduledPublishTime.AddHours(3);
                }
                else
                {
                    nextSlot = DateTime.UtcNow.AddMinutes(5);
                }

                var item = new ShortItem
                {
                    KindKey = kind,
                    SubKindKey = subKind,
                    Streamer = streamer,
                    Title = title,
                    Description = description,
                    Tags = tags,
                    FilePath = shortFilePath,
                    ThumbnailPath = File.Exists(thumbPath) ? thumbPath : null,
                    DurationSeconds = durationSeconds,
                    ScheduledPublishTime = nextSlot,
                    IsUploaded = false
                };

                bag.Add(item);
                JsonFileService.Save(_bagShorts, bag);

                _renderTracker?.CompleteShortProgress($"Short '{title}' queued for {nextSlot:HH:mm:ss} UTC");
                _renderTracker?.AddLog("shorts", $"📅 Queued Short '{title}' scheduled for {nextSlot:HH:mm:ss} UTC (+3h cadence)");
                _logger.LogInformation($"[ShortsScheduler] Queued Short '{title}' scheduled for {nextSlot:HH:mm:ss} UTC (Slot: +3h cadence for {kind})");
                return item;
            }
        }

        public async Task<bool> ProcessNextScheduledShortAsync()
        {
            ShortItem? nextShort = null;

            lock (_fileLock)
            {
                var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);
                // Pick next short that is not uploaded, file exists, and its channel is not currently backed off due to quota limit
                nextShort = bag.FirstOrDefault(s =>
                    !s.IsUploaded &&
                    File.Exists(s.FilePath) &&
                    !IsChannelBackedOff(s.KindKey, out _));

                // If the selected short has a stale ScheduledPublishTime (in the past),
                // reschedule it to a valid future time. YouTube rejects PublishAt in the past.
                // This handles shorts that were rate-limited yesterday and need to be retried today.
                if (nextShort != null && nextShort.ScheduledPublishTime < DateTime.UtcNow)
                {
                    var latestPendingForKind = bag
                        .Where(s => !s.IsUploaded && s.KindKey == nextShort.KindKey && s.ShortId != nextShort.ShortId)
                        .OrderByDescending(s => s.ScheduledPublishTime)
                        .FirstOrDefault();

                    DateTime newSlot;
                    if (latestPendingForKind != null && latestPendingForKind.ScheduledPublishTime > DateTime.UtcNow)
                    {
                        newSlot = latestPendingForKind.ScheduledPublishTime.AddHours(3);
                    }
                    else
                    {
                        newSlot = DateTime.UtcNow.AddMinutes(5);
                    }

                    nextShort.ScheduledPublishTime = newSlot;

                    // Persist the rescheduled time so it survives restarts
                    var match = bag.FirstOrDefault(s => s.ShortId == nextShort.ShortId);
                    if (match != null)
                    {
                        match.ScheduledPublishTime = newSlot;
                        JsonFileService.Save(_bagShorts, bag);
                    }
                }
            }

            if (nextShort == null)
                return false;

            try
            {
                var creds = _profileService.GetYoutubeCredentials(nextShort.KindKey);

                _renderTracker?.AddLog("shorts", $"🚀 Uploading Short '{nextShort.Title}' to YouTube ({nextShort.KindKey.ToUpperInvariant()} - Slot: {nextShort.ScheduledPublishTime:yyyy-MM-dd HH:mm:ss} UTC)...");
                _logger.LogInformation($"[ShortsScheduler] Uploading Short '{nextShort.Title}' to YouTube ({nextShort.KindKey} - Scheduled release: {nextShort.ScheduledPublishTime:yyyy-MM-dd HH:mm:ss} UTC)...");

                string videoId = await _uploader.UploadAsync(
                    nextShort.FilePath,
                    nextShort.Title,
                    nextShort.Description,
                    creds,
                    nextShort.ScheduledPublishTime,
                    nextShort.Tags
                );

                if (!string.IsNullOrEmpty(videoId))
                {
                    _renderTracker?.AddLog("shorts", $"🎉 Short published: https://youtube.com/shorts/{videoId} (ID: {videoId})");
                    _logger.LogInformation($"✅ [ShortsScheduler] Short uploaded successfully: Video ID = {videoId}");

                    // Set thumbnail if generated
                    if (!string.IsNullOrEmpty(nextShort.ThumbnailPath) && File.Exists(nextShort.ThumbnailPath))
                    {
                        try
                        {
                            await _uploader.SetThumbnailAsync(videoId, nextShort.ThumbnailPath, creds);
                            _renderTracker?.AddLog("thumb", $"🖼️ Attached 9:16 thumbnail to Short {videoId}");
                        }
                        catch (Exception exThumb)
                        {
                            _logger.LogWarning(exThumb, $"[ShortsScheduler] Could not set custom thumbnail for Short {videoId}");
                        }
                    }

                    lock (_fileLock)
                    {
                        var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);
                        var match = bag.FirstOrDefault(s => s.ShortId == nextShort.ShortId);
                        if (match != null)
                        {
                            match.IsUploaded = true;
                            match.YoutubeVideoId = videoId;
                            JsonFileService.Save(_bagShorts, bag);
                        }
                    }

                    // Clean up local short file and thumbnail to conserve disk space
                    try
                    {
                        if (File.Exists(nextShort.FilePath))
                        {
                            File.Delete(nextShort.FilePath);
                            _logger.LogInformation($"[ShortsScheduler] Cleaned up rendered short video: {nextShort.FilePath}");
                        }
                        if (!string.IsNullOrEmpty(nextShort.ThumbnailPath) && File.Exists(nextShort.ThumbnailPath))
                        {
                            File.Delete(nextShort.ThumbnailPath);
                        }
                    }
                    catch { }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"🚨 [ShortsScheduler] Failed to upload Short: {nextShort.Title}");

                bool isRateLimit = ex.Message.Contains("uploadLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("exceeded the number of videos", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase);

                if (isRateLimit)
                {
                    // YouTube daily upload limit (uploadLimitExceeded) operates on a 24-hour rolling window.
                    // Back off for 6 hours so it does not prematurely retry inside the same window.
                    var backoffUntil = DateTime.UtcNow.AddHours(6);
                    SetChannelBackoff(nextShort.KindKey, backoffUntil);
                    _renderTracker?.AddLog("shorts", $"⏳ YouTube daily upload limit reached for channel '{nextShort.KindKey}'. Channel backed off until {backoffUntil:HH:mm} UTC (other channels continue).");
                    _logger.LogWarning($"[ShortsScheduler] Daily upload limit reached for '{nextShort.KindKey}'. Channel backed off until {backoffUntil:yyyy-MM-dd HH:mm:ss} UTC.");
                }
            }

            return false;
        }
    }
}
