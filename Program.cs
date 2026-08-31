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
//builder.Services.AddScoped<VideoPipeline>();
builder.Services.AddScoped<IYoutubeUploader, YoutubeUploader>();
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

// API
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));
app.Run();
