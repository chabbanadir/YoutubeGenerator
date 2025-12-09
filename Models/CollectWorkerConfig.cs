namespace YoutubeGenerator.Models
{
    public class CollectWorkerConfig
    {
        public string BagDownloadPath { get; set; } = "bag_download.json";
        public string BagTempPath { get; set; }  = "bag_download_temp.json";
        public string BagTrimmedPath { get; set; } = "bag_trimmed.json";
        public string BagMontagePath { get; set; } = "bag_montage.json";
        public string BagYoutubePath { get; set; } = "bag_youtube.json";
        public string BagYoutubeFailedPath { get; set; } = "bag_youtube_failed.json";
        public string BagYoutubeDonePath { get; set; } = "bag_youtube_done.json";
        public string BagHistoryPath { get; set; } = "bag_history.json";

        public string BagFailed { get; set; } = "bag_failed.json";
        public string WorkerFile { get; set; } = "worker_collect.json";
        public string MontageFile { get; set; } = "worker_montage.json";
        public string IntroFile { get; set; } = "Intro1.mp4";
        
        // 🌟 Ajoutez ces deux propriétés 🌟
        public int BatchSize { get; set; } = 4;
        public double MinMontageDurationMinutes { get; set; } = 12;
        public string TransitionAnimation { get; set; } = "circlecrop";
        public double TransitionDuration { get; set; } = 1;
    }

}
