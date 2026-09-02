using System.Text;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class LlmService : ILlmService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _primaryModel;
        private readonly SemaphoreSlim _rateLimiter;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private const string OllamaApiUrl = "https://ollama.com/api/chat";
        private const string OpenRouterApiUrl = "https://openrouter.ai/api/v1/chat/completions";

        public LlmService(string apiKey, string model = "gemma4:31b")
            : this(new HttpClient(), apiKey, model)
        {
        }

        public LlmService(HttpClient httpClient, string apiKey, string model = "gemma4:31b")
        {
            _httpClient = httpClient ?? new HttpClient();
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _primaryModel = string.IsNullOrWhiteSpace(model) ? "gemma4:31b" : model;
            _rateLimiter = new SemaphoreSlim(1, 1);
        }

        public async Task<MontageLLMResult> GenerateMetadataAsync(MontageItem package, MontageProfile profile)
        {
            var result = new MontageLLMResult();

            if (package.Clips == null || !package.Clips.Any())
            {
                result.Title = $"{profile.KindKey} Highlights";
                result.Description = "New highlights compilation!";
                return result;
            }

            var lockAcquired = false;
            try
            {
                lockAcquired = await _rateLimiter.WaitAsync(TimeSpan.FromSeconds(10));

                var prompt = BuildComprehensivePrompt(package, profile);

                var rawResponse = await ExecuteLlmRequestWithFallbackAsync(prompt);
                var jsonText = ExtractJson(rawResponse);

                var metadata = JsonSerializer.Deserialize<MetadataResponse>(jsonText, _jsonOptions);

                if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Title))
                {
                    result.Title = CleanTitle(metadata.Title);
                    result.Description = BuildFinalDescription(metadata.Description, package, profile);
                }
                else
                {
                    result.Title = $"{profile.KindKey} Highlights - {DateTime.Now:MMM dd}";
                    result.Description = BuildFallbackDescription(package, profile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LlmService] Error generating metadata via LLM: {ex.Message}");
                result.Title = $"{profile.KindKey} Montage - {DateTime.Now:MMM dd}";
                result.Description = BuildFallbackDescription(package, profile);
            }
            finally
            {
                if (lockAcquired)
                {
                    _rateLimiter.Release();
                }
            }

            return result;
        }

        private async Task<string> ExecuteLlmRequestWithFallbackAsync(string prompt)
        {
            var modelsToTry = new List<string>
            {
                _primaryModel,
                "gemma4:31b",
                "gpt-oss:20b",
                "deepseek-v4-flash:0731",
                "glm-5.3-flash",
                "nemotron-3-nano:30b"
            }.Distinct().ToList();

            // 1️⃣ Try Ollama Cloud API first
            foreach (var model in modelsToTry)
            {
                try
                {
                    Console.WriteLine($"[LlmService] Requesting Ollama Cloud with model '{model}'...");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OllamaApiUrl);

                    requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");

                    var requestPayload = new
                    {
                        model = model,
                        messages = new[]
                        {
                            new { role = "system", content = "You are a YouTube SEO expert specializing in gaming content. Always return valid raw JSON." },
                            new { role = "user", content = prompt }
                        },
                        stream = false
                    };

                    var jsonContent = JsonSerializer.Serialize(requestPayload);
                    requestMessage.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(requestMessage, cts.Token);
                    var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[LlmService] Ollama model '{model}' status {response.StatusCode}: {responseBody}");
                        continue;
                    }

                    using var doc = JsonDocument.Parse(responseBody);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("message", out var msgObj) && msgObj.TryGetProperty("content", out var contentElem))
                    {
                        var text = contentElem.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            Console.WriteLine($"[LlmService] Successfully generated metadata with Ollama Cloud model '{model}'");
                            return text;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LlmService] Ollama model '{model}' attempt failed: {ex.Message}");
                }
            }

            // 2️⃣ Fallback to OpenRouter API if needed
            foreach (var model in new[] { "nvidia/nemotron-3-ultra-550b-a55b:free", "google/gemma-4-31b-it:free" })
            {
                try
                {
                    Console.WriteLine($"[LlmService] Requesting OpenRouter fallback with model '{model}'...");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenRouterApiUrl);

                    requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");
                    requestMessage.Headers.Add("HTTP-Referer", "https://github.com/chabbanadir/YoutubeGenerator");
                    requestMessage.Headers.Add("X-Title", "YoutubeGenerator");

                    var requestPayload = new
                    {
                        model = model,
                        messages = new[]
                        {
                            new { role = "system", content = "You are a YouTube SEO expert specializing in gaming content. Always return valid raw JSON." },
                            new { role = "user", content = prompt }
                        },
                        temperature = 0.7
                    };

                    var jsonContent = JsonSerializer.Serialize(requestPayload);
                    requestMessage.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(requestMessage, cts.Token);
                    var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

                    if (!response.IsSuccessStatusCode)
                        continue;

                    using var doc = JsonDocument.Parse(responseBody);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out var message) &&
                            message.TryGetProperty("content", out var content))
                        {
                            var text = content.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                Console.WriteLine($"[LlmService] Successfully generated metadata with OpenRouter model '{model}'");
                                return text;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore fallback exceptions
                }
            }

            throw new Exception("All LLM candidate models failed.");
        }

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            if (text.Contains("```"))
            {
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"^```(?:json)?\s*|\s*```$", "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            }

            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return text.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return text.Trim();
        }

        private string BuildComprehensivePrompt(MontageItem package, MontageProfile profile)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# TASK: Generate YouTube Video Metadata");
            sb.AppendLine();
            sb.AppendLine("You are a YouTube SEO expert specializing in gaming content. Generate an engaging title and description.");
            sb.AppendLine();
            sb.AppendLine("## VIDEO INFORMATION:");
            sb.AppendLine($"- Game: {profile.KindKey}");
            sb.AppendLine($"- Content Type: {profile.SubKindKey ?? "Highlights"}");
            sb.AppendLine($"- Total Clips: {package.Clips.Count}");
            sb.AppendLine($"- Video Duration: {package.TotalDuration.TotalMinutes:F1} minutes");
            sb.AppendLine();

            sb.AppendLine("## STREAMERS FEATURED:");
            var streamers = package.Clips
                .Select(c => c.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .Take(10)
                .ToList();

            foreach (var streamer in streamers)
            {
                sb.AppendLine($"- {streamer}");
            }

            if (streamers.Count > 10)
            {
                sb.AppendLine($"...and {package.Clips.Count - 10} more");
            }
            sb.AppendLine();

            sb.AppendLine("## CLIP TIMELINE:");
            sb.AppendLine(package.ToPromptString());
            sb.AppendLine();

            sb.AppendLine("## REQUIREMENTS:");
            sb.AppendLine("### Title (45-70 characters):");
            sb.AppendLine("- Must be catchy and SEO-optimized");
            sb.AppendLine("- Include game name and content type");
            sb.AppendLine("- Use emojis (🔥, ⚡, 🎯, 💥) strategically (1-2 emojis max)");
            sb.AppendLine("- Make it clickable but not clickbait");
            sb.AppendLine($"- Example style: '🔥 INSANE {profile.KindKey.ToUpper()} Plays - Epic Moments #127'");
            sb.AppendLine();

            sb.AppendLine("### Description (100-250 words, maximum 1500 characters):");
            sb.AppendLine("- Start with a hook (2-3 sentences)");
            sb.AppendLine("- Mention top streamers featured");
            sb.AppendLine("- Include relevant keywords naturally");
            sb.AppendLine("- End with a call-to-action (subscribe/like)");
            sb.AppendLine("- DO NOT include social media links or timestamps (I'll add them)");
            sb.AppendLine("- Keep it concise and professional (must leave room for timestamps & links)");
            sb.AppendLine();

            sb.AppendLine("## OUTPUT FORMAT:");
            sb.AppendLine("Return ONLY valid JSON (no markdown, no extra commentary):");
            sb.AppendLine("{");
            sb.AppendLine("  \"title\": \"Your catchy title here\",");
            sb.AppendLine("  \"description\": \"Your engaging description here\"");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string BuildFinalDescription(string aiDescription, MontageItem package, MontageProfile profile)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(aiDescription))
            {
                sb.AppendLine(aiDescription.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("⏱️ TIMESTAMPS:");
            TimeSpan cumulativeTime = TimeSpan.Zero;
            var timestampCount = 0;

            foreach (var clip in package.Clips.Take(20))
            {
                if (clip != null && !string.IsNullOrWhiteSpace(clip.Description))
                {
                    sb.AppendLine($"{cumulativeTime:mm\\:ss} - {clip.Description}");
                    cumulativeTime += clip.Duration;
                    timestampCount++;
                }
            }

            if (package.Clips.Count > timestampCount)
            {
                sb.AppendLine($"...and {package.Clips.Count - timestampCount} more epic moments!");
            }
            sb.AppendLine();

            var uniqueStreamers = package.Clips
                .Select(c => c.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .Take(15)
                .ToList();

            if (uniqueStreamers.Any())
            {
                sb.AppendLine("🎮 FEATURED STREAMERS:");
                foreach (var streamer in uniqueStreamers)
                {
                    sb.AppendLine($"🔸 https://www.twitch.tv/{streamer}");
                }
                sb.AppendLine();
            }

            var hasSocial = !string.IsNullOrEmpty(profile.FacebookLink) ||
                           !string.IsNullOrEmpty(profile.InstagramLink) ||
                           !string.IsNullOrEmpty(profile.TiktokLink);

            if (hasSocial)
            {
                sb.AppendLine("📱 FOLLOW US:");
                if (!string.IsNullOrEmpty(profile.FacebookLink))
                    sb.AppendLine($"▶️ Facebook: {profile.FacebookLink}");
                if (!string.IsNullOrEmpty(profile.InstagramLink))
                    sb.AppendLine($"📸 Instagram: {profile.InstagramLink}");
                if (!string.IsNullOrEmpty(profile.TiktokLink))
                    sb.AppendLine($"🎵 TikTok: {profile.TiktokLink}");
                sb.AppendLine();
            }

            var kindKeyClean = profile.KindKey.Replace(" ", "");
            sb.AppendLine($"#Gaming #{kindKeyClean} #Highlights #EpicMoments #Twitch #Compilation");

            return TextFormatter.FixDescription(sb.ToString(), 4800);
        }

        private string BuildFallbackDescription(MontageItem package, MontageProfile profile)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Check out this amazing {profile.KindKey} compilation featuring the best moments from top streamers!");
            sb.AppendLine();
            sb.AppendLine("This video showcases incredible plays, funny moments, and epic highlights.");
            sb.AppendLine();

            var streamers = package.Clips
                .Select(c => c.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .Take(5)
                .ToList();

            if (streamers.Any())
            {
                sb.AppendLine("Featured streamers:");
                foreach (var s in streamers)
                {
                    sb.AppendLine($"🔸 https://www.twitch.tv/{s}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("Don't forget to like and subscribe for more content!");

            return TextFormatter.FixDescription(sb.ToString(), 4800);
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return $"Gaming Highlights - {DateTime.Now:MMM dd}";

            title = title.Trim().Trim('"', '\'', ' ');

            if (title.Length > 95)
            {
                title = title.Substring(0, 92) + "...";
            }

            return title;
        }
    }

    public class MetadataResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
