using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class TransitionService : ITransitionService
    {
        private readonly ILogger<TransitionService> _logger;
        private readonly IRenderTrackerService? _renderTracker;
        private readonly int _fps = 30;
        private readonly string _width = "1920";
        private readonly string _height = "1080";

        private static readonly Regex FfmpegProgressRegex = new(
            @"(?:frame=\s*(?<frame>\d+))?\s*(?:fps=\s*(?<fps>[\d\.]+))?.*?(?:time=\s*(?<time>[\d:\.]+)).*?(?:bitrate=\s*(?<bitrate>[\d\.]+kbits/s|N/A))?.*?(?:speed=\s*(?<speed>[\d\.]+x))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public TransitionService(ILogger<TransitionService> logger, IRenderTrackerService? renderTracker = null)
        {
            _logger = logger;
            _renderTracker = renderTracker;
        }

        public async Task<double> GetVideoDurationAsync(string videoPath, CancellationToken ct = default)
        {
            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                "ffprobe",
                $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                ct: ct
            );

            if (exitCode != 0)
                throw new Exception($"Failed to get video duration for '{videoPath}': {stderr}");

            var durationStr = stdout.Trim();
            if (!double.TryParse(durationStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                throw new Exception($"Invalid duration format: {durationStr}");

            return duration;
        }

        private class PassContext
        {
            public int CurrentPass { get; set; } = 0;
            public int TotalPasses { get; set; } = 1;
        }

        public static int CalculateTotalPasses(int clipCount, int batchSize = 4)
        {
            if (clipCount <= batchSize) return 1;
            int total = 0;
            int current = clipCount;
            while (current > batchSize)
            {
                int chunks = (int)Math.Ceiling((double)current / batchSize);
                total += chunks;
                current = chunks;
            }
            total += 1; // Final assembly merge pass
            return total;
        }

        /// <summary>
        /// Merges clips using single-pass filter complex with xfade and acrossfade.
        /// Uses hierarchical chunking for batches &gt; 4 clips to guarantee minimal memory footprint (&lt; 250MB RAM)
        /// and prevent FFmpeg from exceeding container OOM limits.
        /// </summary>
        public async Task<bool> MergeVideosWithTransitionsAsync(
            List<string> clips,
            string finalOutputPath,
            string transitionType,
            double transitionDuration,
            CancellationToken ct = default)
        {
            if (clips == null || clips.Count == 0)
                throw new ArgumentException("No video provided", nameof(clips));

            if (string.IsNullOrWhiteSpace(transitionType))
                transitionType = "fade";

            if (clips.Count == 1)
            {
                await FinalizeToAacAsync(clips[0], finalOutputPath, isFinalTarget: true);
                return true;
            }

            int totalPasses = CalculateTotalPasses(clips.Count, 4);
            var context = new PassContext
            {
                CurrentPass = 0,
                TotalPasses = totalPasses
            };

            _logger.LogInformation($"📦 Programmed {totalPasses} total encoding passes for {clips.Count} clips.");
            _renderTracker?.AddLog($"[Pipeline] Programmed {totalPasses} total encoding passes for {clips.Count} source clips (batch chunk size: 4)", "step");

            return await MergeVideosInternalAsync(clips, finalOutputPath, transitionType, transitionDuration, context, isFinalTarget: true, ct);
        }

        private async Task<bool> MergeVideosInternalAsync(
            List<string> clips,
            string outputPath,
            string transitionType,
            double transitionDuration,
            PassContext context,
            bool isFinalTarget,
            CancellationToken ct)
        {
            const int maxBatchSize = 4;

            if (clips.Count <= maxBatchSize)
            {
                context.CurrentPass++;
                _renderTracker?.SetBatchInfo(context.CurrentPass, context.TotalPasses, clips.Count, outputPath);
                string label = isFinalTarget
                    ? $"[Pass {context.CurrentPass}/{context.TotalPasses}] Final Assembly: Merging {clips.Count} segments -> {Path.GetFileName(outputPath)}"
                    : $"[Pass {context.CurrentPass}/{context.TotalPasses}] Encoding intermediate batch of {clips.Count} segments -> {Path.GetFileName(outputPath)}";
                _renderTracker?.AddLog(label, "step");

                return await MergeSingleBatchWithTransitionsAsync(clips, outputPath, transitionType, transitionDuration, context.CurrentPass, context.TotalPasses, isFinalTarget, ct);
            }

            int batchChunksCount = (int)Math.Ceiling((double)clips.Count / maxBatchSize);
            string outputDir = Path.GetDirectoryName(outputPath)!;
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            CleanupStaleChunks(outputDir);

            var chunkFiles = new List<string>();
            try
            {
                int chunkIndex = 0;
                for (int i = 0; i < clips.Count; i += maxBatchSize)
                {
                    if (ct.IsCancellationRequested) return false;

                    chunkIndex++;
                    context.CurrentPass++;
                    var batchClips = clips.GetRange(i, Math.Min(maxBatchSize, clips.Count - i));
                    string chunkPath = Path.Combine(outputDir, $"chunk_{Guid.NewGuid():N}.mp4");
                    chunkFiles.Add(chunkPath);

                    _renderTracker?.SetBatchInfo(context.CurrentPass, context.TotalPasses, batchClips.Count, chunkPath);
                    _renderTracker?.AddLog($"[Pass {context.CurrentPass}/{context.TotalPasses}] Encoding batch chunk {chunkIndex}/{batchChunksCount} ({batchClips.Count} clips) -> {Path.GetFileName(chunkPath)}", "step");

                    bool success = await MergeSingleBatchWithTransitionsAsync(batchClips, chunkPath, transitionType, transitionDuration, context.CurrentPass, context.TotalPasses, isFinalTarget: false, ct);
                    if (!success || !File.Exists(chunkPath))
                    {
                        _logger.LogError($"❌ Chunk rendering failed for pass {context.CurrentPass}");
                        _renderTracker?.AddLog($"[Pass {context.CurrentPass}/{context.TotalPasses}] ❌ FFmpeg chunk rendering failed", "error");
                        return false;
                    }
                }

                // Recursively merge the rendered chunks into the output
                return await MergeVideosInternalAsync(chunkFiles, outputPath, transitionType, transitionDuration, context, isFinalTarget, ct);
            }
            finally
            {
                foreach (var chunkFile in chunkFiles)
                {
                    if (File.Exists(chunkFile))
                    {
                        try { File.Delete(chunkFile); } catch { }
                    }
                }
            }
        }

        private void CleanupStaleChunks(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var staleChunks = Directory.GetFiles(directory, "chunk_*.mp4");
                var threshold = DateTime.UtcNow.AddMinutes(-30);
                foreach (var file in staleChunks)
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < threshold)
                        {
                            File.Delete(file);
                            _logger.LogInformation($"🧹 Cleaned up stale chunk file: {file}");
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run stale chunk cleanup.");
            }
        }

        private async Task<bool> MergeSingleBatchWithTransitionsAsync(
            List<string> clips,
            string outputPath,
            string transitionType,
            double transitionDuration,
            int currentPass,
            int totalPasses,
            bool isFinalTarget,
            CancellationToken ct)
        {
            if (clips.Count == 1)
            {
                await FinalizeToAacAsync(clips[0], outputPath, isFinalTarget);
                return true;
            }

            string overlayPath = Path.Combine(Directory.GetCurrentDirectory(), "workspace", "ressources", "overlays", "like_subscribe.png");
            if (!File.Exists(overlayPath))
            {
                overlayPath = Path.Combine(AppContext.BaseDirectory, "workspace", "ressources", "overlays", "like_subscribe.png");
            }
            bool hasOverlay = isFinalTarget && File.Exists(overlayPath);

            // Stage 3.3: Probe clip durations
            _renderTracker?.SetStep(3, $"Probing {clips.Count} clips for durations & transition alignment");
            var durations = new List<double>();
            for (int i = 0; i < clips.Count; i++)
            {
                if (ct.IsCancellationRequested) return false;
                var clip = clips[i];
                double dur = await GetVideoDurationAsync(clip, ct);
                durations.Add(dur);

                double probePercent = (double)(i + 1) / clips.Count * 100;
                _renderTracker?.UpdateStepProgress(3, probePercent, $"Probed {i + 1}/{clips.Count} clips");
                _renderTracker?.AddLog($"[Probe] {Path.GetFileName(clip)}: {dur:F2}s", "info");
            }

            // Compute transition offsets
            var offsets = new List<double>();
            double accumulatedDuration = durations[0];

            for (int i = 0; i < clips.Count - 1; i++)
            {
                double offset = Math.Max(0, accumulatedDuration - transitionDuration);
                offsets.Add(offset);
                accumulatedDuration = offset + durations[i + 1];
            }

            _renderTracker?.AddLog($"[Offsets] Calculated {offsets.Count} transition offsets. Batch runtime: {accumulatedDuration:F2}s", "info");

            // Build single-pass filter complex script
            var sbFilter = new System.Text.StringBuilder();

            for (int i = 0; i < clips.Count; i++)
            {
                sbFilter.AppendLine($"[{i}:v]fps={_fps},scale={_width}:{_height}:force_original_aspect_ratio=decrease,pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p,setsar=1,settb=AVTB[v{i}];");
            }

            string prevVideoLabel = "v0";
            for (int i = 0; i < clips.Count - 1; i++)
            {
                string nextVideoLabel = (i == clips.Count - 2) ? "vout" : $"vx{i + 1}";
                string offsetStr = offsets[i].ToString(CultureInfo.InvariantCulture);
                string transDurStr = transitionDuration.ToString(CultureInfo.InvariantCulture);
                sbFilter.AppendLine($"[{prevVideoLabel}][v{i + 1}]xfade=transition={transitionType}:duration={transDurStr}:offset={offsetStr}[{nextVideoLabel}];");
                prevVideoLabel = nextVideoLabel;
            }

            for (int i = 0; i < clips.Count; i++)
            {
                sbFilter.AppendLine($"[{i}:a]aformat=sample_fmts=s16:sample_rates=48000:channel_layouts=stereo[a{i}];");
            }

            string prevAudioLabel = "a0";
            for (int i = 0; i < clips.Count - 1; i++)
            {
                string nextAudioLabel = (i == clips.Count - 2) ? "aout" : $"ax{i + 1}";
                string transDurStr = transitionDuration.ToString(CultureInfo.InvariantCulture);
                sbFilter.AppendLine($"[{prevAudioLabel}][a{i + 1}]acrossfade=d={transDurStr}:c1=tri:c2=tri[{nextAudioLabel}];");
                prevAudioLabel = nextAudioLabel;
            }

            if (hasOverlay)
            {
                double tStart = Math.Max(2.0, (accumulatedDuration / 2.0) - 1.0);
                double tEnd = Math.Min(accumulatedDuration, tStart + 2.0);
                string tStartStr = tStart.ToString("0.0", CultureInfo.InvariantCulture);
                string tEndStr = tEnd.ToString("0.0", CultureInfo.InvariantCulture);

                int overlayIdx = clips.Count;
                sbFilter.AppendLine($"[{overlayIdx}:v]scale=680:-1[sub];");
                sbFilter.AppendLine($"[vout][sub]overlay=(W-w)/2:H-h-120:enable='between(t,{tStartStr},{tEndStr})'[vfinal];");
                _renderTracker?.AddLog($"[Montage] Like & Subscribe banner scheduled at midpoint {tStartStr}s - {tEndStr}s (2s duration)", "info");
            }

            string outputDir = Path.GetDirectoryName(outputPath)!;
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string scriptPath = Path.Combine(outputDir, $"filter_script_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(scriptPath, sbFilter.ToString(), ct);

            // Stage 3.4: Real FFmpeg execution
            string passLabel = totalPasses > 1 ? $" (Pass {currentPass}/{totalPasses})" : "";
            _renderTracker?.SetStep(4, $"Encoding crossfade transitions: {Path.GetFileName(outputPath)}{passLabel}");
            _renderTracker?.AddLog($"[FFmpeg] Starting video & audio transition encoder...", "step");

            try
            {
                var sbArgs = new System.Text.StringBuilder();
                sbArgs.Append("-y ");
                foreach (var clip in clips)
                {
                    sbArgs.Append($"-i \"{clip}\" ");
                }
                if (hasOverlay)
                {
                    sbArgs.Append($"-i \"{overlayPath}\" ");
                }

                sbArgs.Append($"-filter_complex_script \"{scriptPath}\" ");
                string vMap = hasOverlay ? "[vfinal]" : "[vout]";
                sbArgs.Append($"-map \"{vMap}\" -map \"[aout]\" ");

                bool isChunk = outputPath.Contains("chunk_");
                string videoEncode = isChunk
                    ? "-c:v libx264 -preset superfast -crf 14 -pix_fmt yuv420p"
                    : "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p";

                sbArgs.Append($"{videoEncode} -threads 2 ");
                sbArgs.Append("-c:a aac -b:a 192k -ar 48000 -ac 2 ");
                sbArgs.Append("-vsync cfr ");
                sbArgs.Append($"\"{outputPath}\"");

                _lastLoggedProgressBucket = -1;
                var (exitCode, stdout, stderr) = await ProcessRunner.RunStreamingAsync(
                    "ffmpeg",
                    sbArgs.ToString(),
                    null,
                    onLine: line =>
                    {
                        ParseAndReportFfmpegOutput(line, accumulatedDuration, currentPass, totalPasses);
                    },
                    ct: ct);

                if (exitCode != 0)
                {
                    _logger.LogError($"❌ FFmpeg batch merge failed (exit code {exitCode}). Stderr:\n{stderr}");
                    _logger.LogError($"Filter script content:\n{sbFilter}");
                    _renderTracker?.AddLog($"[FFmpeg] ❌ FFmpeg returned exit code {exitCode}: {stderr}", "error");
                    return false;
                }

                _renderTracker?.UpdateStepProgress(4, 100, $"Encoded {Path.GetFileName(outputPath)}");
                _renderTracker?.AddLog($"[FFmpeg] ✅ Encoding finished: {Path.GetFileName(outputPath)}", "success");
                return true;
            }
            finally
            {
                if (File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }
        }

        private int _lastLoggedProgressBucket = -1;

        private void ParseAndReportFfmpegOutput(string line, double expectedDurationSeconds, int currentPass, int totalPasses)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            var match = FfmpegProgressRegex.Match(line);
            if (match.Success && match.Groups["time"].Success)
            {
                string timeStr = match.Groups["time"].Value;
                string speedStr = match.Groups["speed"].Success ? match.Groups["speed"].Value : "";
                string bitrateStr = match.Groups["bitrate"].Success ? match.Groups["bitrate"].Value : "";
                double fps = 0;
                if (match.Groups["fps"].Success && double.TryParse(match.Groups["fps"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double f))
                {
                    fps = f;
                }

                double currentSeconds = ParseTimeToSeconds(timeStr);
                double stepPercent = expectedDurationSeconds > 0 ? (currentSeconds / expectedDurationSeconds) * 100 : 0;
                stepPercent = Math.Clamp(stepPercent, 0, 100);

                // Calculate accurate overall percentage based on batch progression (35% to 92%)
                double passWeight = 55.0 / Math.Max(1, totalPasses);
                double baseOffset = 35.0 + ((currentPass - 1) * passWeight);
                double overallPercent = baseOffset + (stepPercent / 100.0 * passWeight);
                overallPercent = Math.Clamp(overallPercent, 0, 95);

                _renderTracker?.UpdateFfmpegProgress(fps, speedStr, timeStr, stepPercent, overallPercent, bitrateStr);

                // Forward periodic progress milestone to terminal (at 25%, 50%, 75%)
                int bucket = (int)(stepPercent / 25.0) * 25;
                if (bucket > 0 && bucket < 100 && bucket != _lastLoggedProgressBucket)
                {
                    _lastLoggedProgressBucket = bucket;
                    _renderTracker?.AddLog($"[FFmpeg] Pass {currentPass}/{totalPasses}: {bucket}% encoded (time={timeStr}, speed={speedStr}, {fps:F0} fps)", "ffmpeg");
                }
            }
            else if (line.Contains("Error", StringComparison.OrdinalIgnoreCase) || line.Contains("Failed", StringComparison.OrdinalIgnoreCase))
            {
                _renderTracker?.AddLog($"[FFmpeg] ⚠️ {line.Trim()}", "warning");
            }
        }

        private static double ParseTimeToSeconds(string timeStr)
        {
            try
            {
                var parts = timeStr.Split(':');
                if (parts.Length == 3)
                {
                    double h = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    double m = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    double s = double.Parse(parts[2], CultureInfo.InvariantCulture);
                    return h * 3600 + m * 60 + s;
                }
            }
            catch { }
            return 0;
        }

        private async Task<bool> FinalizeToAacAsync(string input, string output, bool isFinalTarget = false)
        {
            string overlayPath = Path.Combine(Directory.GetCurrentDirectory(), "workspace", "ressources", "overlays", "like_subscribe.png");
            if (!File.Exists(overlayPath))
            {
                overlayPath = Path.Combine(AppContext.BaseDirectory, "workspace", "ressources", "overlays", "like_subscribe.png");
            }

            string temp = output + ".aacfinal.mp4";
            string args;
            if (isFinalTarget && File.Exists(overlayPath))
            {
                double duration = await GetVideoDurationAsync(input);
                double tStart = Math.Max(2.0, (duration / 2.0) - 1.0);
                double tEnd = Math.Min(duration, tStart + 2.0);
                string tStartStr = tStart.ToString("0.0", CultureInfo.InvariantCulture);
                string tEndStr = tEnd.ToString("0.0", CultureInfo.InvariantCulture);

                args =
                    $"-y -i \"{input}\" -i \"{overlayPath}\" " +
                    $"-filter_complex \"[1:v]scale=680:-1[sub];[0:v][sub]overlay=(W-w)/2:H-h-120:enable='between(t,{tStartStr},{tEndStr})'[v]\" " +
                    "-map \"[v]\" -map 0:a? " +
                    "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p " +
                    "-c:a aac -b:a 192k -ar 48000 -ac 2 " +
                    $"\"{temp}\"";
            }
            else
            {
                args =
                    $"-y -i \"{input}\" " +
                    "-c:v copy " +
                    "-c:a aac -b:a 192k -ar 48000 -ac 2 " +
                    $"\"{temp}\"";
            }

            _renderTracker?.AddLog($"[Finalize] Normalizing audio container for single clip...", "info");

            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", args, null, CancellationToken.None);

            if (exitCode != 0)
            {
                if (File.Exists(temp)) File.Delete(temp);
                Console.WriteLine(stderr);
                _renderTracker?.AddLog($"[Finalize] ❌ Audio copy failed: {stderr}", "error");
                return false;
            }

            if (File.Exists(input)) File.Delete(input);
            File.Move(temp, output);
            return true;
        }
    }
}
