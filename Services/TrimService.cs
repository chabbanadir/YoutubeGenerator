using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class TrimService : ITrimService
    {
        private readonly string _ffmpegPath =
            @"C:\Users\nadir\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0-full_build\bin\ffmpeg.exe";

        private string NormalizeTime(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var parts = input.Split(':');

            return parts.Length switch
            {
                1 => $"00:00:{int.Parse(parts[0]):D2}",
                2 => $"00:{int.Parse(parts[0]):D2}:{int.Parse(parts[1]):D2}",
                3 => $"{int.Parse(parts[0]):D2}:{int.Parse(parts[1]):D2}:{int.Parse(parts[2]):D2}",
                _ => input
            };
        }

        // 🔥 ONE SINGLE PERFECT STEP: trim + normalize + reencode
        public async Task<TrimResult> TrimAsync(string inputFile, ClipInfo clip, string outputDir, int index)
        {
            string outputPath = Path.Combine(outputDir, $"{index}.mp4");

            string start = NormalizeTime(clip.Start);
            string end = NormalizeTime(clip.End);

            string timeArgs = "";
            if (!string.IsNullOrEmpty(start)) timeArgs += $"-ss {start} ";
            if (!string.IsNullOrEmpty(end)) timeArgs += $"-to {end} ";

            // ------------------------------------------------------------
            // ⭐ Fast & Accurate FFmpeg pipeline
            // ------------------------------------------------------------
            string args =
                $"-y {timeArgs} -i \"{inputFile}\" " +
                // video filters
                "-vf \"fps=30," +
                    "scale=1920:1080:force_original_aspect_ratio=decrease," +
                    "pad=1920:1080:(ow-iw)/2:(oh-ih)/2," +
                    "format=yuv420p\" " +
                // audio loudness normalization
                "-af \"loudnorm=I=-14:TP=-1.5:LRA=11\" " +
                // fast video re-encode
                "-c:v libx264 -preset superfast -crf 20 -pix_fmt yuv420p " +
                // re-encode audio
                "-c:a aac -b:a 192k -ar 48000 -ac 2 " +
                $"\"{outputPath}\"";

            var (code, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", args, outputDir);

            if (code != 0)
                throw new Exception($"FFmpeg TRIM failed:\n{stderr}");

            return new TrimResult
            {
                File = outputPath,
                Start = clip.Start,
                End = clip.End,
                Description = clip.Description
            };
        }

        public async Task NormalizeVideoOnlyAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Video not found", filePath);

            string tempOut = filePath + ".normalized.mp4";

            string args =
                $"-y -i \"{filePath}\" " +
                "-vf \"fps=30," +
                      "scale=1920:1080:force_original_aspect_ratio=decrease," +
                      "pad=1920:1080:(ow-iw)/2:(oh-ih)/2,format=yuv420p\" " +
                "-af \"loudnorm=I=-14:TP=-1.5:LRA=11\" " +
                "-c:v libx264 -preset superfast -crf 20 -pix_fmt yuv420p " +
                "-c:a aac -b:a 192k -ar 48000 -ac 2 " +
                $"\"{tempOut}\"";

            await ProcessRunner.RunAsync("ffmpeg", args);

            File.Delete(filePath);
            File.Move(tempOut, filePath);
        }

        public async Task<bool> PrependIntroAsync(
             string introPath,
             string firstClipPath,
             string workingDir)
        {
            string concatListPath = Path.Combine(workingDir, "concat_intro_first.txt");
            string tempOutputPath = Path.Combine(workingDir, "0_with_intro.mp4");

            if (!File.Exists(introPath))
                throw new FileNotFoundException("Intro video not found", introPath);

            if (!File.Exists(firstClipPath))
                throw new FileNotFoundException("First trimmed clip not found", firstClipPath);

            // 1. Create concat list
            File.WriteAllText(concatListPath,
                $"file '{introPath.Replace("\\", "/")}'\n" +
                $"file '{firstClipPath.Replace("\\", "/")}'\n");

            // 2. Concat with video re-encode
            string args =
                $"-y -f concat -safe 0 -i \"{concatListPath}\" " +
                "-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p " +
                "-c:a aac -b:a 192k -ar 48000 -ac 2 " +
                $"\"{tempOutputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = Process.Start(psi);
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"FFmpeg concat intro failed: {stderr}");

            // Replace clip0 with concatenated file
            File.Delete(firstClipPath);
            File.Move(tempOutputPath, firstClipPath);

            // 3. Cleanup
            if (File.Exists(concatListPath))
                File.Delete(concatListPath);

            return true;
        }

    }
}
