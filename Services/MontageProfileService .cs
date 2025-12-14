using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class MontageProfileService : IMontageProfileService
    {
        private readonly ILogger<MontageProfileService> _logger ;
        private readonly string _rootDir;
        private readonly string _kindsDir;
        private readonly string _subKindsDir;

        public MontageProfileService(IWebHostEnvironment env, ILogger<MontageProfileService> logger)
        {
            _rootDir = Path.Combine(env.ContentRootPath, "workspace", "profiles");
            _logger = logger;
            _kindsDir = Path.Combine(_rootDir, "kinds");
            _subKindsDir = Path.Combine(_rootDir, "subkinds");

            Directory.CreateDirectory(_kindsDir);
            Directory.CreateDirectory(_subKindsDir);
            _logger = logger;
        }

        // -----------------------------
        // SUBKIND: MONTAGE SETTINGS
        // -----------------------------
        public MontageProfile GetProfile(string kindKey, string subKindKey)
        {
            string file = Path.Combine(_subKindsDir, $"{kindKey}_{subKindKey}.json");

            if (!File.Exists(file)){
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

            return JsonFileService.Load<YoutubeCredentials>(file);
        }

        public void SaveYoutubeCredentials(string kindKey, YoutubeCredentials creds)
        {
            string file = Path.Combine(_kindsDir, $"{kindKey}.json");
            JsonFileService.Save(file, creds);
        }
    }
}
