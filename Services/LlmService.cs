using GenerativeAI;
using GenerativeAI.Types;
using GenerativeAI.Exceptions;
using System.Text;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class LlmService : ILlmService
    {
        private readonly GenerativeModel _model;
        private readonly SemaphoreSlim _rateLimiter;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public LlmService(string apiKey)
        {
            var key = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            var googleAI = new GoogleAi(key);

            // Use stable, free model: gemini-1.5-flash (15 RPM free tier)
            _model = googleAI.CreateGenerativeModel("models/gemini-2.0-flash");

            _model.Config = new GenerationConfig
            {
                Temperature = 0.8,
                TopP = 0.95,
                TopK = 40,
                MaxOutputTokens = 2048,
                ResponseMimeType = "application/json"
            };

            // Rate limiter: Max 15 requests per minute (free tier)
            _rateLimiter = new SemaphoreSlim(1, 1);
        }

        public async Task<MontageLLMResult> GenerateMetadataAsync(MontageItem package, MontageProfile profile)
        {
            var result = new MontageLLMResult();

            // Safety check
            if (package.Clips == null || !package.Clips.Any())
            {
                result.Title = $"{profile.KindKey} Highlights";
                result.Description = "New highlights compilation!";
                return result;
            }

            try
            {
                // Rate limiting: Wait 4 seconds between requests (15 RPM = 1 per 4 sec)
                await _rateLimiter.WaitAsync();
                await Task.Delay(4000); // Ensure we don't hit rate limits

                var prompt = BuildComprehensivePrompt(package, profile);

                var request = new GenerateContentRequest();
                request.AddText(prompt);

                var response = await ExecuteWithRetryAsync(action: async () =>
                {
                    return await _model.GenerateContentAsync(request);
                });

                // Parse JSON response
                var jsonText = response.Text?.Trim() ?? "{}";

                // Remove markdown code blocks if present
                if (jsonText.StartsWith("```json"))
                {
                    jsonText = jsonText.Replace("```json", "").Replace("```", "").Trim();
                }

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
                Console.WriteLine($"[LlmService] Error: {ex.Message}");
                result.Title = $"{profile.KindKey} Montage - {DateTime.Now:MMM dd}";
                result.Description = BuildFallbackDescription(package, profile);
            }
            finally
            {
                _rateLimiter.Release();
            }

            return result;
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

            sb.AppendLine("### Description (200-400 words):");
            sb.AppendLine("- Start with a hook (2-3 sentences)");
            sb.AppendLine("- Mention top streamers featured");
            sb.AppendLine("- Include relevant keywords naturally");
            sb.AppendLine("- End with a call-to-action (subscribe/like)");
            sb.AppendLine("- DO NOT include social media links or timestamps (I'll add them)");
            sb.AppendLine("- Use \\n for line breaks (properly escaped for JSON)");
            sb.AppendLine("- Ensure all special characters are JSON-escaped"); 
            sb.AppendLine("- Keep it engaging and professional");
            sb.AppendLine();

            sb.AppendLine("## OUTPUT FORMAT:");
            sb.AppendLine("Return ONLY valid JSON (no markdown, no code blocks):");
            sb.AppendLine("{");
            sb.AppendLine("  \"title\": \"Your catchy title here\",");
            sb.AppendLine("  \"description\": \"Your engaging description here\"");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string BuildFinalDescription(string aiDescription, MontageItem package, MontageProfile profile)
        {
            var sb = new StringBuilder();

            // AI-generated description
            if (!string.IsNullOrWhiteSpace(aiDescription))
            {
                sb.AppendLine(aiDescription.Trim());
                sb.AppendLine();
            }

            // Timestamps section
            sb.AppendLine("⏱️ TIMESTAMPS:");
            TimeSpan cumulativeTime = TimeSpan.Zero;
            var timestampCount = 0;

            foreach (var clip in package.Clips.Take(20)) // Limit to 20 timestamps
            {
                if (clip != null && !string.IsNullOrWhiteSpace(clip.Description))
                {
                    sb.AppendLine($"{cumulativeTime:mm\\:ss} - {clip.Description}");
                    cumulativeTime += clip.Duration; // TimeSpan += TimeSpan
                    timestampCount++;
                }
            }

            if (package.Clips.Count > timestampCount)
            {
                sb.AppendLine($"...and {package.Clips.Count - timestampCount} more epic moments!");
            }
            sb.AppendLine();

            // Streamer credits
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

            // Social media (from profile)
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

            // Tags
            var kindKeyClean = profile.KindKey.Replace(" ", "");
            sb.AppendLine($"#Gaming #{kindKeyClean} #Highlights #EpicMoments #Twitch #Compilation");

            return sb.ToString().Trim();
        }

        private string BuildFallbackDescription(MontageItem package, MontageProfile profile)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Check out this amazing {profile.KindKey} compilation featuring the best moments from top streamers!");
            sb.AppendLine();
            sb.AppendLine("This video showcases incredible plays, funny moments, and epic highlights.");
            sb.AppendLine();

            // Add streamers
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

            return sb.ToString();
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return $"Gaming Highlights - {DateTime.Now:MMM dd}";

            // Remove quotes if present
            title = title.Trim().Trim('"', '\'', ' ');

            // Ensure it's not too long (YouTube limit is 100 chars)
            if (title.Length > 95)
            {
                title = title.Substring(0, 92) + "...";
            }

            return title;
        }

        private async Task<GenerateContentResponse> ExecuteWithRetryAsync(
            Func<Task<GenerateContentResponse>> action,
            int maxRetries = 3)
        {
            Exception lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var response = await action();
                    return response;
                }
                catch (ApiException ex) when (
                    ex.Message.Contains("503") ||
                    ex.Message.Contains("overloaded") ||
                    ex.Message.Contains("429") ||
                    ex.Message.Contains("quota"))
                {
                    lastException = ex;
                    if (i == maxRetries - 1) throw;

                    var delayMs = (int)(Math.Pow(2, i + 2) * 1000); // 4000ms, 8000ms, 16000ms
                    Console.WriteLine($"[LlmService] Rate limited. Retrying in {delayMs / 1000}s... (Attempt {i + 1}/{maxRetries})");
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"[LlmService] Attempt {i + 1}/{maxRetries} failed: {ex.Message}");
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(2000);
                }
            }

            throw new Exception($"LLM connection failed after {maxRetries} retries.", lastException);
        }
    }

    public class MetadataResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
