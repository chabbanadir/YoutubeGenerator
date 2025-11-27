namespace YoutubeGenerator.Models
{
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } // Valid only if Success == true
        public string ErrorMessage { get; set; } // Valid only if Success == false
        public string ErrorType { get; set; } // e.g., "HTTP_416", "GeoBlock", etc.
    }
}