using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class MontageProfileService : IMontageProfileService
    {
        private readonly ILogger<MontageProfileService> _logger;
        private readonly IConfiguration _config;
        private readonly string _rootDir;
        private readonly string _kindsDir;
        private readonly string _subKindsDir;

        public MontageProfileService(
            IWebHostEnvironment env,
            ILogger<MontageProfileService> logger,
            IConfiguration config)
        {
            _rootDir = Path.Combine(env.ContentRootPath, "workspace", "profiles");
            _logger = logger;
            _config = config;
            _kindsDir = Path.Combine(_rootDir, "kinds");
            _subKindsDir = Path.Combine(_rootDir, "subkinds");

            Directory.CreateDirectory(_kindsDir);
            Directory.CreateDirectory(_subKindsDir);
        }

        // -----------------------------
        // SUBKIND: MONTAGE SETTINGS
        // -----------------------------
        public MontageProfile GetProfile(string kindKey, string subKindKey)
        {
            string file = Path.Combine(_subKindsDir, $"{kindKey}_{subKindKey}.json");

            if (!File.Exists(file))
            {
                _logger.LogInformation($"Missing profile for {kindKey}/{subKindKey}");
                string normal = Path.Combine(_subKindsDir, $"{kindKey}_normal.json");
                return JsonFileService.Load<MontageProfile>(normal);
            }
            return JsonFileService.Load<MontageProfile>(file);
        }

        // --------------------------------
        // KIND: YOUTUBE UPLOAD CREDENTIALS
        // --------------------------------
        public YoutubeCredentials GetYoutubeCredentials(string kindKey)
        {
            string file = Path.Combine(_kindsDir, $"{kindKey}.json");

            if (!File.Exists(file))
                throw new Exception($"Missing YouTube credentials for {kindKey}");

            var creds = JsonFileService.Load<YoutubeCredentials>(file);

            if (string.IsNullOrWhiteSpace(creds.YoutubeClientId))
            {
                creds.YoutubeClientId = _config["YouTube:ClientId"] 
                                     ?? _config["YOUTUBE_CLIENT_ID"] 
                                     ?? Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_ID") 
                                     ?? "";
            }

            if (string.IsNullOrWhiteSpace(creds.YoutubeClientSecret))
            {
                creds.YoutubeClientSecret = _config["YouTube:ClientSecret"] 
                                         ?? _config["YOUTUBE_CLIENT_SECRET"] 
                                         ?? Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_SECRET") 
                                         ?? "";
            }

            return creds;
        }

        public void SaveYoutubeCredentials(string kindKey, YoutubeCredentials creds)
        {
            string file = Path.Combine(_kindsDir, $"{kindKey}.json");
            JsonFileService.Save(file, creds);
        }
    }
}
