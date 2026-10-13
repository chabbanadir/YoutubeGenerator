using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DashboardService> _logger;
        private readonly IOptions<CollectWorkerConfig> _cfg;
        private readonly IYoutubeQuotaService _quotaService;
        private readonly string _bagsDir;
        private readonly string _kindsDir;

        private static readonly HashSet<string> SensitiveFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "appsettings.json",
            "appsettings.development.json",
            "appsettings.production.json",
            ".env",
            ".env.example",
            "credentials.json",
            "client_secret.json"
        };

        private static readonly string[] SensitiveKeys = new[]
        {
            "token", "secret", "apikey", "password", "auth", "credential", "privatekey", "api_key", "private_key"
        };

        public DashboardService(
            IWebHostEnvironment env,
            ILogger<DashboardService> logger,
            IOptions<CollectWorkerConfig> cfg,
            IYoutubeQuotaService quotaService)
        {
            _env = env;
            _logger = logger;
            _cfg = cfg;
            _quotaService = quotaService;

            var rootPath = Path.Combine(_env.ContentRootPath, "workspace");
            _bagsDir = Path.Combine(rootPath, "bags");
            _kindsDir = Path.Combine(rootPath, "profiles", "kinds");
            Directory.CreateDirectory(_bagsDir);
            Directory.CreateDirectory(_kindsDir);
        }

        public object GetOverview(bool isAuthenticated)
        {
            var workerCollectFile = Path.Combine(_bagsDir, _cfg.Value.WorkerFile);
            var workerMontageFile = Path.Combine(_bagsDir, _cfg.Value.MontageFile);

            var workerCollectState = JsonFileService.Load<Worker1Model>(workerCollectFile);
            var workerMontageState = JsonFileService.Load<Worker2State>(workerMontageFile);

            var bagsInfo = GetBags(isAuthenticated);
            var channels = GetChannelsOverview();
            var recentPublished = GetRecentPublishedVideos(6);
            var recentActivity = GetRecentActivity(8);
            var publicationQueue = GetPublicationQueue();
            var kpis = GetOverviewKpis(channels, bagsInfo);
            var youtubeQuota = _quotaService.GetAllChannelsQuotaStatus();

            return new
            {
                timestamp = DateTime.UtcNow,
                isAuthenticated = isAuthenticated,
                readOnlyMode = !isAuthenticated,
                currentUser = isAuthenticated ? "Admin" : null,
                userRole = isAuthenticated ? "Admin" : "Guest",
                kpis = kpis,
                youtubeQuota = youtubeQuota,
                channels = channels,
                publicationQueue = publicationQueue,
                recentPublished = recentPublished,
                recentActivity = recentActivity,
                workers = new
                {
                    collectWorkerActive = workerCollectState.Status,
                    montageWorkerActive = workerMontageState.Status,
                    collect = new
                    {
                        file = _cfg.Value.WorkerFile,
                        status = workerCollectState.Status,
                        downloadCounter = workerCollectState.DownloadCounter,
                        trimCounter = workerCollectState.TrimCounter
                    },
                    montage = new
                    {
                        file = _cfg.Value.MontageFile,
                        status = workerMontageState.Status,
                        finalCounter = workerMontageState.FinalCounter
                    }
                },
                bags = bagsInfo
            };
        }

        public object GetPublicStats()
        {
            // 1. Channels Handled
            var channelsList = new List<object>();
            int channelsHandled = 0;
            if (Directory.Exists(_kindsDir))
            {
                foreach (var file in Directory.GetFiles(_kindsDir, "*.json").OrderBy(f => f))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        if (string.IsNullOrWhiteSpace(content)) continue;

                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;
                        var kindName = Path.GetFileNameWithoutExtension(file);

                        string title = "";
                        if (root.TryGetProperty("YoutubeChannelTitle", out var tProp))
                            title = tProp.GetString() ?? "";

                        string channelId = "";
                        if (root.TryGetProperty("YoutubeChannelId", out var cProp))
                            channelId = cProp.GetString() ?? "";

                        bool hasTikTok = false;
                        if (root.TryGetProperty("TikTokConfig", out var ttProp) && ttProp.ValueKind == JsonValueKind.Object)
                        {
                            if (ttProp.TryGetProperty("Connected", out var ttConn))
                                hasTikTok = ttConn.GetBoolean();
                        }

                        channelsHandled++;
                        channelsList.Add(new
                        {
                            key = kindName.ToLowerInvariant(),
                            name = string.IsNullOrWhiteSpace(title) ? kindName.ToUpperInvariant() : title,
                            channelId = channelId,
                            youtubeUrl = !string.IsNullOrWhiteSpace(channelId) ? $"https://www.youtube.com/channel/{channelId}" : null,
                            hasTikTok = hasTikTok
                        });
                    }
                    catch { }
                }
            }

            // 2. Clips Downloaded & Trimmed
            int clipsDownloaded = 0;
            int clipsTrimmed = 0;
            var workerCollectFile = Path.Combine(_bagsDir, _cfg.Value.WorkerFile ?? "worker_collect.json");
            if (File.Exists(workerCollectFile))
            {
                try
                {
                    var w1 = JsonFileService.Load<Worker1Model>(workerCollectFile);
                    if (w1 != null)
                    {
                        clipsDownloaded = w1.DownloadCounter;
                        clipsTrimmed = w1.TrimCounter;
                    }
                }
                catch { }
            }

            // 3. Montages Created & Published
            int montagesCreated = 0;
            var workerMontageFile = Path.Combine(_bagsDir, _cfg.Value.MontageFile ?? "worker_montage.json");
            if (File.Exists(workerMontageFile))
            {
                try
                {
                    var w2 = JsonFileService.Load<Worker2State>(workerMontageFile);
                    if (w2 != null)
                    {
                        montagesCreated = w2.FinalCounter;
                    }
                }
                catch { }
            }

            int montagesPublished = 0;
            double totalDurationSeconds = 0;
            var doneFile = Path.Combine(_bagsDir, _cfg.Value.BagYoutubeDonePath ?? "bag_youtube_done.json");
            if (File.Exists(doneFile))
            {
                try
                {
                    var doneItems = JsonFileService.Load<List<MontageItem>>(doneFile);
                    if (doneItems != null)
                    {
                        montagesPublished = doneItems.Count;
                        if (montagesPublished > montagesCreated)
                        {
                            montagesCreated = montagesPublished;
                        }
                        foreach (var m in doneItems)
                        {
                            totalDurationSeconds += m.TotalDuration.TotalSeconds;
                        }
                    }
                }
                catch { }
            }

            // 4. Shorts Published
            int shortsTotal = 0;
            int shortsPublished = 0;
            int shortsPublishedYt = 0;
            int shortsPublishedTikTok = 0;
            var shortsFile = Path.Combine(_bagsDir, "bag_shorts.json");
            if (File.Exists(shortsFile))
            {
                try
                {
                    var shorts = JsonFileService.Load<List<ShortItem>>(shortsFile);
                    if (shorts != null)
                    {
                        shortsTotal = shorts.Count;
                        foreach (var s in shorts)
                        {
                            bool isYt = s.IsUploaded;
                            bool isTt = s.IsTikTokUploaded;
                            if (isYt) shortsPublishedYt++;
                            if (isTt) shortsPublishedTikTok++;
                            if (isYt || isTt) shortsPublished++;
                            if (isYt && s.DurationSeconds > 0)
                            {
                                totalDurationSeconds += s.DurationSeconds;
                            }
                        }
                    }
                }
                catch { }
            }

            int totalVideosPublished = montagesPublished + shortsPublished;
            var totalTimeSpan = TimeSpan.FromSeconds(totalDurationSeconds);
            string formattedDuration = $"{(int)totalTimeSpan.TotalHours}h {totalTimeSpan.Minutes}m";

            return new
            {
                status = "success",
                timestamp = DateTime.UtcNow,
                stats = new
                {
                    channelsHandled = channelsHandled,
                    clipsDownloaded = clipsDownloaded,
                    clipsProcessed = clipsTrimmed,
                    montagesCreated = montagesCreated,
                    montagesPublished = montagesPublished,
                    shortsPublished = shortsPublished,
                    shortsTotal = shortsTotal,
                    totalVideosPublished = totalVideosPublished,
                    totalDurationSeconds = (int)totalDurationSeconds,
                    totalDurationFormatted = formattedDuration
                },
                breakdown = new
                {
                    youtube = new
                    {
                        montages = montagesPublished,
                        shorts = shortsPublishedYt,
                        total = montagesPublished + shortsPublishedYt
                    },
                    tiktok = new
                    {
                        shorts = shortsPublishedTikTok
                    }
                },
                channels = channelsList,
                automation = new
                {
                    status = "Operational",
                    mode = "Autonomous Scheduled Workers",
                    supportedPlatforms = new[] { "YouTube", "TikTok" }
                }
            };
        }

        private List<object> GetChannelsOverview()
        {
            var channels = new List<object>();
            if (!Directory.Exists(_kindsDir)) return channels;

            foreach (var file in Directory.GetFiles(_kindsDir, "*.json").OrderBy(f => f))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    var kindKey = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (root.TryGetProperty("KindKey", out var kProp) && !string.IsNullOrWhiteSpace(kProp.GetString()))
                    {
                        kindKey = kProp.GetString()!.ToLowerInvariant();
                    }

                    string channelId = "";
                    if (root.TryGetProperty("YoutubeChannelId", out var cProp) && cProp.ValueKind == JsonValueKind.String)
                    {
                        channelId = cProp.GetString() ?? "";
                    }

                    string channelTitle = "";
                    if (root.TryGetProperty("YoutubeChannelTitle", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                    {
                        channelTitle = tProp.GetString() ?? "";
                    }

                    bool isConnected = false;
                    if (root.TryGetProperty("YoutubeRefreshToken", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                    {
                        var token = rProp.GetString();
                        isConnected = !string.IsNullOrWhiteSpace(token) && !token.Contains("***");
                    }

                    bool enabled = false;
                    int minDelayHours = 6;
                    DateTime? lastUploadTime = null;
                    int uploadCounter = 0;

                    if (root.TryGetProperty("UploadConfig", out var uProp) && uProp.ValueKind == JsonValueKind.Object)
                    {
                        if (uProp.TryGetProperty("Enabled", out var eProp)) enabled = eProp.GetBoolean();
                        if (uProp.TryGetProperty("MinDelayHours", out var mProp) && mProp.TryGetInt32(out var md)) minDelayHours = md;
                        if (uProp.TryGetProperty("LastUploadTime", out var lProp) && lProp.TryGetDateTime(out var lu)) lastUploadTime = lu;
                        if (uProp.TryGetProperty("UploadCounter", out var ucProp) && ucProp.TryGetInt32(out var uc)) uploadCounter = uc;
                    }

                    bool isTikTokConnected = false;
                    string tikTokDisplayName = "";
                    if (root.TryGetProperty("TikTokConfig", out var ttProp) && ttProp.ValueKind == JsonValueKind.Object)
                    {
                        if (ttProp.TryGetProperty("Connected", out var ttConn)) isTikTokConnected = ttConn.GetBoolean();
                        if (ttProp.TryGetProperty("DisplayName", out var ttName)) tikTokDisplayName = ttName.GetString() ?? "";
                    }

                    channels.Add(new
                    {
                        kindKey = kindKey,
                        name = Path.GetFileNameWithoutExtension(file),
                        channelId = channelId,
                        channelTitle = channelTitle,
                        isConnected = isConnected,
                        isTikTokConnected = isTikTokConnected,
                        tikTokDisplayName = tikTokDisplayName,
                        enabled = enabled,
                        minDelayHours = minDelayHours,
                        lastUploadTime = lastUploadTime,
                        uploadCounter = uploadCounter,
                        youtubeUrl = !string.IsNullOrWhiteSpace(channelId) ? $"https://www.youtube.com/channel/{channelId}" : null
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to parse kind profile '{file}' for overview");
                }
            }
            return channels;
        }

        private List<object> GetRecentPublishedVideos(int limit = 6)
        {
            var list = new List<object>();
            var doneFile = Path.Combine(_bagsDir, _cfg.Value.BagYoutubeDonePath ?? "bag_youtube_done.json");
            if (!File.Exists(doneFile)) return list;

            try
            {
                var items = JsonFileService.Load<List<MontageItem>>(doneFile);
                if (items != null && items.Count > 0)
                {
                    var recent = items.AsEnumerable().Reverse().Take(limit);
                    foreach (var item in recent)
                    {
                        var streamers = item.Clips?
                            .Select(c => c.Description?.Trim())
                            .Where(d => !string.IsNullOrWhiteSpace(d))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(5)
                            .ToList() ?? new List<string?>();

                        var durationStr = item.TotalDuration != TimeSpan.Zero 
                            ? item.TotalDuration.ToString(@"hh\:mm\:ss").TrimStart('0', ':')
                            : "";
                        if (string.IsNullOrWhiteSpace(durationStr) || durationStr == "00") durationStr = "0:00";

                        var montageId = item.MontageId ?? "";
                        bool isRealYtId = !string.IsNullOrWhiteSpace(montageId) && !montageId.Contains("-") && montageId.Length >= 8;

                        list.Add(new
                        {
                            montageId = montageId,
                            generatedTitle = item.GeneratedTitle ?? $"Video Montage ({item.KindKey})",
                            totalDuration = durationStr,
                            totalDurationRaw = item.TotalDuration.ToString(),
                            kindKey = item.KindKey,
                            subKindKey = item.SubKindKey,
                            clipCount = item.Clips?.Count ?? 0,
                            streamers = streamers,
                            youtubeUrl = isRealYtId ? $"https://www.youtube.com/watch?v={montageId}" : null,
                            thumbnailUrl = isRealYtId ? $"https://img.youtube.com/vi/{montageId}/mqdefault.jpg" : null,
                            finalVideoPath = item.FinalVideoPath,
                            completed = item.Completed
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read published videos for overview");
            }

            return list;
        }

        private class PublicationQueueItem
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "short";
            public string KindKey { get; set; } = "";
            public string SubKindKey { get; set; } = "";
            public string Title { get; set; } = "";
            public string Streamer { get; set; } = "";
            public double DurationSeconds { get; set; }
            public string DurationFormatted { get; set; } = "";
            public DateTime? ScheduledPublishTime { get; set; }
            public string? ScheduledPublishTimeUtc { get; set; }
            public DateTime? CreatedAt { get; set; }
            public string Countdown { get; set; } = "";
            public string Status { get; set; } = "ready";
            public string? QuotaReason { get; set; }
            public bool IsFuture { get; set; }
            public bool IsUploadedYt { get; set; }
            public string? YoutubeVideoId { get; set; }
            public string? YoutubeUrl { get; set; }
            public bool IsUploadedTikTok { get; set; }
            public string? TikTokPublishId { get; set; }
            public string? FilePath { get; set; }
        }

        private object GetPublicationQueue()
        {
            var upcoming = new List<PublicationQueueItem>();
            var publishedRecent = new List<PublicationQueueItem>();

            var bagShortsFile = Path.Combine(_bagsDir, "bag_shorts.json");
            if (File.Exists(bagShortsFile))
            {
                try
                {
                    var shorts = JsonFileService.Load<List<ShortItem>>(bagShortsFile);
                    if (shorts != null)
                    {
                        // Upcoming queue: strictly videos that are pending YouTube upload!
                        var pending = shorts
                            .Where(s => !s.IsUploaded)
                            .OrderBy(s => s.ScheduledPublishTime)
                            .ToList();

                        foreach (var s in pending)
                        {
                            var isFuture = s.ScheduledPublishTime > DateTime.UtcNow;
                            var diff = s.ScheduledPublishTime - DateTime.UtcNow;
                            string countdown;
                            string status;
                            string? quotaReasonText = null;

                            if (!_quotaService.CanUploadShort(s.KindKey, out var quotaReason))
                            {
                                countdown = "Held (Pacing / Quota)";
                                status = "held";
                                quotaReasonText = quotaReason;
                            }
                            else if (isFuture)
                            {
                                status = "scheduled";
                                if (diff.TotalHours >= 24)
                                    countdown = $"in {(int)diff.TotalDays}d {diff.Hours}h";
                                else if (diff.TotalHours >= 1)
                                    countdown = $"in {(int)diff.TotalHours}h {diff.Minutes}m";
                                else if (diff.TotalMinutes >= 1)
                                    countdown = $"in {(int)diff.TotalMinutes}m";
                                else
                                    countdown = "in < 1m";
                            }
                            else
                            {
                                status = "ready";
                                countdown = "Ready";
                            }

                            upcoming.Add(new PublicationQueueItem
                            {
                                Id = s.ShortId,
                                Type = "short",
                                KindKey = s.KindKey,
                                SubKindKey = s.SubKindKey,
                                Title = string.IsNullOrWhiteSpace(s.Title) ? $"Short by @{s.Streamer}" : s.Title,
                                Streamer = s.Streamer,
                                DurationSeconds = s.DurationSeconds,
                                DurationFormatted = s.DurationSeconds > 0 ? $"{Math.Round(s.DurationSeconds)}s" : "--",
                                ScheduledPublishTime = s.ScheduledPublishTime,
                                ScheduledPublishTimeUtc = s.ScheduledPublishTime.ToString("o"),
                                CreatedAt = s.CreatedAt,
                                Countdown = countdown,
                                Status = status,
                                QuotaReason = quotaReasonText,
                                IsFuture = isFuture,
                                IsUploadedYt = s.IsUploaded,
                                YoutubeVideoId = s.YoutubeVideoId,
                                YoutubeUrl = !string.IsNullOrWhiteSpace(s.YoutubeVideoId) ? $"https://www.youtube.com/shorts/{s.YoutubeVideoId}" : null,
                                IsUploadedTikTok = s.IsTikTokUploaded,
                                TikTokPublishId = s.TikTokPublishId,
                                FilePath = s.FilePath
                            });
                        }

                        var done = shorts
                            .Where(s => s.IsUploaded)
                            .OrderByDescending(s => s.ScheduledPublishTime)
                            .Take(10)
                            .ToList();

                        foreach (var s in done)
                        {
                            publishedRecent.Add(new PublicationQueueItem
                            {
                                Id = s.ShortId,
                                Type = "short",
                                KindKey = s.KindKey,
                                SubKindKey = s.SubKindKey,
                                Title = s.Title,
                                Streamer = s.Streamer,
                                DurationSeconds = s.DurationSeconds,
                                DurationFormatted = s.DurationSeconds > 0 ? $"{Math.Round(s.DurationSeconds)}s" : "--",
                                ScheduledPublishTime = s.ScheduledPublishTime,
                                ScheduledPublishTimeUtc = s.ScheduledPublishTime.ToString("o"),
                                CreatedAt = s.CreatedAt,
                                Countdown = "Published",
                                Status = "published",
                                IsFuture = false,
                                IsUploadedYt = s.IsUploaded,
                                YoutubeVideoId = s.YoutubeVideoId,
                                YoutubeUrl = !string.IsNullOrWhiteSpace(s.YoutubeVideoId) ? $"https://www.youtube.com/shorts/{s.YoutubeVideoId}" : null,
                                IsUploadedTikTok = s.IsTikTokUploaded,
                                TikTokPublishId = s.TikTokPublishId,
                                FilePath = s.FilePath
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load publication queue from bag_shorts.json");
                }
            }

            var bagYtFile = Path.Combine(_bagsDir, _cfg.Value.BagYoutubePath ?? "bag_youtube.json");
            if (File.Exists(bagYtFile))
            {
                try
                {
                    var montages = JsonFileService.Load<List<MontageItem>>(bagYtFile);
                    if (montages != null)
                    {
                        foreach (var m in montages.Where(m => !m.Completed))
                        {
                            var durationStr = m.TotalDuration != TimeSpan.Zero
                                ? m.TotalDuration.ToString(@"hh\:mm\:ss").TrimStart('0', ':')
                                : "--";
                            if (string.IsNullOrWhiteSpace(durationStr) || durationStr == "00") durationStr = "0:00";

                            upcoming.Add(new PublicationQueueItem
                            {
                                Id = m.MontageId,
                                Type = "montage",
                                KindKey = m.KindKey,
                                SubKindKey = m.SubKindKey,
                                Title = m.GeneratedTitle ?? $"Montage Package ({m.KindKey})",
                                Streamer = m.Clips?.FirstOrDefault()?.Description ?? "Multiple Streamers",
                                DurationSeconds = m.TotalDuration.TotalSeconds,
                                DurationFormatted = durationStr,
                                ScheduledPublishTime = null,
                                ScheduledPublishTimeUtc = null,
                                CreatedAt = null,
                                Countdown = "Queued for Upload",
                                Status = "queued",
                                IsFuture = false,
                                IsUploadedYt = false,
                                YoutubeVideoId = null,
                                YoutubeUrl = null,
                                IsUploadedTikTok = false,
                                TikTokPublishId = null,
                                FilePath = m.FinalVideoPath
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load publication queue from bag_youtube.json");
                }
            }

            var nextItem = upcoming
                .Where(x => x.ScheduledPublishTime.HasValue && x.ScheduledPublishTime.Value > DateTime.UtcNow)
                .OrderBy(x => x.ScheduledPublishTime!.Value)
                .FirstOrDefault();

            return new
            {
                totalCount = upcoming.Count,
                nextReleaseTime = nextItem?.ScheduledPublishTimeUtc,
                nextCountdown = nextItem?.Countdown,
                items = upcoming,
                recentPublished = publishedRecent
            };
        }

        private List<object> GetRecentActivity(int limit = 8)
        {
            var list = new List<object>();
            var historyFile = Path.Combine(_bagsDir, _cfg.Value.BagHistoryPath ?? "bag_history.json");
            if (!File.Exists(historyFile)) return list;

            try
            {
                var items = JsonFileService.Load<List<ClipInfo>>(historyFile);
                if (items != null && items.Count > 0)
                {
                    var recent = items.AsEnumerable().Reverse().Take(limit);
                    foreach (var c in recent)
                    {
                        list.Add(new
                        {
                            link = c.Link,
                            streamer = c.Description ?? "Streamer",
                            start = c.Start,
                            end = c.End,
                            kindKey = c.KindKey,
                            subKindKey = c.SubKindKey,
                            importedAt = c.ImportedAt
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read history activity for overview");
            }

            return list;
        }

        private object GetOverviewKpis(List<object> channels, List<object> bagsInfo)
        {
            int totalPublished = 0;
            int totalHarvestedClips = 0;
            int activeQueue = 0;
            int failedCount = 0;
            TimeSpan totalDuration = TimeSpan.Zero;

            var doneFile = Path.Combine(_bagsDir, _cfg.Value.BagYoutubeDonePath ?? "bag_youtube_done.json");
            if (File.Exists(doneFile))
            {
                try
                {
                    var items = JsonFileService.Load<List<MontageItem>>(doneFile);
                    if (items != null)
                    {
                        totalPublished = items.Count;
                        foreach (var it in items)
                        {
                            totalDuration += it.TotalDuration;
                        }
                    }
                }
                catch { }
            }

            var historyFile = Path.Combine(_bagsDir, _cfg.Value.BagHistoryPath ?? "bag_history.json");
            if (File.Exists(historyFile))
            {
                try
                {
                    var items = JsonFileService.Load<List<ClipInfo>>(historyFile);
                    if (items != null) totalHarvestedClips = items.Count;
                }
                catch { }
            }

            var queueBagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                _cfg.Value.BagDownloadPath ?? "bag_download.json",
                _cfg.Value.BagTrimmedPath ?? "bag_trimmed.json",
                _cfg.Value.BagMontagePath ?? "bag_montage.json",
                _cfg.Value.BagYoutubePath ?? "bag_youtube.json",
                "bag_shorts.json"
            };

            int failedClipsCount = 0;
            int failedMontagesCount = 0;

            var failedFile = Path.Combine(_bagsDir, _cfg.Value.BagFailed ?? "bag_failed.json");
            if (File.Exists(failedFile))
            {
                try
                {
                    var items = JsonFileService.Load<List<ClipInfo>>(failedFile);
                    if (items != null)
                    {
                        failedClipsCount = items.Count(c => !string.IsNullOrWhiteSpace(c.Link));
                    }
                }
                catch { }
            }

            var ytFailedFile = Path.Combine(_bagsDir, _cfg.Value.BagYoutubeFailedPath ?? "bag_youtube_failed.json");
            if (File.Exists(ytFailedFile))
            {
                try
                {
                    var items = JsonFileService.Load<List<MontageItem>>(ytFailedFile);
                    if (items != null)
                    {
                        failedMontagesCount = items.Count(m => !string.IsNullOrWhiteSpace(m.MontageId) || (m.Clips != null && m.Clips.Count > 0));
                    }
                }
                catch { }
            }

            failedCount = failedClipsCount + failedMontagesCount;

            foreach (var b in bagsInfo)
            {
                if (b is null) continue;
                var fProp = b.GetType().GetProperty("fileName")?.GetValue(b)?.ToString() ?? "";
                var cProp = Convert.ToInt32(b.GetType().GetProperty("itemCount")?.GetValue(b) ?? 0);

                if (queueBagNames.Contains(fProp)) activeQueue += cProp;
            }

            int connectedCount = 0;
            foreach (var ch in channels)
            {
                var isConn = ch.GetType().GetProperty("isConnected")?.GetValue(ch) as bool? ?? false;
                if (isConn) connectedCount++;
            }

            int totalSuccessOps = totalPublished + totalHarvestedClips;
            int totalAttemptedOps = totalSuccessOps + failedCount;
            int successRate = totalAttemptedOps == 0 ? 100 : (int)Math.Round(((double)totalSuccessOps / totalAttemptedOps) * 100);

            var hours = (int)totalDuration.TotalHours;
            var minutes = totalDuration.Minutes;
            var seconds = totalDuration.Seconds;
            string formattedDuration = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m {seconds}s";

            return new
            {
                totalPublishedVideos = totalPublished,
                totalClipsHarvested = totalHarvestedClips,
                activeQueueItems = activeQueue,
                connectedChannelsCount = connectedCount,
                totalChannelsCount = channels.Count,
                failedItemsCount = failedCount,
                successRatePercent = successRate,
                totalDurationFormatted = formattedDuration,
                totalDurationSeconds = (int)totalDuration.TotalSeconds
            };
        }

        public List<object> GetBags(bool isAuthenticated)
        {
            string[] knownBags = new[]
            {
                _cfg.Value.BagDownloadPath,
                _cfg.Value.BagTempPath,
                _cfg.Value.BagTrimmedPath,
                _cfg.Value.BagMontagePath,
                _cfg.Value.BagYoutubePath,
                _cfg.Value.BagYoutubeDonePath,
                _cfg.Value.BagYoutubeFailedPath,
                _cfg.Value.BagFailed,
                _cfg.Value.BagHistoryPath,
                _cfg.Value.WorkerFile,
                _cfg.Value.MontageFile
            };

            var allFilesQuery = Directory.GetFiles(_bagsDir, "*.json")
                .Select(Path.GetFileName)
                .Cast<string>()
                .Union(knownBags)
                .Distinct()
                .OrderBy(f => f);

            var allFiles = isAuthenticated 
                ? allFilesQuery 
                : allFilesQuery.Where(name => !IsFileSensitive(name));

            var list = new List<object>();

            foreach (var fileName in allFiles)
            {
                var fullPath = Path.Combine(_bagsDir, fileName);
                bool exists = File.Exists(fullPath);
                int count = 0;
                long size = 0;
                DateTime? lastModified = null;
                string type = "unknown";
                bool isSensitive = IsFileSensitive(fileName);

                if (exists)
                {
                    var fileInfo = new FileInfo(fullPath);
                    size = fileInfo.Length;
                    lastModified = fileInfo.LastWriteTimeUtc;

                    try
                    {
                        var json = File.ReadAllText(fullPath);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                count = doc.RootElement.GetArrayLength();
                                type = "array";
                            }
                            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                count = 1;
                                type = "object";
                            }
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors for metadata
                    }
                }

                list.Add(new
                {
                    fileName,
                    exists,
                    itemCount = count,
                    sizeBytes = size,
                    lastModified,
                    type,
                    isSensitive
                });
            }

            return list;
        }

        public object GetBagContent(string bagName, bool isAuthenticated)
        {
            var cleanName = Path.GetFileName(bagName);

            if (IsFileSensitive(cleanName) && !isAuthenticated)
            {
                throw new UnauthorizedAccessException($"Access to sensitive file '{cleanName}' requires authentication.");
            }

            var fullPath = Path.Combine(_bagsDir, cleanName);

            if (!File.Exists(fullPath))
            {
                return new
                {
                    fileName = cleanName,
                    rawJson = "[]",
                    parsed = new object[0],
                    exists = false,
                    isRedacted = !isAuthenticated
                };
            }

            try
            {
                var rawJson = File.ReadAllText(fullPath);
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    return new { fileName = cleanName, rawJson = "[]", parsed = new object[0], exists = true, isRedacted = !isAuthenticated };
                }

                if (!isAuthenticated)
                {
                    try
                    {
                        var node = JsonNode.Parse(rawJson);
                        SanitizeNode(node);

                        var sanitizedJson = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "[]";
                        var parsedObj = JsonSerializer.Deserialize<object>(sanitizedJson);

                        return new
                        {
                            fileName = cleanName,
                            rawJson = sanitizedJson,
                            parsed = parsedObj,
                            exists = true,
                            isRedacted = true
                        };
                    }
                    catch
                    {
                        return new
                        {
                            fileName = cleanName,
                            rawJson = rawJson,
                            parsed = new object[0],
                            exists = true,
                            isRedacted = true
                        };
                    }
                }
                else
                {
                    var parsedObj = JsonSerializer.Deserialize<object>(rawJson);
                    return new
                    {
                        fileName = cleanName,
                        rawJson = rawJson,
                        parsed = parsedObj,
                        exists = true,
                        isRedacted = false
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to read bag '{cleanName}'");
                return new
                {
                    fileName = cleanName,
                    rawJson = "[]",
                    parsed = new object[0],
                    exists = false,
                    isRedacted = !isAuthenticated
                };
            }
        }

        public void SaveBagContent(string bagName, JsonElement content)
        {
            var cleanName = Path.GetFileName(bagName);
            var fullPath = Path.Combine(_bagsDir, cleanName);

            JsonElement targetElement = content;

            // Handle legacy or wrapped payloads like { "content": "..." } or { "content": [...] }
            if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("content", out var contentProp))
            {
                if (contentProp.ValueKind == JsonValueKind.String)
                {
                    var strVal = contentProp.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(strVal))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(strVal);
                            targetElement = doc.RootElement.Clone();
                        }
                        catch
                        {
                            // Keep original contentProp string if parsing fails
                        }
                    }
                }
                else if (contentProp.ValueKind == JsonValueKind.Object || contentProp.ValueKind == JsonValueKind.Array)
                {
                    targetElement = contentProp;
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string formattedJson = JsonSerializer.Serialize(targetElement, options);
            File.WriteAllText(fullPath, formattedJson);
            _logger.LogInformation($"Saved bag file '{cleanName}'.");
        }

        private static bool IsFileSensitive(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return true;
            if (SensitiveFiles.Contains(fileName)) return true;
            if (fileName.EndsWith(".env", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Contains("credential", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Contains("secret", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsSensitiveKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return false;

            if (keyName.Equals("KindKey", StringComparison.OrdinalIgnoreCase) ||
                keyName.Equals("SubKindKey", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var lower = keyName.ToLowerInvariant();
            if (lower.Equals("key")) return true;

            return SensitiveKeys.Any(k => lower.Contains(k));
        }

        private static void SanitizeNode(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                var keysToMask = new List<string>();
                foreach (var kvp in obj)
                {
                    if (IsSensitiveKey(kvp.Key))
                    {
                        keysToMask.Add(kvp.Key);
                    }
                    else
                    {
                        SanitizeNode(kvp.Value);
                    }
                }

                foreach (var key in keysToMask)
                {
                    obj[key] = "***SENSITIVE_DATA_REDACTED***";
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    SanitizeNode(item);
                }
            }
        }
    }
}
