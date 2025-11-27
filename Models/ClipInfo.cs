using System.Text.Json.Serialization;

namespace YoutubeGenerator.Models
{
    public class ClipInfo
    {
        [JsonPropertyName("link")]
        public string? Link { get; set; }
        public string? Description { get; set; }
        public string? Start { get; set; }   // ✔ accepte null
        public string? End { get; set; }
    }
}
