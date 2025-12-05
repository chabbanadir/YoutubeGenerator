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

            try
            {
                // 1. TENTATIVE DE REMPLISSAGE (Phase 0)
                var tempBag = JsonFileService.Load<List<ClipInfo>>(_bagTemp);
                var downloadBag = JsonFileService.Load<List<ClipInfo>>(_bagSource);

                if (downloadBag.Count == 0)
                {
                    _logger.LogInformation("📥 Download bag empty → Fetching CSV from GitHub...");

                    using var stream = await _githubCsvFetcher.DownloadCsvAsync();
                    var items = await _csvService.ParseCsvAsync(stream);

                    if (items.Count > 0)
                    {
                        downloadBag.AddRange(items);
                        JsonFileService.Save(_bagSource, downloadBag);

                        // Clear remote CSV
                        await _githubCsvFetcher.ClearCsvAsync();
                        _logger.LogInformation($"🧹 Remote CSV cleared ({items.Count} items imported)");
                    }
                    else
                    {
                        _logger.LogInformation("CSV was empty — nothing to import.");
                    }
                }
                if (tempBag.Count == 0)
                {
                    if (downloadBag.Count > 0)
                    {
                        var itemsToMove = downloadBag.Take(n).ToList();
                        tempBag.AddRange(itemsToMove);
                        downloadBag = downloadBag.Skip(n).ToList();
                        JsonFileService.Save(_bagTemp, tempBag);
                        JsonFileService.Save(_bagSource, downloadBag);
                    }
                    // On ne retourne plus ici! On continue vers le traitement.
                }

                // 2. CLAUSE DE GARDE GLOBALE
                if (tempBag.Count == 0)
                {
                    _logger.LogInformation("Le Temp Bag est vide. Pas de travail à effectuer.");
                    return; // Sortie élégante si rien n'a pu être chargé.
                }

                // --------------------------------------------------------
                // PHASE 1 : TRAITEMENT DES CLIPS INDIVIDUELS
                // --------------------------------------------------------

                var trimBag = JsonFileService.Load<List<TrimResult>>(_bagTrim);
                // Load the failed bag
                var failedBag = JsonFileService.Load<List<ClipInfo>>(_bagFailed);
                for (int i = tempBag.Count - 1; i >= 0; i--)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var clip = tempBag[i];
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
                        tempBag.RemoveAt(i);

                        JsonFileService.Save(_bagFailed, failedBag);
                        JsonFileService.Save(_bagTemp, tempBag);
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
                        tempBag.RemoveAt(i);

                        // SAVE STATE
                        JsonFileService.Save(_bagTrim, trimBag);
                        JsonFileService.Save(_bagTemp, tempBag);
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