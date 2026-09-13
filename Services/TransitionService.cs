using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class TransitionService : ITransitionService
    {
        private readonly ILogger<TransitionService> _logger;
        private readonly int _fps = 30;
        private readonly string _width = "1920";
        private readonly string _height = "1080";

        public TransitionService(ILogger<TransitionService> logger)
        {
            _logger = logger;
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

        /// <summary>
        /// Merges clips using single-pass filter complex with xfade and acrossfade.
        /// Uses chunking for batches > 6 clips to guarantee minimal memory footprint (< 250MB RAM)
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
                await FinalizeToAacAsync(clips[0], finalOutputPath);
                return true;
            }

            const int maxBatchSize = 4;
            if (clips.Count <= maxBatchSize)
            {
                return await MergeSingleBatchWithTransitionsAsync(clips, finalOutputPath, transitionType, transitionDuration, ct);
            }

            _logger.LogInformation($"📦 Batching {clips.Count} clips into sub-groups of {maxBatchSize} to optimize memory...");

            string outputDir = Path.GetDirectoryName(finalOutputPath)!;
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Proactively clean up any stale chunk files older than 30 minutes
            CleanupStaleChunks(outputDir);

            var chunkFiles = new List<string>();
            try
            {
                for (int i = 0; i < clips.Count; i += maxBatchSize)
                {
                    if (ct.IsCancellationRequested) return false;

                    var batchClips = clips.GetRange(i, Math.Min(maxBatchSize, clips.Count - i));
                    string chunkPath = Path.Combine(outputDir, $"chunk_{Guid.NewGuid():N}.mp4");
                    chunkFiles.Add(chunkPath); // Track immediately so finally always cleans up even on crash or failure!

                    bool success = await MergeSingleBatchWithTransitionsAsync(batchClips, chunkPath, transitionType, transitionDuration, ct);
                    if (!success || !File.Exists(chunkPath))
                    {
                        _logger.LogError($"❌ Chunk rendering failed for batch starting at index {i}");
                        return false;
                    }
                }

                // Recursively merge the rendered chunks into final output
                return await MergeVideosWithTransitionsAsync(chunkFiles, finalOutputPath, transitionType, transitionDuration, ct);
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
            CancellationToken ct)
        {
            if (clips.Count == 1)
            {
                await FinalizeToAacAsync(clips[0], outputPath);
                return true;
            }

            // 1. Obtain duration for all clips to calculate exact transition offsets
            var durations = new List<double>();
            foreach (var clip in clips)
            {
                if (ct.IsCancellationRequested) return false;
                double dur = await GetVideoDurationAsync(clip, ct);
                durations.Add(dur);
            }

            // 2. Compute transition offsets
            var offsets = new List<double>();
            double accumulatedDuration = durations[0];

            for (int i = 0; i < clips.Count - 1; i++)
            {
                double offset = Math.Max(0, accumulatedDuration - transitionDuration);
                offsets.Add(offset);
                accumulatedDuration = offset + durations[i + 1];
            }

            // 3. Build single-pass filter complex script
            var sbFilter = new System.Text.StringBuilder();

            // Video normalization (fps, scale, pad, pix_fmt, sar, settb)
            for (int i = 0; i < clips.Count; i++)
            {
                sbFilter.AppendLine($"[{i}:v]fps={_fps},scale={_width}:{_height}:force_original_aspect_ratio=decrease,pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p,setsar=1,settb=AVTB[v{i}];");
            }

            // Video xfade chain
            string prevVideoLabel = "v0";
            for (int i = 0; i < clips.Count - 1; i++)
            {
                string nextVideoLabel = (i == clips.Count - 2) ? "vout" : $"vx{i + 1}";
                string offsetStr = offsets[i].ToString(CultureInfo.InvariantCulture);
                string transDurStr = transitionDuration.ToString(CultureInfo.InvariantCulture);
                sbFilter.AppendLine($"[{prevVideoLabel}][v{i + 1}]xfade=transition={transitionType}:duration={transDurStr}:offset={offsetStr}[{nextVideoLabel}];");
                prevVideoLabel = nextVideoLabel;
            }

            // Audio normalization & acrossfade chain
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

            string outputDir = Path.GetDirectoryName(outputPath)!;
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string scriptPath = Path.Combine(outputDir, $"filter_script_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(scriptPath, sbFilter.ToString(), ct);

            try
            {
                var sbArgs = new System.Text.StringBuilder();
                sbArgs.Append("-y ");
                foreach (var clip in clips)
                {
                    sbArgs.Append($"-i \"{clip}\" ");
                }

                sbArgs.Append($"-filter_complex_script \"{scriptPath}\" ");
                sbArgs.Append("-map \"[vout]\" -map \"[aout]\" ");

                bool isChunk = outputPath.Contains("chunk_");
                string videoEncode = isChunk
                    ? "-c:v libx264 -preset superfast -crf 14 -pix_fmt yuv420p"
                    : "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p";

                sbArgs.Append($"{videoEncode} -threads 2 ");
                sbArgs.Append("-c:a aac -b:a 192k -ar 48000 -ac 2 ");
                sbArgs.Append("-vsync cfr ");
                sbArgs.Append($"\"{outputPath}\"");

                var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", sbArgs.ToString(), null, ct);

                if (exitCode != 0)
                {
                    _logger.LogError($"❌ FFmpeg batch merge failed (exit code {exitCode}). Stderr:\n{stderr}");
                    _logger.LogError($"Filter script content:\n{sbFilter}");
                    return false;
                }

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

        private async Task<bool> FinalizeToAacAsync(string input, string output)
        {
            string temp = output + ".aacfinal.mp4";

            string args =
                $"-y -i \"{input}\" " +
                "-c:v copy " +
                "-c:a aac -b:a 192k -ar 48000 -ac 2 " +
                $"\"{temp}\"";

            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", args, null, CancellationToken.None);

            if (exitCode != 0)
            {
                if (File.Exists(temp)) File.Delete(temp);
                Console.WriteLine(stderr);
                return false;
            }

            if (File.Exists(input)) File.Delete(input);
            File.Move(temp, output);
            return true;
        }
    }
}
