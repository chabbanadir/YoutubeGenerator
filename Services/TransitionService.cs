    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
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
                if (!double.TryParse(durationStr, out double duration))
                    throw new Exception($"Invalid duration format: {durationStr}");

                return duration;
            }

            public async Task<bool> MergeVideosWithTransitionsAsync(
                List<string> videoPaths,
                string outputPath,
                string transitionType = "fade",
                double transitionDuration = 2.0,
                CancellationToken ct = default)
            {
                if (videoPaths == null || videoPaths.Count < 2)
                    throw new ArgumentException("Need at least 2 videos for transitions", nameof(videoPaths));

                Console.WriteLine($"[TransitionService] Starting merge of {videoPaths.Count} videos...");

                // Step 1: Get all video durations
                var durations = new List<double>();
                for (int i = 0; i < videoPaths.Count; i++)
                {
                    var duration = await GetVideoDurationAsync(videoPaths[i], ct);
                    durations.Add(duration);
                    Console.WriteLine($"[TransitionService] Video {i}: {videoPaths[i]} - Duration: {duration:F2}s");
                }

                // Step 2: Build the FFmpeg command
                var arguments = BuildFFmpegArguments(videoPaths, durations, outputPath, transitionType, transitionDuration);

                Console.WriteLine($"[TransitionService] Executing FFmpeg command...");
                Console.WriteLine($"[TransitionService] Output: {outputPath}");

                // Step 3: Execute FFmpeg
                var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                    "ffmpeg",
                    arguments,
                    ct: ct
                );

                if (exitCode != 0)
                {
                    Console.WriteLine($"[TransitionService] FFmpeg failed with exit code {exitCode}");
                    Console.WriteLine($"[TransitionService] Error output:\n{stderr}");
                    return false;
                }

                Console.WriteLine($"[TransitionService] ✓ Success! Video created: {outputPath}");
                return true;
            }

            private string BuildFFmpegArguments(
                List<string> videoPaths,
                List<double> durations,
                string outputPath,
                string transitionType,
                double transitionDuration)
            {
                // Build input arguments
                var inputs = string.Join(" ", videoPaths.Select(p => $"-hwaccel cuda -i \"{p}\""));

                // Build normalizer: standardize fps, resolution, format for all videos
                var normalizer = "";
                for (int i = 0; i < videoPaths.Count; i++)
                {
                    normalizer += $"[{i}:v]fps={_fps}," +
                                  $"scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                                  $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2," +
                                  $"settb=AVTB," +
                                  $"setpts=PTS-STARTPTS," +
                                  $"format=yuv420p[v{i}];";
                }

                // Build video transitions chain
                var videoTransitions = "";
                var audioTransitions = "";
                var lastVideoOutput = "v0";
                var lastAudioOutput = "0:a";
                double cumulativeOffset = 0;

                for (int i = 1; i < videoPaths.Count; i++)
                {
                    // Calculate offset for this transition
                    // Formula: offset = sum of previous video durations - sum of previous transitions
                    cumulativeOffset += durations[i - 1] - transitionDuration;

                    var currentVideoOutput = $"vt{i}";
                    var currentAudioOutput = $"at{i}";

                    // Video xfade transition
                    videoTransitions += $"[{lastVideoOutput}][v{i}]" +
                                       $"xfade=transition={transitionType}:" +
                                       $"duration={transitionDuration}:" +
                                       $"offset={cumulativeOffset:F3}" +
                                       $"[{currentVideoOutput}];";

                    // Audio crossfade
                    audioTransitions += $"[{lastAudioOutput}][{i}:a]" +
                                       $"acrossfade=d={transitionDuration}" +
                                       $"[{currentAudioOutput}];";

                    lastVideoOutput = currentVideoOutput;
                    lastAudioOutput = currentAudioOutput;
                }

                // Remove trailing semicolon from audio transitions
                audioTransitions = audioTransitions.TrimEnd(';');

                // Complete filter complex
                var filterComplex = normalizer + videoTransitions + audioTransitions;

                // Build final FFmpeg arguments
                var arguments = $"{inputs} " +
                   $"-filter_complex \"{filterComplex}\" " +
                   $"-map \"[{lastVideoOutput}]\" " +
                   $"-map \"[{lastAudioOutput}]\" " +
                   $"-c:v h264_nvenc " +      // GPU Encoder
                   $"-preset p4 " +           // NVENC preset
                   $"-cq 23 -rc vbr_hq " +    // Quality settings
                   $"-profile:v high " +
                   $"-level 4.0 " +
                   $"-pix_fmt yuv420p " +
                   $"-c:a aac " +
                   $"-b:a 192k " +
                   $"-movflags +faststart " +
                   $"-y " +
                   $"\"{outputPath}\"";

            return arguments;
            }
        }
    }


            //Transition Description
            //fade	Simple cross-fade (recommended)
            //dissolve	Smooth dissolve blend
            //wipeleft	Wipe from right to left
            //wiperight	Wipe from left to right
            //wipeup	Wipe from bottom to top
            //wipedown	Wipe from top to bottom
            //slideleft	Slide from right to left
            //slideright	Slide from left to right
            //slideup	Slide from bottom to top
            //slidedown	Slide from top to bottom
            //circlecrop	Circular reveal
            //pixelize	Pixelated transition