using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Services
{
    public class ShortsRenderService : IShortsRenderService
    {
        private readonly ILogger<ShortsRenderService> _logger;
        private readonly IWebHostEnvironment _env;

        public ShortsRenderService(
            ILogger<ShortsRenderService> logger,
            IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public async Task<string?> GenerateShortAsync(
            string inputVideoPath,
            string outputDir,
            string kindKey,
            string? streamerName = null)
        {
            if (string.IsNullOrEmpty(inputVideoPath) || !File.Exists(inputVideoPath))
            {
                _logger.LogWarning($"[Shorts] Source video not found: {inputVideoPath}");
                return null;
            }

            Directory.CreateDirectory(outputDir);
            string fileName = $"short_{Path.GetFileNameWithoutExtension(inputVideoPath)}.mp4";
            string outputPath = Path.Combine(outputDir, fileName);

            var kind = (kindKey ?? "lol").Trim().ToLowerInvariant();

            try
            {
                if (kind == "valorant")
                {
                    return await RenderValorantShortAsync(inputVideoPath, outputPath, streamerName);
                }
                else
                {
                    // Default / LoL: Ambient Blurred Background
                    return await RenderAmbientBlurShortAsync(inputVideoPath, outputPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Shorts] Failed to render short for {inputVideoPath}");
                return null;
            }
        }

        private async Task<string?> RenderAmbientBlurShortAsync(string inputVideoPath, string outputPath)
        {
            _logger.LogInformation($"[Shorts] Rendering Ambient Blur 9:16 Short -> {outputPath}");

            string filterComplex =
                "[0:v]scale=1920:1080,split=2[n1][n2];" +
                "[n1]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=25:5,eq=brightness=-0.1:saturation=1.2[bg];" +
                "[n2]scale=1080:608[fg];" +
                "[bg][fg]overlay=0:(1920-608)/2[v]";

            string args = $"-y -i \"{inputVideoPath}\" -filter_complex \"{filterComplex}\" " +
                          $"-map \"[v]\" -map 0:a? -c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 192k -movflags +faststart \"{outputPath}\"";

            bool success = await RunFfmpegCommandAsync(args);
            return (success && File.Exists(outputPath)) ? outputPath : null;
        }

        private async Task<string?> RenderValorantShortAsync(string inputVideoPath, string outputPath, string? streamerName)
        {
            // Probe keyframe for webcam detection
            string probeFrame = Path.Combine(Path.GetTempPath(), $"probe_{Guid.NewGuid():N}.jpg");
            bool extracted = await ExtractProbeFrameAsync(inputVideoPath, probeFrame);

            var webcamCoords = DetectWebcam(probeFrame, streamerName);

            if (File.Exists(probeFrame))
            {
                try { File.Delete(probeFrame); } catch { }
            }

            if (webcamCoords != null && webcamCoords.HasFacecam)
            {
                _logger.LogInformation($"[Shorts] Valorant webcam detected: x={webcamCoords.X}, y={webcamCoords.Y}, w={webcamCoords.W}, h={webcamCoords.H}. Rendering 2-Tier Stack.");
                return await RenderStackedValorantShortAsync(inputVideoPath, outputPath, webcamCoords);
            }
            else
            {
                _logger.LogInformation("[Shorts] No webcam detected in Valorant clip. Falling back to Ambient Blur.");
                return await RenderAmbientBlurShortAsync(inputVideoPath, outputPath);
            }
        }

        private async Task<string?> RenderStackedValorantShortAsync(string inputVideoPath, string outputPath, WebcamCoordinates coords)
        {
            string filterComplex =
                $"[0:v]scale=1920:1080[norm];" +
                $"[norm]split=2[n1][n2];" +
                $"[n1]crop={coords.W}:{coords.H}:{coords.X}:{coords.Y},scale=1080:680:force_original_aspect_ratio=increase,crop=1080:680[cam];" +
                $"[n2]crop=1080:1080:420:0,scale=1080:1240:force_original_aspect_ratio=increase,crop=1080:1240[game];" +
                $"[cam][game]vstack=inputs=2,drawbox=y=676:color=0x0f1923:t=8[v]";

            string args = $"-y -i \"{inputVideoPath}\" -filter_complex \"{filterComplex}\" " +
                          $"-map \"[v]\" -map 0:a? -c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 192k -movflags +faststart \"{outputPath}\"";

            bool success = await RunFfmpegCommandAsync(args);
            return (success && File.Exists(outputPath)) ? outputPath : null;
        }

        private async Task<bool> ExtractProbeFrameAsync(string videoPath, string framePath)
        {
            string args = $"-y -ss 00:00:02 -i \"{videoPath}\" -vframes 1 \"{framePath}\"";
            return await RunFfmpegCommandAsync(args);
        }

        private WebcamCoordinates? DetectWebcam(string framePath, string? streamerName)
        {
            try
            {
                string scriptPath = Path.Combine(_env.ContentRootPath, "Services", "WebcamDetector.py");
                if (!File.Exists(scriptPath))
                {
                    scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Services", "WebcamDetector.py");
                }

                if (!File.Exists(scriptPath))
                {
                    return null;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{scriptPath}\" \"{framePath}\" \"{streamerName ?? ""}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<WebcamCoordinates>(output.Trim(), options);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Shorts] Error during webcam detection probe.");
            }

            return null;
        }

        private async Task<bool> RunFfmpegCommandAsync(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string error = await errorTask;
                    _logger.LogWarning($"[Shorts] FFmpeg failed with exit code {process.ExitCode}: {error.Substring(0, Math.Min(300, error.Length))}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Shorts] Exception running FFmpeg command.");
                return false;
            }
        }

        public class WebcamCoordinates
        {
            public bool HasFacecam { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
        }
    }
}
