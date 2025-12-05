using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class CsvService : ICsvService
    {
        public async Task<List<ClipInfo>> ParseCsvAsync(Stream csvStream)
        {
            using var reader = new StreamReader(csvStream);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                TrimOptions = TrimOptions.Trim,
                IgnoreBlankLines = true,
                PrepareHeaderForMatch = args => args.Header.ToLower(),   // 👌 Case-insensitive matching
            };

            using var csv = new CsvReader(reader, config);

            var records = new List<ClipInfo>();

            await foreach (var rec in csv.GetRecordsAsync<RawCsvRecord>())
            {
                // Skip rows missing the URL
                if (string.IsNullOrWhiteSpace(rec.Link))
                    continue;

                var clip = new ClipInfo
                {
                    Link = rec.Link?.Trim(),
                    Description = rec.Description?.Trim(),
                    Start = rec.Start,
                    End = rec.End,
                    KindKey = rec.KindKey,
                    SubKindKey = rec.SubKindKey
                };

                Console.WriteLine(
                    $"CSV: Url={clip.Link}, Start={clip.Start}, End={clip.End}, KindKey={clip.KindKey}, SubKindKey={clip.SubKindKey}"
                );

                records.Add(clip);
            }

            return records;
        }

        // ⚠ MUST MATCH THE CSV HEADER EXACTLY (case-insensitive is now allowed)
        private class RawCsvRecord
        {
            public string? Link { get; set; }
            public string? Description { get; set; }
            public string? Start { get; set; }
            public string? End { get; set; }
            public string? KindKey { get; set; }
            public string? SubKindKey { get; set; }
        }
    }
}
