using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class DownloadService : IDownloadService
    {
        private readonly string _ytDlpPath = "C:\\Users\\nadir\\AppData\\Local\\Microsoft\\WinGet\\Packages\\yt-dlp.yt-dlp_Microsoft.Winget.Source_8wekyb3d8bbwe\\yt-dlp.exe";
        private readonly ITrimService _trimService;

        public DownloadService(ITrimService trimService)
        {
            _trimService = trimService;
        }
        public async Task<DownloadResult> DownloadAsync(ClipInfo clip, string outputDir, int increment)
        {
            string fileName = $"{increment}.mp4"; // or however you name your files
            string outputPath = Path.Combine(outputDir, fileName);

            // Basic check to avoid re-downloading if file exists and is good
            if (File.Exists(outputPath))
            {
                return new DownloadResult { Success = true, FilePath = outputPath };
            }

            // Construct your yt-dlp arguments (Using your existing logic)
            // Note: Added --no-continue to help with 416 errors sometimes, 
            // but for now let's just focus on capturing the error.
            string args = $"-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" " +
                          $"-o \"{outputPath}\" " +
                          $"\"{clip.Link}\"";

            try
            {
                var (code, stdout, stderr) = await ProcessRunner.RunAsync(_ytDlpPath, args, outputDir);

                if (code != 0)
                {
                    // Return failure instead of throwing
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = stderr,
                        // specific check for your current error
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
