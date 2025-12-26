using Microsoft.Extensions.AI;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using GenerativeAI;
using GenerativeAI.Types;
using GenerativeAI.Exceptions; 
using System.Text;

namespace YoutubeGenerator.Services
{
    public class LlmService : ILlmService
    {
        private readonly GenerativeModel _model;

        public LlmService(string apiKey)
        {
            var key = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            var googleAI = new GoogleAi(key);
            
            // FIX: Some versions of this SDK require the "models/" prefix 
            // to resolve the 404 Not Found error.
            _model = googleAI.CreateGenerativeModel("gemini-2.0-flash-exp");

            _model.Config = new GenerationConfig
            {
                Temperature = 0.7,
                TopP = 0.9,
                ResponseMimeType = "application/json"
            };
        }

        public async Task<MontageLLMResult> GenerateMetadataAsync(MontageItem package, MontageProfile profile)
        {
            var result = new MontageLLMResult();

            // Safety check: If package has no clips, don't even call the API
            if (package.Clips == null || !package.Clips.Any())
            {
                result.Title = $"{profile.KindKey} Highlights";
                result.Description = "New highlights compilation!";
                return result;
            }

            try 
            {
                // 1. Generate Title
                var titleRequest = new GenerateContentRequest();
                titleRequest.AddText(BuildTitlePrompt(package, profile));
                titleRequest.UseJsonMode<TitleResult>();

                var titleResponse = await ExecuteWithRetryAsync(() => _model.GenerateContentAsync(titleRequest));
                var titleResult = titleResponse.ToObject<TitleResult>();
                result.Title = titleResult?.Title ?? $"Epic {profile.KindKey} Montage";

                // 2. Generate Description
                var descRequest = new GenerateContentRequest();
                descRequest.AddText(BuildDescriptionPrompt(package, profile));
                descRequest.UseJsonMode<DescriptionResult>();

                var descResponse = await ExecuteWithRetryAsync(() => _model.GenerateContentAsync(descRequest));
                var descResult = descResponse.ToObject<DescriptionResult>();

                var aiDescription = descResult?.Description ?? "Enjoy these highlights!";
                var socialMedia = BuildSocialMediaBlock(profile);
                
                result.Description = $"{aiDescription}\n\n{socialMedia}".Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LlmService] Error: {ex.Message}");
                result.Title = $"{profile.KindKey} Montage";
                result.Description = "Automated gaming compilation.";
            }

            return result;
        }

        private async Task<GenerateContentResponse> ExecuteWithRetryAsync(Func<Task<GenerateContentResponse>> action, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await action();
                }
                catch (ApiException ex) when (ex.Message.Contains("503") || ex.Message.Contains("overloaded"))
                {
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i + 1)));
                }
            }
            throw new Exception("LLM connection failed.");
        }

        private string BuildSocialMediaBlock(MontageProfile profile)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(profile.FacebookLink)) sb.AppendLine($"▶️ Facebook: {profile.FacebookLink}");
            if (!string.IsNullOrEmpty(profile.InstagramLink)) sb.AppendLine($"📸 Instagram: {profile.InstagramLink}");
            if (!string.IsNullOrEmpty(profile.TiktokLink)) sb.AppendLine($"🎵 TikTok: {profile.TiktokLink}");
            return sb.ToString();
        }

        private string BuildTitlePrompt(MontageItem pkg, MontageProfile profile)
        {
            return $@"JSON: {{ ""title"": ""string"" }}. Task: SEO Title for {profile.KindKey}. Data: {pkg.ToPromptString()}";
        }

        private string BuildDescriptionPrompt(MontageItem pkg, MontageProfile profile)
        {
            var links = string.Join("\n", pkg.Clips.Select(c => $"- https://www.twitch.tv/{c.Description}"));
            return $@"JSON: {{ ""description"": ""string"" }}. Data: {pkg.ToPromptString()}. Links: {links}";
        }
    }

    public class TitleResult { public string Title { get; set; } = string.Empty; }
    public class DescriptionResult { public string Description { get; set; } = string.Empty; }
}