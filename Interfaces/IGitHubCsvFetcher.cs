namespace YoutubeGenerator.Interfaces
{
    public interface IGitHubCsvFetcher
    {
        Task<string> DownloadCsvAsync(CancellationToken ct = default);
        Task ClearCsvAsync();
    }
}
