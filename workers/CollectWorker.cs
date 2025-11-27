using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Workers
{
    public class CollectWorker
    {
        private readonly IDownloadService _download;
        private readonly ITrimService _trim;
        private readonly ITransitionService _transition;
        private readonly ILogger<CollectWorker> _logger;
        private readonly string _dirRaw;
        private readonly string _dirTrim;
        private readonly string _dirBags;
        private readonly IHostApplicationLifetime _appLifetime;

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
          IHostApplicationLifetime appLifetime)
        {
            _download = download;
            _trim = trim;
            _transition = transition;
            _logger = logger;
            _appLifetime = appLifetime;

            var cfgValue = cfg.Value;
            n = cfgValue.BatchSize;
            
            // Construction des chemins (simplifiée)
            var root = Path.Combine(env.ContentRootPath, "workspace");
            _dirRaw = Path.Combine(root, "jobs", "raw");
            _dirTrim = Path.Combine(root, "jobs", "trim");
            _dirBags = Path.Combine(root, "bags");

            Directory.CreateDirectory(_dirRaw);
            Directory.CreateDirectory(_dirTrim);
            Directory.CreateDirectory(_dirBags);

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
                var worker = Load<Worker1Model>(_workerFile);
                if (worker.Status)
                {
                    worker.Status = false;
                    Save(_workerFile, worker);
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
            var worker = Load<Worker1Model>(_workerFile);
            if (worker.Status)
                return;

            worker.Status = true;
            Save(_workerFile, worker);

            try
            {
                // 1. TENTATIVE DE REMPLISSAGE (Phase 0)
                var tempBag = Load<List<ClipInfo>>(_bagTemp);

                if (tempBag.Count == 0)
                {
                    var downloadBag = Load<List<ClipInfo>>(_bagSource);
                    if (downloadBag.Count > 0)
                    {
                        var itemsToMove = downloadBag.Take(n).ToList();
                        tempBag.AddRange(itemsToMove);
                        downloadBag = downloadBag.Skip(n).ToList();
                        Save(_bagTemp, tempBag);
                        Save(_bagSource, downloadBag);
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

                var trimBag = Load<List<TrimResult>>(_bagTrim);
                // Load the failed bag
                var failedBag = Load<List<ClipInfo>>(_bagFailed);
                for (int i = tempBag.Count - 1; i >= 0; i--)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var clip = tempBag[i];
                    _logger.LogInformation($"🚀 Processing clip {i}: {clip.Link}");

                    // --- CHANGED LOGIC HERE ---

                    // 1. Call Download and get Result object
                    var downloadResult = await _download.DownloadAsync(clip, _dirRaw, ++worker.DownloadCounter);

                    if (!downloadResult.Success)
                    {
                        _logger.LogWarning($"⚠️ Download failed for clip {i}. Reason: {downloadResult.ErrorType}. Moving to Failed Bag.");

                        // 2. Add specific error info to the clip description or a new field if you want debugging later
                        clip.Description += $" [FAILED: {downloadResult.ErrorType}]";

                        // 3. Move to Failed Bag
                        failedBag.Add(clip);
                        tempBag.RemoveAt(i); // Remove from active queue

                        // 4. Save Immediately
                        Save(_bagFailed, failedBag);
                        Save(_bagTemp, tempBag);

                        // Continue to next clip, do NOT try to trim
                        continue;
                    }

                    // If we get here, download was successful
                    string rawFilePath = downloadResult.FilePath;

                    try
                    {
                        // 5. Proceed with Trim using the valid path
                        var trimResult = await _trim.TrimAsync(rawFilePath, clip, _dirTrim, ++worker.TrimCounter);

                        // ... Rest of your existing trim logic ...
                        await _trim.NormalizeOneAsync(rawFilePath);
                        double duration = await _transition.GetVideoDurationAsync(trimResult.File);

                        trimResult.Duration = duration;
                        trimBag.Add(trimResult);

                        if (File.Exists(rawFilePath)) File.Delete(rawFilePath);

                        tempBag.RemoveAt(i);
                        Save(_bagTrim, trimBag);
                        Save(_bagTemp, tempBag);
                        Save(_workerFile, worker);
                    }
                    catch (Exception ex)
                    {
                        // This catch block now handles TRIM errors, not download errors
                        _logger.LogError(ex, $"🚨 Trim/Process failed for clip {i}.");
                    }
                    finally
                    {
                        // AJOUTER CE BLOC FINALLY pour garantir la suppression du fichier brut 
                        // MÊME en cas d'échec du trim/normalisation (sauf si vous voulez le conserver pour debug)
                        if (File.Exists(rawFilePath))
                        {
                            File.Delete(rawFilePath);
                            _logger.LogInformation($"🗑️ Fichier brut {rawFilePath} supprimé après traitement.");
                        }
                    }
                }


                // --------------------------------------------------------
                // PHASE 2 : ORCHESTRATION DU MONTAGE
                // --------------------------------------------------------
                if (trimBag.Count > 0)
                {
                    var montageBag = Load<List<MontageItem>>(_bagMontage);
                    TimeSpan maxMontageDuration = TimeSpan.FromMinutes(12);

                    for (int i = trimBag.Count - 1; i >= 0; i--)
                    {
                        var clipToPackage = trimBag[i];
                        TimeSpan clipDuration = TimeSpan.FromSeconds(clipToPackage.Duration);

                        var currentPackage = montageBag.LastOrDefault();
                        bool mustCreateNew = currentPackage == null || (currentPackage.TotalDuration + clipDuration) > maxMontageDuration;

                        if (mustCreateNew)
                        {
                            currentPackage = new MontageItem
                            {
                                MontageId = Guid.NewGuid().ToString(),
                                TotalDuration = TimeSpan.Zero
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
                        _logger.LogInformation($"➡️ Clip ajouté au paquet {currentPackage.MontageId}. Nouvelle durée: {currentPackage.TotalDuration:mm\\:ss}");

                        // Nettoyage et Sauvegarde
                        trimBag.RemoveAt(i);
                        Save(_bagMontage, montageBag);
                        Save(_bagTrim, trimBag);
                    } // Fin de la boucle trimBag
                } // Fin du if (trimBag.Count > 0)
            }
            finally
            {
                worker.Status = false;
                Save(_workerFile, worker);
            }
        }
        private T Load<T>(string file)
        {
            if (!File.Exists(file))
                return Activator.CreateInstance<T>();

            var json = File.ReadAllText(file);
            return string.IsNullOrWhiteSpace(json)
                ? Activator.CreateInstance<T>()
                : JsonSerializer.Deserialize<T>(json)!;
        }
        private void Save<T>(string file, T obj)
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
    }
}