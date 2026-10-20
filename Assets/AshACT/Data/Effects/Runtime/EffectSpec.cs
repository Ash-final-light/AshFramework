using System.Collections.Generic;
using System.Collections.ObjectModel;
using AshACT.Attributes;
using Framework.FixedPoint;

namespace AshACT.Effects
{
    public readonly struct EffectParameter
    {
        public uint Id { get; }
        public FP64 Value { get; }

        public EffectParameter(uint id, FP64 value)
        {
            Id = id;
            Value = value;
        }
    }

    /// <summary>一次施加的只读输入，多目标共享输入但各自创建独立实例。</summary>
    public sealed class EffectSpec
    {
        public EffectDefinition Definition { get; }
        public EntityId SourceId { get; }
        public IReadOnlyDictionary<uint, FP64> Parameters { get; }

        private EffectSpec(EffectDefinition definition, EntityId sourceId, Dictionary<uint, FP64> parameters)
        {
            Definition = definition;
            SourceId = sourceId;
            Parameters = new ReadOnlyDictionary<uint, FP64>(parameters);
        }

        public static EffectResult TryCreate(EffectDefinition definition, EntityId sourceId,
            IReadOnlyList<EffectParameter> parameters, out EffectSpec spec)
        {
            spec = null;
            if (definition == null || !sourceId.IsValid || parameters == null)
            {
                return new EffectResult(EffectError.InvalidSpec);
            }

            Dictionary<uint, FP64> copy = new Dictionary<uint, FP64>();
            foreach (EffectParameter parameter in parameters)
            {
                if (parameter.Id == 0 || !AttributeMath.IsConfigurationValue(parameter.Value))
                {
                    return new EffectResult(EffectError.InvalidParameter, parameterId: parameter.Id);
                }

                if (copy.ContainsKey(parameter.Id))
                {
                    return new EffectResult(EffectError.DuplicateParameter, parameterId: parameter.Id);
                }

                copy.Add(parameter.Id, parameter.Value);
            }

            spec = new EffectSpec(definition, sourceId, copy);
            return new EffectResult(EffectError.None);
        }
    }
}
