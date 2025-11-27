namespace YoutubeGenerator.Models
{
    public class DownloadItem
    {
        public string Id { get; set; }
        public string Link { get; set; }
        public string Description { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public DateTime AddedAt { get; set; }
    }

}
