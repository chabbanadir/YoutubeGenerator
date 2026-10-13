using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class YoutubeUploadRecord
    {
        public string VideoId { get; set; } = "";
        public string ChannelKey { get; set; } = "";
        public string VideoType { get; set; } = "short"; // "long" or "short"
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public class YoutubeQuotaService : IYoutubeQuotaService
    {
        private readonly ILogger<YoutubeQuotaService> _logger;
        private readonly IConfiguration _config;
        private readonly string _quotaFile;
        private readonly string _bagsDir;
        private static readonly object _fileLock = new();

        public int MaxDailyLongVideos { get; }
        public int MaxDailyShorts { get; }
        public int MinShortDelayMinutes { get; }
        public int MinLongVideoDelayHours { get; }

        public YoutubeQuotaService(
            IWebHostEnvironment env,
            IConfiguration config,
            ILogger<YoutubeQuotaService> logger)
        {
            _config = config;
            _logger = logger;

            _bagsDir = Path.Combine(env.ContentRootPath, "workspace", "bags");
            Directory.CreateDirectory(_bagsDir);
            _quotaFile = Path.Combine(_bagsDir, "youtube_quota.json");

            // Configurable quota targets:
            // Default: 2 long videos per day, 4 shorts per day (Total 6 uploads = safely within 10,000 quota units / 1,600 per upload)
            MaxDailyLongVideos = _config.GetValue<int>("YouTube:MaxDailyLongVideos", 2);
            MaxDailyShorts = _config.GetValue<int>("YouTube:MaxDailyShorts", 4);
            MinShortDelayMinutes = _config.GetValue<int>("YouTube:MinShortDelayMinutes", 180); // 3 hours between shorts
            MinLongVideoDelayHours = _config.GetValue<int>("YouTube:MinLongVideoDelayHours", 12); // 12 hours between long videos

            SeedHistoryIfEmpty();
        }

        private void SeedHistoryIfEmpty()
        {
            lock (_fileLock)
            {
                try
                {
                    var existing = LoadRecordsUnsafe();
                    if (existing.Count > 0) return;

                    var records = new List<YoutubeUploadRecord>();

                    // 1. Seed past long videos from bag_youtube_done.json
                    var doneFile = Path.Combine(_bagsDir, "bag_youtube_done.json");
                    if (File.Exists(doneFile))
                    {
                        var montages = JsonFileService.Load<List<MontageItem>>(doneFile);
                        if (montages != null)
                        {
                            foreach (var m in montages.Where(x => !string.IsNullOrEmpty(x.MontageId) && !x.MontageId.Contains("-")))
                            {
                                records.Add(new YoutubeUploadRecord
                                {
                                    VideoId = m.MontageId,
                                    ChannelKey = m.KindKey?.ToLowerInvariant() ?? "lol",
                                    VideoType = "long",
                                    UploadedAt = m.UploadedAt ?? DateTime.UtcNow.AddHours(-1)
                                });
                            }
                        }
                    }

                    // 2. Seed past shorts from bag_shorts.json
                    var shortsFile = Path.Combine(_bagsDir, "bag_shorts.json");
                    if (File.Exists(shortsFile))
                    {
                        var shorts = JsonFileService.Load<List<ShortItem>>(shortsFile);
                        if (shorts != null)
                        {
                            foreach (var s in shorts.Where(x => x.IsUploaded && !string.IsNullOrEmpty(x.YoutubeVideoId)))
                            {
                                records.Add(new YoutubeUploadRecord
                                {
                                    VideoId = s.YoutubeVideoId!,
                                    ChannelKey = s.KindKey?.ToLowerInvariant() ?? "lol",
                                    VideoType = "short",
                                    UploadedAt = s.YoutubeUploadedAt ?? s.ScheduledPublishTime
                                });
                            }
                        }
                    }

                    if (records.Count > 0)
                    {
                        JsonFileService.Save(_quotaFile, records);
                        _logger.LogInformation($"[YoutubeQuota] Seeded {records.Count} past upload records into youtube_quota.json.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[YoutubeQuota] Error seeding past upload history.");
                }
            }
        }

        private List<YoutubeUploadRecord> LoadRecordsUnsafe()
        {
            if (!File.Exists(_quotaFile))
                return new List<YoutubeUploadRecord>();

            try
            {
                var list = JsonFileService.Load<List<YoutubeUploadRecord>>(_quotaFile) ?? new List<YoutubeUploadRecord>();
                // Auto prune records older than 7 days
                var cutoff = DateTime.UtcNow.AddDays(-7);
                var pruned = list.Where(r => r.UploadedAt >= cutoff).ToList();
                if (pruned.Count != list.Count)
                {
                    JsonFileService.Save(_quotaFile, pruned);
                }
                return pruned;
            }
            catch
            {
                return new List<YoutubeUploadRecord>();
            }
        }

        public bool CanUploadLongVideo(string channelKey, out string reason)
        {
            reason = string.Empty;
            var key = (channelKey ?? "lol").Trim().ToLowerInvariant();

            lock (_fileLock)
            {
                var records = LoadRecordsUnsafe();
                var cutoff24h = DateTime.UtcNow.AddHours(-24);

                var longVideos24h = records
                    .Where(r => r.ChannelKey == key && r.VideoType == "long" && r.UploadedAt >= cutoff24h)
                    .ToList();

                if (longVideos24h.Count >= MaxDailyLongVideos)
                {
                    reason = $"Daily long video quota reached ({longVideos24h.Count}/{MaxDailyLongVideos} in 24h for '{key}').";
                    return false;
                }
            }

            return true;
        }

        public bool CanUploadShort(string channelKey, out string reason)
        {
            reason = string.Empty;
            var key = (channelKey ?? "lol").Trim().ToLowerInvariant();

            // 1. PRIORITY CHECK: Is there a long video montage waiting in bag_youtube.json?
            // If a long video is queued, pause Shorts uploading to ensure quota is preserved for the long video!
            var bagYtFile = Path.Combine(_bagsDir, "bag_youtube.json");
            if (File.Exists(bagYtFile))
            {
                try
                {
                    var pendingMontages = JsonFileService.Load<List<MontageItem>>(bagYtFile);
                    var matchingPending = pendingMontages?
                        .Where(m => !m.Completed && m.KindKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (matchingPending != null && matchingPending.Count > 0)
                    {
                        reason = $"Prioritizing Long Video: Montage package '{matchingPending[0].MontageId}' is queued for upload on channel '{key}'. Shorts yield.";
                        return false;
                    }
                }
                catch { }
            }

            lock (_fileLock)
            {
                var records = LoadRecordsUnsafe();
                var cutoff24h = DateTime.UtcNow.AddHours(-24);

                // 2. DAILY QUOTA CHECK FOR SHORTS
                var shorts24h = records
                    .Where(r => r.ChannelKey == key && r.VideoType == "short" && r.UploadedAt >= cutoff24h)
                    .ToList();

                if (shorts24h.Count >= MaxDailyShorts)
                {
                    reason = $"Daily Shorts quota reached ({shorts24h.Count}/{MaxDailyShorts} in 24h for '{key}'). Remaining quota is strictly reserved for 1-2 long videos.";
                    return false;
                }

                // 3. ANTI-BURST PACING CHECK (Spacing between Shorts uploads)
                var lastShort = records
                    .Where(r => r.ChannelKey == key && r.VideoType == "short")
                    .OrderByDescending(r => r.UploadedAt)
                    .FirstOrDefault();

                if (lastShort != null)
                {
                    var timeSinceLast = DateTime.UtcNow - lastShort.UploadedAt;
                    var minDelay = TimeSpan.FromMinutes(MinShortDelayMinutes);
                    if (timeSinceLast < minDelay)
                    {
                        var remaining = minDelay - timeSinceLast;
                        reason = $"Short pacing active: last Short uploaded {(int)timeSinceLast.TotalMinutes}m ago. Next slot in {(int)remaining.TotalMinutes}m (Cadence: {MinShortDelayMinutes}m).";
                        return false;
                    }
                }
            }

            return true;
        }

        public Task RecordUploadAsync(string channelKey, string videoType, string videoId)
        {
            var key = (channelKey ?? "lol").Trim().ToLowerInvariant();
            var type = (videoType ?? "short").Trim().ToLowerInvariant();

            lock (_fileLock)
            {
                var records = LoadRecordsUnsafe();
                records.Add(new YoutubeUploadRecord
                {
                    VideoId = videoId,
                    ChannelKey = key,
                    VideoType = type,
                    UploadedAt = DateTime.UtcNow
                });
                JsonFileService.Save(_quotaFile, records);
                _logger.LogInformation($"[YoutubeQuota] Recorded YouTube upload: Type={type.ToUpperInvariant()}, ID={videoId}, Channel={key}. Total 24h records updated.");
            }

            return Task.CompletedTask;
        }

        public ChannelQuotaStatus GetChannelQuotaStatus(string channelKey)
        {
            var key = (channelKey ?? "lol").Trim().ToLowerInvariant();
            var status = new ChannelQuotaStatus
            {
                ChannelKey = key,
                MaxDailyLongVideos = MaxDailyLongVideos,
                MaxDailyShorts = MaxDailyShorts
            };

            // Check if there is a pending long video
            var bagYtFile = Path.Combine(_bagsDir, "bag_youtube.json");
            if (File.Exists(bagYtFile))
            {
                try
                {
                    var pendingMontages = JsonFileService.Load<List<MontageItem>>(bagYtFile);
                    status.HasPendingLongVideo = pendingMontages?
                        .Any(m => !m.Completed && m.KindKey.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? false;
                }
                catch { }
            }

            lock (_fileLock)
            {
                var records = LoadRecordsUnsafe();
                var cutoff24h = DateTime.UtcNow.AddHours(-24);

                var channelRecords24h = records
                    .Where(r => r.ChannelKey == key && r.UploadedAt >= cutoff24h)
                    .ToList();

                status.LongVideosLast24h = channelRecords24h.Count(r => r.VideoType == "long");
                status.ShortsLast24h = channelRecords24h.Count(r => r.VideoType == "short");

                var lastLong = records
                    .Where(r => r.ChannelKey == key && r.VideoType == "long")
                    .OrderByDescending(r => r.UploadedAt)
                    .FirstOrDefault();
                status.LastLongUploadAt = lastLong?.UploadedAt;

                var lastShort = records
                    .Where(r => r.ChannelKey == key && r.VideoType == "short")
                    .OrderByDescending(r => r.UploadedAt)
                    .FirstOrDefault();
                status.LastShortUploadAt = lastShort?.UploadedAt;

                if (lastShort != null)
                {
                    status.NextAvailableShortUploadAt = lastShort.UploadedAt.AddMinutes(MinShortDelayMinutes);
                }

                if (status.HasPendingLongVideo)
                {
                    status.StatusSummary = "Long Video Montage Queued (Shorts yielding quota)";
                }
                else if (status.ShortsLast24h >= MaxDailyShorts)
                {
                    status.StatusSummary = $"Daily Shorts cap reached ({status.ShortsLast24h}/{MaxDailyShorts}). Reserving quota for 1-2 Long Videos.";
                }
                else if (status.NextAvailableShortUploadAt.HasValue && status.NextAvailableShortUploadAt.Value > DateTime.UtcNow)
                {
                    var rem = (int)(status.NextAvailableShortUploadAt.Value - DateTime.UtcNow).TotalMinutes;
                    status.StatusSummary = $"Pacing active. Next Short slot in {rem}m.";
                }
                else
                {
                    status.StatusSummary = $"Balanced: {status.LongVideosLast24h}/{MaxDailyLongVideos} Long, {status.ShortsLast24h}/{MaxDailyShorts} Shorts in last 24h.";
                }
            }

            return status;
        }

        public List<ChannelQuotaStatus> GetAllChannelsQuotaStatus()
        {
            var kindsDir = Path.Combine(_bagsDir, "..", "profiles", "kinds");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lol", "valorant" };

            if (Directory.Exists(kindsDir))
            {
                foreach (var file in Directory.GetFiles(kindsDir, "*.json"))
                {
                    keys.Add(Path.GetFileNameWithoutExtension(file));
                }
            }

            return keys.Select(GetChannelQuotaStatus).ToList();
        }
    }
}
