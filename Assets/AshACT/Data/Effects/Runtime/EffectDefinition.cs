using System;
using System.Collections.Generic;
using AshACT.Attributes;
using Framework.FixedPoint;

namespace AshACT.Effects
{
    public enum EffectDurationKind
    {
        Instant,
        Finite,
        Infinite
    }

    public enum EffectValueKind
    {
        Constant,
        SetByCaller,
        SourceBase,
        SourceCurrent,
        TargetBase,
        TargetCurrent,
        Arithmetic
    }

    public readonly struct EffectModifier
    {
        public AttributeId AttributeId { get; }
        public AttributeOperation Operation { get; }
        public EffectValue Value { get; }
        public int Priority { get; }

        public EffectModifier(AttributeId attributeId, AttributeOperation operation,
            EffectValue value, int priority = 0)
        {
            AttributeId = attributeId;
            Operation = operation;
            Value = value;
            Priority = priority;
        }
    }

    /// <summary>通过工厂校验并复制的共享定义；战斗期间由调用方锁定定义编号与版本。</summary>
    public sealed class EffectDefinition
    {
        public EffectDefinitionId Id { get; }
        public EffectDurationKind DurationKind { get; }
        public long DurationFrames { get; }
        public long PeriodFrames { get; }
        public IReadOnlyList<EffectModifier> Modifiers { get; }

        private EffectDefinition(EffectDefinitionId id, EffectDurationKind durationKind,
            long durationFrames, long periodFrames, EffectModifier[] modifiers)
        {
            Id = id;
            DurationKind = durationKind;
            DurationFrames = durationFrames;
            PeriodFrames = periodFrames;
            Modifiers = Array.AsReadOnly(modifiers);
        }

        public static EffectResult TryCreate(EffectDefinitionId id, EffectDurationKind durationKind,
            long durationFrames, long periodFrames,
            IReadOnlyList<EffectModifier> modifiers, out EffectDefinition definition)
        {
            definition = null;
            if (!id.IsValid || modifiers == null || periodFrames < 0 ||
                durationKind < EffectDurationKind.Instant || durationKind > EffectDurationKind.Infinite ||
                (durationKind == EffectDurationKind.Finite ? durationFrames <= 0 : durationFrames != 0) ||
                (durationKind == EffectDurationKind.Instant && periodFrames != 0))
            {
                return new EffectResult(EffectError.InvalidDefinition);
            }

            EffectModifier[] modifierCopy = new EffectModifier[modifiers.Count];
            for (int i = 0; i < modifierCopy.Length; i++)
            {
                EffectModifier modifier = modifiers[i];
                EffectError valueError = modifier.Value.Validate();
                if (valueError != EffectError.None)
                {
                    return new EffectResult(valueError, modifierIndex: i);
                }

                if (!modifier.AttributeId.IsValid || modifier.Operation < AttributeOperation.Add ||
                    modifier.Operation > AttributeOperation.Override ||
                    (modifier.Value.Kind == EffectValueKind.SetByCaller && modifier.Value.ParameterId == 0))
                {
                    return new EffectResult(EffectError.InvalidDefinition, modifierIndex: i);
                }

                if (modifier.Value.Kind == EffectValueKind.Constant &&
                    ValidateOperand(modifier.Operation, modifier.Value.Constant) != EffectError.None)
                {
                    return new EffectResult(EffectError.InvalidDefinition, modifierIndex: i);
                }

                modifierCopy[i] = modifier;
            }

            definition = new EffectDefinition(id, durationKind, durationFrames, periodFrames, modifierCopy);
            return new EffectResult(EffectError.None);
        }

        internal static EffectError ValidateOperand(AttributeOperation operation, FP64 value)
        {
            if (!AttributeMath.IsConfigurationValue(value) ||
                (operation == AttributeOperation.Multiply && value < FP64.Zero) ||
                (operation == AttributeOperation.Divide && value < FP64.One))
            {
                return EffectError.InvalidParameter;
            }

            return EffectError.None;
        }
    }
}
