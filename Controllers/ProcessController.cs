//using Microsoft.AspNetCore.Hosting; // Nécessaire pour IWebHostEnvironment
//using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Logging;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Threading.Tasks;
//using YoutubeGenerator.Interfaces;
//using YoutubeGenerator.Models;
//using YoutubeGenerator.Orchestrator;

//namespace YoutubeGenerator.Controllers
//{
//    [ApiController]
//    [Route("[controller]")]
//    public class ProcessController : ControllerBase
//    {
//        private readonly ILogger<ProcessController> _logger;
//        private readonly IDownloadService _downloadService;
//        private readonly ICsvService _csvService;
//        private readonly VideoPipeline _videoPipeline;
//        private readonly IWebHostEnvironment _env; // 1. On déclare l'environnement

//        public ProcessController(
//            ILogger<ProcessController> logger,
//            IDownloadService downloadService,
//            ICsvService csvService,
//            VideoPipeline videoPipeline,
//            IWebHostEnvironment env) // 2. On l'injecte ici
//        {
//            _logger = logger;
//            _downloadService = downloadService;
//            _csvService = csvService;
//            _videoPipeline = videoPipeline;
//            _env = env;
//        }

//        [HttpPost]
//        public async Task<IActionResult> Post([FromForm] ProcessRequestDto request)
//        {
//            if (request.File == null || request.File.Length == 0)
//            {
//                return BadRequest("Aucun fichier CSV fourni.");
//            }

//            // 3. On construit le chemin de manière portable
//            // ContentRootPath pointe vers la racine de ton projet (là où est le .csproj)
//            string workspacePath = Path.Combine(_env.ContentRootPath, "workspace", "jobs");
//            string ressourcesPath = Path.Combine(_env.ContentRootPath, "workspace", "ressources");
//            string gifPath = Path.Combine(ressourcesPath,"sub.gif");
//            string jobId = Guid.NewGuid().ToString();
//            string jobFolder = Path.Combine(workspacePath, jobId);

//            // On s'assure que le dossier "workspace/jobs" existe, sinon le CreateDirectory du pipeline pourrait échouer s'il manque des parents
//            if (!Directory.Exists(workspacePath))
//            {
//                Directory.CreateDirectory(workspacePath);
//            }

//            _logger.LogInformation("Job {JobId} commencé. Dossier: {JobFolder}", jobId, jobFolder);

//            try
//            {
//                List<ClipInfo> clipsFile = await _csvService.ParseCsvAsync(request.File.OpenReadStream());
//                var result = await _videoPipeline.ExecuteAsync(jobFolder, ressourcesPath, gifPath, clipsFile, request.opt);

//                return Ok(new
//                {
//                    JobId = jobId,
//                    Message = "Traitement terminé avec succès",
//                    ResultPaths = result
//                });
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Erreur pendant le traitement du job {JobId}", jobId);
//                return StatusCode(500, $"Erreur interne: {ex.Message}");
//            }
//        }
//    }
//}