using System.Text.Json;
using System.Net.Http.Headers;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Services
{
    public class GitHubCsvFetcher : IGitHubCsvFetcher
    {
        private readonly HttpClient _http;
        private readonly ILogger<GitHubCsvFetcher> _logger;

        private const string Owner = "chabbanadir";
        private const string Repo = "Youtubecsv";
        private const string FilePath = "video.csv";

        public GitHubCsvFetcher(IConfiguration config, ILogger<GitHubCsvFetcher> logger)
        {
            _logger = logger;

            _http = new HttpClient();

            // Load token
            string token = config["GitHub:Token"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token); // correct
            }

            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YoutubeGeneratorBot");
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public async Task<Stream> DownloadCsvAsync()
        {
            var url = $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/{FilePath}";
            _logger.LogWarning($"🌐 Fetching CSV from: {url}");

            var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadAsStreamAsync();
        }

        public async Task ClearCsvAsync()
        {
            string apiUrl =
                $"https://api.github.com/repos/{Owner}/{Repo}/contents/{FilePath}";

            _logger.LogWarning($"🧹 Clearing CSV via API: {apiUrl}");

            // 1️⃣ Get current file SHA
            var metaResp = await _http.GetAsync(apiUrl);
            metaResp.EnsureSuccessStatusCode();

            var metaJson = await metaResp.Content.ReadAsStringAsync();
            var meta = JsonSerializer.Deserialize<GitHubMeta>(metaJson);

            // 2️⃣ Build new CSV content
            var newContent = "Link,Description,Start,End,KindKey,SubKindKey\n";

            var payload = new
            {
                message = "Auto-clear video CSV after sync",
                content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newContent)),
                sha = meta.sha
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // 3️⃣ PUT update to GitHub
            var result = await _http.PutAsync(apiUrl, content);
            result.EnsureSuccessStatusCode();
        }

        private class GitHubMeta
        {
            public string sha { get; set; }
        }
    }
}
