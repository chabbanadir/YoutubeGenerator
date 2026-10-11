using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YoutubeGenerator.workers;
using YoutubeGenerator.Workers;

namespace YoutubeGenerator.crons
{
    public class CronCollectService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CronCollectService> _logger;

        public CronCollectService(IServiceScopeFactory scopeFactory, ILogger<CronCollectService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CronCollectService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation($"## CRON Download & Trim : Time check = {DateTime.UtcNow}");
                using var scope = _scopeFactory.CreateScope(); // ✅ un nouveau scope

                var collectWorker = scope.ServiceProvider.GetRequiredService<CollectWorker>();
               
                await collectWorker.RunAsync(stoppingToken); // appel du worker !

                // Check for new clips in GitHub CSV every 1 minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);


            }
        }
    }
}
