using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Services
{
    public class TikTokAuthService : ITikTokAuthService
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<TikTokAuthService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _kindsDir;

        public TikTokAuthService(
            IConfiguration config,
            IWebHostEnvironment env,
            ILogger<TikTokAuthService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _env = env;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _kindsDir = Path.Combine(_env.ContentRootPath, "workspace", "profiles", "kinds");
            Directory.CreateDirectory(_kindsDir);
        }

        public (string ClientKey, string ClientSecret) GetGlobalCredentials()
        {
            var key = _config["TikTok:ClientKey"]
                   ?? _config["TIKTOK_CLIENT_KEY"]
                   ?? Environment.GetEnvironmentVariable("TIKTOK_CLIENT_KEY")
                   ?? "awlwxrmcsawcrfy4";

            var secret = _config["TikTok:ClientSecret"]
                      ?? _config["TIKTOK_CLIENT_SECRET"]
                      ?? Environment.GetEnvironmentVariable("TIKTOK_CLIENT_SECRET")
                      ?? "Pvf1JZHMItyauHkqf8NtvV8e8k9bkpqG";

            return (key.Trim(), secret.Trim());
        }

        public string GetAuthorizationUrl(string kindKey, string redirectUri)
        {
            var (clientKey, _) = GetGlobalCredentials();
            if (string.IsNullOrWhiteSpace(clientKey))
            {
                throw new InvalidOperationException("TikTok Client Key is not configured on the server.");
            }

            var encodedRedirect = Uri.EscapeDataString(redirectUri);
            var scopes = _config["TikTok:Scopes"] ?? "user.info.profile";
            return $"https://www.tiktok.com/v2/auth/authorize/?client_key={clientKey}&scope={scopes}&response_type=code&redirect_uri={encodedRedirect}&state={kindKey}";
        }

        public async Task<(string AccessToken, string RefreshToken, string OpenId, string DisplayName, string AvatarUrl, int ExpiresIn, int RefreshExpiresIn)> ExchangeCodeAndFetchUserAsync(string code, string redirectUri)
        {
            var (clientKey, clientSecret) = GetGlobalCredentials();
            var client = _httpClientFactory.CreateClient();

            // 1. Exchange code for OAuth token
            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/oauth/token/");
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_key", clientKey },
                { "client_secret", clientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", redirectUri }
            });

            var tokenResp = await client.SendAsync(tokenRequest);
            var tokenBody = await tokenResp.Content.ReadAsStringAsync();

            if (!tokenResp.IsSuccessStatusCode)
            {
                _logger.LogError($"[TikTokAuth] Token exchange failed: {tokenBody}");
                throw new Exception($"TikTok token exchange failed: {tokenBody}");
            }

            var json = JsonNode.Parse(tokenBody);
            var data = json?["data"];
            if (data == null)
            {
                throw new Exception($"Invalid response from TikTok token endpoint: {tokenBody}");
            }

            string accessToken = data["access_token"]?.ToString() ?? "";
            string refreshToken = data["refresh_token"]?.ToString() ?? "";
            string openId = data["open_id"]?.ToString() ?? "";
            int expiresIn = data["expires_in"]?.GetValue<int>() ?? 86400;
            int refreshExpiresIn = data["refresh_expires_in"]?.GetValue<int>() ?? 31536000;

            if (string.IsNullOrEmpty(accessToken))
            {
                throw new Exception($"No access_token returned by TikTok: {tokenBody}");
            }

            // 2. Fetch User Profile Info
            string displayName = "TikTok Creator";
            string avatarUrl = "";
            try
            {
                var userReq = new HttpRequestMessage(HttpMethod.Get, "https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name,avatar_url,username");
                userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var userResp = await client.SendAsync(userReq);
                if (userResp.IsSuccessStatusCode)
                {
                    var userBody = await userResp.Content.ReadAsStringAsync();
                    var userNode = JsonNode.Parse(userBody);
                    var userObj = userNode?["data"]?["user"];
                    if (userObj != null)
                    {
                        displayName = userObj["display_name"]?.ToString() ?? userObj["username"]?.ToString() ?? displayName;
                        avatarUrl = userObj["avatar_url"]?.ToString() ?? "";
                    }
                }
            }
            catch (Exception exUser)
            {
                _logger.LogWarning(exUser, "[TikTokAuth] Could not fetch detailed profile info from TikTok.");
            }

            return (accessToken, refreshToken, openId, displayName, avatarUrl, expiresIn, refreshExpiresIn);
        }

        public void SaveConnection(string kindKey, string openId, string accessToken, string refreshToken, string displayName, string avatarUrl, int expiresIn, int refreshExpiresIn)
        {
            var filePath = Path.Combine(_kindsDir, $"{kindKey}.json");
            JsonObject obj;

            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath);
                obj = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
            }
            else
            {
                obj = new JsonObject { ["KindKey"] = kindKey };
            }

            var tikTokConfig = new JsonObject
            {
                ["Connected"] = true,
                ["OpenId"] = openId,
                ["DisplayName"] = displayName,
                ["AvatarUrl"] = avatarUrl,
                ["AccessToken"] = accessToken,
                ["RefreshToken"] = refreshToken,
                ["ConnectedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["TokenExpiresUtc"] = DateTime.UtcNow.AddSeconds(expiresIn).ToString("O"),
                ["RefreshTokenExpiresUtc"] = DateTime.UtcNow.AddSeconds(refreshExpiresIn).ToString("O")
            };

            obj["TikTokConfig"] = tikTokConfig;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(filePath, obj.ToJsonString(options));
            _logger.LogInformation($"[TikTokAuth] Successfully saved TikTok connection for '{kindKey}': @{displayName}");
        }

        public void Disconnect(string kindKey)
        {
            var filePath = Path.Combine(_kindsDir, $"{kindKey}.json");
            if (!File.Exists(filePath)) return;

            var content = File.ReadAllText(filePath);
            var obj = JsonNode.Parse(content) as JsonObject;
            if (obj != null && obj.ContainsKey("TikTokConfig"))
            {
                obj.Remove("TikTokConfig");
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(filePath, obj.ToJsonString(options));
                _logger.LogInformation($"[TikTokAuth] Disconnected TikTok for '{kindKey}'");
            }
        }
    }
}
