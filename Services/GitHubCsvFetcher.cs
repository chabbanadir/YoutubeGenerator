using System.Net.Http.Headers;
using System.Text.Json;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Services
{
    public class GitHubCsvFetcher : IGitHubCsvFetcher
    {
        private readonly HttpClient _rawHttp;
        private readonly HttpClient _apiHttp;
        private readonly ILogger<GitHubCsvFetcher> _logger;

        private const string Owner = "chabbanadir";
        private const string Repo = "Youtubecsv";
        private const string FilePath = "video.csv";

        public GitHubCsvFetcher(IConfiguration config, ILogger<GitHubCsvFetcher> logger)
        {
            _logger = logger;

            string token = config["GitHub:Token"];

            // HTTP client pour RAW
            _rawHttp = new HttpClient();
            _rawHttp.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/plain"));
            _rawHttp.DefaultRequestHeaders.UserAgent.ParseAdd("YoutubeGeneratorBot");

            // HTTP client pour API GitHub
            _apiHttp = new HttpClient();
            _apiHttp.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _apiHttp.DefaultRequestHeaders.UserAgent.ParseAdd("YoutubeGeneratorBot");

            if (!string.IsNullOrWhiteSpace(token))
            {
                _apiHttp.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        }

        // Retourne un STRING et plus un stream
        public async Task<string> DownloadCsvAsync(CancellationToken ct = default)
        {
            string url =
                $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/{FilePath}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            _logger.LogWarning($"🌐 Fetching CSV from: {url}");

            var resp = await _rawHttp.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            string content = await resp.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(content))
                _logger.LogWarning("⚠ CSV empty or whitespace");

            return content;
        }

        public async Task ClearCsvAsync()
        {
            string apiUrl =
                $"https://api.github.com/repos/{Owner}/{Repo}/contents/{FilePath}";

            _logger.LogWarning($"🧹 Clearing CSV via API: {apiUrl}");

            var metaResp = await _apiHttp.GetAsync(apiUrl);
            metaResp.EnsureSuccessStatusCode();

            var metaJson = await metaResp.Content.ReadAsStringAsync();
            var meta = JsonSerializer.Deserialize<GitHubMeta>(metaJson);

            string newContent = "Link,Description,Start,End,KindKey,SubKindKey\n";

            var payload = new
            {
                message = "Auto-clear video CSV after sync",
                content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newContent)),
                sha = meta.sha
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var result = await _apiHttp.PutAsync(apiUrl, content);
            result.EnsureSuccessStatusCode();
        }

        private class GitHubMeta
        {
            public string sha { get; set; }
        }
    }
}
