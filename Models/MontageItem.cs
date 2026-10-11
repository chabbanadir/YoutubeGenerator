using System.Text;

namespace YoutubeGenerator.Models
{
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
        public string KindKey { get; set; } = "";
        public string SubKindKey { get; set; } = "";
        public string? GeneratedTitle { get; set; } 
        public string? GeneratedDescription { get; set; } 
        public List<string> GeneratedTags { get; set; } = new();
        public bool Completed { get; set; } = false;

        public string? FinalVideoPath { get; set; }
        public string? GeneratedThumbnailPath { get; set; }


        public string ToPromptString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Kind: {KindKey}");
            sb.AppendLine($"SubKind: {SubKindKey}");
            sb.AppendLine($"Total Duration: {TotalDuration}");
            sb.AppendLine();
            sb.AppendLine("CLIPS:");

            TimeSpan cumulative = TimeSpan.Zero;

            foreach (var clip in Clips)
            {
                sb.AppendLine(
                    $" - {cumulative:mm\\:ss} → \"{clip.Description}\" ({clip.Duration.TotalSeconds:F2}s)"
                );
                //clipsInfo += $" - {cumulative:mm\\:ss} → \"{clip.Description}\" ({clip.Duration.TotalSeconds:F2}s)";
                cumulative += clip.Duration;
            }

            return sb.ToString();
        }
    }
}
