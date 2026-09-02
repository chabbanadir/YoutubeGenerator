using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Services
{
    public class ProfileManagementService : IProfileManagementService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ProfileManagementService> _logger;
        private readonly IConfiguration _config;
        private readonly string _kindsDir;
        private readonly string _subkindsDir;

        private static readonly string[] SensitiveKeys = new[]
        {
            "token", "secret", "apikey", "password", "auth", "credential", "privatekey", "api_key", "private_key"
        };

        public ProfileManagementService(
            IWebHostEnvironment env,
            ILogger<ProfileManagementService> logger,
            IConfiguration config)
        {
            _env = env;
            _logger = logger;
            _config = config;

            var rootPath = Path.Combine(_env.ContentRootPath, "workspace");
            var profilesDir = Path.Combine(rootPath, "profiles");
            _kindsDir = Path.Combine(profilesDir, "kinds");
            _subkindsDir = Path.Combine(profilesDir, "subkinds");

            Directory.CreateDirectory(_kindsDir);
            Directory.CreateDirectory(_subkindsDir);
        }

        public object GetProfiles(bool isAuthenticated)
        {
            var kindsList = new List<object>();
            if (Directory.Exists(_kindsDir))
            {
                var kindFiles = Directory.GetFiles(_kindsDir, "*.json");
                foreach (var file in kindFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var name = Path.GetFileNameWithoutExtension(file);
                    var rawJson = File.ReadAllText(file);
                    object? parsedData = null;
                    bool isRedacted = false;

                    try
                    {
                        if (!isAuthenticated)
                        {
                            var node = JsonNode.Parse(rawJson);
                            SanitizeNode(node);
                            var sanitized = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
                            parsedData = JsonSerializer.Deserialize<object>(sanitized);
                            rawJson = sanitized;
                            isRedacted = true;
                        }
                        else
                        {
                            parsedData = JsonSerializer.Deserialize<object>(rawJson);
                        }
                    }
                    catch
                    {
                        parsedData = new { error = "Invalid JSON structure" };
                    }

                    kindsList.Add(new
                    {
                        name,
                        fileName,
                        rawJson,
                        data = parsedData,
                        isRedacted
                    });
                }
            }

            var subkindsList = new List<object>();
            if (Directory.Exists(_subkindsDir))
            {
                var subkindFiles = Directory.GetFiles(_subkindsDir, "*.json");
                foreach (var file in subkindFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var name = Path.GetFileNameWithoutExtension(file);
                    var rawJson = File.ReadAllText(file);
                    object? parsedData = null;

                    try
                    {
                        parsedData = JsonSerializer.Deserialize<object>(rawJson);
                    }
                    catch
                    {
                        parsedData = new { error = "Invalid JSON structure" };
                    }

                    subkindsList.Add(new
                    {
                        name,
                        fileName,
                        rawJson,
                        data = parsedData
                    });
                }
            }

            return new
            {
                isAuthenticated,
                kinds = kindsList,
                subkinds = subkindsList
            };
        }

        public object CreateSubKindProfile(JsonElement content)
        {
            string kindKey = "";
            string subKindKey = "";

            if (content.ValueKind == JsonValueKind.Object)
            {
                if (content.TryGetProperty("KindKey", out var kProp) ||
                    content.TryGetProperty("kindKey", out kProp) ||
                    content.TryGetProperty("kind_key", out kProp))
                {
                    kindKey = kProp.GetString() ?? "";
                }

                if (content.TryGetProperty("SubKindKey", out var skProp) ||
                    content.TryGetProperty("subKindKey", out skProp) ||
                    content.TryGetProperty("sub_kind_key", out skProp))
                {
                    subKindKey = skProp.GetString() ?? "";
                }
            }

            if (string.IsNullOrWhiteSpace(kindKey) || string.IsNullOrWhiteSpace(subKindKey))
            {
                throw new ArgumentException("Both 'KindKey' and 'SubKindKey' are required to create a SubKind profile.");
            }

            string fileName = $"{kindKey.ToLowerInvariant()}_{subKindKey.ToLowerInvariant()}.json";
            string fullPath = Path.Combine(_subkindsDir, fileName);

            string formattedJson = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fullPath, formattedJson);

            _logger.LogInformation($"Created subkind profile '{fileName}'.");

            return new
            {
                success = true,
                message = $"SubKind profile '{fileName}' created successfully.",
                fileName,
                data = content
            };
        }

        public object UpdateSubKindProfile(string fileName, JsonElement content)
        {
            var cleanName = Path.GetFileName(fileName);
            if (!cleanName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".json";
            }

            var fullPath = Path.Combine(_subkindsDir, cleanName);

            Dictionary<string, object> existingDict = new Dictionary<string, object>();
            if (File.Exists(fullPath))
            {
                string existingJson = File.ReadAllText(fullPath);
                existingDict = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? new Dictionary<string, object>();
            }

            if (content.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in content.EnumerateObject())
                {
                    string key = prop.Name;
                    string targetKey = key;
                    foreach (var existingKey in existingDict.Keys)
                    {
                        if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                        {
                            targetKey = existingKey;
                            break;
                        }
                    }
                    existingDict[targetKey] = prop.Value;
                }
            }
            else
            {
                throw new ArgumentException("Invalid JSON payload.");
            }

            string formattedJson = JsonSerializer.Serialize(existingDict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fullPath, formattedJson);

            _logger.LogInformation($"Updated subkind profile '{cleanName}'.");

            return new
            {
                success = true,
                message = $"SubKind profile '{cleanName}' updated successfully.",
                fileName = cleanName,
                data = existingDict
            };
        }

        public void DeleteSubKindProfile(string fileName)
        {
            var cleanName = Path.GetFileName(fileName);
            if (!cleanName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".json";
            }

            var fullPath = Path.Combine(_subkindsDir, cleanName);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"SubKind profile '{cleanName}' not found.");
            }

            File.Delete(fullPath);
            _logger.LogInformation($"Deleted subkind profile '{cleanName}'.");
        }

        public object CreateKindProfile(JsonElement content)
        {
            string kindKey = "";

            if (content.ValueKind == JsonValueKind.Object)
            {
                if (content.TryGetProperty("KindKey", out var kProp) ||
                    content.TryGetProperty("kindKey", out kProp) ||
                    content.TryGetProperty("kind_key", out kProp))
                {
                    kindKey = kProp.GetString() ?? "";
                }
            }

            if (string.IsNullOrWhiteSpace(kindKey))
            {
                throw new ArgumentException("'KindKey' (Game/Kind identifier) is required to create a Kind profile.");
            }

            string cleanKey = kindKey.Trim().ToLowerInvariant();
            string fileName = $"{cleanKey}.json";
            string fullPath = Path.Combine(_kindsDir, fileName);

            if (File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Kind profile '{fileName}' already exists.");
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(content.GetRawText()) ?? new();

            dict["KindKey"] = cleanKey;

            var clientId = _config["YouTube:ClientId"] ?? _config["YOUTUBE_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_ID") ?? "";
            var clientSecret = _config["YouTube:ClientSecret"] ?? _config["YOUTUBE_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_SECRET") ?? "";

            if (!dict.ContainsKey("YoutubeClientId") || string.IsNullOrWhiteSpace(dict["YoutubeClientId"]?.ToString()))
            {
                dict["YoutubeClientId"] = clientId;
            }
            if (!dict.ContainsKey("YoutubeClientSecret") || string.IsNullOrWhiteSpace(dict["YoutubeClientSecret"]?.ToString()))
            {
                dict["YoutubeClientSecret"] = clientSecret;
            }
            if (!dict.ContainsKey("YoutubeChannelId"))
            {
                dict["YoutubeChannelId"] = "";
            }
            if (!dict.ContainsKey("YoutubeRefreshToken"))
            {
                dict["YoutubeRefreshToken"] = "";
            }
            if (!dict.ContainsKey("UploadConfig"))
            {
                dict["UploadConfig"] = new YoutubeGenerator.Models.UploadConfig();
            }

            string formattedJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fullPath, formattedJson);

            _logger.LogInformation($"Created kind profile '{fileName}'.");

            return new
            {
                success = true,
                message = $"Kind profile '{fileName}' created successfully.",
                fileName,
                data = dict
            };
        }

        public object UpdateKindProfile(string fileName, JsonElement content)
        {
            var cleanName = Path.GetFileName(fileName);
            if (!cleanName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".json";
            }

            var fullPath = Path.Combine(_kindsDir, cleanName);

            var existingDict = new Dictionary<string, object>();
            if (File.Exists(fullPath))
            {
                var existingJson = File.ReadAllText(fullPath);
                existingDict = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? new Dictionary<string, object>();
            }

            if (content.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in content.EnumerateObject())
                {
                    var key = prop.Name;
                    var valStr = prop.Value.ToString();

                    if ((key.Equals("YoutubeClientId", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("YoutubeClientSecret", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("YoutubeRefreshToken", StringComparison.OrdinalIgnoreCase)) &&
                        valStr.Contains("***REDACTED***"))
                    {
                        continue;
                    }

                    if (key.Equals("UploadConfig", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        Dictionary<string, object> uploadConfig = new Dictionary<string, object>();

                        string? existingUploadConfigKey = null;
                        foreach (var k in existingDict.Keys)
                        {
                            if (string.Equals(k, "UploadConfig", StringComparison.OrdinalIgnoreCase))
                            {
                                existingUploadConfigKey = k;
                                break;
                            }
                        }

                        if (existingUploadConfigKey != null && existingDict[existingUploadConfigKey] is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                        {
                            uploadConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(elem.GetRawText()) ?? new Dictionary<string, object>();
                        }
                        else if (existingUploadConfigKey != null && existingDict[existingUploadConfigKey] is Dictionary<string, object> d)
                        {
                            uploadConfig = d;
                        }

                        var newUploadCfg = JsonSerializer.Deserialize<Dictionary<string, object>>(prop.Value.GetRawText()) ?? new Dictionary<string, object>();
                        foreach (var cfgKvp in newUploadCfg)
                        {
                            string targetKey = cfgKvp.Key;
                            foreach (var existingKey in uploadConfig.Keys)
                            {
                                if (string.Equals(existingKey, cfgKvp.Key, StringComparison.OrdinalIgnoreCase))
                                {
                                    targetKey = existingKey;
                                    break;
                                }
                            }
                            uploadConfig[targetKey] = cfgKvp.Value;
                        }

                        existingDict[existingUploadConfigKey ?? "UploadConfig"] = uploadConfig;
                    }
                    else
                    {
                        string targetKey = key;
                        foreach (var existingKey in existingDict.Keys)
                        {
                            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                            {
                                targetKey = existingKey;
                                break;
                            }
                        }
                        existingDict[targetKey] = prop.Value;
                    }
                }
            }
            else
            {
                throw new ArgumentException("Invalid JSON payload.");
            }

            string formattedJson = JsonSerializer.Serialize(existingDict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fullPath, formattedJson);

            _logger.LogInformation($"Updated kind profile '{cleanName}'.");

            return new
            {
                success = true,
                message = $"Kind profile '{cleanName}' updated successfully.",
                fileName = cleanName,
                data = existingDict
            };
        }

        public object UpdateKindMinDelay(string fileName, JsonElement body)
        {
            var cleanName = Path.GetFileName(fileName);
            if (!cleanName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".json";
            }

            var fullPath = Path.Combine(_kindsDir, cleanName);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Kind profile '{cleanName}' not found.");
            }

            int minDelayHours = 8;
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("minDelayHours", out var prop))
            {
                minDelayHours = prop.GetInt32();
            }
            else if (body.ValueKind == JsonValueKind.Number)
            {
                minDelayHours = body.GetInt32();
            }

            if (minDelayHours < 1) minDelayHours = 1;

            string jsonContent = File.ReadAllText(fullPath);
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent) ?? new Dictionary<string, JsonElement>();
            var resultDict = new Dictionary<string, object>();

            Dictionary<string, object> uploadConfig = new Dictionary<string, object>();

            foreach (var kvp in dictionary)
            {
                if (string.Equals(kvp.Key, "UploadConfig", StringComparison.OrdinalIgnoreCase))
                {
                    if (kvp.Value.ValueKind == JsonValueKind.Object)
                    {
                        var cfgDict = JsonSerializer.Deserialize<Dictionary<string, object>>(kvp.Value.GetRawText());
                        if (cfgDict != null) uploadConfig = cfgDict;
                    }
                }
                else
                {
                    resultDict[kvp.Key] = kvp.Value;
                }
            }

            uploadConfig["MinDelayHours"] = minDelayHours;
            resultDict["UploadConfig"] = uploadConfig;

            string updatedJson = JsonSerializer.Serialize(resultDict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fullPath, updatedJson);

            _logger.LogInformation($"Updated min delay to {minDelayHours} hrs for '{cleanName}'.");

            return new
            {
                success = true,
                message = $"Updated Min Delay to {minDelayHours} hrs.",
                fileName = cleanName,
                minDelayHours
            };
        }

        public void DeleteKindProfile(string fileName)
        {
            var cleanName = Path.GetFileName(fileName);
            if (!cleanName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".json";
            }

            var fullPath = Path.Combine(_kindsDir, cleanName);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Kind profile '{cleanName}' not found.");
            }

            File.Delete(fullPath);
            _logger.LogInformation($"Deleted kind profile '{cleanName}'.");
        }

        private static bool IsSensitiveKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return false;

            if (keyName.Equals("KindKey", StringComparison.OrdinalIgnoreCase) ||
                keyName.Equals("SubKindKey", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var lower = keyName.ToLowerInvariant();
            if (lower.Equals("key")) return true;

            return SensitiveKeys.Any(k => lower.Contains(k));
        }

        private static void SanitizeNode(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                var keysToMask = new List<string>();
                foreach (var kvp in obj)
                {
                    if (IsSensitiveKey(kvp.Key))
                    {
                        keysToMask.Add(kvp.Key);
                    }
                    else
                    {
                        SanitizeNode(kvp.Value);
                    }
                }

                foreach (var key in keysToMask)
                {
                    obj[key] = "***SENSITIVE_DATA_REDACTED***";
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    SanitizeNode(item);
                }
            }
        }

        public List<string> GetResourceFiles()
        {
            var candidateDirs = new[]
            {
                Path.Combine(_env.ContentRootPath, "workspace", "ressources"),
                Path.Combine(_env.ContentRootPath, "workspace", "profiles", "ressources"),
                Path.Combine(_env.ContentRootPath, "ressources"),
                Path.Combine(_env.ContentRootPath, "profiles", "ressources")
            };

            var filesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in candidateDirs)
            {
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir)
                        .Select(Path.GetFileName);

                    foreach (var f in files)
                    {
                        if (!string.IsNullOrEmpty(f))
                        {
                            filesSet.Add(f);
                        }
                    }
                }
            }

            return filesSet.OrderBy(x => x).ToList();
        }
    }
}
