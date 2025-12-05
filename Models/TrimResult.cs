using System.Text;

namespace YoutubeGenerator.Models
{
    public class TrimResult
    {
        public string File { get; set; } = "";
        public string? Start { get; set; }
        public string? End { get; set; }
        public Double Duration { get; set; }
        public string? Description { get; set; }
        public string KindKey { get; set; } = "default";    // "lol"
        public string SubKindKey { get; set; } = "default"; // "penta"
    }
    
}
