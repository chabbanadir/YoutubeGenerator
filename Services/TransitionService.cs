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
        private readonly int _fps = 30;
        private readonly string _width = "1920";
        private readonly string _height = "1080";

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
        /// Single-pass merge: chains all input clips in a single FFmpeg execution using
        /// a filter graph script (-filter_complex_script) with xfade and acrossfade.
        /// This transforms quadratic O(N^2) re-encoding latency into linear O(N) single-pass execution.
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

            // 1. Obtain duration for all clips to calculate exact transition offsets
            var durations = new List<double>();
            foreach (var clip in clips)
            {
                if (ct.IsCancellationRequested) return false;
                double dur = await GetVideoDurationAsync(clip, ct);
                durations.Add(dur);
            }

            // 2. Compute transition offsets
            // Offset for transition k: Accumulated stream duration up to clip k minus transitionDuration
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

            // Video normalization (fps, scale, pad, pix_fmt, sar)
            for (int i = 0; i < clips.Count; i++)
            {
                sbFilter.AppendLine($"[{i}:v]fps={_fps},scale={_width}:{_height}:force_original_aspect_ratio=decrease,pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p,setsar=1[v{i}];");
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

            // Write script file to disk to avoid command line length limits
            string outputDir = Path.GetDirectoryName(finalOutputPath)!;
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string scriptPath = Path.Combine(outputDir, $"filter_script_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(scriptPath, sbFilter.ToString(), ct);

            try
            {
                // Build FFmpeg command with all inputs and filter script
                var sbArgs = new System.Text.StringBuilder();
                sbArgs.Append("-y ");
                foreach (var clip in clips)
                {
                    sbArgs.Append($"-i \"{clip}\" ");
                }

                sbArgs.Append($"-filter_complex_script \"{scriptPath}\" ");
                sbArgs.Append("-map \"[vout]\" -map \"[aout]\" ");
                sbArgs.Append("-c:v libx264 -preset superfast -crf 20 -pix_fmt yuv420p ");
                sbArgs.Append("-c:a aac -b:a 192k -ar 48000 -ac 2 ");
                sbArgs.Append($"\"{finalOutputPath}\"");

                var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", sbArgs.ToString(), null, ct);

                if (exitCode != 0)
                {
                    Console.WriteLine($"FFmpeg single-pass merge failed: {stderr}");
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
