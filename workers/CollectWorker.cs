using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Services;

namespace YoutubeGenerator.Workers
{
    public class CollectWorker
    {
        private readonly IDownloadService _download;
        private readonly ITrimService _trim;
        private readonly ITransitionService _transition;
        private readonly ILogger<CollectWorker> _logger;
        private readonly IPathService _pathService;
        private readonly IGitHubCsvFetcher _githubCsvFetcher;
        private readonly ICsvService _csvService;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IMontageProfileService _profiles;
        private string _rootPath;
        private TimeSpan _minMontageDurationMinutes;
        private readonly string _dirBags;

        private readonly string _workerFile;
        private readonly string _bagSource;
        private readonly string _bagTemp;
        private readonly string _bagTrim;
        private readonly string _bagMontage;
        private readonly string _bagFailed;
        private readonly string _bagHistory;

        private readonly int n;

        public CollectWorker(
          IDownloadService download,
          ITrimService trim,
          ITransitionService transition,
          IOptions<CollectWorkerConfig> cfg,
          IWebHostEnvironment env,
          ILogger<CollectWorker> logger,
          IHostApplicationLifetime appLifetime,
          IGitHubCsvFetcher githubCsvFetcher,
          ICsvService csvService,
          IMontageProfileService profiels,
          IPathService pathService)
        {
            _pathService = pathService;
            _download = download;
            _trim = trim;
            _transition = transition;
            _logger = logger;
            _appLifetime = appLifetime;
            _githubCsvFetcher = githubCsvFetcher;
            _csvService = csvService;
            _profiles = profiels;

            var cfgValue = cfg.Value;
            n = cfgValue.BatchSize;

            _rootPath = Path.Combine(env.ContentRootPath, "workspace");
            _dirBags = Path.Combine(_rootPath, "bags");

            _bagSource = Path.Combine(_dirBags, cfgValue.BagDownloadPath);
            _bagTemp = Path.Combine(_dirBags, cfgValue.BagTempPath);
            _bagTrim = Path.Combine(_dirBags, cfgValue.BagTrimmedPath);
            _bagMontage = Path.Combine(_dirBags, cfgValue.BagMontagePath);

            _workerFile = Path.Combine(_dirBags, cfgValue.WorkerFile);
            _bagFailed = Path.Combine(_dirBags, cfgValue.BagFailed);
            _bagHistory = Path.Combine(_dirBags, cfgValue.BagHistoryPath);

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

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var worker = JsonFileService.Load<Worker1Model>(_workerFile);
            if (worker.Status)
                return;

            worker.Status = true;
            JsonFileService.Save(_workerFile, worker);

            var trimBag = JsonFileService.Load<List<TrimResult>>(_bagTrim);
            var failedBag = JsonFileService.Load<List<ClipInfo>>(_bagFailed);
            var downloadBag = JsonFileService.Load<List<ClipInfo>>(_bagSource);

            try
            {
                // ============================================================================
                //  PHASE 0 : FETCH CSV SI bag_download VIDE
                // ============================================================================

                if (downloadBag.Count == 0)
                {
                    _logger.LogInformation("📥 bag_download empty → Fetching CSV from GitHub...");

                    string csvText = string.Empty;

                    try
                    {
                        csvText = await _githubCsvFetcher.DownloadCsvAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to download CSV from GitHub.");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(csvText))
                    {
                        _logger.LogWarning("⚠ CSV empty — nothing to parse.");
                        return;
                    }
                    using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvText));

                    var items = await _csvService.ParseCsvAsync(ms);

                    if (items.Count == 0)
                    {
                        _logger.LogWarning("⚠ CSV parsed but no valid items.");
                        return;
                    }

                    // ----------- Dedup with history -----------

                    var historyBag = JsonFileService.Load<List<ClipInfo>>(_bagHistory);

                    var threshold = DateTime.UtcNow.AddDays(-10);
                    historyBag = historyBag.Where(h => h.ImportedAt >= threshold).ToList();
                    JsonFileService.Save(_bagHistory, historyBag);

                    var existing = new HashSet<string>(historyBag.Select(h => h.UniqueKey()));
                    var uniqueItems = items.Where(c => !existing.Contains(c.UniqueKey())).ToList();

                    if (uniqueItems.Count == 0)
                    {
                        _logger.LogInformation("🟦 All items are duplicates of history.");
                        _logger.LogInformation("🟦 No unique items to import.");
                        return;
                    }

                    // Add to download bag
                    downloadBag.AddRange(uniqueItems);
                    JsonFileService.Save(_bagSource, downloadBag);

                    // Update history
                    foreach (var u in uniqueItems)
                        u.ImportedAt = DateTime.UtcNow;

                    historyBag.AddRange(uniqueItems);
                    JsonFileService.Save(_bagHistory, historyBag);

                    // Clear CSV on GitHub
                    try
                    {
                        await _githubCsvFetcher.ClearCsvAsync();
                        _logger.LogInformation("🧹 Remote CSV cleared.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to clear remote CSV.");
                    }
                }

                // ============================================================================
                //  PHASE 1 : PROCESSING DES CLIPS → DOWNLOAD + TRIM
                // ============================================================================

                for (int i = downloadBag.Count - 1; i >= 0; i--)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var clip = downloadBag[i];
                    _logger.LogInformation($"🚀 Processing clip {clip.Link}");

                    var dirRaw = _pathService.GetRawDir(clip.KindKey, clip.SubKindKey, _rootPath);
                    var dirTrim = _pathService.GetTrimDir(clip.KindKey, clip.SubKindKey, _rootPath);

                    // ----------- STEP 1: DOWNLOAD -----------

                    var downloadResult = await _download.DownloadAsync(clip, dirRaw, worker.DownloadCounter);

                    if (!downloadResult.Success)
                    {
                        clip.Description += $" [FAILED: {downloadResult.ErrorType}]";
                        failedBag.Add(clip);
                        downloadBag.RemoveAt(i);

                        JsonFileService.Save(_bagFailed, failedBag);
                        JsonFileService.Save(_bagSource, downloadBag);
                        continue;
                    }

                    worker.DownloadCounter++;
                    string rawFilePath = downloadResult.FilePath;

                    // ----------- STEP 2: TRIM -----------

                    try
                    {
                        var trimResult =
                          await _trim.TrimAsync(rawFilePath, clip, dirTrim, worker.TrimCounter);

                        worker.TrimCounter++;

                        trimResult.KindKey = clip.KindKey ?? "";
                        trimResult.SubKindKey = clip.SubKindKey ?? "";
                        trimResult.Duration =
                            await _transition.GetVideoDurationAsync(trimResult.File);

                        trimBag.Add(trimResult);

                        downloadBag.RemoveAt(i);

                        JsonFileService.Save(_bagTrim, trimBag);
                        JsonFileService.Save(_bagSource, downloadBag);
                        JsonFileService.Save(_workerFile, worker);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"🚨 Failed to trim clip {clip.Link}");
                    }
                    finally
                    {
                        if (File.Exists(rawFilePath))
                            File.Delete(rawFilePath);
                    }
                }

                // ============================================================================
                //  PHASE 2 : PACKAGING EN MONTAGE
                // ============================================================================

                if (trimBag.Count > 0)
                {
                    var montageBag = JsonFileService.Load<List<MontageItem>>(_bagMontage);
                    TimeSpan maxMontageDuration = TimeSpan.FromMinutes(12);

                    for (int i = trimBag.Count - 1; i >= 0; i--)
                    {
                        var clip = trimBag[i];
                        var clipDuration = TimeSpan.FromSeconds(clip.Duration);

                        var matching = montageBag
                            .Where(x => x.KindKey == clip.KindKey && x.SubKindKey == clip.SubKindKey)
                            .ToList();

                        var pkg = matching.LastOrDefault();

                        bool mustCreate = pkg == null || pkg.Completed;

                        if (mustCreate)
                        {
                            pkg = new MontageItem
                            {
                                MontageId = Guid.NewGuid().ToString(),
                                KindKey = clip.KindKey,
                                SubKindKey = clip.SubKindKey,
                                TotalDuration = TimeSpan.Zero
                            };

                            montageBag.Add(pkg);
                        }

                        var mclip = new MontageClip
                        {
                            File = clip.File,
                            Description = clip.Description,
                            Duration = clipDuration
                        };

                        pkg.Clips.Add(mclip);
                        pkg.TotalDuration += clipDuration;

                        _minMontageDurationMinutes =
                            TimeSpan.FromMinutes(_profiles.GetProfile(pkg.KindKey, pkg.SubKindKey).MinMontageMinutes);

                        if (pkg.TotalDuration > _minMontageDurationMinutes)
                            pkg.Completed = true;

                        trimBag.RemoveAt(i);
                        JsonFileService.Save(_bagMontage, montageBag);
                        JsonFileService.Save(_bagTrim, trimBag);
                    }
                }
            }
            finally
            {
                worker.Status = false;
                JsonFileService.Save(_workerFile, worker);
            }
        }
    }
}
