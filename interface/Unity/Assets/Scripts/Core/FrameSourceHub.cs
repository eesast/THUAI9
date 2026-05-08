using System;
using System.Threading;
using Protobuf;

namespace THUAI9.Unity.Core
{
    /// <summary>
    /// Shared ingress point for every visual frame source.
    ///
    /// THUAI7/THUAI8 both separate "where frames come from" from "how frames render":
    /// live networking and playback feed one queue/current-frame surface, and the render
    /// layer consumes that surface.  THUAI9 keeps that contract here so replay and live
    /// debugging expose the same counters, timing and queue trimming behavior.
    /// </summary>
    public static class FrameSourceHub
    {
        public enum SourceKind
        {
            None,
            Playback,
            Live
        }

        public static event Action<MessageToClient> ImmediateFrameSubmitted;
        public static event Action PumpRequested;

        public static SourceKind ActiveKind { get; private set; } = SourceKind.None;
        public static string ActiveName { get; private set; } = "未选择";
        public static string StatusText { get; private set; } = "未选择帧源";
        public static int SubmittedFrameCount { get; private set; }
        public static int DequeuedFrameCount { get; private set; }
        public static int RenderedFrameCount { get; private set; }
        public static int DroppedFrameCount { get; private set; }
        public static int LastSubmittedFrameIndex { get; private set; } = -1;
        public static int LastSubmittedElapsedMilliseconds { get; private set; }
        private static SynchronizationContext mainThreadContext;
        private static int mainThreadId;
        private static int pumpScheduled;

        public static int QueueSize => CoreParam.frameQueue.GetSize();
        public static bool HasFirstFrame => CoreParam.firstFrame != null;

        public static void BindMainThread()
        {
            mainThreadContext = SynchronizationContext.Current;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void Reset(SourceKind kind, string sourceName, string status = null, bool resetCore = true)
        {
            if (resetCore)
            {
                CoreParam.Reset();
            }
            else
            {
                CoreParam.frameQueue.Clear();
            }

            ActiveKind = kind;
            ActiveName = string.IsNullOrWhiteSpace(sourceName) ? TranslateSourceKind(kind) : sourceName;
            StatusText = status ?? "已重置";
            SubmittedFrameCount = 0;
            DequeuedFrameCount = 0;
            RenderedFrameCount = 0;
            DroppedFrameCount = 0;
            LastSubmittedFrameIndex = -1;
            LastSubmittedElapsedMilliseconds = 0;
        }

        public static void SetStatus(SourceKind kind, string sourceName, string status)
        {
            ActiveKind = kind;
            ActiveName = string.IsNullOrWhiteSpace(sourceName) ? TranslateSourceKind(kind) : sourceName;
            StatusText = string.IsNullOrWhiteSpace(status) ? StatusText : status;
        }

        public static void SetFirstFrame(MessageToClient frame, string status = null)
        {
            if (frame == null)
            {
                return;
            }

            CoreParam.firstFrame = frame;
            RecordSubmitted(-1, 0);
            if (!string.IsNullOrWhiteSpace(status))
            {
                StatusText = status;
            }
        }

        public static void EnqueueFrame(MessageToClient frame, int frameIndex = -1, int elapsedMilliseconds = 0, string status = null)
        {
            if (frame == null)
            {
                return;
            }

            ApplyPlaybackClock(frameIndex, elapsedMilliseconds);
            CoreParam.frameQueue.Add(frame);
            RecordSubmitted(frameIndex, elapsedMilliseconds);
            if (!string.IsNullOrWhiteSpace(status))
            {
                StatusText = status;
            }

            RequestPump();
        }

        public static void SubmitImmediate(MessageToClient frame, int frameIndex = -1, int elapsedMilliseconds = 0, string status = null)
        {
            if (frame == null)
            {
                return;
            }

            ApplyPlaybackClock(frameIndex, elapsedMilliseconds);
            CoreParam.currentFrame = frame;
            RecordSubmitted(frameIndex, elapsedMilliseconds);
            if (!string.IsNullOrWhiteSpace(status))
            {
                StatusText = status;
            }

            if (ImmediateFrameSubmitted != null)
            {
                ImmediateFrameSubmitted.Invoke(frame);
            }
            else
            {
                CoreParam.firstFrame = frame;
            }
        }

        public static bool TryDequeueFrame(out MessageToClient frame)
        {
            if (!CoreParam.initialized && CoreParam.firstFrame != null)
            {
                frame = CoreParam.firstFrame;
                CoreParam.firstFrame = null;
            }
            else
            {
                frame = CoreParam.frameQueue.GetValue();
            }

            if (frame == null)
            {
                return false;
            }

            CoreParam.currentFrame = frame;
            DequeuedFrameCount++;
            return true;
        }

        public static void TrimQueueTo(int maxQueueSize)
        {
            while (CoreParam.frameQueue.GetSize() > maxQueueSize)
            {
                if (CoreParam.frameQueue.GetValue() == null)
                {
                    return;
                }

                DroppedFrameCount++;
            }
        }

        public static void RequestPump()
        {
            if (PumpRequested == null)
            {
                return;
            }

            if (mainThreadContext == null)
            {
                if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
                {
                    PumpRequested.Invoke();
                }
                return;
            }

            if (Interlocked.Exchange(ref pumpScheduled, 1) == 1)
            {
                return;
            }

            mainThreadContext.Post(_ =>
            {
                Interlocked.Exchange(ref pumpScheduled, 0);
                PumpRequested?.Invoke();
            }, null);
        }

        public static void MarkRendered(MessageToClient frame)
        {
            if (frame == null)
            {
                return;
            }

            RenderedFrameCount++;
        }

        public static void ApplyPlaybackClock(int frameIndex, int elapsedMilliseconds)
        {
            CoreParam.playbackCurrentFrameIndex = frameIndex;
            CoreParam.playbackElapsedMilliseconds = Math.Max(0, elapsedMilliseconds);
            if (frameIndex >= 0)
            {
                CoreParam.frameCount = frameIndex;
            }

            LastSubmittedFrameIndex = frameIndex;
            LastSubmittedElapsedMilliseconds = Math.Max(0, elapsedMilliseconds);
        }

        public static string BuildDebugText()
        {
            return
                $"帧源：{TranslateSourceKind(ActiveKind)}  {ActiveName}\n" +
                $"状态：{StatusText}\n" +
                $"队列：{QueueSize}  首帧缓存：{(HasFirstFrame ? "是" : "否")}\n" +
                $"提交/出队/渲染：{SubmittedFrameCount}/{DequeuedFrameCount}/{RenderedFrameCount}  丢弃：{DroppedFrameCount}\n" +
                $"当前帧：{CoreParam.frameCount}  回放索引：{CoreParam.playbackCurrentFrameIndex + 1}";
        }

        private static void RecordSubmitted(int frameIndex, int elapsedMilliseconds)
        {
            SubmittedFrameCount++;
            LastSubmittedFrameIndex = frameIndex;
            LastSubmittedElapsedMilliseconds = Math.Max(0, elapsedMilliseconds);
        }

        private static string TranslateSourceKind(SourceKind kind)
        {
            return kind switch
            {
                SourceKind.Playback => "回放",
                SourceKind.Live => "实时",
                _ => "无"
            };
        }
    }
}
