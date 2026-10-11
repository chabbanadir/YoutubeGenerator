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

            var streamers = package?.Clips?
                .Select(c => c.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            var subKindKey = !string.IsNullOrWhiteSpace(package?.SubKindKey) ? package!.SubKindKey : profile.SubKindKey;
            result.Tags = TagGenerator.GenerateTags(profile.KindKey, subKindKey, streamers);

            if (package?.Clips == null || !package.Clips.Any())
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
                var subKindLabel = !string.IsNullOrWhiteSpace(package.SubKindKey) && package.SubKindKey != "normal"
                    ? $" {char.ToUpper(package.SubKindKey[0]) + (package.SubKindKey.Length > 1 ? package.SubKindKey.Substring(1) : "")}"
                    : "";
                result.Title = $"{profile.KindKey.ToUpper()}{subKindLabel} Montage - {DateTime.Now:MMM dd}";
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
            var contentType = !string.IsNullOrWhiteSpace(package.SubKindKey) ? package.SubKindKey : (profile.SubKindKey ?? "Highlights");
            sb.AppendLine($"- Content Type: {contentType}");
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
            sb.AppendLine("### Title (50-70 characters):");
            sb.AppendLine("- High-CTR packaging: Front-load the biggest featured creator or play type, followed by an emotional hook");
            sb.AppendLine("- Do NOT use generic episode numbers like '#127' or robotic filler text");
            sb.AppendLine("- Include the game name naturally (e.g. 'LoL' or 'Valorant')");
            sb.AppendLine("- 1 emoji maximum (e.g., 💀, 😱, 🔥, ⚡)");
            sb.AppendLine($"- Proven top-channel style examples: 'FAKER DID IT AGAIN! 😱 Best LoL Highlights' or 'TENZ UNREAL 1v5 CLUTCH! 🔥 Valorant Highlights'");
            sb.AppendLine();

            sb.AppendLine("### Description (100-200 words, maximum 1200 characters):");
            sb.AppendLine("- Front-load the first 2 sentences with an irresistible summary hook containing natural gaming keywords");
            sb.AppendLine("- Mention the top creators featured naturally in the text");
            sb.AppendLine("- Include a clear engagement call-to-action (e.g. 'Which play was your favorite? Let us know in the comments!')");
            sb.AppendLine("- DO NOT include any links, URLs, or social media addresses");
            sb.AppendLine("- DO NOT include timestamps (they are generated separately)");
            sb.AppendLine("- Keep it punchy and engaging for YouTube Browse & Suggested algorithms");
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

            sb.Append(BuildSmartTimestamps(package));
            sb.AppendLine();

            var uniqueStreamers = package.Clips
                .Select(c => c.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .Take(15)
                .ToList();

            if (uniqueStreamers.Any())
            {
                sb.AppendLine("🎮 FEATURED CREATORS:");
                sb.AppendLine(string.Join(", ", uniqueStreamers.Select(s => $"@{s}")));
                sb.AppendLine();
            }

            var gameHashtags = (profile.KindKey ?? "").ToLower() switch
            {
                "lol" => "#LeagueOfLegends #LoLHighlights #LeagueOfLegendsClips #LoL",
                "valorant" => "#VALORANT #ValorantHighlights #ValorantClips #VCT",
                _ => $"#Gaming #{(profile.KindKey ?? "").Replace(" ", "")} #Highlights #GamingClips"
            };
            sb.AppendLine(gameHashtags);

            return TextFormatter.FixDescription(sb.ToString(), 4800);
        }

        private string BuildFallbackDescription(MontageItem package, MontageProfile profile)
        {
            var sb = new StringBuilder();
            var contentLabel = !string.IsNullOrWhiteSpace(package.SubKindKey) && package.SubKindKey != "normal"
                ? $" {package.SubKindKey.ToUpper()}"
                : "";
            sb.AppendLine($"Check out this amazing {profile.KindKey}{contentLabel} compilation featuring the best moments from top streamers!");
            sb.AppendLine();
            sb.AppendLine("This video showcases incredible plays, funny moments, and epic highlights.");
            sb.AppendLine();

            sb.Append(BuildSmartTimestamps(package));
            sb.AppendLine();

            var streamers = package.Clips
                .Select(c => c.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .Take(10)
                .ToList();

            if (streamers.Any())
            {
                sb.AppendLine("🎮 FEATURED CREATORS:");
                sb.AppendLine(string.Join(", ", streamers.Select(s => $"@{s}")));
                sb.AppendLine();
            }

            sb.AppendLine("Don't forget to like and subscribe for more content!");
            sb.AppendLine();

            var gameHashtags = (profile.KindKey ?? "").ToLower() switch
            {
                "lol" => "#LeagueOfLegends #LoLHighlights #LeagueOfLegendsClips #LoL",
                "valorant" => "#VALORANT #ValorantHighlights #ValorantClips #VCT",
                _ => $"#Gaming #{(profile.KindKey ?? "").Replace(" ", "")} #Highlights #GamingClips"
            };
            sb.AppendLine(gameHashtags);

            return TextFormatter.FixDescription(sb.ToString(), 4800);
        }

        private string BuildSmartTimestamps(MontageItem package)
        {
            var sb = new StringBuilder();
            sb.AppendLine("⏱️ TIMESTAMPS:");

            if (package?.Clips == null || !package.Clips.Any())
            {
                sb.AppendLine("00:00 - Highlights");
                return sb.ToString();
            }

            // Group consecutive clips by streamer to prevent duplicate timestamp spam
            var blocks = new List<(string Streamer, TimeSpan StartTime, TimeSpan Duration, int ClipCount)>();
            TimeSpan currentElapsed = TimeSpan.Zero;
            string? currentStreamer = null;
            TimeSpan blockStart = TimeSpan.Zero;
            TimeSpan blockDuration = TimeSpan.Zero;
            int blockCount = 0;

            foreach (var clip in package.Clips)
            {
                var rawStreamer = string.IsNullOrWhiteSpace(clip.Description) ? "Highlights" : clip.Description.Trim();

                if (currentStreamer == null)
                {
                    currentStreamer = rawStreamer;
                    blockStart = currentElapsed;
                    blockDuration = clip.Duration;
                    blockCount = 1;
                }
                else if (string.Equals(currentStreamer, rawStreamer, StringComparison.OrdinalIgnoreCase))
                {
                    blockDuration += clip.Duration;
                    blockCount++;
                }
                else
                {
                    blocks.Add((currentStreamer, blockStart, blockDuration, blockCount));
                    currentStreamer = rawStreamer;
                    blockStart = currentElapsed;
                    blockDuration = clip.Duration;
                    blockCount = 1;
                }

                currentElapsed += clip.Duration;
            }

            if (currentStreamer != null)
            {
                blocks.Add((currentStreamer, blockStart, blockDuration, blockCount));
            }

            var chapters = new List<(TimeSpan Time, string Label)>();

            if (blocks.Count >= 3)
            {
                // Multi-streamer compilation: emit one chapter per creator block
                foreach (var block in blocks)
                {
                    var label = block.Streamer == "Highlights"
                        ? "Epic Highlights"
                        : $"@{block.Streamer} Highlights";

                    if (!chapters.Any())
                    {
                        chapters.Add((TimeSpan.Zero, label));
                    }
                    else if (block.StartTime - chapters.Last().Time >= TimeSpan.FromSeconds(10))
                    {
                        chapters.Add((block.StartTime, label));
                    }
                }
            }
            else
            {
                // Single or duo streamer montage: distribute chronological chapters across milestones
                chapters.Add((TimeSpan.Zero, blocks.Any() && blocks[0].Streamer != "Highlights"
                    ? $"@{blocks[0].Streamer} Opening Plays"
                    : "Opening Highlights"));

                TimeSpan runningTime = TimeSpan.Zero;
                int part = 2;
                foreach (var clip in package.Clips)
                {
                    runningTime += clip.Duration;
                    if (runningTime - chapters.Last().Time >= TimeSpan.FromSeconds(60) &&
                        runningTime < package.TotalDuration - TimeSpan.FromSeconds(15))
                    {
                        var streamerPrefix = !string.IsNullOrWhiteSpace(clip.Description) ? $"@{clip.Description} " : "";
                        chapters.Add((runningTime, $"{streamerPrefix}Highlights Part {part++}"));
                    }
                }
            }

            // YouTube requires at least 3 chapters in ascending order starting at 00:00
            if (chapters.Count < 3 && package.TotalDuration > TimeSpan.FromSeconds(30))
            {
                chapters.Clear();
                var mainStreamer = package.Clips.FirstOrDefault()?.Description;
                var prefix = !string.IsNullOrWhiteSpace(mainStreamer) ? $"@{mainStreamer} " : "";
                var midpoint = TimeSpan.FromSeconds(package.TotalDuration.TotalSeconds / 2);

                chapters.Add((TimeSpan.Zero, $"{prefix}Opening Moments"));
                chapters.Add((midpoint, $"{prefix}Mid-Game Action"));
                chapters.Add((package.TotalDuration - TimeSpan.FromSeconds(15), $"{prefix}Final Clutches"));
            }

            // Ensure first chapter is always 00:00
            if (!chapters.Any())
            {
                chapters.Add((TimeSpan.Zero, "Highlights"));
            }
            else if (chapters[0].Time != TimeSpan.Zero)
            {
                chapters[0] = (TimeSpan.Zero, chapters[0].Label);
            }

            foreach (var ch in chapters)
            {
                sb.AppendLine($"{ch.Time:mm\\:ss} - {ch.Label}");
            }

            return sb.ToString();
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
