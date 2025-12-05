namespace YoutubeGenerator.Models
{
    public class MontageProfile
    {
        public string KindKey { get; set; } = "";
        public string SubKindKey { get; set; } = "";

        // Editing settings
        public string IntroFile { get; set; } = "";
        public string TransitionAnimation { get; set; } = "fade";
        public double TransitionDuration { get; set; } = 2.0;
        public double MinMontageMinutes { get; set; } = 12.0;

       

        //// LLM templates
        public string? FacebookLink { get; set; } 
        public string? InstagramLink { get; set; } 
        public string? TiktokLink { get; set; } 
    }
    public class UploadConfig
    {
        public double MinDelayHours { get; set; }
        public DateTime LastUploadTime { get; set; }
        public int UploadCounter { get; set; }
        public bool Enabled { get; set; } = true;
    }
    public class YoutubeCredentials
    {
        public string YoutubeChannelId { get; set; } = "";
        public string YoutubeClientId { get; set; } = "";
        public string YoutubeClientSecret { get; set; } = "";
        public string YoutubeRefreshToken { get; set; } = "";
        public UploadConfig UploadConfig { get; set; } = new();

    }
}
