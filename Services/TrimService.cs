using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class TrimService : ITrimService
    {
        // Assure-toi que ce chemin est correct via 'where ffmpeg' dans ton terminal
        private readonly string _ffmpegPath = @"C:\Users\nadir\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0-full_build\bin\ffmpeg.exe";

        private string NormalizeTimeString(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var parts = input.Split(':');

            if (parts.Length == 1)
            {
                // seconds only
                int s = int.Parse(parts[0]);
                return $"00:00:{s:D2}";
            }

            if (parts.Length == 2)
            {
                // mm:ss
                int m = int.Parse(parts[0]);
                int s = int.Parse(parts[1]);
                return $"00:{m:D2}:{s:D2}";
            }

            if (parts.Length == 3)
            {
                // hh:mm:ss
                return $"{int.Parse(parts[0]):D2}:{int.Parse(parts[1]):D2}:{int.Parse(parts[2]):D2}";
            }

            return input;
        }

        public async Task<TrimResult> TrimAsync(string inputFile, ClipInfo clip, string outputDir, int index)
        {
            string finalPath = Path.Combine(outputDir, $"{index}.mp4");

            string startArg = NormalizeTimeString(clip.Start);
            string endArg = NormalizeTimeString(clip.End);

            List<string> parts = new();

            if (!string.IsNullOrWhiteSpace(startArg))
                parts.Add($"-ss {startArg}");

            if (!string.IsNullOrWhiteSpace(endArg))
                parts.Add($"-to {endArg}");

            parts.Add($"-i \"{inputFile}\"");
            parts.Add($"-c copy \"{finalPath}\"");

            string args = string.Join(" ", parts);
            Console.WriteLine($"[Arguments] : {args}");

            var (code, stdout, stderr) = await ProcessRunner.RunAsync(_ffmpegPath, args, outputDir);

            if (code != 0)
                throw new Exception($"FFmpeg trim failed for clip {index}. Stderr: {stderr}");

            return new TrimResult
            {
                File = finalPath,
                Start = clip.Start,
                End = clip.End,
                Description = clip.Description,
                
            };
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

            File.WriteAllText(concatListPath,
                $"file '{introPath.Replace("\\", "/")}'\n" +
                $"file '{firstClipPath.Replace("\\", "/")}'\n");

            // 3. Build ffmpeg process
            var args =
                $"-y -f concat -safe 0 -i \"{concatListPath}\" " +
                "-c:v h264_nvenc -preset p4 -cq 23 -rc vbr_hq " + // GPU Encoding
                "-acodec aac -pix_fmt yuv420p " +
                $"\"{tempOutputPath}\"";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var process = Process.Start(psi);
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"FFmpeg concat intro failed: {stderr}");
            // 4. Après succès : Remplacer le fichier original (firstClipPath) par le nouveau fichier
            File.Delete(firstClipPath);
            File.Move(tempOutputPath, firstClipPath);
            await NormalizeOneAsync(firstClipPath);
            File.Delete(concatListPath);

            return true;
        }
        //public async Task<string> ConcatIntroWithFirstClipAsync(
        //    string introPath,
        //    string rowDir,
        //    string trimDir,
        //    string ffmpegPath = "")
        //{
        //    // 1. Ensure both files exist
        //    string firstClip = Path.Combine(rowDir, "0.mp4");
        //    ffmpegPath = _ffmpegPath;
        //    if (!File.Exists(introPath))
        //        throw new FileNotFoundException("Intro video not found", introPath);

        //    if (!File.Exists(firstClip))
        //        throw new FileNotFoundException("First raw clip 0.mp4 not found", firstClip);

        //    // 2. Create concat file
        //    string concatListPath = Path.Combine(trimDir, "concat_intro_0.txt");
        //    string outputPath = Path.Combine(trimDir, "0.mp4");

        //    File.WriteAllText(concatListPath,
        //        $"file '{introPath.Replace("\\", "/")}'\n" +
        //        $"file '{firstClip.Replace("\\", "/")}'\n");

        //    // 3. Build ffmpeg process
        //    var args =
        //        $"-y -f concat -safe 0 -i \"{concatListPath}\" " +
        //        "-vcodec libx264 -acodec aac -pix_fmt yuv420p " +
        //        $"\"{outputPath}\"";

        //    var psi = new System.Diagnostics.ProcessStartInfo
        //    {
        //        FileName = ffmpegPath,
        //        Arguments = args,
        //        CreateNoWindow = true,
        //        RedirectStandardOutput = true,
        //        RedirectStandardError = true,
        //        UseShellExecute = false
        //    };

        //    var process = Process.Start(psi);
        //    string stderr = await process.StandardError.ReadToEndAsync();
        //    await process.WaitForExitAsync();

        //    if (process.ExitCode != 0)
        //        throw new Exception($"FFmpeg concat intro failed: {stderr}");

        //    // 4. Replace original 0.mp4 with new combined file
        //    string backup = Path.Combine(rowDir, "0_backup.mp4");
        //    File.Move(firstClip, backup, true);
        //    File.Move(outputPath, firstClip, true);
        //    File.Delete(backup);

        //    // Cleanup
        //    File.Delete(concatListPath);

        //    return firstClip; // returns the new 0.mp4
        //}

        public async Task NormalizeOneAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Video not found", filePath);

            string tempOut = filePath + ".normalized.mp4";

            // FFmpeg command for normalization
            string args =
                    $"-y -i \"{filePath}\" " +
                    "-vf \"scale=1920:1080:force_original_aspect_ratio=decrease," +
                          "pad=1920:1080:(ow-iw)/2:(oh-ih)/2,fps=30\" " +
                    "-c:v h264_nvenc -preset p4 -cq 23 -rc vbr_hq " +
                    "-pix_fmt yuv420p " +
                    "-c:a aac -b:a 192k -ac 2 " +
                    "-fflags +genpts " +
                    $"\"{tempOut}\"";

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath, // Use your injected ffmpeg path
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
                throw new Exception($"Normalization failed for {filePath}: {stderr}");

            // Replace original file
            File.Delete(filePath);
            File.Move(tempOut, filePath);
        }


    }
}