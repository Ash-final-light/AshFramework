using System;

namespace AshACT.Effects
{
    public enum MarkerRequestKind
    {
        Add = 1,
        Remove = 2
    }

    public enum MarkerRequestRemovalScope
    {
        AllSources,
        RequestSource
    }

    public enum MarkerRequestRemovalMode
    {
        Count,
        All
    }

    /// <summary>提交给外部标记管理器的不可变请求；不包含资产引用或标记处理算法。</summary>
    public readonly struct MarkerRequest
    {
        public MarkerRequestKind Kind
        {
            get;
        }
        public MarkerId MarkerId
        {
            get;
        }
        public EntityId SourceId
        {
            get;
        }
        public EntityId RecipientId
        {
            get;
        }
        public long Frame
        {
            get;
        }
        public int Count
        {
            get;
        }
        public MarkerRequestRemovalScope RemovalScope
        {
            get;
        }
        public MarkerRequestRemovalMode RemovalMode
        {
            get;
        }
        public EffectDefinitionId DefinitionId
        {
            get;
        }
        public EffectInstanceId InstanceId
        {
            get;
        }
        public int OperationIndex
        {
            get;
        }

        /// <summary>默认结构体无效；全部移除使用零数量，其余请求要求正整数数量。</summary>
        public bool IsValid => MarkerId.IsValid && SourceId.IsValid && RecipientId.IsValid && Frame >= 0 &&
            OperationIndex >= 0 && (!InstanceId.IsValid || DefinitionId.IsValid) &&
            ((Kind == MarkerRequestKind.Add && Count > 0 &&
            RemovalScope == MarkerRequestRemovalScope.AllSources && RemovalMode == MarkerRequestRemovalMode.Count) ||
            (Kind == MarkerRequestKind.Remove &&
            (RemovalScope == MarkerRequestRemovalScope.AllSources || RemovalScope == MarkerRequestRemovalScope.RequestSource) &&
            ((RemovalMode == MarkerRequestRemovalMode.Count && Count > 0) ||
            (RemovalMode == MarkerRequestRemovalMode.All && Count == 0))));

        public MarkerRequest(MarkerRequestKind kind, MarkerId markerId, EntityId sourceId, EntityId recipientId,
            long frame, int count = 1, MarkerRequestRemovalScope removalScope = MarkerRequestRemovalScope.AllSources,
            MarkerRequestRemovalMode removalMode = MarkerRequestRemovalMode.Count,
            EffectDefinitionId definitionId = default, EffectInstanceId instanceId = default, int operationIndex = 0)
        {
            Kind = kind;
            MarkerId = markerId;
            SourceId = sourceId;
            RecipientId = recipientId;
            Frame = frame;
            Count = count;
            RemovalScope = removalScope;
            RemovalMode = removalMode;
            DefinitionId = definitionId;
            InstanceId = instanceId;
            OperationIndex = operationIndex;
            if (!IsValid)
            {
                throw new ArgumentException("标记请求要求有效身份、非负帧与操作序号，以及与操作方式一致的数量和移除范围。");
            }
        }
    }
}
