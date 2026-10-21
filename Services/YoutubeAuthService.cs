using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class YoutubeAuthService : IYoutubeAuthService
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<YoutubeAuthService> _logger;
        private readonly string _kindsDir;

        public YoutubeAuthService(
            IConfiguration config,
            IWebHostEnvironment env,
            ILogger<YoutubeAuthService> logger)
        {
            _config = config;
            _env = env;
            _logger = logger;
            _kindsDir = Path.Combine(_env.ContentRootPath, "workspace", "profiles", "kinds");
            Directory.CreateDirectory(_kindsDir);
        }

        public (string ClientId, string ClientSecret) GetGlobalCredentials()
        {
            var clientId = _config["YouTube:ClientId"]
                        ?? _config["YOUTUBE_CLIENT_ID"]
                        ?? Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_ID")
                        ?? "";

            var clientSecret = _config["YouTube:ClientSecret"]
                            ?? _config["YOUTUBE_CLIENT_SECRET"]
                            ?? Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_SECRET")
                            ?? "";

            // Fallback: search existing kind json files if global config is not set yet
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                if (Directory.Exists(_kindsDir))
                {
                    foreach (var file in Directory.GetFiles(_kindsDir, "*.json"))
                    {
                        try
                        {
                            var content = File.ReadAllText(file);
                            var node = JsonNode.Parse(content);
                            if (node is JsonObject obj)
                            {
                                var id = obj["YoutubeClientId"]?.ToString();
                                var sec = obj["YoutubeClientSecret"]?.ToString();

                                if (string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(id) && !id.Contains("***"))
                                {
                                    clientId = id;
                                }
                                if (string.IsNullOrWhiteSpace(clientSecret) && !string.IsNullOrWhiteSpace(sec) && !sec.Contains("***"))
                                {
                                    clientSecret = sec;
                                }
                            }
                        }
                        catch
                        {
                            // ignore individual file parse errors
                        }
                        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
                        {
                            break;
                        }
                    }
                }
            }

            return (clientId, clientSecret);
        }

        public (string ClientId, string ClientSecret) GetCredentialsForKind(string kindKey)
        {
            var cleanKey = kindKey.Trim().ToLowerInvariant();
            var file = Path.Combine(_kindsDir, $"{cleanKey}.json");

            if (File.Exists(file))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var node = JsonNode.Parse(content);
                    if (node is JsonObject obj)
                    {
                        var id = obj["YoutubeClientId"]?.ToString()?.Trim() ?? "";
                        var sec = obj["YoutubeClientSecret"]?.ToString()?.Trim() ?? "";

                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(sec) && !id.Contains("***") && !sec.Contains("***"))
                        {
                            return (id, sec);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to parse credentials from {file}");
                }
            }

            // Fallback to global credentials
            return GetGlobalCredentials();
        }

        public string GetAuthorizationUrl(string kindKey, string redirectUri)
        {
            var (clientId, clientSecret) = GetCredentialsForKind(kindKey);

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException($"YouTube Google Cloud OAuth credentials are not configured for channel/profile '{kindKey}'. Please upload the Google credentials JSON file first.");
            }

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                Scopes = new[]
                {
                    YouTubeService.Scope.YoutubeUpload,
                    YouTubeService.Scope.YoutubeReadonly,
                    YouTubeService.Scope.Youtube
                }
            });

            var request = flow.CreateAuthorizationCodeRequest(redirectUri);
            request.State = kindKey;

            var baseUrl = request.Build().ToString();
            // GoogleAuthorizationCodeFlow automatically includes access_type=offline in request.Build().
            // We only need to add prompt=consent if not already present.
            if (!baseUrl.Contains("prompt="))
            {
                return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(baseUrl, "prompt", "consent");
            }

            return baseUrl;
        }

        public async Task<(string RefreshToken, string AccessToken, string ChannelId, string ChannelTitle)> ExchangeCodeAndFetchChannelAsync(string code, string redirectUri, string? kindKey = null)
        {
            var (clientId, clientSecret) = !string.IsNullOrWhiteSpace(kindKey) 
                ? GetCredentialsForKind(kindKey) 
                : GetGlobalCredentials();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException($"YouTube Client ID and Client Secret are missing for '{(kindKey ?? "global")}'.");
            }

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                Scopes = new[]
                {
                    YouTubeService.Scope.YoutubeUpload,
                    YouTubeService.Scope.YoutubeReadonly,
                    YouTubeService.Scope.Youtube
                }
            });

            TokenResponse tokenResponse = await flow.ExchangeCodeForTokenAsync("user", code, redirectUri, CancellationToken.None);

            string refreshToken = tokenResponse.RefreshToken ?? "";
            string accessToken = tokenResponse.AccessToken ?? "";

            var credential = new UserCredential(flow, "user", tokenResponse);
            var youtubeService = new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeGenerator"
            });

            string channelId = "";
            string channelTitle = "";

            try
            {
                var channelsRequest = youtubeService.Channels.List("snippet");
                channelsRequest.Mine = true;
                var channelsResponse = await channelsRequest.ExecuteAsync();

                if (channelsResponse.Items != null && channelsResponse.Items.Count > 0)
                {
                    var ch = channelsResponse.Items[0];
                    channelId = ch.Id ?? "";
                    channelTitle = ch.Snippet?.Title ?? "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve channel details from YouTube API.");
            }

            return (refreshToken, accessToken, channelId, channelTitle);
        }

        public void SaveChannelConnection(string kindKey, string channelId, string refreshToken, string? channelTitle = null)
        {
            var cleanKey = kindKey.Trim().ToLowerInvariant();
            var file = Path.Combine(_kindsDir, $"{cleanKey}.json");

            var (clientId, clientSecret) = GetCredentialsForKind(cleanKey);

            Dictionary<string, object> existingDict = new();
            if (File.Exists(file))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    existingDict = JsonSerializer.Deserialize<Dictionary<string, object>>(text) ?? new();
                }
                catch
                {
                    existingDict = new();
                }
            }

            existingDict["KindKey"] = cleanKey;
            if (!string.IsNullOrWhiteSpace(channelId))
            {
                existingDict["YoutubeChannelId"] = channelId;
            }
            if (!string.IsNullOrWhiteSpace(channelTitle))
            {
                existingDict["YoutubeChannelTitle"] = channelTitle.Trim();
            }
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                existingDict["YoutubeRefreshToken"] = refreshToken;
            }
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                existingDict["YoutubeClientId"] = clientId;
            }
            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                existingDict["YoutubeClientSecret"] = clientSecret;
            }

            if (!existingDict.ContainsKey("UploadConfig"))
            {
                existingDict["UploadConfig"] = new UploadConfig();
            }

            string updatedJson = JsonSerializer.Serialize(existingDict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, updatedJson);

            _logger.LogInformation($"Successfully updated YouTube channel connection for kind '{cleanKey}'. ChannelId: {channelId}, Title: {channelTitle}");
        }

        public void DisconnectChannel(string kindKey)
        {
            var cleanKey = kindKey.Trim().ToLowerInvariant();
            var file = Path.Combine(_kindsDir, $"{cleanKey}.json");

            if (!File.Exists(file))
            {
                throw new FileNotFoundException($"Kind profile '{cleanKey}' not found.");
            }

            var text = File.ReadAllText(file);
            var existingDict = JsonSerializer.Deserialize<Dictionary<string, object>>(text) ?? new();

            existingDict["YoutubeRefreshToken"] = "";
            existingDict["YoutubeChannelId"] = "";

            string updatedJson = JsonSerializer.Serialize(existingDict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, updatedJson);

            _logger.LogInformation($"Successfully disconnected YouTube channel for kind '{cleanKey}'.");
        }

        public async Task<bool> RefreshAndValidateTokenAsync(string kindKey)
        {
            var cleanKey = kindKey.Trim().ToLowerInvariant();
            var file = Path.Combine(_kindsDir, $"{cleanKey}.json");
            if (!File.Exists(file)) return false;

            var (clientId, clientSecret) = GetCredentialsForKind(cleanKey);

            Dictionary<string, object> existingDict;
            try
            {
                var text = await File.ReadAllTextAsync(file);
                existingDict = JsonSerializer.Deserialize<Dictionary<string, object>>(text) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to parse kind profile for '{cleanKey}'.");
                return false;
            }

            string refreshToken = "";
            if (existingDict.TryGetValue("YoutubeRefreshToken", out var rtObj) && rtObj != null)
            {
                refreshToken = rtObj.ToString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                _logger.LogInformation($"[TokenRefresh] Channel '{cleanKey}' has no refresh token configured. Skipping.");
                return false;
            }

            try
            {
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = clientId,
                        ClientSecret = clientSecret
                    },
                    Scopes = new[]
                    {
                        YouTubeService.Scope.YoutubeUpload,
                        YouTubeService.Scope.YoutubeReadonly,
                        YouTubeService.Scope.Youtube
                    }
                });

                _logger.LogInformation($"[TokenRefresh] Validating & refreshing YouTube OAuth token for channel '{cleanKey}'...");
                TokenResponse tokenResponse = await flow.RefreshTokenAsync("user", refreshToken, CancellationToken.None);

                if (!string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                {
                    _logger.LogInformation($"✅ [TokenRefresh] YouTube token is VALID for channel '{cleanKey}'.");

                    if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) && tokenResponse.RefreshToken != refreshToken)
                    {
                        existingDict["YoutubeRefreshToken"] = tokenResponse.RefreshToken;
                        _logger.LogInformation($"🔄 [TokenRefresh] Captured rotated refresh token for '{cleanKey}'.");
                    }

                    existingDict["LastTokenRefreshUtc"] = DateTime.UtcNow.ToString("o");
                    existingDict["TokenStatus"] = "Valid";

                    string updatedJson = JsonSerializer.Serialize(existingDict, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(file, updatedJson);
                    return true;
                }
            }
            catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant" || ex.Message.Contains("invalid_grant"))
            {
                _logger.LogError($"🚨 [TokenRefresh] YouTube OAuth token for channel '{cleanKey}' is EXPIRED or REVOKED (invalid_grant)! Channel re-authentication required.");
                existingDict["TokenStatus"] = "Expired";
                existingDict["LastTokenRefreshUtc"] = DateTime.UtcNow.ToString("o");

                string updatedJson = JsonSerializer.Serialize(existingDict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(file, updatedJson);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"⚠️ [TokenRefresh] Error validating token for channel '{cleanKey}'.");
            }

            return false;
        }

        public async Task<Dictionary<string, bool>> RefreshAllTokensAsync()
        {
            var results = new Dictionary<string, bool>();
            if (!Directory.Exists(_kindsDir)) return results;

            foreach (var filePath in Directory.GetFiles(_kindsDir, "*.json"))
            {
                var kindKey = Path.GetFileNameWithoutExtension(filePath);
                bool success = await RefreshAndValidateTokenAsync(kindKey);
                results[kindKey] = success;
            }

            return results;
        }
    }
}
