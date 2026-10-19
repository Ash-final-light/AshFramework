using System;

namespace AshACT.Unity
{
    public enum MarkerOperationKind
    {
        Add,
        Remove
    }

    public enum MarkerOperationTiming
    {
        Applied,
        Periodic
    }

    public enum MarkerOperationTarget
    {
        Target,
        Source
    }

    public enum MarkerRemovalScope
    {
        AllSources,
        EffectSource
    }

    public enum MarkerRemovalMode
    {
        Count,
        All
    }

    /// <summary>保存向独立标记系统提出的操作意图；本阶段不执行请求。</summary>
    [Serializable]
    public sealed class EffectMarkerOperation
    {
        public MarkerOperationKind Kind;
        public MarkerOperationTiming Timing;
        public MarkerOperationTarget Target;
        public uint MarkerId;
        public int Count = 1;
        public MarkerRemovalScope RemovalScope;
        public MarkerRemovalMode RemovalMode;
    }
}
