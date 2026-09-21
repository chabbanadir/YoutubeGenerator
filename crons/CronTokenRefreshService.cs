using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.crons
{
    public class CronTokenRefreshService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CronTokenRefreshService> _logger;

        public CronTokenRefreshService(IServiceScopeFactory scopeFactory, ILogger<CronTokenRefreshService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CronTokenRefreshService initialized (Interval: every 24 hours / check before 7-day expiration).");

            // Initial check 30 seconds after startup
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation($"## CRON Token Refresh : Time check = {DateTime.UtcNow} UTC");
                    using var scope = _scopeFactory.CreateScope();
                    var authService = scope.ServiceProvider.GetRequiredService<IYoutubeAuthService>();
                    await authService.RefreshAllTokensAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error during CronTokenRefreshService execution.");
                }

                // Run every 24 hours to ensure tokens stay validated well within the 7-day period
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}
