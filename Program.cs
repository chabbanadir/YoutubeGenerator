using Microsoft.AspNetCore.Mvc;
using YoutubeGenerator.crons;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
//using YoutubeGenerator.Orchestrator;
using YoutubeGenerator.Services;
using YoutubeGenerator.workers;
using YoutubeGenerator.Workers;

var builder = WebApplication.CreateBuilder(args);

// ----------------------
// Services
// ----------------------
builder.Services.AddScoped<IDownloadService, DownloadService>();
builder.Services.AddScoped<ICsvService, CsvService>();
builder.Services.AddScoped<ITrimService, TrimService>();
builder.Services.AddScoped<ITransitionService, TransitionService>();


//builder.Services.AddScoped<VideoPipeline>();

builder.Services.Configure<CollectWorkerConfig>(
    builder.Configuration.GetSection("CollectWorker")
);

// CollectWorker reste SCOPED !
builder.Services.AddScoped<CollectWorker>();
builder.Services.AddScoped<MontageWorker>();

// Cron = toujours Singleton
builder.Services.AddHostedService<CronCollectService>();

// API
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ----------------------
// Middleware
// ----------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();
