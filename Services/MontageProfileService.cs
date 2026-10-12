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
            var safeKind = string.IsNullOrWhiteSpace(kindKey) ? "lol" : kindKey.Trim().ToLower();
            var safeSubKind = string.IsNullOrWhiteSpace(subKindKey) ? "normal" : subKindKey.Trim().ToLower();

            string file = Path.Combine(_subKindsDir, $"{safeKind}_{safeSubKind}.json");

            if (!File.Exists(file))
            {
                _logger.LogInformation($"🔍 Subkind profile not found for '{safeKind}/{safeSubKind}'. Auto-creating default profile as a copy of '{safeKind}_normal.json'...");

                MontageProfile newProfile;
                string normalFile = Path.Combine(_subKindsDir, $"{safeKind}_normal.json");

                if (File.Exists(normalFile))
                {
                    var normal = JsonFileService.Load<MontageProfile>(normalFile);
                    newProfile = new MontageProfile
                    {
                        KindKey = safeKind,
                        SubKindKey = safeSubKind,
                        IntroFile = normal.IntroFile,
                        TransitionAnimation = normal.TransitionAnimation,
                        TransitionDuration = normal.TransitionDuration,
                        MinMontageMinutes = normal.MinMontageMinutes,
                        FacebookLink = normal.FacebookLink,
                        InstagramLink = normal.InstagramLink,
                        TiktokLink = normal.TiktokLink
                    };
                }
                else
                {
                    string defaultIntro = (safeKind.Contains("val") || safeKind == "gaming")
                        ? "valo_intro.mp4"
                        : $"intro_{safeKind}.mp4";

                    newProfile = new MontageProfile
                    {
                        KindKey = safeKind,
                        SubKindKey = safeSubKind,
                        IntroFile = defaultIntro,
                        TransitionAnimation = "fade",
                        TransitionDuration = 1.0,
                        MinMontageMinutes = 12.0
                    };
                }

                try
                {
                    JsonFileService.Save(file, newProfile);
                    _logger.LogInformation($"✅ Created and saved default profile for {safeKind}/{safeSubKind} at {file}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"🚨 Failed to persist auto-created profile to {file}");
                }

                return newProfile;
            }

            return JsonFileService.Load<MontageProfile>(file);
        }

        public MontageProfile EnsureProfileExists(string kindKey, string subKindKey)
        {
            return GetProfile(kindKey, subKindKey);
        }

        // --------------------------------
        // KIND: YOUTUBE UPLOAD CREDENTIALS
        // --------------------------------
        public YoutubeCredentials GetYoutubeCredentials(string kindKey)
        {
            var safeKey = string.IsNullOrWhiteSpace(kindKey) ? "lol" : kindKey.Trim().ToLower();
            if (safeKey == "valo") safeKey = "valorant";

            string file = Path.Combine(_kindsDir, $"{safeKey}.json");

            if (!File.Exists(file))
            {
                // Fallback aliases: if gaming, highlight, or valo, try valorant.json
                if (safeKey == "gaming" || safeKey == "highlight" || safeKey.Contains("val"))
                {
                    var valFile = Path.Combine(_kindsDir, "valorant.json");
                    if (File.Exists(valFile))
                    {
                        file = valFile;
                    }
                }
                else
                {
                    var lolFile = Path.Combine(_kindsDir, "lol.json");
                    if (File.Exists(lolFile))
                    {
                        file = lolFile;
                    }
                }
            }

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
