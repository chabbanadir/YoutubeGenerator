using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;
using YoutubeGenerator.Utils;

namespace YoutubeGenerator.Services
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ThumbnailService> _logger;
        private readonly IRenderTrackerService? _renderTracker;
        private readonly string _assetsRoot;

        public ThumbnailService(
            IWebHostEnvironment env,
            ILogger<ThumbnailService> logger,
            IRenderTrackerService? renderTracker = null)
        {
            _env = env;
            _logger = logger;
            _renderTracker = renderTracker;
            _assetsRoot = Path.Combine(env.ContentRootPath, "workspace", "ressources", "thumbnails");
        }

        public async Task<string?> GenerateThumbnailAsync(MontageItem package, string? workingDir = null)
        {
            try
            {
                _logger.LogInformation($"🎨 Generating high-CTR thumbnail for montage {package.MontageId} ({package.KindKey}/{package.SubKindKey})");

                // 1. Resolve output directory
                string outputDir = ResolveOutputDir(package, workingDir);
                Directory.CreateDirectory(outputDir);
                string outputPath = Path.Combine(outputDir, $"thumb_{package.MontageId}.jpg");

                // 2. Select source video to extract action keyframe from
                var (sourceVideo, timestamp) = ResolveSourceVideo(package);
                if (string.IsNullOrEmpty(sourceVideo) || !File.Exists(sourceVideo))
                {
                    _logger.LogWarning($"⚠️ No valid video source found to extract thumbnail for montage {package.MontageId}");
                    return null;
                }

                // 3. Resolve Creator cutout (hybrid fallback)
                string? creatorPath = ResolveCreatorAsset(package);

                // 4. Resolve Game Badge and Rank Icon
                var (badgePath, rankPath) = ResolveGameBadges(package);

                // 5. Resolve Hook Typography
                var (hookText, fontPath) = ResolveHookTypography(package);

                // 6. Build and execute FFmpeg filter graph
                string ffmpegArgs = BuildFfmpegArguments(
                    sourceVideo,
                    timestamp,
                    creatorPath,
                    badgePath,
                    rankPath,
                    hookText,
                    fontPath,
                    outputPath
                );

                _logger.LogInformation($"🎬 Running FFmpeg thumbnail compositing for {package.MontageId}");
                var (code, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", ffmpegArgs, outputDir);

                if (code != 0 || !File.Exists(outputPath))
                {
                    _logger.LogWarning($"⚠️ FFmpeg thumbnail generation failed (exit code {code}): {stderr}");
                    return null;
                }

                _logger.LogInformation($"✅ Generated high-CTR thumbnail: {outputPath} ({new FileInfo(outputPath).Length / 1024} KB)");
                package.GeneratedThumbnailPath = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Unexpected error while generating thumbnail for montage {package.MontageId}");
                return null;
            }
        }

        public async Task<string?> GenerateShortThumbnailAsync(
            string shortVideoPath,
            string kindKey,
            string? streamerName = null,
            string? subKindKey = null,
            string? workingDir = null)
        {
            try
            {
                if (string.IsNullOrEmpty(shortVideoPath) || !File.Exists(shortVideoPath))
                {
                    _logger.LogWarning($"⚠️ No valid video source found to extract Short thumbnail: {shortVideoPath}");
                    return null;
                }

                string outputDir = !string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir)
                    ? workingDir
                    : (Path.GetDirectoryName(shortVideoPath) ?? Path.Combine(_env.ContentRootPath, "workspace", "thumbnails"));
                Directory.CreateDirectory(outputDir);

                string baseName = Path.GetFileNameWithoutExtension(shortVideoPath);
                string outputPath = Path.Combine(outputDir, $"thumb_{baseName}.jpg");

                _renderTracker?.UpdateShortProgress(75, "Extracting 9:16 Thumbnail");
                _renderTracker?.AddLog("thumb", $"🎨 Extracting clean 9:16 thumbnail for {baseName} ({kindKey}/{subKindKey})");
                _logger.LogInformation($"🎨 Extracting high-CTR 9:16 Short thumbnail for {baseName} ({kindKey}/{subKindKey})");

                // The vertical short video already contains the branded game header, streamer tag, and CTA sticker.
                // Extracting the keyframe directly at 00:00:03 avoids duplicate text and mismatched overlay layers.
                string ffmpegArgs = $"-y -ss 00:00:03 -i \"{shortVideoPath}\" -vframes 1 -q:v 2 \"{outputPath}\"";

                var (code, stdout, stderr) = await ProcessRunner.RunAsync("ffmpeg", ffmpegArgs, outputDir);

                if (code != 0 || !File.Exists(outputPath))
                {
                    _logger.LogWarning($"⚠️ FFmpeg Short thumbnail generation failed (exit code {code}): {stderr}");
                    return null;
                }

                _renderTracker?.AddLog("thumb", $"✅ Short thumbnail generated: {Path.GetFileName(outputPath)} ({new FileInfo(outputPath).Length / 1024} KB)");
                _logger.LogInformation($"✅ Generated high-CTR 9:16 Short thumbnail: {outputPath} ({new FileInfo(outputPath).Length / 1024} KB)");
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Unexpected error while generating Short thumbnail for {shortVideoPath}");
                return null;
            }
        }

        private string ResolveOutputDir(MontageItem package, string? workingDir)
        {
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
                return workingDir;

            if (!string.IsNullOrEmpty(package.FinalVideoPath))
            {
                var dir = Path.GetDirectoryName(package.FinalVideoPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }

            return Path.Combine(_env.ContentRootPath, "workspace", "thumbnails");
        }

        private (string? VideoPath, string Timestamp) ResolveSourceVideo(MontageItem package)
        {
            // Priority 1: Rendered final montage video (grab at 00:00:20 where intro is past and high action starts)
            if (!string.IsNullOrEmpty(package.FinalVideoPath) && File.Exists(package.FinalVideoPath))
            {
                return (package.FinalVideoPath, "00:00:22");
            }

            // Priority 2: Primary headline clip
            if (package.Clips != null && package.Clips.Count > 0)
            {
                var primaryClip = package.Clips.FirstOrDefault(c => File.Exists(c.File))
                                  ?? package.Clips.OrderByDescending(c => c.Duration).FirstOrDefault(c => File.Exists(c.File));

                if (primaryClip != null && File.Exists(primaryClip.File))
                {
                    // Grab 25% into the clip
                    var seconds = Math.Max(2, (int)(primaryClip.Duration.TotalSeconds * 0.3));
                    return (primaryClip.File, $"00:00:{seconds:D2}");
                }
            }

            // Priority 3: Fallback intro video
            var kind = (package.KindKey ?? "").ToLower();
            string introFallback = (kind.Contains("val") || kind.Contains("gaming"))
                ? Path.Combine(_env.ContentRootPath, "workspace", "ressources", "valo_intro.mp4")
                : Path.Combine(_env.ContentRootPath, "workspace", "ressources", "intro_lol.mp4");

            if (File.Exists(introFallback))
            {
                return (introFallback, "00:00:02");
            }

            return (null, "00:00:01");
        }

        private string? ResolveCreatorAsset(MontageItem package)
        {
            string creatorsDir = Path.Combine(_assetsRoot, "creators");
            if (!Directory.Exists(creatorsDir))
                return null;

            var candidates = new List<string>();

            // Collect featured streamer names from clip descriptions
            if (package.Clips != null)
            {
                foreach (var clip in package.Clips)
                {
                    if (!string.IsNullOrWhiteSpace(clip.Description))
                    {
                        var clean = Regex.Replace(clip.Description.Trim().ToLower(), @"[^a-z0-9_]", "");
                        if (!string.IsNullOrEmpty(clean) && !candidates.Contains(clean))
                            candidates.Add(clean);
                    }
                }
            }

            // Check for matched streamer cutouts
            foreach (var name in candidates)
            {
                string match = Path.Combine(creatorsDir, $"{name}.png");
                if (File.Exists(match))
                    return match;

                // Check common aliases
                if (name == "thebausffs" && File.Exists(Path.Combine(creatorsDir, "baus.png")))
                    return Path.Combine(creatorsDir, "baus.png");
                if (name == "lol_nemesis" && File.Exists(Path.Combine(creatorsDir, "nemesis.png")))
                    return Path.Combine(creatorsDir, "nemesis.png");
                if (name == "tyler1" && File.Exists(Path.Combine(creatorsDir, "loltyler1.png")))
                    return Path.Combine(creatorsDir, "loltyler1.png");
            }

            // Hybrid Fallback: Default gamer reaction cutouts
            var kind = (package.KindKey ?? "").ToLower();
            string fallbackDefault = kind.Contains("val")
                ? Path.Combine(creatorsDir, "default_val_gamer.png")
                : Path.Combine(creatorsDir, "default_lol_gamer.png");

            if (File.Exists(fallbackDefault))
                return fallbackDefault;

            return null;
        }

        private (string? BadgePath, string? RankPath) ResolveGameBadges(MontageItem package)
        {
            string badgesDir = Path.Combine(_assetsRoot, "badges");
            if (!Directory.Exists(badgesDir))
                return (null, null);

            var kind = (package.KindKey ?? "").ToLower();
            var subKind = (package.SubKindKey ?? "").ToLower();

            if (kind.Contains("val"))
            {
                string aceBadge = Path.Combine(badgesDir, "ace.png");
                string radiantBadge = Path.Combine(badgesDir, "radiant_badge.png");
                return (
                    subKind.Contains("ace") && File.Exists(aceBadge) ? aceBadge : null,
                    File.Exists(radiantBadge) ? radiantBadge : null
                );
            }
            else
            {
                string pentakillBadge = Path.Combine(badgesDir, "pentakill.png");
                string challengerIcon = Path.Combine(badgesDir, "challenger_icon.png");
                return (
                    subKind.Contains("penta") && File.Exists(pentakillBadge) ? pentakillBadge : null,
                    File.Exists(challengerIcon) ? challengerIcon : null
                );
            }
        }

        private (string HookText, string FontPath) ResolveHookTypography(MontageItem package)
        {
            string fontsDir = Path.Combine(_assetsRoot, "fonts");
            string beaufortFont = Path.Combine(fontsDir, "beaufort_heavy.otf");
            string boldFont = Path.Combine(fontsDir, "font_bold.ttf");

            var kind = (package.KindKey ?? "").ToLower();
            var subKind = (package.SubKindKey ?? "").ToLower();

            string fontPath = (kind.Contains("lol") && File.Exists(beaufortFont))
                ? beaufortFont
                : (File.Exists(boldFont) ? boldFont : "Arial");

            // Curated high-CTR hook phrases by game and subkind
            if (subKind.Contains("penta"))
                return ("PENTAKILL?!", fontPath);

            if (subKind.Contains("ace"))
                return ("RADIANT 1v5?!", fontPath);

            if (subKind.Contains("aram"))
                return ("ARAM 200 IQ?!", fontPath);

            if (subKind.Contains("tourn") || subKind.Contains("pro") || subKind.Contains("esport"))
                return ("PRO PLAY 1v5?!", fontPath);

            if (kind.Contains("val"))
                return ("HOW IS HE REAL?!", fontPath);

            return ("1000 IQ PLAY?!", fontPath);
        }

        private string BuildFfmpegArguments(
            string sourceVideo,
            string timestamp,
            string? creatorPath,
            string? badgePath,
            string? rankPath,
            string hookText,
            string fontPath,
            string outputPath)
        {
            var inputs = new List<string>
            {
                $"-ss {timestamp} -i \"{sourceVideo}\""
            };

            int nextInputIndex = 1;
            int? creatorIndex = null;
            int? badgeIndex = null;

            if (!string.IsNullOrEmpty(creatorPath) && File.Exists(creatorPath))
            {
                inputs.Add($"-i \"{creatorPath}\"");
                creatorIndex = nextInputIndex++;
            }

            // In Pentakill/Ace montages, the badge IS the primary hook
            bool isPentaOrAce = hookText.Contains("PENTAKILL") || hookText.Contains("RADIANT");
            if (isPentaOrAce && !string.IsNullOrEmpty(badgePath) && File.Exists(badgePath))
            {
                inputs.Add($"-i \"{badgePath}\"");
                badgeIndex = nextInputIndex++;
            }

            var filter = new List<string>();

            // 1. Background video frame grading: 1280x720, vibrant & natural contrast
            filter.Add("[0:v]scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,eq=contrast=1.15:saturation=1.25[bg]");
            string currentLayer = "[bg]";

            // 2. Creator cutout on bottom-right (scaled cleanly to height 490, covers corner webcam)
            if (creatorIndex.HasValue)
            {
                filter.Add($"[{creatorIndex.Value}:v]scale=-1:490[creator]");
                filter.Add($"{currentLayer}[creator]overlay=x=W-w:y=H-h[layer_creator]");
                currentLayer = "[layer_creator]";
            }

            // 3. Focal element in top-left (Clean Separation: EITHER Badge OR 2-word Hook Text, never stacked)
            if (badgeIndex.HasValue)
            {
                // In-game kill announcement banner in top-left
                filter.Add($"[{badgeIndex.Value}:v]scale=380:-1[badge]");
                filter.Add($"{currentLayer}[badge]overlay=x=50:y=40[out]");
            }
            else
            {
                // Concise 2-word punchy hook text in top-left
                string escapedFont = fontPath.Replace("\\", "/").Replace(":", "\\:");
                string escapedText = hookText.Replace("'", "\\'").Replace(":", "\\:").Replace("%", "\\%");

                string drawTextFilter =
                    $"drawtext=fontfile='{escapedFont}':text='{escapedText}':" +
                    "fontcolor=0xFFE600:fontsize=96:borderw=10:bordercolor=black:x=60:y=40";

                filter.Add($"{currentLayer}{drawTextFilter}[out]");
            }

            string filterComplex = string.Join(";", filter);
            string inputArgs = string.Join(" ", inputs);

            return $"-y {inputArgs} -filter_complex \"{filterComplex}\" -map \"[out]\" -vframes 1 -q:v 2 \"{outputPath}\"";
        }
    }
}
