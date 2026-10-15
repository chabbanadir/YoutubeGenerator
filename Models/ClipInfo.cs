using System.Text.Json.Serialization;

namespace YoutubeGenerator.Models
{
    public class ClipInfo
    {
        [JsonPropertyName("Link")]
        public string? Link { get; set; }

        [JsonPropertyName("Description")]
        public string? Description { get; set; }

        [JsonPropertyName("Start")]
        public string? Start { get; set; }   // ✔ accepte null

        [JsonPropertyName("End")]
        public string? End { get; set; }

        [JsonPropertyName("KindKey")]
        public string KindKey { get; set; } = "default";

        [JsonPropertyName("SubKindKey")]
        public string SubKindKey { get; set; } = "default";

        [JsonPropertyName("IsShort")]
        public bool IsShort { get; set; } = false;

        [JsonPropertyName("ShortLayout")]
        public string ShortLayout { get; set; } = "ambient";

        [JsonPropertyName("CatchStart")]
        public string? CatchStart { get; set; }

        [JsonPropertyName("CatchAccelerate")]
        public bool CatchAccelerate { get; set; } = false;

        public DateTime? ImportedAt { get; set; } = DateTime.UtcNow;

        public string UniqueKey()
        {
            return $"{Link}::{KindKey}::{SubKindKey}".ToLower();
        }
    }
}
/*
 {
  "link": "...",
  "description": "Gosu",
  "start": "",
  "end": "",
  "kindKey": "lol",
  "subKindKey": "penta"
}
 */