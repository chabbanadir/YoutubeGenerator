namespace YoutubeGenerator.Models
{
    public class TrimResult
    {
        public string File { get; set; } = "";
        public string? Start { get; set; }
        public string? End { get; set; }
        public Double Duration { get; set; }
        public string? Description { get; set; }
    }
    public class MontageClip
    {
        public string File { get; set; } = "";
        public string? Description { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class MontageItem
    {
        public string MontageId { get; set; }
        public TimeSpan TotalDuration { get; set; } = TimeSpan.Zero;
        public List<MontageClip> Clips { get; set; } = new();
    }
}
