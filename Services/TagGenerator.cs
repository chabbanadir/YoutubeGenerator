using System;
using System.Collections.Generic;
using System.Linq;

namespace YoutubeGenerator.Services
{
    public static class TagGenerator
    {
        public static List<string> GenerateTags(string kindKey, string subKindKey, IEnumerable<string?>? streamers)
        {
            var tags = new List<string>();
            var kind = (kindKey ?? "").ToLower().Trim();
            var subKind = (subKindKey ?? "").ToLower().Trim();

            if (kind == "lol")
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
            else if (kind == "valorant")
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
                        if (kind == "lol")
                            tags.Add($"{clean} LoL");
                        else if (kind == "valorant")
                            tags.Add($"{clean} Valorant");
                    }
                }
            }

            // Enforce YouTube tag limits:
            // YouTube maximum limit across all tags combined is 500 characters.
            // Truncate safely at 450 characters.
            var finalTags = new List<string>();
            int totalChars = 0;

            foreach (var tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
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
