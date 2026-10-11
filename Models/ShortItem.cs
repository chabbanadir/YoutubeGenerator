using System;
using System.Collections.Generic;

namespace YoutubeGenerator.Models
{
    public class ShortItem
    {
        public string ShortId { get; set; } = Guid.NewGuid().ToString("N");
        public string KindKey { get; set; } = "";
        public string SubKindKey { get; set; } = "normal";
        public string Streamer { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string FilePath { get; set; } = "";
        public double DurationSeconds { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ScheduledPublishTime { get; set; }
        public bool IsUploaded { get; set; }
        public string? YoutubeVideoId { get; set; }
        public string? ThumbnailPath { get; set; }
    }
}
