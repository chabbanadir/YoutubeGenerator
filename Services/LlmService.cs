using Microsoft.Extensions.AI;
using System.Text.Json;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using GenerativeAI;
using GenerativeAI.Types;

namespace YoutubeGenerator.Services
{
    public class LlmService : ILlmService
    {
        private readonly GenerativeModel _model;
        private readonly IMontageProfileService? _montageProfileService;

        public LlmService(string apiKey)
        {
            var googleAI = new GoogleAi(apiKey);
            _model = googleAI.CreateGenerativeModel("gemini-2.5-flash");

            _model.Config = new GenerationConfig
            {
                Temperature = 0.7,
                TopP = 0.9,
                ResponseMimeType = "application/json"
            };
        }

        public async Task<MontageLLMResult> GenerateMetadataAsync(
                      MontageItem package,
                      MontageProfile profile
                    )
        {
            var result = new MontageLLMResult();

            // --- 1. Generate Title (Same as before) ---
            var titleRequest = new GenerateContentRequest();
            titleRequest.AddText(BuildTitlePrompt(package, profile));
            titleRequest.UseJsonMode<TitleResult>();

            var titleResponse = await _model.GenerateContentAsync(titleRequest);
            var titleResult = titleResponse.ToObject<TitleResult>();
            result.Title = titleResult?.Title ?? "Default Title";


            // --- 2. Generate Description (Corrected Logic) ---
            var socialMediaBlock = BuildSocialMediaBlock(profile);
            var descRequest = new GenerateContentRequest();

            // Pass the placeholder to the prompt builder
            descRequest.AddText(BuildDescriptionPrompt(package, profile));
            descRequest.UseJsonMode<DescriptionResult>();

            var descResponse = await _model.GenerateContentAsync(descRequest);
            var descResult = descResponse.ToObject<DescriptionResult>();

            // --- CRITICAL FIX: Use the constant to replace the placeholder ---
            var rawDescription = descResult?.Description ?? "Default Description";
            result.Description = rawDescription;
            return result;
        }

        // --- New Prompt Builder Functions ---
        /// <summary>
        /// Builds a formatted block of social media links only for those that are provided.
        /// </summary>
        private string BuildSocialMediaBlock(MontageProfile profile)
        {
            var sb = new System.Text.StringBuilder();

            // Add a header only if at least one link exists
            if (!string.IsNullOrEmpty(profile.FacebookLink) ||
                !string.IsNullOrEmpty(profile.InstagramLink) ||
                !string.IsNullOrEmpty(profile.TiktokLink))
            {
                sb.AppendLine("Check out our social media channels below:");
            }

            if (!string.IsNullOrEmpty(profile.FacebookLink))
            {
                sb.AppendLine($"▶️ Facebook: {profile.FacebookLink}");
            }
            if (!string.IsNullOrEmpty(profile.InstagramLink))
            {
                sb.AppendLine($"📸 Instagram: {profile.InstagramLink}");
            }
            if (!string.IsNullOrEmpty(profile.TiktokLink))
            {
                sb.AppendLine($"🎵 TikTok: {profile.TiktokLink}");
            }

            // Return the block, trimmed to remove trailing newlines if no links were added.
            return sb.ToString().Trim();
        }
        private string BuildTitlePrompt(MontageItem pkg, MontageProfile profile)
        {
            return $@"
You are an AI assistant for a YouTube video automation script. Your sole task is to generate a compelling video title based on the provided data.
Return STRICT JSON ONLY, following exactly:

{{
  ""title"": ""string""
}}

The title must be SEO Friendly, concise and engaging, including key players or themes from the timestamps. **Include a few relevant emojis in the title.**

MONTAGE DATA:
{pkg.ToPromptString()}

Game: {profile.KindKey}, Style: {profile.SubKindKey}
";
        }

        // ⭐️ Modified signature to accept the pre-formatted social media block
        private string BuildDescriptionPrompt(MontageItem pkg, MontageProfile profile)
        {
            var attributionBlock = "Clip used with permission/fair use - all credit to original streamer"; // NEW BLOCK

            return $@"
            You are an AI system generating YouTube descriptions with a **stylized and streamer-focused tone**.
            Return **ONLY JSON**, with the structure:

            {{
              ""description"": ""string""
            }}

            ### DESCRIPTION FORMAT RULES (MODIFIED)

            Your generated description MUST follow **this general structure**, using **real newline characters (\n)**:

            1. **Opening Hook (1–2 lines)** - Very engaging, including **2-3 relevant emojis**.  
                - Must use a strong, direct hook (e.g., 'Another epic moment' or 'Get ready to laugh').
                - Must be SEO Friendly.
                

            2. **Video Summary (2–5 lines)** - Explain the content (e.g., 'compilation of the best recent plays' OR 'hilarious, frustrating fails').  
                - The tone must match the 'Style: {profile.SubKindKey}' (e.g., **aggressive/hype** for highlights, **humorous/self-deprecating** for fails).

            3. **Timestamps/Chapters Section (MULTILINE, OPTIONAL)** - Format MUST be exactly: 'Chapitres dans la vidéo :' followed by the list.
                - If the video contains a compilation of distinct streamers/moments, include them. If not, omit this block.
                - Example: 'Chapitres dans la vidéo :\n0:00 Thebausffs\n3:51 Solarbacca'

            4. **Attribution Block (STRICT CONTENT)** - You **MUST** insert the following line EXACTLY:
                {attributionBlock}
            
            5. **Add a list of streamears twitch link.
                - https://www.twitch.tv/clips(i).description 
                - clips(i).description is elementes on {pkg.Clips}.

            6. **Call To Action & Hashtags (1-2 lines total)** - Include a short call to action (e.g., 'Drop your rank in comments!').  
                - Add **10-15 highly relevant hashtags** at the end of this block.

           

            IMPORTANT:
            - Use REAL newline characters (\n) inside the ""description"" field.
            - Do NOT escape them (no \\n).
            - The overall length should be shorter and punchier than previous attempts.
            - use always double newline btw paragraphs 
            MONTAGE DATA:
            {pkg.ToPromptString()}
    
            VIDEO CONTEXT:
            Game: {profile.KindKey}  
            Style: {profile.SubKindKey} **(Crucial: Use this to determine the tone!)**


            ";
        }
    }
}