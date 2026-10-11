using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class RenderTrackerService : IRenderTrackerService
    {
        private readonly ILogger<RenderTrackerService> _logger;
        private readonly object _lock = new();
        private readonly RenderProgressState _state;
        private const int MaxLogEntries = 400;

        // Subscribers for SSE stream
        private readonly List<Channel<RenderProgressState>> _subscribers = new();

        public RenderTrackerService(ILogger<RenderTrackerService> logger)
        {
            _logger = logger;
            _state = new RenderProgressState
            {
                IsActive = false,
                Status = RenderSessionStatus.Idle,
                SubSteps = RenderProgressState.CreateDefaultSubSteps(),
                RecentLogs = new List<RenderLogEntry>()
            };
        }

        public void StartSession(string montageId, string kindKey, string subKindKey, int totalClips, double expectedDurationSeconds = 0)
        {
            lock (_lock)
            {
                _state.IsActive = true;
                _state.Status = RenderSessionStatus.Preparing;
                _state.MontageId = montageId;
                _state.KindKey = kindKey;
                _state.SubKindKey = subKindKey;
                _state.TotalClips = totalClips;
                _state.CurrentStepIndex = 1;
                _state.CurrentStepName = "Assets & Specs";
                _state.StepProgressPercent = 0;
                _state.OverallProgressPercent = 0;
                _state.CurrentPass = 0;
                _state.TotalPasses = 0;
                _state.BatchClipsCount = 0;
                _state.CurrentBatchTarget = "";
                _state.StartedAt = DateTime.UtcNow;
                _state.CompletedAt = null;
                _state.ErrorMessage = null;
                _state.FinalVideoPath = null;
                _state.LastUpdatedAt = DateTime.UtcNow;

                _state.Telemetry = new RenderTelemetry
                {
                    TotalExpectedDurationSeconds = expectedDurationSeconds,
                    OverallProgressPercent = 0,
                    Speed = "0.0x",
                    Fps = 0,
                    TimeEncoded = "00:00:00"
                };

                _state.SubSteps = RenderProgressState.CreateDefaultSubSteps();
                _state.SubSteps[0].Status = SubStepStatus.Active;
                _state.SubSteps[0].StartedAt = DateTime.UtcNow;

                InternalAddLog($"🎬 Render session started for package {montageId} ({kindKey}/{subKindKey}, {totalClips} clips)", "step");
            }

            BroadcastState();
        }

        public void SetStep(int stepIndex, string? detail = null)
        {
            lock (_lock)
            {
                if (!_state.IsActive) _state.IsActive = true;

                for (int i = 0; i < _state.SubSteps.Count; i++)
                {
                    var step = _state.SubSteps[i];
                    if (step.Index < stepIndex)
                    {
                        if (step.Status != SubStepStatus.Completed)
                        {
                            step.Status = SubStepStatus.Completed;
                            step.ProgressPercent = 100;
                            step.CompletedAt ??= DateTime.UtcNow;
                        }
                    }
                    else if (step.Index == stepIndex)
                    {
                        step.Status = SubStepStatus.Active;
                        step.StartedAt ??= DateTime.UtcNow;
                        if (!string.IsNullOrEmpty(detail))
                            step.Detail = detail;
                    }
                    else
                    {
                        step.Status = SubStepStatus.Pending;
                        step.ProgressPercent = 0;
                    }
                }

                _state.CurrentStepIndex = stepIndex;
                var currentStep = _state.SubSteps.FirstOrDefault(s => s.Index == stepIndex);
                if (currentStep != null)
                {
                    _state.CurrentStepName = currentStep.Name;
                    _state.StepProgressPercent = currentStep.ProgressPercent;
                }

                _state.Status = stepIndex switch
                {
                    1 => RenderSessionStatus.Preparing,
                    2 => RenderSessionStatus.IntroConcat,
                    3 => RenderSessionStatus.Probing,
                    4 => RenderSessionStatus.Rendering,
                    5 => RenderSessionStatus.Finalizing,
                    _ => _state.Status
                };

                double basePercent = stepIndex switch
                {
                    1 => 5,
                    2 => 15,
                    3 => 25,
                    4 => 35,
                    5 => 92,
                    _ => 0
                };
                if (_state.OverallProgressPercent < basePercent)
                {
                    _state.OverallProgressPercent = basePercent;
                    _state.Telemetry.OverallProgressPercent = basePercent;
                }

                _state.LastUpdatedAt = DateTime.UtcNow;
                InternalAddLog($"⏩ [Step 3.{stepIndex}] {_state.CurrentStepName}" + (string.IsNullOrEmpty(detail) ? "" : $": {detail}"), "step");
            }

            BroadcastState();
        }

        public void UpdateStepProgress(int stepIndex, double progressPercent, string? detail = null)
        {
            lock (_lock)
            {
                var step = _state.SubSteps.FirstOrDefault(s => s.Index == stepIndex);
                if (step != null)
                {
                    step.ProgressPercent = Math.Clamp(progressPercent, 0, 100);
                    if (!string.IsNullOrEmpty(detail))
                        step.Detail = detail;
                }

                if (_state.CurrentStepIndex == stepIndex)
                {
                    _state.StepProgressPercent = Math.Clamp(progressPercent, 0, 100);
                }

                _state.LastUpdatedAt = DateTime.UtcNow;
            }

            BroadcastState();
        }

        public void UpdateFfmpegProgress(double fps, string speed, string timeEncoded, double stepProgress, double overallProgress, string bitrate = "")
        {
            lock (_lock)
            {
                _state.Telemetry.Fps = fps;
                if (!string.IsNullOrWhiteSpace(speed)) _state.Telemetry.Speed = speed;
                if (!string.IsNullOrWhiteSpace(timeEncoded)) _state.Telemetry.TimeEncoded = timeEncoded;
                if (!string.IsNullOrWhiteSpace(bitrate)) _state.Telemetry.Bitrate = bitrate;

                if (stepProgress > 0)
                {
                    _state.StepProgressPercent = Math.Clamp(stepProgress, 0, 100);
                    var current = _state.SubSteps.FirstOrDefault(s => s.Index == _state.CurrentStepIndex);
                    if (current != null) current.ProgressPercent = _state.StepProgressPercent;
                }

                if (overallProgress > 0)
                {
                    _state.OverallProgressPercent = Math.Clamp(overallProgress, 0, 100);
                    _state.Telemetry.OverallProgressPercent = _state.OverallProgressPercent;
                }

                _state.LastUpdatedAt = DateTime.UtcNow;
            }

            BroadcastState();
        }

        public void SetBatchInfo(int currentBatch, int totalBatches, int batchClipsCount, string batchTarget)
        {
            lock (_lock)
            {
                _state.CurrentPass = currentBatch;
                _state.TotalPasses = totalBatches;
                _state.BatchClipsCount = batchClipsCount;
                _state.CurrentBatchTarget = batchTarget;
                _state.LastUpdatedAt = DateTime.UtcNow;
            }

            BroadcastState();
        }

        public void AddLog(string message, string level = "info")
        {
            lock (_lock)
            {
                InternalAddLog(message, level);
            }

            BroadcastState();
        }

        private void InternalAddLog(string message, string level = "info")
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var entry = new RenderLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level.ToLowerInvariant(),
                Message = message.Trim()
            };

            _state.RecentLogs.Add(entry);
            if (_state.RecentLogs.Count > MaxLogEntries)
            {
                _state.RecentLogs.RemoveRange(0, _state.RecentLogs.Count - MaxLogEntries);
            }

            _state.LastUpdatedAt = DateTime.UtcNow;
        }

        public void CompleteSession(string finalVideoPath)
        {
            lock (_lock)
            {
                _state.IsActive = false;
                _state.Status = RenderSessionStatus.Completed;
                _state.FinalVideoPath = finalVideoPath;
                _state.CompletedAt = DateTime.UtcNow;
                _state.StepProgressPercent = 100;
                _state.OverallProgressPercent = 100;
                _state.Telemetry.OverallProgressPercent = 100;

                foreach (var step in _state.SubSteps)
                {
                    step.Status = SubStepStatus.Completed;
                    step.ProgressPercent = 100;
                    step.CompletedAt ??= DateTime.UtcNow;
                }

                _state.LastUpdatedAt = DateTime.UtcNow;
                InternalAddLog($"🎉 Montage render finished successfully! Final output: {finalVideoPath}", "success");
            }

            BroadcastState();
        }

        public void FailSession(string errorMessage)
        {
            lock (_lock)
            {
                _state.IsActive = false;
                _state.Status = RenderSessionStatus.Failed;
                _state.ErrorMessage = errorMessage;
                _state.CompletedAt = DateTime.UtcNow;

                var currentStep = _state.SubSteps.FirstOrDefault(s => s.Index == _state.CurrentStepIndex);
                if (currentStep != null)
                {
                    currentStep.Status = SubStepStatus.Failed;
                    currentStep.Detail = errorMessage;
                }

                _state.LastUpdatedAt = DateTime.UtcNow;
                InternalAddLog($"❌ Montage render failed: {errorMessage}", "error");
            }

            BroadcastState();
        }

        public void StartShortProgress(string streamer, string kindKey, string subKindKey, string step = "Starting")
        {
            lock (_lock)
            {
                _state.ActiveShort = new ShortProgressState
                {
                    IsActive = true,
                    Streamer = streamer,
                    KindKey = kindKey,
                    SubKindKey = subKindKey,
                    Step = step,
                    ProgressPercent = 10,
                    StartedAt = DateTime.UtcNow
                };
                _state.LastUpdatedAt = DateTime.UtcNow;
                InternalAddLog($"⚡ [Shorts] Starting pipeline for @{streamer} ({kindKey}/{subKindKey}): {step}", "shorts");
            }

            BroadcastState();
        }

        public void UpdateShortProgress(double progressPercent, string? step = null)
        {
            lock (_lock)
            {
                if (_state.ActiveShort != null)
                {
                    _state.ActiveShort.ProgressPercent = Math.Clamp(progressPercent, 0, 100);
                    if (!string.IsNullOrEmpty(step))
                    {
                        _state.ActiveShort.Step = step;
                    }
                    _state.LastUpdatedAt = DateTime.UtcNow;
                }
            }

            BroadcastState();
        }

        public void CompleteShortProgress(string? message = null)
        {
            lock (_lock)
            {
                if (_state.ActiveShort != null)
                {
                    _state.ActiveShort.IsActive = false;
                    _state.ActiveShort.ProgressPercent = 100;
                    _state.ActiveShort.CompletedAt = DateTime.UtcNow;
                    _state.ActiveShort.Step = "Completed";
                    _state.LastUpdatedAt = DateTime.UtcNow;
                }

                if (!string.IsNullOrEmpty(message))
                {
                    InternalAddLog($"✅ [Shorts] {message}", "success");
                }
            }

            BroadcastState();
        }

        public RenderProgressState GetSnapshot()
        {
            lock (_lock)
            {
                return CloneSnapshot(_state);
            }
        }

        public async IAsyncEnumerable<RenderProgressState> SubscribeAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var channel = Channel.CreateBounded<RenderProgressState>(new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

            lock (_lock)
            {
                _subscribers.Add(channel);
                channel.Writer.TryWrite(CloneSnapshot(_state));
            }

            try
            {
                while (!ct.IsCancellationRequested && await channel.Reader.WaitToReadAsync(ct))
                {
                    while (channel.Reader.TryRead(out var snapshot))
                    {
                        yield return snapshot;
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    _subscribers.Remove(channel);
                }
                channel.Writer.TryComplete();
            }
        }

        private void BroadcastState()
        {
            RenderProgressState snapshot;
            List<Channel<RenderProgressState>> subscribersCopy;

            lock (_lock)
            {
                snapshot = CloneSnapshot(_state);
                subscribersCopy = _subscribers.ToList();
            }

            foreach (var sub in subscribersCopy)
            {
                sub.Writer.TryWrite(snapshot);
            }
        }

        private static RenderProgressState CloneSnapshot(RenderProgressState src)
        {
            return new RenderProgressState
            {
                IsActive = src.IsActive,
                Status = src.Status,
                MontageId = src.MontageId,
                KindKey = src.KindKey,
                SubKindKey = src.SubKindKey,
                TotalClips = src.TotalClips,
                CurrentStepIndex = src.CurrentStepIndex,
                CurrentStepName = src.CurrentStepName,
                StepProgressPercent = src.StepProgressPercent,
                OverallProgressPercent = src.OverallProgressPercent,
                CurrentPass = src.CurrentPass,
                TotalPasses = src.TotalPasses,
                BatchClipsCount = src.BatchClipsCount,
                CurrentBatchTarget = src.CurrentBatchTarget,
                StartedAt = src.StartedAt,
                CompletedAt = src.CompletedAt,
                LastUpdatedAt = src.LastUpdatedAt,
                ErrorMessage = src.ErrorMessage,
                FinalVideoPath = src.FinalVideoPath,
                ActiveShort = src.ActiveShort == null ? null : new ShortProgressState
                {
                    IsActive = src.ActiveShort.IsActive,
                    Streamer = src.ActiveShort.Streamer,
                    KindKey = src.ActiveShort.KindKey,
                    SubKindKey = src.ActiveShort.SubKindKey,
                    Step = src.ActiveShort.Step,
                    ProgressPercent = src.ActiveShort.ProgressPercent,
                    StartedAt = src.ActiveShort.StartedAt,
                    CompletedAt = src.ActiveShort.CompletedAt
                },
                Telemetry = new RenderTelemetry
                {
                    CurrentFrame = src.Telemetry.CurrentFrame,
                    Fps = src.Telemetry.Fps,
                    Speed = src.Telemetry.Speed,
                    TimeEncoded = src.Telemetry.TimeEncoded,
                    TotalExpectedDurationSeconds = src.Telemetry.TotalExpectedDurationSeconds,
                    Bitrate = src.Telemetry.Bitrate,
                    OverallProgressPercent = src.Telemetry.OverallProgressPercent,
                    EstimatedRemainingTime = src.Telemetry.EstimatedRemainingTime
                },
                SubSteps = src.SubSteps.Select(s => new RenderSubStep
                {
                    Index = s.Index,
                    Key = s.Key,
                    Name = s.Name,
                    Description = s.Description,
                    Status = s.Status,
                    ProgressPercent = s.ProgressPercent,
                    StartedAt = s.StartedAt,
                    CompletedAt = s.CompletedAt,
                    Detail = s.Detail
                }).ToList(),
                RecentLogs = src.RecentLogs.Select(l => new RenderLogEntry
                {
                    Timestamp = l.Timestamp,
                    Level = l.Level,
                    Message = l.Message
                }).ToList()
            };
        }
    }
}
