using AshFramework.Math;

namespace AshFramework.Collide.Unity
{
    public enum PositionCorrectionStatus
    {
        NotCompleted,
        Resolved,
        IterationLimit,
        NoProgress,
        Skipped,
        InProgress,
        FrameLimit
    }

    /// <summary>最终一次成功 Step 的修正状态及最大剩余阻挡深度。</summary>
    public readonly struct PositionCorrectionResult
    {
        public PositionCorrectionStatus Status
        {
            get;
        }

        public int Iterations
        {
            get;
        }

        public Fix64 RemainingDepth
        {
            get;
        }

        /// <summary>Number of logic frames consumed by this gradual correction.</summary>
        public int ProcessedFrames
        {
            get;
        }

        internal PositionCorrectionResult(PositionCorrectionStatus status, int iterations, Fix64 remainingDepth, int processedFrames = 0)
        {
            Status = status;
            Iterations = iterations;
            RemainingDepth = remainingDepth;
            ProcessedFrames = processedFrames;
        }
    }
}
