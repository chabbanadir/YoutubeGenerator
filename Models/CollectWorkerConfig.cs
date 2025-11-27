namespace YoutubeGenerator.Models
{
    public class CollectWorkerConfig
    {
        public string BagDownloadPath { get; set; } = "bag_download.json";
        public string BagTempPath { get; set; }  = "bag_download_temp.json";
        public string BagTrimmedPath { get; set; } = "bag_trimmed.json";
        public string BagMontagePath { get; set; } = "bag_montage.json";
        public string TimestampsFile { get; set; } = "bag_timestamps.json";
        public string BagFailed { get; set; } = "bag_failed.json";
        public string WorkerFile { get; set; } = "worker_collect.json";
        public string MontageFile { get; set; } = "worker_montage.json";
        public string IntroFile { get; set; } = "Intro1.mp4";
        // 🌟 Ajoutez ces deux propriétés 🌟
        public int BatchSize { get; set; } = 12;
        public double MaxMontageDurationMinutes { get; set; } = 12;
    }

}
