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
        private TimeSpan _minMontageDurationMinutes ;
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
            var cfgValue = cfg.Value;
            n = cfgValue.BatchSize;
            _profiles = profiels;
      // Construction des chemins (simplifiée)
                  _rootPath = Path.Combine(env.ContentRootPath, "workspace");
            _dirBags = Path.Combine(_rootPath, "bags");
            //_minMontageDurationMinutes = TimeSpan.FromMinutes(cfgValue.MinMontageDurationMinutes);

            // Attribution des chemins de fichiers (sans la config pour éviter la duplication)
            // --- 3. Construction des Chemins de Fichiers (Utilisation du nom du fichier configuré) ---
            _bagSource = Path.Combine(_dirBags, cfgValue.BagDownloadPath);
            _bagTemp = Path.Combine(_dirBags, cfgValue.BagTempPath);
            _bagTrim = Path.Combine(_dirBags, cfgValue.BagTrimmedPath);
            _bagMontage = Path.Combine(_dirBags, cfgValue.BagMontagePath);
            _workerFile = Path.Combine(_dirBags, cfgValue.WorkerFile);
            _bagFailed = Path.Combine(_dirBags, cfgValue.BagFailed);
            _bagHistory = Path.Combine(_dirBags, cfgValue.BagHistoryPath);

            // REGISTER THE SHUTDOWN HOOK
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
            // 0. Verrouillage
            var worker = JsonFileService.Load<Worker1Model>(_workerFile);
            if (worker.Status)
                return;

            worker.Status = true;
            JsonFileService.Save(_workerFile, worker);
            var trimBag = JsonFileService.Load<List<TrimResult>>(_bagTrim);
            // Load the failed bag
            var failedBag = JsonFileService.Load<List<ClipInfo>>(_bagFailed);

            try
            {
                // 1. TENTATIVE DE REMPLISSAGE (Phase 0)
                //var tempBag = JsonFileService.Load<List<ClipInfo>>(_bagTemp);
                var downloadBag = JsonFileService.Load<List<ClipInfo>>(_bagSource);

                if (downloadBag.Count == 0)
                {
                    _logger.LogInformation("📥 Download bag empty → Fetching CSV from GitHub...");

                    using var stream = await _githubCsvFetcher.DownloadCsvAsync();
                    var items = await _csvService.ParseCsvAsync(stream);

                    if (items.Count > 0)
                    {
                        // 1️⃣ Load history
                        var historyBag = JsonFileService.Load<List<ClipInfo>>(_bagHistory);

                        // 2️⃣ Drop old entries (>10 days)
                        var threshold = DateTime.UtcNow.AddDays(-10);
                        int before = historyBag.Count;

                        historyBag = historyBag
                            .Where(h => h.ImportedAt >= threshold)
                            .ToList();

                        if (before != historyBag.Count)
                            _logger.LogInformation($"🧹 History pruned: Before={before}, After={historyBag.Count}");

                        // 3️⃣ Save cleaned history
                        JsonFileService.Save(_bagHistory, historyBag);

                        // 4️⃣ Build fast lookup dedupe index
                        var existing = new HashSet<string>(historyBag.Select(h => h.UniqueKey()));

                        // 5️⃣ Filter unique items
                        var uniqueItems = items
                            .Where(c => !existing.Contains(c.UniqueKey()))
                            .ToList();

                        _logger.LogInformation(
                            $"🧠 Dedupe: Received={items.Count}, Unique={uniqueItems.Count}, Removed={items.Count - uniqueItems.Count}"
                        );

                        if (uniqueItems.Count > 0)
                        {
                            // 6️⃣ Add into download bag
                            downloadBag.AddRange(uniqueItems);
                            JsonFileService.Save(_bagSource, downloadBag);

                            // 7️⃣ Add to history with timestamp
                            foreach (var u in uniqueItems)
                                u.ImportedAt = DateTime.UtcNow;

                            historyBag.AddRange(uniqueItems);
                            JsonFileService.Save(_bagHistory, historyBag);

                            // 8️⃣ Clear CSV file on GitHub
                            await _githubCsvFetcher.ClearCsvAsync();
                            _logger.LogInformation($"🧹 Remote CSV cleared.");
                        }
                        else
                        {
                            _logger.LogInformation("🟦 No unique clips to import — all duplicates.");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("CSV was empty — nothing to import.");
                    }
                }
                else {

                    // --------------------------------------------------------
                    // PHASE 1 : TRAITEMENT DES CLIPS INDIVIDUELS
                    // --------------------------------------------------------

                    
                    for (int i = downloadBag.Count - 1; i >= 0; i--)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var clip = downloadBag[i];
                        _logger.LogInformation($"🚀 Processing clip {i}: {clip.Link}");

                        // Get correct directories
                        var dirRaw = _pathService.GetRawDir(clip.KindKey, clip.SubKindKey, _rootPath);
                        var dirTrim = _pathService.GetTrimDir(clip.KindKey, clip.SubKindKey, _rootPath);

                        // 🔹 STEP 1 — DOWNLOAD
                        var downloadResult = await _download.DownloadAsync(clip, dirRaw, worker.DownloadCounter);

                        if (!downloadResult.Success)
                        {
                            _logger.LogWarning($"⚠️ Download failed for clip {i}. Reason: {downloadResult.ErrorType}");
                            clip.Description += $" [FAILED: {downloadResult.ErrorType}]";
                            failedBag.Add(clip);
                            downloadBag.RemoveAt(i);

                            JsonFileService.Save(_bagFailed, failedBag);
                            JsonFileService.Save(_bagTemp, downloadBag);
                            continue;
                        }

                        worker.DownloadCounter++;
                        string rawFilePath = downloadResult.FilePath;

                        try
                        {
                            // 🔹 STEP 2 — TRIM + NORMALIZE (1 single operation)
                            var trimResult =
                                await _trim.TrimAsync(rawFilePath, clip, dirTrim, worker.TrimCounter);

                            worker.TrimCounter++;

                            // Copy metadata
                            trimResult.KindKey = clip.KindKey ?? "";
                            trimResult.SubKindKey = clip.SubKindKey ?? "";

                            // 🔹 STEP 3 — GET DURATION
                            trimResult.Duration =
                                await _transition.GetVideoDurationAsync(trimResult.File);

                            // Add to trim bag
                            trimBag.Add(trimResult);

                            // Remove raw file
                            if (File.Exists(rawFilePath))
                                File.Delete(rawFilePath);

                            // Remove from temp bag
                            downloadBag.RemoveAt(i);

                            // SAVE STATE
                            JsonFileService.Save(_bagTrim, trimBag);
                            JsonFileService.Save(_bagSource, downloadBag);
                            JsonFileService.Save(_workerFile, worker);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"🚨 Trim/Process failed for clip {i}.");
                        }
                        finally
                        {
                            // Always clean raw file safely
                            if (File.Exists(rawFilePath))
                            {
                                File.Delete(rawFilePath);
                                _logger.LogInformation($"🗑️ Deleted raw file {rawFilePath}");
                            }
                        }
                    }
                }




                // --------------------------------------------------------
                // PHASE 2 : ORCHESTRATION DU MONTAGE
                // --------------------------------------------------------
                if (trimBag.Count > 0)
                {
                    var montageBag = JsonFileService.Load<List<MontageItem>>(_bagMontage);
                    TimeSpan maxMontageDuration = TimeSpan.FromMinutes(12);

                    for (int i = trimBag.Count - 1; i >= 0; i--)
                    {
                        var clipToPackage = trimBag[i];
                        TimeSpan clipDuration = TimeSpan.FromSeconds(clipToPackage.Duration);
                        var matchingPackage = montageBag.Where(x => x.KindKey == clipToPackage.KindKey && x.SubKindKey == clipToPackage.SubKindKey)
                            .ToList();
                        var currentPackage = matchingPackage.LastOrDefault();



                        bool mustCreateNew = currentPackage == null || currentPackage.Completed;

                        if (mustCreateNew)
                        {
                            currentPackage = new MontageItem
                            {
                                MontageId = Guid.NewGuid().ToString(),
                                TotalDuration = TimeSpan.Zero,
                                KindKey = clipToPackage.KindKey,
                                SubKindKey = clipToPackage.SubKindKey
                            };
                            montageBag.Add(currentPackage);
                            _logger.LogInformation($"📦 Création d'un nouveau paquet de montage: {currentPackage.MontageId}");
                        }

                        // Mappage de TrimResult vers MontageClip
                        var newMontageClip = new MontageClip
                        {
                            File = clipToPackage.File,
                            Description = clipToPackage.Description,
                            Duration = clipDuration
                        };

                        // Ajout du clip au paquet et mise à jour de la durée totale
                        currentPackage.Clips.Add(newMontageClip);
                        currentPackage.TotalDuration += newMontageClip.Duration;
                        _minMontageDurationMinutes = TimeSpan.FromMinutes( _profiles.GetProfile(currentPackage.KindKey, currentPackage.SubKindKey).MinMontageMinutes);
                        _logger.LogInformation($"➡️ Clip ajouté au paquet {currentPackage.MontageId}. Nouvelle durée: {currentPackage.TotalDuration:mm\\:ss}");
                        if (currentPackage.TotalDuration > _minMontageDurationMinutes ) currentPackage.Completed = true;

                        // Nettoyage et Sauvegarde
                        trimBag.RemoveAt(i);
                        JsonFileService.Save(_bagMontage, montageBag);
                        JsonFileService.Save(_bagTrim, trimBag);
                    } // Fin de la boucle trimBag
                } // Fin du if (trimBag.Count > 0)
            }
            finally
            {
                worker.Status = false;
                JsonFileService.Save(_workerFile, worker);
            }
        }

    }
}