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