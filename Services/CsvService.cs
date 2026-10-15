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

                string shortLayout = "ambient";
                if (!string.IsNullOrWhiteSpace(rec.ShortLayout))
                {
                    var layoutVal = rec.ShortLayout.Trim().ToLowerInvariant();
                    if (layoutVal.Contains("stack") || layoutVal == "a" || layoutVal == "cam" || layoutVal == "split")
                        shortLayout = "stacked";
                    else
                        shortLayout = "ambient";
                }

                string rawSubKind = (rec.SubKindKey ?? "").Trim();
                string subKindKey = (!string.IsNullOrWhiteSpace(rawSubKind))
                    ? rawSubKind
                    : (isShort ? "" : "default");

                bool catchAccelerate = false;
                if (!string.IsNullOrWhiteSpace(rec.CatchAccelerate))
                {
                    var accVal = rec.CatchAccelerate.Trim().ToLowerInvariant();
                    catchAccelerate = accVal == "true" || accVal == "1" || accVal == "yes" || accVal == "x2" || accVal == "2";
                }

                string? catchStart = string.IsNullOrWhiteSpace(rec.CatchStart) ? null : rec.CatchStart.Trim();

                var clip = new ClipInfo
                {
                    Link = rec.Link?.Trim(),
                    Description = rec.Description?.Trim(),
                    Start = rec.Start,
                    End = rec.End,
                    KindKey = string.IsNullOrWhiteSpace(rec.KindKey) ? "default" : rec.KindKey.Trim(),
                    SubKindKey = subKindKey,
                    IsShort = isShort,
                    ShortLayout = shortLayout,
                    CatchStart = catchStart,
                    CatchAccelerate = catchAccelerate
                };

                Console.WriteLine(
                    $"CSV: Url={clip.Link}, Start={clip.Start}, End={clip.End}, KindKey={clip.KindKey}, SubKindKey={clip.SubKindKey}, IsShort={clip.IsShort}, ShortLayout={clip.ShortLayout}"
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

            [CsvHelper.Configuration.Attributes.Name("shortlayout", "layout")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? ShortLayout { get; set; }

            [CsvHelper.Configuration.Attributes.Name("catch_start", "catchstart", "catch", "hook", "hook_start", "hookstart")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? CatchStart { get; set; }

            [CsvHelper.Configuration.Attributes.Name("catch_accelerate", "catchaccelerate", "accelerate", "speed_x2", "speedx2", "x2")]
            [CsvHelper.Configuration.Attributes.Optional]
            public string? CatchAccelerate { get; set; }
        }
    }
}
