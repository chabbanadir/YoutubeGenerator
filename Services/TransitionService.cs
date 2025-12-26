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
        /// Merge sequentially: base + next clip with xfade and acrossfade.
        /// Uses libx264 (CPU) for deterministic encoding during merges,
        /// keeps audio as pcm_s16le during merges to avoid drift, and
        /// re-encodes to AAC once at the end.
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

            if (clips.Count == 1)
            {
                // Single clip: ensure final is AAC encoded (in case clip uses pcm/aac mismatch)
                await FinalizeToAacAsync(clips[0], finalOutputPath);
                return true;
            }

            // Start with first clip as base
            File.Copy(clips[0], finalOutputPath, overwrite: true);

            for (int i = 1; i < clips.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    return false;

                string clipNext = clips[i];

                // get base duration to compute offset for xfade
                double durationBase = await GetVideoDurationAsync(finalOutputPath, ct);
                double offset = Math.Max(0, durationBase - transitionDuration);

                string offsetStr = offset.ToString(CultureInfo.InvariantCulture);
                string transDurStr = transitionDuration.ToString(CultureInfo.InvariantCulture);

                string temp = Path.Combine(Path.GetDirectoryName(finalOutputPath)!, $"temp_merge_{i}.mp4");

                // Build filter complex:
                // - ensure both videos have same fps/scale/pixfmt and setsar=1
                // - xfade on video
                // - aformat to s16 48k stereo for safe acrossfade
                // - acrossfade for audio
                string args =
                    $"-y -i \"{finalOutputPath}\" -i \"{clipNext}\" " +
                    "-filter_complex " +
                    $"\"[0:v]fps={_fps},scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                    $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p,setsar=1[v0];" +
                    $"[1:v]fps={_fps},scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                    $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p,setsar=1[v1];" +
                    $"[v0][v1]xfade=transition={transitionType}:duration={transDurStr}:offset={offsetStr}[vout];" +

                    // audio: convert both to s16 @ 48k stereo, then acrossfade
                    $"[0:a]aformat=sample_fmts=s16:sample_rates=48000:channel_layouts=stereo[a0];" +
                    $"[1:a]aformat=sample_fmts=s16:sample_rates=48000:channel_layouts=stereo[a1];" +
                    $"[a0][a1]acrossfade=d={transDurStr}:c1=tri:c2=tri[aout]\" " +

                    // map and encode: libx264 for video, keep audio as PCM during merge
                    "-map \"[vout]\" -map \"[aout]\" " +
                    "-c:v libx265 -preset ultrafast -crf 30 -pix_fmt yuv420p " +
                    "-c:a pcm_s16le " +
                    $"-y \"{temp}\"";

                var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", args, null, ct);

                if (exitCode != 0)
                {
                    // cleanup and exit
                    if (File.Exists(temp)) File.Delete(temp);
                    Console.WriteLine(stderr);
                    return false;
                }

                // replace base with new merged result (overwrite)
                if (File.Exists(finalOutputPath)) File.Delete(finalOutputPath);
                File.Move(temp, finalOutputPath);
            }

            // Final step: encode audio to AAC and keep video as-is (libx264 output OK)
            bool finalizeOk = await FinalizeToAacAsync(finalOutputPath, finalOutputPath);
            return finalizeOk;
        }

        /// <summary>
        /// Re-encode audio to AAC and keep video stream as-is.
        /// Writes to a temp file and replaces input with final output.
        /// </summary>
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

            // Replace input with final
            if (File.Exists(input)) File.Delete(input);
            File.Move(temp, output);
            return true;
        }
    }
}
