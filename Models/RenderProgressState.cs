using System;
using System.Collections.Generic;

namespace YoutubeGenerator.Models
{
    public enum RenderSessionStatus
    {
        Idle,
        Preparing,
        IntroConcat,
        Probing,
        Rendering,
        Finalizing,
        Completed,
        Failed
    }

    public enum SubStepStatus
    {
        Pending,
        Active,
        Completed,
        Failed
    }

    public class RenderSubStep
    {
        public int Index { get; set; }
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public SubStepStatus Status { get; set; } = SubStepStatus.Pending;
        public double ProgressPercent { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Detail { get; set; }
    }

    public class RenderLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Level { get; set; } = "info"; // info, step, ffmpeg, warning, error, success
        public string Message { get; set; } = "";
    }

    public class RenderTelemetry
    {
        public long CurrentFrame { get; set; }
        public double Fps { get; set; }
        public string Speed { get; set; } = "";
        public string TimeEncoded { get; set; } = "";
        public double TotalExpectedDurationSeconds { get; set; }
        public string Bitrate { get; set; } = "";
        public double OverallProgressPercent { get; set; }
        public string EstimatedRemainingTime { get; set; } = "";
    }

    public class ShortProgressState
    {
        public bool IsActive { get; set; }
        public string? Streamer { get; set; }
        public string? KindKey { get; set; }
        public string? SubKindKey { get; set; }
        public string Step { get; set; } = "Idle";
        public double ProgressPercent { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public class RenderProgressState
    {
        public bool IsActive { get; set; }
        public RenderSessionStatus Status { get; set; } = RenderSessionStatus.Idle;
        public string MontageId { get; set; } = "";
        public string KindKey { get; set; } = "";
        public string SubKindKey { get; set; } = "";
        public int TotalClips { get; set; }
        public int CurrentStepIndex { get; set; } // 1 to 5
        public string CurrentStepName { get; set; } = "Idle";
        public double StepProgressPercent { get; set; }
        public double OverallProgressPercent { get; set; }
        
        // Exact Batch Programmed Information
        public int CurrentPass { get; set; }
        public int TotalPasses { get; set; }
        public int BatchClipsCount { get; set; }
        public string CurrentBatchTarget { get; set; } = "";

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
        public string? ErrorMessage { get; set; }
        public string? FinalVideoPath { get; set; }

        public ShortProgressState? ActiveShort { get; set; }

        public RenderTelemetry Telemetry { get; set; } = new();
        public List<RenderSubStep> SubSteps { get; set; } = new();
        public List<RenderLogEntry> RecentLogs { get; set; } = new();

        public static List<RenderSubStep> CreateDefaultSubSteps()
        {
            return new List<RenderSubStep>
            {
                new RenderSubStep
                {
                    Index = 1,
                    Key = "prep",
                    Name = "Assets & Specs",
                    Description = "Resolve profiles, resources & directory setup",
                    Status = SubStepStatus.Pending
                },
                new RenderSubStep
                {
                    Index = 2,
                    Key = "intro",
                    Name = "Intro Stitching",
                    Description = "Prepend channel intro to lead clip via FFmpeg",
                    Status = SubStepStatus.Pending
                },
                new RenderSubStep
                {
                    Index = 3,
                    Key = "probe",
                    Name = "Clip Probing",
                    Description = "Analyze durations & compute transition offsets",
                    Status = SubStepStatus.Pending
                },
                new RenderSubStep
                {
                    Index = 4,
                    Key = "merge",
                    Name = "Crossfade Merge",
                    Description = "Filter complex xfade & audio acrossfade encoding",
                    Status = SubStepStatus.Pending
                },
                new RenderSubStep
                {
                    Index = 5,
                    Key = "handoff",
                    Name = "Handoff & Cleanup",
                    Description = "Stage to YouTube bag & clean intermediate clips",
                    Status = SubStepStatus.Pending
                }
            };
        }
    }
}
