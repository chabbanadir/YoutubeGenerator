using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DashboardService> _logger;
        private readonly IOptions<CollectWorkerConfig> _cfg;
        private readonly string _bagsDir;

        private static readonly HashSet<string> SensitiveFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "appsettings.json",
            "appsettings.development.json",
            "appsettings.production.json",
            ".env",
            ".env.example",
            "credentials.json",
            "client_secret.json"
        };

        private static readonly string[] SensitiveKeys = new[]
        {
            "token", "secret", "apikey", "password", "auth", "credential", "privatekey", "api_key", "private_key"
        };

        public DashboardService(
            IWebHostEnvironment env,
            ILogger<DashboardService> logger,
            IOptions<CollectWorkerConfig> cfg)
        {
            _env = env;
            _logger = logger;
            _cfg = cfg;

            var rootPath = Path.Combine(_env.ContentRootPath, "workspace");
            _bagsDir = Path.Combine(rootPath, "bags");
            Directory.CreateDirectory(_bagsDir);
        }

        public object GetOverview(bool isAuthenticated)
        {
            var workerCollectFile = Path.Combine(_bagsDir, _cfg.Value.WorkerFile);
            var workerMontageFile = Path.Combine(_bagsDir, _cfg.Value.MontageFile);

            var workerCollectState = JsonFileService.Load<Worker1Model>(workerCollectFile);
            var workerMontageState = JsonFileService.Load<Worker2State>(workerMontageFile);

            var bagsInfo = GetBags(isAuthenticated);

            return new
            {
                timestamp = DateTime.UtcNow,
                isAuthenticated = isAuthenticated,
                readOnlyMode = !isAuthenticated,
                currentUser = isAuthenticated ? "Admin" : null,
                userRole = isAuthenticated ? "Admin" : "Guest",
                workers = new
                {
                    collectWorkerActive = workerCollectState.Status,
                    montageWorkerActive = workerMontageState.Status,
                    collect = new
                    {
                        file = _cfg.Value.WorkerFile,
                        status = workerCollectState.Status,
                        downloadCounter = workerCollectState.DownloadCounter,
                        trimCounter = workerCollectState.TrimCounter
                    },
                    montage = new
                    {
                        file = _cfg.Value.MontageFile,
                        status = workerMontageState.Status,
                        finalCounter = workerMontageState.FinalCounter
                    }
                },
                bags = bagsInfo
            };
        }

        public List<object> GetBags(bool isAuthenticated)
        {
            string[] knownBags = new[]
            {
                _cfg.Value.BagDownloadPath,
                _cfg.Value.BagTempPath,
                _cfg.Value.BagTrimmedPath,
                _cfg.Value.BagMontagePath,
                _cfg.Value.BagYoutubePath,
                _cfg.Value.BagYoutubeDonePath,
                _cfg.Value.BagYoutubeFailedPath,
                _cfg.Value.BagFailed,
                _cfg.Value.BagHistoryPath,
                _cfg.Value.WorkerFile,
                _cfg.Value.MontageFile
            };

            var allFilesQuery = Directory.GetFiles(_bagsDir, "*.json")
                .Select(Path.GetFileName)
                .Cast<string>()
                .Union(knownBags)
                .Distinct()
                .OrderBy(f => f);

            var allFiles = isAuthenticated 
                ? allFilesQuery 
                : allFilesQuery.Where(name => !IsFileSensitive(name));

            var list = new List<object>();

            foreach (var fileName in allFiles)
            {
                var fullPath = Path.Combine(_bagsDir, fileName);
                bool exists = File.Exists(fullPath);
                int count = 0;
                long size = 0;
                DateTime? lastModified = null;
                string type = "unknown";
                bool isSensitive = IsFileSensitive(fileName);

                if (exists)
                {
                    var fileInfo = new FileInfo(fullPath);
                    size = fileInfo.Length;
                    lastModified = fileInfo.LastWriteTimeUtc;

                    try
                    {
                        var json = File.ReadAllText(fullPath);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                count = doc.RootElement.GetArrayLength();
                                type = "array";
                            }
                            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                count = 1;
                                type = "object";
                            }
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors for metadata
                    }
                }

                list.Add(new
                {
                    fileName,
                    exists,
                    itemCount = count,
                    sizeBytes = size,
                    lastModified,
                    type,
                    isSensitive
                });
            }

            return list;
        }

        public object GetBagContent(string bagName, bool isAuthenticated)
        {
            var cleanName = Path.GetFileName(bagName);

            if (IsFileSensitive(cleanName) && !isAuthenticated)
            {
                throw new UnauthorizedAccessException($"Access to sensitive file '{cleanName}' requires authentication.");
            }

            var fullPath = Path.Combine(_bagsDir, cleanName);

            if (!File.Exists(fullPath))
            {
                return new
                {
                    fileName = cleanName,
                    rawJson = "[]",
                    parsed = new object[0],
                    exists = false,
                    isRedacted = !isAuthenticated
                };
            }

            try
            {
                var rawJson = File.ReadAllText(fullPath);
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    return new { fileName = cleanName, rawJson = "[]", parsed = new object[0], exists = true, isRedacted = !isAuthenticated };
                }

                if (!isAuthenticated)
                {
                    try
                    {
                        var node = JsonNode.Parse(rawJson);
                        SanitizeNode(node);

                        var sanitizedJson = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "[]";
                        var parsedObj = JsonSerializer.Deserialize<object>(sanitizedJson);

                        return new
                        {
                            fileName = cleanName,
                            rawJson = sanitizedJson,
                            parsed = parsedObj,
                            exists = true,
                            isRedacted = true
                        };
                    }
                    catch
                    {
                        return new
                        {
                            fileName = cleanName,
                            rawJson = rawJson,
                            parsed = new object[0],
                            exists = true,
                            isRedacted = true
                        };
                    }
                }
                else
                {
                    var parsedObj = JsonSerializer.Deserialize<object>(rawJson);
                    return new
                    {
                        fileName = cleanName,
                        rawJson = rawJson,
                        parsed = parsedObj,
                        exists = true,
                        isRedacted = false
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to read bag '{cleanName}'");
                return new
                {
                    fileName = cleanName,
                    rawJson = "[]",
                    parsed = new object[0],
                    exists = false,
                    isRedacted = !isAuthenticated
                };
            }
        }

        public void SaveBagContent(string bagName, JsonElement content)
        {
            var cleanName = Path.GetFileName(bagName);
            var fullPath = Path.Combine(_bagsDir, cleanName);

            string formattedJson = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fullPath, formattedJson);
            _logger.LogInformation($"Saved bag file '{cleanName}'.");
        }

        private static bool IsFileSensitive(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return true;
            if (SensitiveFiles.Contains(fileName)) return true;
            if (fileName.EndsWith(".env", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Contains("credential", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Contains("secret", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
    }
}
