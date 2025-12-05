namespace YoutubeGenerator.Interfaces
{
    public interface IGitHubCsvFetcher
    {
        Task<Stream> DownloadCsvAsync();
        Task ClearCsvAsync();
    }
}
