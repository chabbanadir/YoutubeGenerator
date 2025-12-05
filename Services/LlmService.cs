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

            // --- 1. Generate Title ---
            var titleRequest = new GenerateContentRequest();
            titleRequest.AddText(BuildTitlePrompt(package, profile));
            titleRequest.UseJsonMode<TitleResult>();

            var titleResponse = await _model.GenerateContentAsync(titleRequest);
            var titleResult = titleResponse.ToObject<TitleResult>();
            result.Title = titleResult?.Title ?? "Default Title";


            // --- 2. Generate Description ---
            var socialMediaBlock = BuildSocialMediaBlock(profile);
            var descRequest = new GenerateContentRequest();
            descRequest.AddText(BuildDescriptionPrompt(package, profile, socialMediaBlock));
            descRequest.UseJsonMode<DescriptionResult>();

            var descResponse = await _model.GenerateContentAsync(descRequest);
            var descResult = descResponse.ToObject<DescriptionResult>();
            result.Description = descResult?.Description ?? "Default Description";

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

The title must be concise and engaging, including key players or themes from the timestamps. **Include a few relevant emojis in the title.**

MONTAGE DATA:
{pkg.ToPromptString()}

Game: {profile.KindKey}, Style: {profile.SubKindKey}
";
        }

        // ⭐️ Modified signature to accept the pre-formatted social media block
        private string BuildDescriptionPrompt(MontageItem pkg, MontageProfile profile, string socialMediaBlock)
        {
            var socialMediaPlaceholder = "###SOCIAL_MEDIA_LINKS###";

            return $@"
You are an AI system generating YouTube descriptions with **strict formatting requirements**.
Return **ONLY JSON**, with the structure:

{{
  ""description"": ""string""
}}

### DESCRIPTION FORMAT RULES (STRICT)

Your generated description MUST follow **this exact structure**, using **real newline characters (\n)**:

1. **Opening Hook (1–2 lines)**  
   - Very engaging  
   - Includes at least **2 relevant emojis**  
   - Game-themed wording

2. **Short Summary (1–2 lines)**  
   - General explanation of what the viewer will see  

3. **Timestamps Section (MULTILINE)**  
   MUST be formatted exactly:
Timestamps:
00:00 - First event
00:25 - Second event
01:12 - Another play
- Each timestamp MUST be on its own line  
- NO '*' bullets, NO inline timestamps  
- STRICT multiline formatting

4. **Social Media Block**  
Insert the content here: {socialMediaPlaceholder}  
If the provided block is empty, OMIT this section entirely.  
Social media block must stay on MULTIPLE LINES.

5. **Call To Action (1–2 lines)**  
Example:  
""Be sure to like, comment, and subscribe for more epic highlights!""

6. **Hashtags Section (1 line)**  
- All hashtags on ONE LINE  
- At least **15 highly relevant hashtags**  
- No line breaks inside the hashtags line

IMPORTANT:
- Use REAL newline characters (\n) inside the ""description"" field.
- Do NOT escape them (no \\n).
- This will be placed directly into YouTube description.

MONTAGE DATA:
{pkg.ToPromptString()}

VIDEO CONTEXT:
Game: {profile.KindKey}  
Style: {profile.SubKindKey}

SOCIAL MEDIA LINKS PROVIDED:
{socialMediaBlock}

EXAMPLE (FORMAT ONLY — NOT CONTENT):
---
🚀 Watch the most insane plays from the Rift! 🔥😱  
These highlights showcase clutch outplays and high-level mechanics.

Timestamps:
00:00 - Massive teamfight on Dragon
00:34 - Perfect flash combo from Faker
01:22 - Pentakill cleanup by Viper

{socialMediaPlaceholder}

Be sure to leave a like and subscribe for more League of Legends content!

#LeagueOfLegends #LoLMontage #LOLHighlights #Gaming #Esports #ClutchPlays #LeaguePlays #Ranked #Challenger #Season14 #RiftHighlights #ProPlayers #BestPlays #EpicMoments #ChampionPlays
---
";
        }
    }
}