namespace YoutubeGenerator.Models
{
    public class GifOverlayTask
    {
        public string GifPath { get; set; }
        public TimeSpan StartAt { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool Vibrate { get; set; }
    }
}
