using System;
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
        private readonly string _bagShorts;
        private static readonly object _fileLock = new();

        public ShortsSchedulerService(
            IWebHostEnvironment env,
            ILogger<ShortsSchedulerService> logger,
            IMontageProfileService profileService,
            IYoutubeUploader uploader)
        {
            _logger = logger;
            _profileService = profileService;
            _uploader = uploader;

            string bagsDir = Path.Combine(env.ContentRootPath, "workspace", "bags");
            Directory.CreateDirectory(bagsDir);
            _bagShorts = Path.Combine(bagsDir, "bag_shorts.json");
        }

        public async Task<ShortItem?> QueueShortAsync(string shortFilePath, ClipInfo clip, double durationSeconds)
        {
            if (string.IsNullOrEmpty(shortFilePath) || !File.Exists(shortFilePath))
                return null;

            var kind = (clip.KindKey ?? "lol").Trim().ToLowerInvariant();
            var subKind = (clip.SubKindKey ?? "normal").Trim().ToLowerInvariant();
            var streamer = (clip.Description ?? "Streamer").Trim().TrimStart('@');

            // Format viral Short title and description
            string title;
            string gameTag;
            if (kind == "valorant")
            {
                title = subKind.Contains("ace")
                    ? $"RADIANT 1v5 ACE by @{streamer} 🎯 #Shorts #VALORANT"
                    : $"INSANE 200 IQ PLAY by @{streamer} 🔥 #Shorts #VALORANT";
                gameTag = "VALORANT";
            }
            else
            {
                title = subKind.Contains("penta")
                    ? $"INSANE PENTAKILL by @{streamer} 💥 #Shorts #LoL"
                    : $"INSANE OUTPLAY by @{streamer} 🔥 #Shorts #LoL";
                gameTag = "LeagueOfLegends";
            }

            if (title.Length > 95)
                title = title.Substring(0, 92) + "...";

            string description =
                $"Incredible play by @{streamer}!\n\n" +
                $"Subscribe for daily {gameTag} highlights and shorts!\n\n" +
                $"#Shorts #{gameTag} #Gaming #Clips #Highlights";

            var tags = new List<string>
            {
                "Shorts",
                "YouTube Shorts",
                $"{gameTag} Shorts",
                $"{gameTag} Clips",
                $"{gameTag} Highlights",
                streamer,
                "Gaming"
            };

            lock (_fileLock)
            {
                var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);

                // Calculate 20-minute rolling cadence
                DateTime nextSlot;
                var latestPending = bag
                    .Where(s => !s.IsUploaded)
                    .OrderByDescending(s => s.ScheduledPublishTime)
                    .FirstOrDefault();

                if (latestPending != null && latestPending.ScheduledPublishTime > DateTime.UtcNow)
                {
                    nextSlot = latestPending.ScheduledPublishTime.AddMinutes(20);
                }
                else
                {
                    nextSlot = DateTime.UtcNow.AddMinutes(10);
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
                    DurationSeconds = durationSeconds,
                    ScheduledPublishTime = nextSlot,
                    IsUploaded = false
                };

                bag.Add(item);
                JsonFileService.Save(_bagShorts, bag);

                _logger.LogInformation($"[ShortsScheduler] Queued Short '{title}' scheduled for {nextSlot:HH:mm:ss} UTC (Slot: +20m cadence)");
                return item;
            }
        }

        public async Task<bool> ProcessNextScheduledShortAsync()
        {
            ShortItem? nextShort = null;

            lock (_fileLock)
            {
                var bag = JsonFileService.Load<List<ShortItem>>(_bagShorts);
                nextShort = bag.FirstOrDefault(s => !s.IsUploaded && File.Exists(s.FilePath));
            }

            if (nextShort == null)
                return false;

            try
            {
                var creds = _profileService.GetYoutubeCredentials(nextShort.KindKey);

                _logger.LogInformation($"[ShortsScheduler] Uploading Short '{nextShort.Title}' to YouTube (Scheduled release: {nextShort.ScheduledPublishTime:yyyy-MM-dd HH:mm:ss} UTC)...");

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
                    _logger.LogInformation($"✅ [ShortsScheduler] Short uploaded successfully: Video ID = {videoId}");

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

                    // Clean up local short file to conserve disk space
                    try
                    {
                        if (File.Exists(nextShort.FilePath))
                        {
                            File.Delete(nextShort.FilePath);
                            _logger.LogInformation($"[ShortsScheduler] Cleaned up rendered short video: {nextShort.FilePath}");
                        }
                    }
                    catch { }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"🚨 [ShortsScheduler] Failed to upload Short: {nextShort.Title}");
            }

            return false;
        }
    }
}
