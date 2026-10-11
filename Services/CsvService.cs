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
                HeaderValidated = null,
                MissingFieldFound = null,
                PrepareHeaderForMatch = args => args.Header.ToLower().Replace("_", "").Replace(" ", ""),
            };

            using var csv = new CsvReader(reader, config);

            var records = new List<ClipInfo>();

            await foreach (var rec in csv.GetRecordsAsync<RawCsvRecord>())
            {
                // Skip rows missing the URL
                if (string.IsNullOrWhiteSpace(rec.Link))
                    continue;

                bool isShort = false;
                if (!string.IsNullOrWhiteSpace(rec.IsShort))
                {
                    var val = rec.IsShort.Trim().ToLowerInvariant();
                    isShort = val == "true" || val == "1" || val == "yes" || val == "short";
                }

                var clip = new ClipInfo
                {
                    Link = rec.Link?.Trim(),
                    Description = rec.Description?.Trim(),
                    Start = rec.Start,
                    End = rec.End,
                    KindKey = rec.KindKey ?? "default",
                    SubKindKey = rec.SubKindKey ?? "default",
                    IsShort = isShort
                };

                Console.WriteLine(
                    $"CSV: Url={clip.Link}, Start={clip.Start}, End={clip.End}, KindKey={clip.KindKey}, SubKindKey={clip.SubKindKey}, IsShort={clip.IsShort}"
                );

                records.Add(clip);
            }

            return records;
        }

        // ⚠ Flexible mapping to support both 'Kind' and 'KindKey', 'IsShort' or 'Short'
        private class RawCsvRecord
        {
            [CsvHelper.Configuration.Attributes.Name("link", "url")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? Link { get; set; }

            [CsvHelper.Configuration.Attributes.Name("description", "desc", "title")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? Description { get; set; }

            [CsvHelper.Configuration.Attributes.Name("start")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? Start { get; set; }

            [CsvHelper.Configuration.Attributes.Name("end")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? End { get; set; }

            [CsvHelper.Configuration.Attributes.Name("kind", "kindkey", "game")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? KindKey { get; set; }

            [CsvHelper.Configuration.Attributes.Name("subkind", "subkindkey", "streamer")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? SubKindKey { get; set; }

            [CsvHelper.Configuration.Attributes.Name("isshort", "short")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? IsShort { get; set; }
        }
    }
}
