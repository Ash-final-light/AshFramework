using AshACT.Attributes;

namespace AshACT.Effects
{
    public enum EffectError
    {
        None,
        InvalidDefinition,
        InvalidSpec,
        InvalidEntity,
        DuplicateEntity,
        StoreAlreadyRegistered,
        MissingTarget,
        MissingAttribute,
        InvalidParameter,
        DuplicateParameter,
        MissingParameter,
        UnsupportedValueSource,
        AttributeFailure,
        InvalidFrame,
        InvalidPhase,
        FrameOverflow,
        IdExhausted,
        MissingInstance,
        Faulted,
        MissingSource,
        DependencyCycle,
        EntityInactive,
        InvalidState,
        ConfigurationMismatch,
        ModifierOwnedByEffect,
        NotificationReentrancy
    }

    /// <summary>包含目标、实例、配置项和底层属性事务错误的只读结果。</summary>
    public readonly struct EffectResult
    {
        public EffectError Error { get; }
        public EntityId TargetId { get; }
        public EffectInstanceId InstanceId { get; }
        public int ModifierIndex { get; }
        public uint ParameterId { get; }
        public AttributeId AttributeId { get; }
        public AttributeResult AttributeResult { get; }
        public bool Succeeded => Error == EffectError.None;

        internal EffectResult(EffectError error, EntityId targetId = default,
            EffectInstanceId instanceId = default, int modifierIndex = -1,
            uint parameterId = 0, AttributeResult attributeResult = default, AttributeId attributeId = default)
        {
            Error = error;
            TargetId = targetId;
            InstanceId = instanceId;
            ModifierIndex = modifierIndex;
            ParameterId = parameterId;
            AttributeResult = attributeResult;
            AttributeId = attributeId.IsValid ? attributeId : attributeResult.AttributeId;
        }
    }
}
