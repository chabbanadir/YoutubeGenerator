using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeGenerator.Interfaces
{
    public interface ITransitionService
    {
        /// <summary>
        /// Merges multiple videos with transitions in a single FFmpeg command
        /// </summary>
        /// <param name="videoPaths">List of video file paths in order</param>
        /// <param name="outputPath">Output file path</param>
        /// <param name="transitionType">Transition effect (fade, dissolve, wipeleft, etc.)</param>
        /// <param name="transitionDuration">Duration of each transition in seconds</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if successful</returns>
        Task<bool> MergeVideosWithTransitionsAsync(
            List<string> videoPaths,
            string outputPath,
            string transitionType = "fade",
            double transitionDuration = 2.0,
            CancellationToken ct = default);

        /// <summary>
        /// Gets the duration of a video file in seconds
        /// </summary>
        Task<double> GetVideoDurationAsync(string videoPath, CancellationToken ct = default);
    }
}
