//using Microsoft.AspNetCore.Hosting; // Nécessaire pour IWebHostEnvironment
//using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Logging;
//using System;
//using YoutubeGenerator.Workers;

//namespace YoutubeGenerator.Controllers
//{
//    [ApiController]
//    [Route("api/admin")]
//    public class AdminController : ControllerBase
//    {
//        private readonly CollectWorker _collect;
//        public AdminController(CollectWorker collect) { _collect = collect; }
//        [HttpPost("refresh-csv")]
//        public async Task<IActionResult> RefreshCsv(CancellationToken ct)
//        {
//            await _collect.RunAsync(ct); // attention: si RunAsync est scoped, utiliser un orchestrateur/service public
//            return Ok();
//        }
//    }
//}