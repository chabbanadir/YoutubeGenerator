using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Services;

namespace YoutubeGenerator.workers
{
    public class MontageWorker
    {
        private readonly ILogger<MontageWorker> _logger;
        private readonly ITransitionService _transitionService;
        private readonly ITrimService _trimService;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IPathService _pathService;
        private readonly IMontageProfileService _profileService;

        private readonly string _rootPath;
        private readonly string _dirBags;
        private readonly string _dirResources;

        private readonly string _workerFile;
        private readonly string _bagMontage;
        private readonly string _bagYoutube;

        public MontageWorker(
            ILogger<MontageWorker> logger,
            ITransitionService transitionService,
            ITrimService trimService,
            IOptions<CollectWorkerConfig> cfg,
            IWebHostEnvironment env,
            IHostApplicationLifetime appLifetime,
            IPathService pathService,
            IMontageProfileService profileService)
        {
            _logger = logger;
            _transitionService = transitionService;
            _trimService = trimService;
            _appLifetime = appLifetime;
            _pathService = pathService;
            _profileService = profileService;

            var cfgValue = cfg.Value;

            _rootPath = Path.Combine(env.ContentRootPath, "workspace");
            _dirResources = Path.Combine(_rootPath, "ressources");
            _dirBags = Path.Combine(_rootPath, "bags");

            _workerFile = Path.Combine(_dirBags, cfgValue.MontageFile);
            _bagMontage = Path.Combine(_dirBags, cfgValue.BagMontagePath);
            _bagYoutube = Path.Combine(_dirBags, cfgValue.BagYoutubePath);
            _appLifetime.ApplicationStopping.Register(() =>
            {
                _logger.LogInformation("⚠️ Shutdown signal received. Forcing status update...");
                ForceUnlockWorker();
            });
        }

        private void ForceUnlockWorker()
        {
            try
            {
                var worker = JsonFileService.Load<Worker1Model>(_workerFile);
                if (worker.Status)
                {
                    worker.Status = false;
                    JsonFileService.Save(_workerFile, worker);
                    _logger.LogInformation("✅ Worker status forced to 'false' in worker.json");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update worker status during shutdown.");
            }
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            var worker = JsonFileService.Load<Worker2State>(_workerFile);
            if (worker.Status)
                return;

            worker.Status = true;
            JsonFileService.Save(_workerFile, worker);

            try
            {
                var montageBag = JsonFileService.Load<List<MontageItem>>(_bagMontage);
                if (montageBag.Count == 0)
                {
                    _logger.LogInformation("📭 Montage bag is empty.");
                    return;
                }

                var completedPackages = montageBag.Where(x => x.Completed).ToList();
                if (completedPackages.Count == 0)
                {
                    _logger.LogInformation("❌ No completed montage package found. Nothing to render yet.");
                    return;
                }

                var packageToRender = completedPackages.First();

                _logger.LogInformation(
                    $"🎬 Rendering package: {packageToRender.MontageId} " +
                    $"({packageToRender.KindKey}/{packageToRender.SubKindKey}, {packageToRender.Clips.Count} clips)");

                // Load profile for this specific kind/subkind
                var profile = _profileService.GetProfile(
                    packageToRender.KindKey,
                    packageToRender.SubKindKey
                );

                string introPath = Path.Combine(_dirResources, profile.IntroFile);
                _logger.LogInformation($"📼 Intro path: {introPath}");

                // Directories for montage/trim
                var dirMontage = _pathService.GetMontageDir(
                    packageToRender.KindKey,
                    packageToRender.SubKindKey,
                    _rootPath
                );
                Directory.CreateDirectory(dirMontage);

                var dirTrim = _pathService.GetTrimDir(
                    packageToRender.KindKey,
                    packageToRender.SubKindKey,
                    _rootPath
                );
                Directory.CreateDirectory(dirTrim);

                string finalOutputPath = Path.Combine(dirMontage, $"{packageToRender.MontageId}.mp4");

                // Apply intro to first clip
                string firstClipPath = packageToRender.Clips.First().File;

                // Prepend intro and update path in the package to point to the new trimmed file
                await _trimService.PrependIntroAsync(
                    introPath,
                    firstClipPath,
                    dirTrim
                );

                var newFirstClip = Path.Combine(dirTrim, Path.GetFileName(firstClipPath));
                if (!File.Exists(newFirstClip))
                {
                    // It's possible PrependIntro wrote directly into the same location or different; handle both cases
                    // If PrependIntro replaced file in-place, keep that path; otherwise fallback to original trimmed path
                    newFirstClip = firstClipPath;
                }

                // Update the package's first clip path so later cleanup and youtube bag reference the correct file
                packageToRender.Clips[0].File = newFirstClip;

                // Build ordered list of clips for merging (use updated first clip path)
                List<string> videoPaths = new List<string>();
                videoPaths.Add(newFirstClip);
                videoPaths.AddRange(packageToRender.Clips.Skip(1).Select(c => c.File));

                // Merge with transitions
                bool success = await _transitionService.MergeVideosWithTransitionsAsync(
                    videoPaths,
                    finalOutputPath,
                    profile.TransitionAnimation,
                    profile.TransitionDuration,
                    stoppingToken
                );

                if (success)
                {
                    packageToRender.FinalVideoPath = finalOutputPath;

                    var youtubeBag = JsonFileService.Load<List<MontageItem>>(_bagYoutube);
                    // Mark package as not completed to avoid re-render; move to youtube queue
                    packageToRender.Completed = false;
                    youtubeBag.Add(packageToRender);
                    JsonFileService.Save(_bagYoutube, youtubeBag);

                    // Cleanup trimmed clips EXCEPT the new first clip (we already updated reference)
                    // This prevents deleting the file we just prepended intro into
                    foreach (var clip in packageToRender.Clips)
                    {
                        // If the clip file equals the new first clip and exists, we still remove it because we've moved the package file
                        // But to be safe, skip deleting if it's the same as the finalOutputPath or if it's the updated first clip that we want to keep
                        try
                        {
                            if (string.Equals(clip.File, newFirstClip, StringComparison.OrdinalIgnoreCase))
                            {
                                // Skip deletion of the new first clip here; it's still useful as a source reference (optional)
                                _logger.LogInformation($"ℹ️ Skipping deletion of first clip (intro merged): {clip.File}");
                                continue;
                            }

                            if (File.Exists(clip.File))
                            {
                                File.Delete(clip.File);
                                _logger.LogInformation($"🗑️ Deleted clip: {clip.File}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Failed to delete clip {clip.File} (will continue).");
                        }
                    }

                    // Save metadata JSON for the montage package (for records)
                    var temp = Path.Combine(dirMontage, $"{packageToRender.MontageId}.json");
                    JsonFileService.Save(temp, packageToRender.Clips);

                    // Remove rendered package from montage bag
                    montageBag.Remove(packageToRender);
                    JsonFileService.Save(_bagMontage, montageBag);

                    _logger.LogInformation($"✅ Final video generated and moved to youtube bag: {finalOutputPath}");
                    worker.FinalCounter += 1;
                }
                else
                {
                    _logger.LogError($"❌ Merge failed for package {packageToRender.MontageId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during montage worker execution.");
            }
            finally
            {
                worker.Status = false;
                JsonFileService.Save(_workerFile, worker);
            }
        }
    }
}
