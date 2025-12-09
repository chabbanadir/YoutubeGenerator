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

            // ⭐ VERY IMPORTANT ⭐
            // FORCE re-encode => guaranteed uniform audio/video format
            string args =
                $"-f \"bv*+ba/b\" " +
                $"--merge-output-format mp4 " +
                $"--recode-video mp4 " +
                $"--postprocessor-args \"-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -vf scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2\" " +
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
