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
        private readonly IRenderTrackerService? _renderTracker;

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
            IMontageProfileService profileService,
            IRenderTrackerService? renderTracker = null)
        {
            _logger = logger;
            _transitionService = transitionService;
            _trimService = trimService;
            _appLifetime = appLifetime;
            _pathService = pathService;
            _profileService = profileService;
            _renderTracker = renderTracker;

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
                    _renderTracker?.AddLog("Montage check: bag_montage.json is empty. Standing by.", "info");
                    return;
                }

                var completedPackages = montageBag.Where(x => x.Completed).ToList();
                if (completedPackages.Count == 0)
                {
                    _logger.LogInformation("❌ No completed montage package found. Nothing to render yet.");
                    _renderTracker?.AddLog("Montage check: No completed packages found in bag_montage.json.", "info");
                    return;
                }

                foreach (var packageToRender in completedPackages.ToList())
                {
                    _renderTracker?.StartSession(
                        packageToRender.MontageId,
                        packageToRender.KindKey,
                        packageToRender.SubKindKey,
                        packageToRender.Clips.Count,
                        packageToRender.TotalDuration.TotalSeconds
                    );

                    _logger.LogInformation(
                        $"🎬 Rendering package: {packageToRender.MontageId} " +
                        $"({packageToRender.KindKey}/{packageToRender.SubKindKey}, {packageToRender.Clips.Count} clips)");

                    // Stage 3.1: Profile & Asset Preparation
                    _renderTracker?.SetStep(1, $"Resolving specs for {packageToRender.KindKey}/{packageToRender.SubKindKey}");

                    var profile = _profileService.GetProfile(
                        packageToRender.KindKey,
                        packageToRender.SubKindKey
                    );

                    string introPath = Path.Combine(_dirResources, profile.IntroFile);
                    _logger.LogInformation($"📼 Intro path: {introPath}");
                    _renderTracker?.AddLog($"[Assets] Intro file: {profile.IntroFile}, Transition: {profile.TransitionAnimation} ({profile.TransitionDuration}s)", "info");

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

                    _renderTracker?.UpdateStepProgress(1, 100, "Specs and output directories validated");

                    // Stage 3.2: Intro stitching
                    string firstClipPath = packageToRender.Clips.First().File;

                    await _trimService.PrependIntroAsync(
                        introPath,
                        firstClipPath,
                        dirTrim
                    );

                    var newFirstClip = Path.Combine(dirTrim, Path.GetFileName(firstClipPath));
                    if (!File.Exists(newFirstClip))
                    {
                        newFirstClip = firstClipPath;
                    }

                    // Update package's first clip path
                    packageToRender.Clips[0].File = newFirstClip;

                    // Build ordered list of clips for merging
                    List<string> videoPaths = new List<string>();
                    videoPaths.Add(newFirstClip);
                    videoPaths.AddRange(packageToRender.Clips.Skip(1).Select(c => c.File));

                    // Stage 3.3 & 3.4: Probing and Transition Merging
                    bool success = await _transitionService.MergeVideosWithTransitionsAsync(
                        videoPaths,
                        finalOutputPath,
                        profile.TransitionAnimation,
                        profile.TransitionDuration,
                        stoppingToken
                    );

                    if (success)
                    {
                        // Stage 3.5: Final packaging & pipeline handoff
                        _renderTracker?.SetStep(5, "Moving package to bag_youtube.json and cleaning intermediate files");

                        packageToRender.FinalVideoPath = finalOutputPath;

                        var youtubeBag = JsonFileService.Load<List<MontageItem>>(_bagYoutube);
                        packageToRender.Completed = false;
                        youtubeBag.Add(packageToRender);
                        JsonFileService.Save(_bagYoutube, youtubeBag);

                        _renderTracker?.AddLog($"[Handoff] Added package to bag_youtube.json. Queue count: {youtubeBag.Count}", "step");

                        // Cleanup all trimmed clips for this package
                        foreach (var clip in packageToRender.Clips)
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(clip.File) && File.Exists(clip.File))
                                {
                                    File.Delete(clip.File);
                                    _logger.LogInformation($"🗑️ Deleted trimmed clip: {clip.File}");
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

                        _renderTracker?.UpdateStepProgress(5, 100, "Package staged & intermediate files cleaned");
                        _renderTracker?.CompleteSession(finalOutputPath);
                    }
                    else
                    {
                        _logger.LogError($"❌ Merge failed for package {packageToRender.MontageId}");
                        _renderTracker?.FailSession($"Merge failed for package {packageToRender.MontageId}");
                    }
                }   
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during montage worker execution.");
                _renderTracker?.FailSession(ex.Message);
            }
            finally
            {
                worker.Status = false;
                JsonFileService.Save(_workerFile, worker);
            }
        }
    }
}
