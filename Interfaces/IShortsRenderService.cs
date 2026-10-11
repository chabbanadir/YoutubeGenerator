using System.Threading.Tasks;

namespace YoutubeGenerator.Interfaces
{
    public interface IShortsRenderService
    {
        /// <summary>
        /// Renders a vertical 9:16 (1080x1920) YouTube Short from a source 16:9 clip.
        /// Applies Option A (Stacked Webcam + Crosshair) for Valorant if webcam is present,
        /// or Option B (Ambient Blurred Background) for LoL and clips without webcam.
        /// </summary>
        Task<string?> GenerateShortAsync(string inputVideoPath, string outputDir, string kindKey, string? streamerName = null, string? subKindKey = null);
    }
}
