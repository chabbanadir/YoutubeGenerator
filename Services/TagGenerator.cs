using System;
using System.Collections.Generic;
using System.Linq;

namespace YoutubeGenerator.Services
{
    public static class TagGenerator
    {
        public static List<string> GenerateTags(
            string kindKey,
            string subKindKey,
            IEnumerable<string?>? streamers,
            string? category = null,
            IEnumerable<string>? customDefaultTags = null)
        {
            var tags = new List<string>();
            var kind = (kindKey ?? "").ToLower().Trim();
            var subKind = (subKindKey ?? "").ToLower().Trim();

            // Determine effective category
            var cat = (category ?? "").ToLower().Trim();
            if (string.IsNullOrWhiteSpace(cat) || cat == "gaming")
            {
                if (kind.Contains("val")) cat = "valorant";
                else if (kind == "lol" || kind.Contains("league")) cat = "lol";
                else if (kind.Contains("chat") || kind.Contains("irl")) cat = "just_chatting";
                else if (kind.Contains("meme") || kind.Contains("humor")) cat = "meme";
                else cat = "gaming";
            }

            // Priority 1: User's custom default tags from channel/subkind profile
            if (customDefaultTags != null)
            {
                tags.AddRange(customDefaultTags.Where(t => !string.IsNullOrWhiteSpace(t)));
            }

            if (cat == "lol")
            {
                tags.AddRange(new[]
                {
                    "League of Legends",
                    "LoL",
                    "LoL Highlights",
                    "League of Legends Highlights",
                    "LoL Clips",
                    "League Highlights",
                    "LoL Montage",
                    "Riot Games",
                    "LoL Best Moments",
                    "LoL Outplays",
                    "LoL Funny Moments",
                    "League SoloQ"
                });

                if (subKind.Contains("penta"))
                {
                    tags.Add("LoL Pentakill");
                    tags.Add("Pentakill Montage");
                    tags.Add("1v5 Outplay");
                }
                else if (subKind.Contains("aram"))
                {
                    tags.Add("LoL ARAM");
                    tags.Add("ARAM Highlights");
                    tags.Add("Howling Abyss");
                    tags.Add("ARAM Outplay");
                    tags.Add("ARAM Pentakill");
                    tags.Add("ARAM 2026");
                }
                else if (subKind.Contains("tourn") || subKind.Contains("esport") || subKind.Contains("pro"))
                {
                    tags.Add("LoL Esports");
                    tags.Add("Pro Play Highlights");
                    tags.Add("Competitive LoL");
                    tags.Add("LoL Tournament");
                }
            }
            else if (cat == "valorant")
            {
                tags.AddRange(new[]
                {
                    "Valorant",
                    "Valorant Highlights",
                    "Valorant Clips",
                    "Valorant Montage",
                    "Valorant Gameplay",
                    "Riot Games",
                    "Valorant Best Moments",
                    "Valorant Clutch",
                    "Valorant Radiant",
                    "Valorant Funny Moments"
                });

                if (subKind.Contains("ace"))
                {
                    tags.Add("Valorant Ace");
                    tags.Add("Vandal Ace");
                    tags.Add("1v5 Clutch");
                }
                else if (subKind.Contains("tourn") || subKind.Contains("vct") || subKind.Contains("esport") || subKind.Contains("pro"))
                {
                    tags.Add("VCT Highlights");
                    tags.Add("Valorant Champions");
                    tags.Add("Valorant Masters");
                    tags.Add("Pro Valorant");
                }
            }
            else if (cat == "just_chatting")
            {
                tags.AddRange(new[]
                {
                    "Just Chatting",
                    "Twitch Highlights",
                    "Twitch Clips",
                    "Streamer Moments",
                    "Funniest Twitch Moments",
                    "IRL Clips",
                    "Twitch Drama",
                    "Streamer Reactions",
                    "Best Twitch Clips",
                    "Just Chatting Clips",
                    "Twitch Compilation",
                    "Livestream Highlights",
                    "Twitch"
                });
            }
            else if (cat == "meme")
            {
                tags.AddRange(new[]
                {
                    "Memes",
                    "Dank Memes",
                    "Meme Compilation",
                    "Try Not To Laugh",
                    "Funny Clips",
                    "Viral Memes",
                    "Internet Memes",
                    "Humor",
                    "Funny Moments",
                    "Clean Memes",
                    "Best Memes",
                    "Meme Review",
                    "Trending Memes"
                });
            }
            else
            {
                tags.Add("Gaming");
                tags.Add($"{kind} Highlights");
                tags.Add($"{kind} Clips");
                tags.Add($"{kind} Gameplay");
                tags.Add("Gaming Compilation");
            }

            // Append top featured creators (up to 8 clean streamer handles)
            if (streamers != null)
            {
                foreach (var s in streamers.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(8))
                {
                    var clean = s.Trim().TrimStart('@');
                    if (clean.Length > 2)
                    {
                        tags.Add(clean);
                        if (cat == "lol")
                            tags.Add($"{clean} LoL");
                        else if (cat == "valorant")
                            tags.Add($"{clean} Valorant");
                        else if (cat == "just_chatting")
                            tags.Add($"{clean} Twitch");
                    }
                }
            }

            return EnforceTagLimits(tags);
        }

        public static List<string> GenerateShortTags(
            string kindKey,
            string? category,
            string streamerName,
            IEnumerable<string>? customDefaultTags = null)
        {
            var tags = new List<string> { "Shorts", "YouTube Shorts" };
            var cat = (category ?? "").ToLower().Trim();
            var kind = (kindKey ?? "").ToLower().Trim();

            if (string.IsNullOrWhiteSpace(cat) || cat == "gaming")
            {
                if (kind.Contains("val")) cat = "valorant";
                else if (kind == "lol" || kind.Contains("league")) cat = "lol";
                else if (kind.Contains("chat") || kind.Contains("irl")) cat = "just_chatting";
                else if (kind.Contains("meme") || kind.Contains("humor")) cat = "meme";
                else cat = "gaming";
            }

            if (customDefaultTags != null)
            {
                tags.AddRange(customDefaultTags.Where(t => !string.IsNullOrWhiteSpace(t)));
            }

            var cleanStreamer = (streamerName ?? "").Trim().TrimStart('@');
            if (cleanStreamer.Length > 2)
            {
                tags.Add(cleanStreamer);
            }

            if (cat == "lol")
            {
                tags.AddRange(new[] { "LoL Shorts", "League of Legends", "LoL Clips", "League Highlights", "Outplay" });
                if (cleanStreamer.Length > 2) tags.Add($"{cleanStreamer} LoL");
            }
            else if (cat == "valorant")
            {
                tags.AddRange(new[] { "VALORANT Shorts", "VALORANT", "Valorant Clips", "Valorant Highlights", "Radiant" });
                if (cleanStreamer.Length > 2) tags.Add($"{cleanStreamer} Valorant");
            }
            else if (cat == "just_chatting")
            {
                tags.AddRange(new[] { "Just Chatting", "Twitch Clips", "Twitch Highlights", "Streamer Moments", "IRL", "Funniest Moments" });
                if (cleanStreamer.Length > 2) tags.Add($"{cleanStreamer} Twitch");
            }
            else if (cat == "meme")
            {
                tags.AddRange(new[] { "Memes", "Dank Memes", "Funny Shorts", "Try Not To Laugh", "Viral", "Meme Compilation" });
            }
            else
            {
                tags.AddRange(new[] { $"{kind} Shorts", "Gaming Shorts", "Gaming Clips", "Highlights" });
            }

            return EnforceTagLimits(tags);
        }

        private static List<string> EnforceTagLimits(IEnumerable<string> rawTags)
        {
            // Enforce YouTube tag limits:
            // YouTube maximum limit across all tags combined is 500 characters.
            // Truncate safely at 450 characters.
            var finalTags = new List<string>();
            int totalChars = 0;

            foreach (var tag in rawTags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (totalChars + tag.Length + 1 <= 450)
                {
                    finalTags.Add(tag);
                    totalChars += tag.Length + 1;
                }
                else
                {
                    break;
                }
            }

            return finalTags;
        }
    }
}
