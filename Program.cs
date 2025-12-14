using Microsoft.AspNetCore.Mvc;
using YoutubeGenerator.crons;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
//using YoutubeGenerator.Orchestrator;
using YoutubeGenerator.Services;
using YoutubeGenerator.workers;
using YoutubeGenerator.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILlmService>(sp =>
{
    var apiKey = builder.Configuration["Google:ApiKey"];
    return new LlmService(apiKey);
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
app.Run();
