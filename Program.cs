using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using YoutubeGenerator.crons;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
//using YoutubeGenerator.Orchestrator;
using YoutubeGenerator.Services;
using YoutubeGenerator.workers;
using YoutubeGenerator.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

builder.Services.AddSingleton<ILlmService>(sp =>
{
    var apiKey = builder.Configuration["OLLAMA_API_KEY"]
                 ?? builder.Configuration["Ollama:ApiKey"]
                 ?? builder.Configuration["OpenRouter:ApiKey"] 
                 ?? builder.Configuration["OPENROUTER_API_KEY"] 
                 ?? "7bef61ad0a9d45a1bc155e823a5a7b9c.bRK0S7_z44bEP3aPCNU_0a-w";

    var model = builder.Configuration["OLLAMA_MODEL"]
                ?? builder.Configuration["Ollama:Model"]
                ?? builder.Configuration["OpenRouter:Model"] 
                ?? builder.Configuration["OPENROUTER_MODEL"] 
                ?? "gemma4:31b";

    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("LlmService");
    return new LlmService(httpClient, apiKey, model);
});
// ----------------------
// Services
// ----------------------
builder.Services.AddScoped<IDownloadService, DownloadService>();
builder.Services.AddScoped<ICsvService, CsvService>();
builder.Services.AddScoped<ITrimService, TrimService>();
builder.Services.AddScoped<ITransitionService, TransitionService>();
builder.Services.AddScoped<IPathService, PathService>();
builder.Services.AddScoped<IMontageProfileService, MontageProfileService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IProfileManagementService, ProfileManagementService>();
builder.Services.AddSingleton<IRenderTrackerService, RenderTrackerService>();
//builder.Services.AddScoped<VideoPipeline>();
builder.Services.AddScoped<IYoutubeUploader, YoutubeUploader>();
builder.Services.AddScoped<IThumbnailService, ThumbnailService>();
builder.Services.AddScoped<IShortsRenderService, ShortsRenderService>();
builder.Services.AddScoped<IShortsSchedulerService, ShortsSchedulerService>();
builder.Services.AddScoped<IYoutubeAuthService, YoutubeAuthService>();
builder.Services.AddScoped<IGitHubCsvFetcher, GitHubCsvFetcher>();
builder.Services.Configure<CollectWorkerConfig>(
    builder.Configuration.GetSection("CollectWorker")
);

// CollectWorker reste SCOPED !
builder.Services.AddScoped<CollectWorker>();
builder.Services.AddScoped<MontageWorker>();
builder.Services.AddScoped<YoutubeWorker>();

// Cron = toujours Singleton
builder.Services.AddHostedService<CronCollectService>();
builder.Services.AddHostedService<CronMontageUploadService>();
builder.Services.AddHostedService<CronShortsUploadService>();
builder.Services.AddHostedService<CronTokenRefreshService>();

// Authentication & Authorization
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "YoutubeGenerator.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// API
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Enable Forwarded Headers for reverse proxies (Nginx / Ngrok / Cloudflare / HTTPS on VPS)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// ----------------------
// Middleware
// ----------------------
app.UseSwagger();   // Serves the OpenAPI specification file (e.g., swagger.json)
app.UseSwaggerUI();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

// Startup self-healing: Reset any stale worker locks from previous crashes or SIGKILLs
try
{
    var bagsDir = Path.Combine(app.Environment.ContentRootPath, "workspace", "bags");
    var collectFile = Path.Combine(bagsDir, "worker_collect.json");
    var montageFile = Path.Combine(bagsDir, "worker_montage.json");

    if (File.Exists(collectFile))
    {
        var w1 = JsonFileService.Load<Worker1Model>(collectFile);
        if (w1 != null && w1.Status)
        {
            w1.Status = false;
            JsonFileService.Save(collectFile, w1);
            app.Logger.LogInformation("🔄 Reset stale worker_collect status to false on startup.");
        }
    }

    if (File.Exists(montageFile))
    {
        var w2 = JsonFileService.Load<Worker2State>(montageFile);
        if (w2 != null && w2.Status)
        {
            w2.Status = false;
            JsonFileService.Save(montageFile, w2);
            app.Logger.LogInformation("🔄 Reset stale worker_montage status to false on startup.");
        }
    }

    var youtubeFile = Path.Combine(bagsDir, "bag_youtube.json");
    var youtubeFailedFile = Path.Combine(bagsDir, "bag_youtube_failed.json");
    if (File.Exists(youtubeFailedFile))
    {
        var ytFailed = JsonFileService.Load<List<MontageItem>>(youtubeFailedFile);
        var ytQueue = File.Exists(youtubeFile) ? JsonFileService.Load<List<MontageItem>>(youtubeFile) : new List<MontageItem>();
        if (ytQueue.Count == 0 && ytFailed != null && ytFailed.Count > 0)
        {
            var recoverable = ytFailed.Where(p => !string.IsNullOrEmpty(p.FinalVideoPath) && File.Exists(p.FinalVideoPath)).ToList();
            if (recoverable.Count > 0)
            {
                foreach (var r in recoverable)
                {
                    ytFailed.Remove(r);
                    ytQueue.Add(r);
                }
                JsonFileService.Save(youtubeFile, ytQueue);
                JsonFileService.Save(youtubeFailedFile, ytFailed);
                app.Logger.LogInformation($"🔄 Startup recovery: moved {recoverable.Count} failed upload(s) back into bag_youtube.json.");
            }
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Failed to check or reset worker status on startup.");
}

app.Run();

