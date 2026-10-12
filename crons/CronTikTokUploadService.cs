using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Services;

namespace YoutubeGenerator.crons
{
    public class CronTikTokUploadService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CronTikTokUploadService> _logger;
        private readonly IConfiguration _config;
        private readonly string _bagShorts;
        private readonly string _backoffFile;
        private static readonly object _fileLock = new();

        // In-memory tracker for cadence and daily quota per channel
        private static readonly ConcurrentDictionary<string, DateTime> _lastUploadTimes = new();
        private static readonly ConcurrentDictionary<string, DateTime> _rateLimitBackoffs = new();
        private static readonly ConcurrentDictionary<string, List<DateTime>> _dailyUploadHistory = new();

        public CronTikTokUploadService(
            IServiceScopeFactory scopeFactory,
            ILogger<CronTikTokUploadService> logger,
            IConfiguration config,
            IWebHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _config = config;

            string bagsDir = Path.Combine(env.ContentRootPath, "workspace", "bags");
            Directory.CreateDirectory(bagsDir);
            _bagShorts = Path.Combine(bagsDir, "bag_shorts.json");
            _backoffFile = Path.Combine(bagsDir, "tiktok_backoff.json");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CronTikTokUploadService initialized (Independent TikTok pipeline: Max safe throughput cadence).");

            // Initial short wait to let app startup settle
            await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingTikTokShortsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[CronTikTok] Error in TikTok processing cycle.");
                }

                // Check for new queued shorts every 1 minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task ProcessPendingTikTokShortsAsync(CancellationToken stoppingToken)
        {
            if (!File.Exists(_bagShorts)) return;

            using var scope = _scopeFactory.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<ITikTokAuthService>();
            var uploader = scope.ServiceProvider.GetRequiredService<ITikTokUploader>();
            var renderTracker = scope.ServiceProvider.GetService<IRenderTrackerService>();

            // Configurable cadence parameters:
            // Minimum delay between uploads: default 60 minutes (~24 uploads/day max)
            int minDelayMinutes = _config.GetValue<int>("TikTok:MinDelayMinutes", 60);
            // Daily quota cap: default 18 uploads / 24-hour rolling window (safely under TikTok's 20-25 spam threshold)
            int maxDailyPosts = _config.GetValue<int>("TikTok:MaxDailyPosts", 18);

            List<ShortItem> bag;
            lock (_fileLock)
            {
                bag = JsonFileService.Load<List<ShortItem>>(_bagShorts) ?? new();
            }

            // Find unique connected channels that have pending videos
            var connectedChannels = bag
                .Where(s => !s.IsTikTokUploaded && File.Exists(s.FilePath))
                .Select(s => s.KindKey)
                .Distinct()
                .Where(k => authService.IsConnected(k))
                .ToList();

            foreach (var kindKey in connectedChannels)
            {
                if (stoppingToken.IsCancellationRequested) break;

                // 1. Check Rate Limit / Backoff
                if (_rateLimitBackoffs.TryGetValue(kindKey, out var backoffUntil) && DateTime.UtcNow < backoffUntil)
                {
                    _logger.LogDebug($"[CronTikTok] Channel '{kindKey}' is backed off until {backoffUntil:HH:mm} UTC.");
                    continue;
                }

                // 2. Check Cadence (minimum minutes between consecutive uploads)
                if (_lastUploadTimes.TryGetValue(kindKey, out var lastUpload))
                {
                    var elapsed = DateTime.UtcNow - lastUpload;
                    if (elapsed < TimeSpan.FromMinutes(minDelayMinutes))
                    {
                        var remaining = TimeSpan.FromMinutes(minDelayMinutes) - elapsed;
                        _logger.LogDebug($"[CronTikTok] Cadence active for '{kindKey}': next eligible in {remaining.TotalMinutes:F1} mins.");
                        continue;
                    }
                }

                // 3. Check Daily Quota (rolling 24-hour window)
                var history = _dailyUploadHistory.GetOrAdd(kindKey, _ => new List<DateTime>());
                lock (history)
                {
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    history.RemoveAll(t => t < cutoff);
                    if (history.Count >= maxDailyPosts)
                    {
                        _logger.LogWarning($"[CronTikTok] Daily post ceiling reached for '{kindKey}' ({history.Count}/{maxDailyPosts} in 24h). Pausing until quota resets.");
                        renderTracker?.AddLog("tiktok", $"⏳ TikTok daily limit reached for '{kindKey.ToUpperInvariant()}' ({history.Count}/{maxDailyPosts} posts/24h). Safe pause active.");
                        continue;
                    }
                }

                // 4. Select Next Short to upload
                ShortItem? candidate = null;
                lock (_fileLock)
                {
                    bag = JsonFileService.Load<List<ShortItem>>(_bagShorts) ?? new();
                    candidate = bag.FirstOrDefault(s =>
                        s.KindKey == kindKey &&
                        !s.IsTikTokUploaded &&
                        File.Exists(s.FilePath));
                }

                if (candidate == null) continue;

                _logger.LogInformation($"[CronTikTok] 🚀 Starting upload to TikTok for '{kindKey}': '{candidate.Title}'");
                renderTracker?.AddLog("tiktok", $"🚀 Publishing Short to TikTok: '{candidate.Title}' ({kindKey.ToUpperInvariant()})...");

                try
                {
                    var (success, publishId, message) = await uploader.UploadVideoAsync(
                        candidate.KindKey,
                        candidate.FilePath,
                        candidate.Title,
                        "SELF_ONLY" // Sandbox defaults to self_only; Direct Post or Inbox
                    );

                    if (success)
                    {
                        _lastUploadTimes[kindKey] = DateTime.UtcNow;
                        lock (history)
                        {
                            history.Add(DateTime.UtcNow);
                        }

                        _logger.LogInformation($"[CronTikTok] ✅ Uploaded Short to TikTok: '{candidate.Title}' (PublishId: {publishId}) - {message}");
                        renderTracker?.AddLog("tiktok", $"🎉 Short published to TikTok: '{candidate.Title}' ({message})");

                        // Update bag_shorts
                        lock (_fileLock)
                        {
                            bag = JsonFileService.Load<List<ShortItem>>(_bagShorts) ?? new();
                            var match = bag.FirstOrDefault(s => s.ShortId == candidate.ShortId);
                            if (match != null)
                            {
                                match.IsTikTokUploaded = true;
                                match.TikTokPublishId = publishId;
                                match.TikTokUploadedAt = DateTime.UtcNow;
                                JsonFileService.Save(_bagShorts, bag);
                            }
                        }

                        // STRICT CLEANUP RULE:
                        // Only delete the short video file and thumbnail if published on BOTH YouTube AND TikTok.
                        // If either platform has not yet uploaded, retain the file safely on disk.
                        try
                        {
                            bool bothUploaded = false;
                            lock (_fileLock)
                            {
                                var bagCheck = JsonFileService.Load<List<ShortItem>>(_bagShorts);
                                var matchCheck = bagCheck?.FirstOrDefault(s => s.ShortId == candidate.ShortId);
                                if (matchCheck != null)
                                {
                                    bothUploaded = matchCheck.IsUploaded && matchCheck.IsTikTokUploaded;
                                }
                            }

                            if (bothUploaded)
                            {
                                if (File.Exists(candidate.FilePath))
                                {
                                    File.Delete(candidate.FilePath);
                                    _logger.LogInformation($"[CronTikTok] Both YouTube & TikTok uploaded. Cleaned up video file: {candidate.FilePath}");
                                }
                                if (!string.IsNullOrEmpty(candidate.ThumbnailPath) && File.Exists(candidate.ThumbnailPath))
                                {
                                    File.Delete(candidate.ThumbnailPath);
                                }
                            }
                            else
                            {
                                _logger.LogInformation($"[CronTikTok] TikTok uploaded successfully. Retaining video '{Path.GetFileName(candidate.FilePath)}' on disk until YouTube upload is also completed.");
                            }
                        }
                        catch (Exception exDel)
                        {
                            _logger.LogWarning(exDel, $"[CronTikTok] Error checking/deleting short file: {candidate.FilePath}");
                        }

                        // Wait a brief safety delay (30 seconds) between multiple uploads
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    else
                    {
                        _logger.LogError($"[CronTikTok] ❌ Failed to upload Short to TikTok: {message}");
                        renderTracker?.AddLog("tiktok", $"❌ TikTok upload failed for '{candidate.Title}': {message}");

                        // If rate limit error or spam restriction detected, back off for 2 hours
                        if (message.Contains("429") || message.Contains("rate", StringComparison.OrdinalIgnoreCase) || message.Contains("limit", StringComparison.OrdinalIgnoreCase))
                        {
                            var backoffTime = DateTime.UtcNow.AddHours(2);
                            _rateLimitBackoffs[kindKey] = backoffTime;
                            _logger.LogWarning($"[CronTikTok] TikTok rate limit hit for '{kindKey}'. Backed off until {backoffTime:HH:mm:ss} UTC.");
                            renderTracker?.AddLog("tiktok", $"⏳ TikTok rate limit hit for '{kindKey}'. Backed off until {backoffTime:HH:mm} UTC.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[CronTikTok] Exception during TikTok upload for '{candidate.Title}'");
                }
            }
        }
    }
}
