namespace YoutubeGenerator.Models
{
    public class MontageLLMResult
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }
    // We only need the properties we are requesting
    public class TitleResult
    {
        public string Title { get; set; } = string.Empty;
    }

    public class DescriptionResult
    {
        public string Description { get; set; } = string.Empty;
    }
}
