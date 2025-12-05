using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface ITrimService
    {
        Task<TrimResult> TrimAsync(string inputFile, ClipInfo clip, string outputDir, int increment);
        //Task<string> ConcatIntroWithFirstClipAsync(
        //    string introPath,
        //    string trimmedFolder,
        //    string trimDir,
        //    string ffmpegPath = @"C:\Users\nadir\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0-full_build\bin\ffmpeg.exe");
        Task<bool> PrependIntroAsync(
    string introPath,
    string firstClipPath, // 🌟 Le chemin du clip trimé (ex: .../trim/3.mp4)
    string workingDir);
        Task NormalizeVideoOnlyAsync(string filePath);
    }
}
