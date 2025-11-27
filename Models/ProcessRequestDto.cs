using Microsoft.AspNetCore.Http;

namespace YoutubeGenerator.Models
{
    public class ProcessRequestDto
    {
        // IFormFile est l'interface magique pour les fichiers uploadés
        public IFormFile File { get; set; }
        public PipelineOptions opt { get; set; }
    }
}