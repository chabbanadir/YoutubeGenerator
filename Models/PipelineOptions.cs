namespace YoutubeGenerator.Models
{
    public class PipelineOptions
    {
        public bool Download { get; set; }
        public bool Trim { get; set; }
        public bool Transitions { get; set; }
        public bool Montage { get; set; }

        /// <summary>
        /// Final output video filename (without extension)
        /// </summary>
        public string FinalName { get; set; } = "output";

        /// <summary>
        /// Transition type for video merging (fade, dissolve, wipeleft, etc.)
        /// </summary>
        public string TransitionType { get; set; } = "fade";

        //"fade", "fadeblack", "fadewhite",
        //"wipeleft", "wiperight", "wipeup", "wipedown",
        //"slideleft", "slideright", "slideup", "slidedown",
        //"circlecrop", "rectcrop", "distance",
        //"barnout", "barnin",
        //"diagdown", "diagup",
        //"hlslice", "hrslice", "vuslice", "vdslice"

        /// <summary>
        /// Duration of transitions in seconds
        /// </summary>
        public double TransitionDuration { get; set; } = 2.0;
    }
}
