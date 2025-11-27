using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.workers
{
    public class MontageWorker
    {
        private readonly ILogger<MontageWorker> _logger;
        private readonly ITransitionService _transitionService;
        private readonly ITrimService  _trimService;
        private readonly IHostApplicationLifetime _appLifetime;

        private readonly string _dirMontage;
        private readonly string _dirBags;
        private readonly string _dirResources;
        private readonly string _dirTrim;

        private readonly string _workerFile;
        private readonly string _bagMontage;
        private readonly string _introFile;
        private readonly string _timestamps;
        public MontageWorker(
            ILogger<MontageWorker> logger,
            ITransitionService transitionService,
            ITrimService trimService,
            IOptions<CollectWorkerConfig> cfg,
            IWebHostEnvironment env,
            IHostApplicationLifetime appLifetime)
        {
            // Constructor Services
            _logger = logger;
            _transitionService = transitionService;
            _trimService = trimService;
            _appLifetime = appLifetime;


            // Constructor environment paths
            var cfgValue = cfg.Value;
            var root = Path.Combine(env.ContentRootPath, "workspace");
            
            _dirResources = Path.Combine(root, "ressources");
            _dirBags = Path.Combine(root, "bags");
            _dirMontage = Path.Combine(root, "jobs", "montage");
            _dirTrim = Path.Combine(root, "jobs", "trim");
            Directory.CreateDirectory(_dirMontage);

            _workerFile = Path.Combine(_dirBags, cfgValue.MontageFile);
            _bagMontage = Path.Combine(_dirBags, cfgValue.BagMontagePath);
            _introFile = Path.Combine(_dirResources, cfgValue.IntroFile);

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
        public async Task RunAsync(CancellationToken stoppingToken)
        {
            var worker = Load<Worker2State>(_workerFile);
            if (worker.Status)
                return;

            worker.Status = true;
            Save(_workerFile, worker);

            try
            {
                var montageBag = Load<List<MontageItem>>(_bagMontage);
                if (montageBag.Count == 0 )
                {
                    _logger.LogInformation("Montage bag is empty. No video to process.");
                    return;
                }
                if (montageBag.Count == 1)
                {
                    var packageTest = montageBag.First();
                    if (packageTest.TotalDuration < TimeSpan.FromMinutes(12))
                    {
                        _logger.LogInformation($"Video duration isnt enough to create a montage Total duration = {packageTest.TotalDuration} < 12 min");
                        return;
                    }
                }
                //var packageToRender = montageBag.First();

                var packageToRender = montageBag.First();
                _logger.LogInformation($"Rendering package: {packageToRender.MontageId} ({packageToRender.Clips.Count} clips)");

                string introPath = Path.Combine(_dirResources, _introFile);
                _logger.LogInformation($"Intro full path '{introPath}'");

                string finalOutputPath = Path.Combine(_dirMontage, $"{packageToRender.MontageId}.mp4");

                List<string> videoPaths = new List<string>();

                string firstClipPath = packageToRender.Clips.First().File;

                await _trimService.PrependIntroAsync(
                    introPath,
                    firstClipPath,
                    _dirTrim
                    );

                videoPaths.Add(firstClipPath);
                videoPaths.AddRange(packageToRender.Clips.Skip(1).Select(c => c.File));

                bool success = await _transitionService.MergeVideosWithTransitionsAsync(
                    videoPaths,
                    finalOutputPath,
                    "fade",
                    2
                    );
                if (success)
                {
                    // 6. NETTOYAGE DES FICHIERS TRIMÉS SOURCES
                    foreach (var clip in packageToRender.Clips)
                    {
                        if (File.Exists(clip.File))
                        {
                            File.Delete(clip.File);
                            _logger.LogInformation($"🗑️ Fichier trimé supprimé après montage: {clip.File}");
                        }
                    }
                    var temp  = Path.Combine(_dirMontage, $"{packageToRender.MontageId}.json");
                    // 7. Retirer le paquet rendu du bag
                    Save(temp, packageToRender.Clips);
                    montageBag.Remove(packageToRender);
                    Save(_bagMontage, montageBag);
                }

                _logger.LogInformation($"✅ Rendu et nettoyage du paquet {packageToRender.MontageId} terminé. Vidéo finale: {finalOutputPath}");
                worker.Status = false;
                worker.FinalCounter += 1;
                Save(_workerFile, worker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during montage worker execution.");
                worker.Status = false;
                Save(_workerFile, worker);
                throw;
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
