using Microsoft.Extensions.Options;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Services;

namespace YoutubeGenerator.workers
{
    public class YoutubeWorker
    {
        private readonly ILlmService _llamaService;
        private readonly ILogger<YoutubeWorker> _logger;
        private readonly IMontageProfileService _montageProfileService;
        private readonly IYoutubeUploader _uploader;

        private readonly string _bagYoutube;
        private readonly string _bagYoutubeDone;
        private readonly string _bagYoutubeFailed;

        public YoutubeWorker(
            ILlmService llamaService,
            IOptions<CollectWorkerConfig> cfg,
            IWebHostEnvironment env,
            IMontageProfileService montageProfileService,
            ILogger<YoutubeWorker> logger,
            IYoutubeUploader uploader)
        {
            _llamaService = llamaService;
            _logger = logger;
            _montageProfileService = montageProfileService;
            _uploader = uploader;

            var root = Path.Combine(env.ContentRootPath, "workspace", "bags");
            _bagYoutube = Path.Combine(root, cfg.Value.BagYoutubePath);
            _bagYoutubeDone = Path.Combine(root, cfg.Value.BagYoutubeDonePath);
            _bagYoutubeFailed = Path.Combine(root, cfg.Value.BagYoutubeFailedPath);
        }

        public async Task RunAsync()
        {
            var bag = JsonFileService.Load<List<MontageItem>>(_bagYoutube);
            var bagDone = JsonFileService.Load<List<MontageItem>>(_bagYoutubeDone);
            var bagFailed = JsonFileService.Load<List<MontageItem>>(_bagYoutubeFailed);

            if (bag.Count == 0)
                return;

            // -----------------------------------------------------
            // 1️⃣ METADATA GENERATION
            // -----------------------------------------------------
            //var metaItem = bag.FirstOrDefault(x => x.Completed == false);

            foreach(var metaItem in bag.Where(x => x.Completed == false).ToList()) { 
                if (metaItem != null)
                {
                    try
                    {
                        _logger.LogInformation($"📝 Generating metadata for montage {metaItem.MontageId}");

                        var profile = _montageProfileService.GetProfile(metaItem.KindKey, metaItem.SubKindKey);

                        var gen = await _llamaService.GenerateMetadataAsync(metaItem, profile);

                        metaItem.GeneratedTitle = gen.Title;
                        metaItem.GeneratedDescription = gen.Description;

                        JsonFileService.Save(_bagYoutube, bag);
                        try
                        {
                            var creds = _montageProfileService.GetYoutubeCredentials(metaItem.KindKey);

                            // prevent concurrency
                            if (creds.UploadConfig.Enabled)
                            {
                                _logger.LogWarning($"⚠ Upload for {metaItem.KindKey} already running. Skipping.");
                                return;
                            }

                            // set upload lock
                            creds.UploadConfig.Enabled = true;
                            _montageProfileService.SaveYoutubeCredentials(metaItem.KindKey, creds);

                            // format description before upload
                            metaItem.GeneratedDescription = TextFormatter.FixDescription(metaItem.GeneratedDescription);
                            JsonFileService.Save(_bagYoutube, bag);

                            // compute schedule time
                            DateTime publishTime = creds.UploadConfig.LastUploadTime.AddHours(creds.UploadConfig.MinDelayHours);

                            _logger.LogInformation(
                                $"📤 Uploading montage {metaItem.MontageId}  start — publish at {publishTime}");

                            string videoId = await _uploader.UploadAsync(
                                metaItem.FinalVideoPath,
                                metaItem.GeneratedTitle,
                                metaItem.GeneratedDescription,
                                creds,
                                publishTime
                            );

                            // update stats
                            creds.UploadConfig.LastUploadTime = publishTime;
                            creds.UploadConfig.UploadCounter++;
                            creds.UploadConfig.Enabled = false;
                            _montageProfileService.SaveYoutubeCredentials(metaItem.KindKey, creds);
                            metaItem.Completed = true;

                            // move to DONE
                            metaItem.MontageId = videoId;
                            bagDone.Add(metaItem);
                            bag.Remove(metaItem);

                            JsonFileService.Save(_bagYoutube, bag);
                            JsonFileService.Save(_bagYoutubeDone, bagDone);

                            _logger.LogInformation($"✅ Upload complete: {videoId}");


                        }
                        catch (Exception ex)
                        {
                            bagFailed.Add(metaItem);
                            bag.Remove(metaItem);

                            JsonFileService.Save(_bagYoutube, bag);
                            JsonFileService.Save(_bagYoutubeFailed, bagFailed);

                            _logger.LogError(ex, "❌ Upload failed");

                            // remove upload lock
                            var creds = _montageProfileService.GetYoutubeCredentials(metaItem.KindKey);
                            creds.UploadConfig.Enabled = false;
                            _montageProfileService.SaveYoutubeCredentials(metaItem.KindKey, creds);

                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        bagFailed.Add(metaItem);
                        bag.Remove(metaItem);

                        JsonFileService.Save(_bagYoutube, bag);
                        JsonFileService.Save(_bagYoutubeFailed, bagFailed);

                        _logger.LogError(ex, "❌ Metadata generation failed");
                        return;
                    }
                }
            }

            // -----------------------------------------------------
            // 2️⃣ UPLOAD PROCESS
            // -----------------------------------------------------
            //foreach (var metaItem in bag.Where(x => x.Completed == true).ToList())
            //{
            //    if (metaItem != null)
            //    {
                   
            //    }
            //}
            return;
        }
    }
}
