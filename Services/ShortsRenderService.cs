using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class ShortsRenderService : IShortsRenderService
    {
        private readonly ILogger<ShortsRenderService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IThumbnailService _thumbnailService;
        private readonly IRenderTrackerService? _renderTracker;

        public ShortsRenderService(
            ILogger<ShortsRenderService> logger,
            IWebHostEnvironment env,
            IThumbnailService thumbnailService,
            IRenderTrackerService? renderTracker = null)
        {
            _logger = logger;
            _env = env;
            _thumbnailService = thumbnailService;
            _renderTracker = renderTracker;
        }

        public async Task<string?> GenerateShortAsync(
            string inputVideoPath,
            string outputDir,
            string kindKey,
            string? streamerName = null,
            string? subKindKey = null,
            string? shortLayout = "ambient")
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
            var layout = (shortLayout ?? "ambient").Trim().ToLowerInvariant();
            bool wantsStacked = layout.Contains("stack") || layout == "a" || layout == "cam" || layout == "split";

            try
            {
                _renderTracker?.UpdateShortProgress(30, "Encoding 9:16 Video");
                string layoutLabel = wantsStacked ? "Option A (Stacked Cam)" : "Option B (Ambient Blur)";
                _renderTracker?.AddLog($"🎬 [Shorts Render] Encoding 9:16 {layoutLabel} for @{streamerName ?? "Gamer"} ({kind}/{subKindKey})", "ffmpeg");

                string? renderedPath = null;
                if (wantsStacked)
                {
                    _logger.LogInformation($"[Shorts] Option A (Stacked Facecam + Centered Action) requested for @{streamerName} ({kind}/{subKindKey})");
                    renderedPath = await RenderStackedShortAsync(inputVideoPath, outputPath, streamerName, kindKey, subKindKey);
                }
                else
                {
                    _logger.LogInformation($"[Shorts] Option B (Ambient Blur 16:9 + Badges) requested for @{streamerName} ({kind}/{subKindKey})");
                    renderedPath = await RenderAmbientBlurShortAsync(inputVideoPath, outputPath, streamerName, kindKey, subKindKey);
                }

                if (!string.IsNullOrEmpty(renderedPath) && File.Exists(renderedPath))
                {
                    _renderTracker?.UpdateShortProgress(65, "Generating 9:16 Thumbnail");
                    _renderTracker?.AddLog($"✅ [Shorts Render] 9:16 video rendered: {fileName} ({new FileInfo(renderedPath).Length / (1024 * 1024)} MB)", "success");

                    // Auto-generate matching 9:16 high-CTR thumbnail during the rendering process
                    try
                    {
                        await _thumbnailService.GenerateShortThumbnailAsync(renderedPath, kind, streamerName, subKindKey, outputDir);
                    }
                    catch (Exception exThumb)
                    {
                        _logger.LogWarning(exThumb, $"[Shorts] Could not auto-generate thumbnail for {renderedPath}");
                    }

                    return renderedPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Shorts] Failed to render short for {inputVideoPath}");
                _renderTracker?.AddLog($"❌ [Shorts Render] Error rendering {fileName}: {ex.Message}", "error");
                return null;
            }
        }

        private async Task<string?> RenderAmbientBlurShortAsync(
            string inputVideoPath,
            string outputPath,
            string? streamerName = null,
            string? kindKey = null,
            string? subKindKey = null)
        {
            _logger.LogInformation($"[Shorts] Rendering Ambient Blur 9:16 Short -> {outputPath}");

            string overlaysDir = Path.Combine(_env.ContentRootPath, "workspace", "ressources", "overlays");
            string likeSubscribePath = Path.Combine(overlaysDir, "like_subscribe.png");

            string badgesDir = Path.Combine(_env.ContentRootPath, "workspace", "ressources", "thumbnails", "badges");
            string fontsDir = Path.Combine(_env.ContentRootPath, "workspace", "ressources", "thumbnails", "fonts");

            var kind = (kindKey ?? "lol").ToLowerInvariant();
            var subKind = (subKindKey ?? "normal").ToLowerInvariant();
            var streamer = (streamerName ?? "Streamer").Trim().TrimStart('@');

            // Resolve badge - strictly based on subKind
            string? badgePath = null;
            if (kind.Contains("val"))
            {
                string aceBadge = Path.Combine(badgesDir, "ace.png");
                if (subKind.Contains("ace") && File.Exists(aceBadge))
                    badgePath = aceBadge;
            }
            else
            {
                string pentaBadge = Path.Combine(badgesDir, "pentakill.png");
                if (subKind.Contains("penta") && File.Exists(pentaBadge))
                    badgePath = pentaBadge;
            }

            // Resolve font
            string beaufortFont = Path.Combine(fontsDir, "beaufort_heavy.otf");
            string boldFont = Path.Combine(fontsDir, "font_bold.ttf");
            string fontPath = (kind.Contains("lol") && File.Exists(beaufortFont))
                ? beaufortFont
                : (File.Exists(boldFont) ? boldFont : "Arial");

            var inputs = new List<string> { $"-i \"{inputVideoPath}\"" };
            int nextIdx = 1;
            int? likeSubIdx = null;
            int? badgeIdx = null;

            if (File.Exists(likeSubscribePath))
            {
                inputs.Add($"-i \"{likeSubscribePath}\"");
                likeSubIdx = nextIdx++;
            }

            if (!string.IsNullOrEmpty(badgePath) && File.Exists(badgePath))
            {
                inputs.Add($"-i \"{badgePath}\"");
                badgeIdx = nextIdx++;
            }

            var filter = new List<string>();
            filter.Add("[0:v]scale=1920:1080,split=2[n1][n2]");
            filter.Add("[n1]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=25:5,eq=brightness=-0.1:saturation=1.2[bg]");
            filter.Add("[n2]scale=1080:608[fg]");
            filter.Add("[bg][fg]overlay=0:(1920-608)/2[v1]");
            string currentLayer = "[v1]";

            string escapedFont = fontPath.Replace("\\", "/").Replace(":", "\\:");
            string escapedStreamer = streamer.Replace("'", "\\'").Replace(":", "\\:").Replace("%", "\\%");

            // Top Game Badge + Streamer Handle baked into video
            if (badgeIdx.HasValue)
            {
                filter.Add($"[{badgeIdx.Value}:v]scale=440:-1[badge]");
                filter.Add($"{currentLayer}[badge]overlay=(W-w)/2:120[v_badge]");
                filter.Add($"[v_badge]drawtext=fontfile='{escapedFont}':text='@{escapedStreamer}':fontcolor=white:fontsize=46:borderw=6:bordercolor=black:x=(w-text_w)/2:y=350[v_top]");
                currentLayer = "[v_top]";
            }
            else
            {
                filter.Add($"{currentLayer}drawtext=fontfile='{escapedFont}':text='@{escapedStreamer}':fontcolor=white:fontsize=50:borderw=6:bordercolor=black:x=(w-text_w)/2:y=240[v_top]");
                currentLayer = "[v_top]";
            }

            // Bottom Like & Subscribe Sticker banner baked into video (2s midpoint pop)
            if (likeSubIdx.HasValue)
            {
                double duration = await GetVideoDurationAsync(inputVideoPath);
                double tStart = Math.Max(1.0, (duration / 2.0) - 1.0);
                double tEnd = Math.Min(duration, tStart + 2.0);
                string tStartStr = tStart.ToString("0.0", CultureInfo.InvariantCulture);
                string tEndStr = tEnd.ToString("0.0", CultureInfo.InvariantCulture);

                filter.Add($"[{likeSubIdx.Value}:v]scale=680:-1[sub]");
                filter.Add($"{currentLayer}[sub]overlay=(W-w)/2:1500:enable='between(t,{tStartStr},{tEndStr})'[v]");
                currentLayer = "[v]";
            }

            string filterComplex = string.Join(";", filter);
            string inputArgs = string.Join(" ", inputs);

            string args = $"-y {inputArgs} -filter_complex \"{filterComplex}\" " +
                          $"-map \"{currentLayer}\" -map 0:a? -c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 192k -movflags +faststart \"{outputPath}\"";

            bool success = await RunFfmpegCommandAsync(args);
            return (success && File.Exists(outputPath)) ? outputPath : null;
        }

        private async Task<string?> RenderStackedShortAsync(
            string inputVideoPath,
            string outputPath,
            string? streamerName,
            string? kindKey = null,
            string? subKindKey = null)
        {
            // Probe keyframe across multiple timestamps (second 3, 5, 2) to reliably catch facecam
            WebcamCoordinates? webcamCoords = null;
            int[] probeOffsets = new[] { 3, 5, 2 };

            foreach (var sec in probeOffsets)
            {
                string probeFrame = Path.Combine(Path.GetTempPath(), $"probe_{sec}_{Guid.NewGuid():N}.jpg");
                bool extracted = await ExtractProbeFrameAsync(inputVideoPath, probeFrame, sec);
                if (extracted && File.Exists(probeFrame))
                {
                    try
                    {
                        var coords = DetectWebcam(probeFrame, streamerName);
                        if (coords != null && coords.HasFacecam)
                        {
                            webcamCoords = coords;
                            break;
                        }
                    }
                    finally
                    {
                        try { File.Delete(probeFrame); } catch { }
                    }
                }
            }

            if (webcamCoords != null && webcamCoords.HasFacecam)
            {
                _logger.LogInformation($"[Shorts] Webcam detected for @{streamerName} ({kindKey}): x={webcamCoords.X}, y={webcamCoords.Y}, w={webcamCoords.W}, h={webcamCoords.H}. Rendering Option A (Stacked).");
                return await RenderStackedVideoAsync(inputVideoPath, outputPath, webcamCoords, kindKey);
            }
            else
            {
                _logger.LogInformation($"[Shorts] No webcam detected in {kindKey} clip for @{streamerName}. Falling back to Option B (Ambient Blur).");
                return await RenderAmbientBlurShortAsync(inputVideoPath, outputPath, streamerName, kindKey, subKindKey);
            }
        }

        private async Task<string?> RenderStackedVideoAsync(
            string inputVideoPath,
            string outputPath,
            WebcamCoordinates coords,
            string? kindKey = null)
        {
            string overlaysDir = Path.Combine(_env.ContentRootPath, "workspace", "ressources", "overlays");
            string likeSubscribePath = Path.Combine(overlaysDir, "like_subscribe.png");
            bool hasOverlay = File.Exists(likeSubscribePath);

            string filterComplex;
            string inputArgs;

            string dividerColor = (kindKey ?? "").ToLowerInvariant().Contains("val") ? "0x0f1923" : "0x010a13";

            if (hasOverlay)
            {
                double duration = await GetVideoDurationAsync(inputVideoPath);
                double tStart = Math.Max(1.0, (duration / 2.0) - 1.0);
                double tEnd = Math.Min(duration, tStart + 2.0);
                string tStartStr = tStart.ToString("0.0", CultureInfo.InvariantCulture);
                string tEndStr = tEnd.ToString("0.0", CultureInfo.InvariantCulture);

                inputArgs = $"-y -i \"{inputVideoPath}\" -i \"{likeSubscribePath}\"";
                filterComplex =
                    $"[0:v]scale=1920:1080[norm];" +
                    $"[norm]split=2[n1][n2];" +
                    $"[n1]crop={coords.W}:{coords.H}:{coords.X}:{coords.Y},scale=1080:680:force_original_aspect_ratio=increase,crop=1080:680[cam];" +
                    $"[n2]crop=1080:1080:420:0,scale=1080:1240:force_original_aspect_ratio=increase,crop=1080:1240[game];" +
                    $"[cam][game]vstack=inputs=2,drawbox=y=676:color={dividerColor}:t=8[v1];" +
                    $"[1:v]scale=680:-1[sub];" +
                    $"[v1][sub]overlay=(W-w)/2:1500:enable='between(t,{tStartStr},{tEndStr})'[v]";
            }
            else
            {
                inputArgs = $"-y -i \"{inputVideoPath}\"";
                filterComplex =
                    $"[0:v]scale=1920:1080[norm];" +
                    $"[norm]split=2[n1][n2];" +
                    $"[n1]crop={coords.W}:{coords.H}:{coords.X}:{coords.Y},scale=1080:680:force_original_aspect_ratio=increase,crop=1080:680[cam];" +
                    $"[n2]crop=1080:1080:420:0,scale=1080:1240:force_original_aspect_ratio=increase,crop=1080:1240[game];" +
                    $"[cam][game]vstack=inputs=2,drawbox=y=676:color={dividerColor}:t=8[v]";
            }

            string args = $"{inputArgs} -filter_complex \"{filterComplex}\" " +
                          $"-map \"[v]\" -map 0:a? -c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 192k -movflags +faststart \"{outputPath}\"";

            bool success = await RunFfmpegCommandAsync(args);
            return (success && File.Exists(outputPath)) ? outputPath : null;
        }

        private async Task<double> GetVideoDurationAsync(string videoPath)
        {
            try
            {
                var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                    "ffprobe",
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\""
                );
                if (exitCode == 0 && double.TryParse(stdout.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                {
                    return duration;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[Shorts] Failed to probe video duration for '{videoPath}'. Falling back to 15s.");
            }
            return 15.0;
        }

        private async Task<bool> ExtractProbeFrameAsync(string videoPath, string framePath, int secondOffset = 3)
        {
            string args = $"-y -ss 00:00:{secondOffset:D2} -i \"{videoPath}\" -vframes 1 \"{framePath}\"";
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
