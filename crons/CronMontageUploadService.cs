using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YoutubeGenerator.workers;
using YoutubeGenerator.Workers;

namespace YoutubeGenerator.crons
{
    public class CronMontageUploadService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CronCollectService> _logger;

        public CronMontageUploadService(IServiceScopeFactory scopeFactory, ILogger<CronCollectService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CronCollectService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation($"## CRON Montage & Upload : Time check = {DateTime.UtcNow}");
                using var scope = _scopeFactory.CreateScope(); // ✅ un nouveau scope

                var montageWorker = scope.ServiceProvider.GetRequiredService<MontageWorker>();
                var youtubeWorker = scope.ServiceProvider.GetRequiredService<YoutubeWorker>();

                await montageWorker.RunAsync(stoppingToken); // appel du worker !

                await youtubeWorker.RunAsync(); // appel du worker !
                await Task.Delay(TimeSpan.FromHours(2), stoppingToken);
                
                // await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);


            }
        }
    }
}
