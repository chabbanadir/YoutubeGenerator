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
        private readonly ITikTokAuthService _tikTokAuthService;
        private readonly IYoutubeQuotaService _quotaService;
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
            ITikTokAuthService tikTokAuthService,
            IYoutubeQuotaService quotaService,
            IRenderTrackerService? renderTracker = null)
        {
            _logger = logger;
            _profileService = profileService;
            _uploader = uploader;
            _llmService = llmService;
            _thumbnailService = thumbnailService;
            _tikTokAuthService = tikTokAuthService;
            _quotaService = quotaService;
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

            // Fetch channel & subkind profile for dynamic Category, PromptHint, and Cadence
            var profile = _profileService.GetProfile(kind, subKind);

            // Generate viral Short title, description, and tags via LLM
            _renderTracker?.UpdateShortProgress(85, "AI Metadata (LLM)");
            _renderTracker?.AddLog("ai", $"🤖 Prompting LLM for Short metadata: @{streamer} ({subKind}, Category: {profile.Category})");
            _logger.LogInformation($"🤖 Generating AI metadata via LLM for Short ({kind}/{subKind}, Category: {profile.Category}, Streamer: @{streamer})");
            var meta = await _llmService.GenerateShortMetadataAsync(kind, subKind, streamer, $"{subKind} highlight by @{streamer}", profile);

            string title = meta.Title;
            string description = meta.Description;
            var tags = (meta.Tags != null && meta.Tags.Any())
                ? meta.Tags
                : TagGenerator.GenerateShortTags(kind, profile.Category, streamer, profile.DefaultTags);
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

                // Fetch dynamic cadence settings from kind upload configuration
                double cadenceMinutes = 60.0;
                int startHour = 8;
                int endHour = 23;
                try
                {
                    var creds = _profileService.GetYoutubeCredentials(kind);
                    if (creds?.UploadConfig != null)
                    {
                        if (creds.UploadConfig.ShortsCadenceMinutes > 0)
                            cadenceMinutes = creds.UploadConfig.ShortsCadenceMinutes;
                        startHour = creds.UploadConfig.ActiveHoursStartUtc ?? 8;
                        endHour = creds.UploadConfig.ActiveHoursEndUtc ?? 23;
                    }
                }
                catch { }

                DateTime nextSlot;
                var latestPending = bag
                    .Where(s => !s.IsUploaded && s.KindKey == kind)
                    .OrderByDescending(s => s.ScheduledPublishTime)
                    .FirstOrDefault();

                if (latestPending != null && latestPending.ScheduledPublishTime > DateTime.UtcNow)
                {
                    nextSlot = latestPending.ScheduledPublishTime.AddMinutes(cadenceMinutes);
                }
                else
                {
                    nextSlot = DateTime.UtcNow.AddMinutes(5);
                }

                // Adjust to active hours window if applicable
                if (endHour > startHour)
                {
                    if (nextSlot.Hour < startHour)
                    {
                        nextSlot = new DateTime(nextSlot.Year, nextSlot.Month, nextSlot.Day, startHour, 0, 0, DateTimeKind.Utc);
                    }
                    else if (nextSlot.Hour >= endHour)
                    {
                        var nextDay = nextSlot.Date.AddDays(1);
                        nextSlot = new DateTime(nextDay.Year, nextDay.Month, nextDay.Day, startHour, 0, 0, DateTimeKind.Utc);
                    }
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
                _renderTracker?.AddLog("shorts", $"📅 Queued Short '{title}' scheduled for {nextSlot:HH:mm:ss} UTC (+{cadenceMinutes}m cadence)");
                _logger.LogInformation($"[ShortsScheduler] Queued Short '{title}' scheduled for {nextSlot:HH:mm:ss} UTC (Slot: +{cadenceMinutes}m cadence for {kind})");
                return item;
            }
        }

        public async Task<bool> ForceUploadShortImmediatelyAsync(string shortId)
        {
            if (string.IsNullOrWhiteSpace(shortId)) return false;

            ShortItem? target = null;
            lock (_fileLock)
            {
                var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);
                target = bag.FirstOrDefault(s => s.ShortId == shortId && !s.IsUploaded);
                if (target != null)
                {
                    target.ScheduledPublishTime = DateTime.UtcNow.AddMinutes(1);
                    JsonFileService.Save(_bagShorts, bag);
                }
            }

            if (target == null)
            {
                _logger.LogWarning($"[ShortsScheduler] Force upload requested for unknown or already uploaded short '{shortId}'.");
                return false;
            }

            _logger.LogInformation($"⚡ [ShortsScheduler] Forcing immediate upload of Short '{target.Title}' (ID: {shortId})...");
            _renderTracker?.AddLog("shorts", $"⚡ Immediate upload triggered for Short '{target.Title}'");

            return await UploadSingleShortAsync(target, bypassQuota: true);
        }

        public async Task<bool> ProcessNextScheduledShortAsync(bool forceImmediate = false, string? targetKindKey = null)
        {
            ShortItem? nextShort = null;

            lock (_fileLock)
            {
                var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);
                var query = bag.Where(s => !s.IsUploaded && File.Exists(s.FilePath));

                if (!string.IsNullOrWhiteSpace(targetKindKey))
                {
                    query = query.Where(s => s.KindKey.Equals(targetKindKey, StringComparison.OrdinalIgnoreCase));
                }

                if (!forceImmediate)
                {
                    query = query.Where(s => !IsChannelBackedOff(s.KindKey, out _));
                }

                var candidates = query.OrderBy(s => s.ScheduledPublishTime).ToList();

                if (forceImmediate)
                {
                    // Pick the first candidate immediately regardless of pacing or quota
                    nextShort = candidates.FirstOrDefault();
                }
                else
                {
                    // Find the first eligible short whose channel is clear (not held by quota or pacing)
                    foreach (var candidate in candidates)
                    {
                        if (_quotaService.CanUploadShort(candidate.KindKey, out _))
                        {
                            nextShort = candidate;
                            break;
                        }
                    }
                }

                if (nextShort != null && (forceImmediate || nextShort.ScheduledPublishTime < DateTime.UtcNow))
                {
                    DateTime newSlot = forceImmediate ? DateTime.UtcNow.AddMinutes(1) : DateTime.UtcNow.AddMinutes(5);

                    if (!forceImmediate)
                    {
                        var latestPendingForKind = bag
                            .Where(s => !s.IsUploaded && s.KindKey == nextShort.KindKey && s.ShortId != nextShort.ShortId)
                            .OrderByDescending(s => s.ScheduledPublishTime)
                            .FirstOrDefault();

                        if (latestPendingForKind != null && latestPendingForKind.ScheduledPublishTime > DateTime.UtcNow)
                        {
                            newSlot = latestPendingForKind.ScheduledPublishTime.AddMinutes(60);
                        }
                    }

                    nextShort.ScheduledPublishTime = newSlot;

                    var match = bag.FirstOrDefault(s => s.ShortId == nextShort.ShortId);
                    if (match != null)
                    {
                        match.ScheduledPublishTime = newSlot;
                        JsonFileService.Save(_bagShorts, bag);
                    }
                }
            }

            if (nextShort == null)
            {
                _logger.LogWarning($"[ShortsScheduler] ProcessNextScheduledShortAsync found no eligible un-uploaded short with existing file on disk (forceImmediate={forceImmediate}, targetKind={targetKindKey ?? "any"}).");
                return false;
            }

            return await UploadSingleShortAsync(nextShort, bypassQuota: forceImmediate);
        }

        private async Task<bool> UploadSingleShortAsync(ShortItem nextShort, bool bypassQuota = false)
        {
            // YouTube Quota & Long Video Balance Gatekeeper (double check unless bypassed by admin manual action)
            if (!bypassQuota && !_quotaService.CanUploadShort(nextShort.KindKey, out string quotaReason))
            {
                _logger.LogInformation($"[ShortsScheduler] Postponing Short '{nextShort.Title}': {quotaReason}");
                _renderTracker?.AddLog("shorts", $"⏳ Postponing Short '{nextShort.Title}': {quotaReason}");
                return false;
            }

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
                            match.YoutubeUploadedAt = DateTime.UtcNow;
                            JsonFileService.Save(_bagShorts, bag);
                        }
                    }

                    // Record upload in persistent quota tracker
                    await _quotaService.RecordUploadAsync(nextShort.KindKey, "short", videoId);

                    // STRICT CLEANUP RULE:
                    // Only delete the short video file and thumbnail if published on BOTH YouTube AND TikTok.
                    // If either platform has not yet uploaded, retain the file safely on disk.
                    try
                    {
                        bool bothUploaded = false;
                        lock (_fileLock)
                        {
                            var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);
                            var match = bag?.FirstOrDefault(s => s.ShortId == nextShort.ShortId);
                            if (match != null)
                            {
                                bothUploaded = match.IsUploaded && match.IsTikTokUploaded;
                            }
                        }

                        if (bothUploaded)
                        {
                            if (File.Exists(nextShort.FilePath))
                            {
                                File.Delete(nextShort.FilePath);
                                _logger.LogInformation($"[ShortsScheduler] Both YouTube & TikTok uploaded. Cleaned up rendered short video: {nextShort.FilePath}");
                            }
                            if (!string.IsNullOrEmpty(nextShort.ThumbnailPath) && File.Exists(nextShort.ThumbnailPath))
                            {
                                File.Delete(nextShort.ThumbnailPath);
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"[ShortsScheduler] YouTube uploaded successfully. Retaining video '{Path.GetFileName(nextShort.FilePath)}' on disk until TikTok upload is also completed.");
                        }
                    }
                    catch (Exception exDel)
                    {
                        _logger.LogWarning(exDel, $"[ShortsScheduler] Error checking/deleting short file: {nextShort.FilePath}");
                    }

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
