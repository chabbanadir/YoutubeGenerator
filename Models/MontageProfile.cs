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

       

        // Category & LLM Tuning
        public string Category { get; set; } = "gaming"; // "valorant", "lol", "just_chatting", "meme", "gaming"
        public string? PromptHint { get; set; }
        public List<string>? DefaultTags { get; set; }
        public List<string>? CustomHashtags { get; set; }

        //// LLM templates
        public string? FacebookLink { get; set; } 
        public string? InstagramLink { get; set; } 
        public string? TiktokLink { get; set; } 
    }

    public class UploadConfig
    {
        public double MinDelayHours { get; set; } = 8.0;
        public double ShortsCadenceMinutes { get; set; } = 60.0;
        public double LongVideoCadenceHours { get; set; } = 8.0;
        public int MaxDailyShorts { get; set; } = 6;
        public int? ActiveHoursStartUtc { get; set; } = 8;
        public int? ActiveHoursEndUtc { get; set; } = 23;
        public DateTime LastUploadTime { get; set; }
        public int UploadCounter { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class YoutubeCredentials
    {
        public string KindKey { get; set; } = "";
        public string Category { get; set; } = "gaming"; // "valorant", "lol", "just_chatting", "meme", "gaming"
        public string? PromptHint { get; set; }
        public List<string>? DefaultTags { get; set; }
        public List<string>? CustomHashtags { get; set; }
        public string YoutubeChannelId { get; set; } = "";
        public string YoutubeClientId { get; set; } = "";
        public string YoutubeClientSecret { get; set; } = "";
        public string YoutubeRefreshToken { get; set; } = "";
        public string YoutubeChannelTitle { get; set; } = "";
        public string? TokenStatus { get; set; }
        public DateTime? LastTokenRefreshUtc { get; set; }
        public UploadConfig UploadConfig { get; set; } = new();
    }
}
