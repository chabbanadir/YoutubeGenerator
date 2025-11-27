using CsvHelper;
using System.Formats.Asn1;
using System.Globalization;
using System.Transactions;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class CsvService : ICsvService
    {
        public async Task<List<ClipInfo>> ParseCsvAsync(Stream csvStream)
        {
            using var reader = new StreamReader(csvStream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var records = new List<ClipInfo>();

            await foreach (var rec in csv.GetRecordsAsync<RawCsvRecord>())
            {
                var clip = new ClipInfo
                {
                    Link = rec.link?.Trim(),
                    Description = rec.description?.Trim(),
                    Start = rec.start,
                    End = rec.end
                };

                Console.WriteLine($"CSV: Url={clip.Link}, Start={clip.Start}, End={clip.End}");
                records.Add(clip);
            }

            return records;
        }


        private class RawCsvRecord
        {
            public string? link { get; set; }
            public string? description { get; set; }
            public string? start { get; set; }
            public string? end { get; set; }
        }
    }
}


