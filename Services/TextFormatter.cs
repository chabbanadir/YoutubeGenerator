using System.Text.RegularExpressions;

namespace YoutubeGenerator.Services
{
    public static class TextFormatter
    {
        public static string FixDescription(string input, int maxLength = 4800)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var text = input;

            // Normalize CRLF
            text = text.Replace("\r\n", "\n");

            // Ensure blank line after first sentence
            text = Regex.Replace(text, @"([.!?…])(\s*)([A-Z0-9])", "$1\n\n$3");

            // Ensure "Timestamps:" has blank line before and after
            text = Regex.Replace(text, @"\s*Timestamps:\s*", "\nTimestamps:\n");

            // Ensure bullets start on new lines
            text = Regex.Replace(text, @"\*\s*", "\n* ");

            // Fix cases where timestamps merged
            text = Regex.Replace(text, @"(\d{1,2}:\d{2})\s*-\s*", "$1 - ");

            // Remove accidental multiple blank lines
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            // Format hashtags as a SINGLE line
            // Detect blocks of hashtags (#Something)
            var hashtagPattern = @"(#\w[\w\d]+)";
            var matches = Regex.Matches(text, hashtagPattern);

            if (matches.Count > 3)
            {
                var hashtags = string.Join(" ", matches.Select(m => m.Value));
                // Remove all old hashtag lines
                text = Regex.Replace(text, @"(#\w[\w\d]+[\s]*)+", "");
                // Append clean hashtag line at the end
                text = text.Trim() + "\n\n" + hashtags;
            }

            text = text.Trim();

            // YouTube description hard limit is 5000 characters.
            // Truncate safely at max 4800 characters to prevent YouTube API validation errors.
            if (text.Length > maxLength)
            {
                var truncated = text.Substring(0, maxLength - 10);
                int lastSpace = truncated.LastIndexOf(' ');
                if (lastSpace > maxLength / 2)
                {
                    truncated = truncated.Substring(0, lastSpace);
                }
                text = truncated + "\n...";
            }

            return text;
        }
    }


}
