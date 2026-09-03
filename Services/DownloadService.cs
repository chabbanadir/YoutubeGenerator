using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class DownloadService : IDownloadService
    {
        private readonly string _ytDlpPath =
            "C:\\Users\\nadir\\AppData\\Local\\Microsoft\\WinGet\\Packages\\yt-dlp.yt-dlp_Microsoft.Winget.Source_8wekyb3d8bbwe\\yt-dlp.exe";

        public DownloadService(ITrimService trimService) { }

        public async Task<DownloadResult> DownloadAsync(ClipInfo clip, string outputDir, int increment)
        {
            string fileName = $"{increment}.mp4";
            string outputPath = Path.Combine(outputDir, fileName);

            // If file already exists, skip downloading
            if (File.Exists(outputPath))
                return new DownloadResult { Success = true, FilePath = outputPath };

            // Stream direct download & remux to MP4 container (no full-video re-encoding)
            string args =
                $"-f \"bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b\" " +
                $"--merge-output-format mp4 " +
                $"-o \"{outputPath}\" " +
                $"\"{clip.Link}\"";

            try
            {
                var (code, stdout, stderr) = await ProcessRunner.RunAsync("yt-dlp", args, outputDir);

                if (code != 0)
                {
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = stderr,
                        ErrorType = stderr.Contains("HTTP Error 416") ? "HTTP_416" : "General_Error"
                    };
                }

                return new DownloadResult
                {
                    Success = true,
                    FilePath = outputPath
                };
            }
            catch (Exception ex)
            {
                return new DownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ErrorType = "Exception"
                };
            }
        }
    }
}
