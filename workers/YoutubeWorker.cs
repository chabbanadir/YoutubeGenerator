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
            var metaItem = bag.FirstOrDefault(x => x.Completed == false);
            if (metaItem != null)
            {
                try
                {
                    _logger.LogInformation($"📝 Generating metadata for montage {metaItem.MontageId}");

                    var profile = _montageProfileService.GetProfile(metaItem.KindKey, metaItem.SubKindKey);

                    var gen = await _llamaService.GenerateMetadataAsync(metaItem, profile);

                    metaItem.GeneratedTitle = gen.Title;
                    metaItem.GeneratedDescription = gen.Description;
                    metaItem.Completed = true;

                    JsonFileService.Save(_bagYoutube, bag);
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

            // -----------------------------------------------------
            // 2️⃣ UPLOAD PROCESS
            // -----------------------------------------------------
            var uploadItem = bag.FirstOrDefault(x => x.Completed == true);
            if (uploadItem != null)
            {
                try
                {
                    var creds = _montageProfileService.GetYoutubeCredentials(uploadItem.KindKey);

                    // prevent concurrency
                    if (creds.UploadConfig.Enabled)
                    {
                        _logger.LogWarning($"⚠ Upload for {uploadItem.KindKey} already running. Skipping.");
                        return;
                    }

                    // set upload lock
                    creds.UploadConfig.Enabled = true;
                    _montageProfileService.SaveYoutubeCredentials(uploadItem.KindKey, creds);

                    // format description before upload
                    uploadItem.GeneratedDescription = TextFormatter.FixDescription(uploadItem.GeneratedDescription);
                    JsonFileService.Save(_bagYoutube, bag);

                    // compute schedule time
                    DateTime publishTime = creds.UploadConfig.LastUploadTime.AddHours(creds.UploadConfig.MinDelayHours);

                    _logger.LogInformation(
                        $"📤 Uploading montage {uploadItem.MontageId} — publish at {publishTime}");

                    string videoId = await _uploader.UploadAsync(
                        uploadItem.FinalVideoPath,
                        uploadItem.GeneratedTitle,
                        uploadItem.GeneratedDescription,
                        creds,
                        publishTime
                    );

                    // update stats
                    creds.UploadConfig.LastUploadTime = publishTime;
                    creds.UploadConfig.UploadCounter++;
                    creds.UploadConfig.Enabled = false;
                    _montageProfileService.SaveYoutubeCredentials(uploadItem.KindKey, creds);

                    // move to DONE
                    uploadItem.MontageId = videoId;
                    bagDone.Add(uploadItem);
                    bag.Remove(uploadItem);

                    JsonFileService.Save(_bagYoutube, bag);
                    JsonFileService.Save(_bagYoutubeDone, bagDone);

                    _logger.LogInformation($"✅ Upload complete: {videoId}");
                    return;
                }
                catch (Exception ex)
                {
                    bagFailed.Add(uploadItem);
                    bag.Remove(uploadItem);

                    JsonFileService.Save(_bagYoutube, bag);
                    JsonFileService.Save(_bagYoutubeFailed, bagFailed);

                    _logger.LogError(ex, "❌ Upload failed");

                    // remove upload lock
                    var creds = _montageProfileService.GetYoutubeCredentials(uploadItem.KindKey);
                    creds.UploadConfig.Enabled = false;
                    _montageProfileService.SaveYoutubeCredentials(uploadItem.KindKey, creds);

                    return;
                }
            }
        }
    }
}
