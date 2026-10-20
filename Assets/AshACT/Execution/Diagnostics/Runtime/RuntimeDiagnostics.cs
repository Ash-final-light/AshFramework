using System.Collections.Generic;
using AshACT.Attributes;
using AshACT.Effects;
using Framework.FixedPoint;

namespace AshACT.Unity
{
    public sealed class AttributeAggregateView
    {
        public FP64 Additive { get; internal set; }
        public FP64 Multiplier { get; internal set; } = FP64.One;
        public FP64 Divisor { get; internal set; } = FP64.One;
        public ulong OverrideHandle { get; internal set; }
        public ulong OverrideSequence { get; internal set; }
        public int OverridePriority { get; internal set; }
        public AttributeError Error { get; internal set; }
    }

    public readonly struct DependencyView
    {
        public DependencyNode From { get; }
        public DependencyNode To { get; }
        public EffectInstanceId InstanceId { get; }

        internal DependencyView(DependencyNode from, DependencyNode to, EffectInstanceId instanceId = default)
        {
            From = from;
            To = to;
            InstanceId = instanceId;
        }

        public override string ToString() => $"{From.EntityId}/{From.AttributeId}/{From.Kind} → {To.EntityId}/{To.AttributeId}/{To.Kind}  实例 {InstanceId}";
    }

    public static class RuntimeDiagnostics
    {
        public static AttributeAggregateView Aggregate(AttributeEntryState entry)
        {
            AttributeAggregateView view = new AttributeAggregateView();
            foreach (AttributeModifierState modifier in entry.Modifiers)
            {
                AttributeCommand command = modifier.Command;
                AttributeError error = AttributeError.None;
                switch (command.Operation)
                {
                    case AttributeOperation.Add:
                        error = AttributeMath.Add(view.Additive, command.Operand, out FP64 additive);
                        view.Additive = additive;
                        break;
                    case AttributeOperation.Multiply:
                        error = AttributeMath.Subtract(command.Operand, FP64.One, out FP64 multipleDelta);
                        if (error == AttributeError.None)
                        {
                            error = AttributeMath.Add(view.Multiplier, multipleDelta, out FP64 multiplier);
                            view.Multiplier = multiplier;
                        }
                        break;
                    case AttributeOperation.Divide:
                        error = AttributeMath.Subtract(command.Operand, FP64.One, out FP64 divideDelta);
                        if (error == AttributeError.None)
                        {
                            error = AttributeMath.Add(view.Divisor, divideDelta, out FP64 divisor);
                            view.Divisor = divisor;
                        }
                        break;
                    case AttributeOperation.Override:
                        if (view.OverrideHandle == 0 || command.Priority > view.OverridePriority ||
                            (command.Priority == view.OverridePriority && modifier.Sequence > view.OverrideSequence))
                        {
                            view.OverrideHandle = command.ModifierHandle;
                            view.OverridePriority = command.Priority;
                            view.OverrideSequence = modifier.Sequence;
                        }
                        break;
                }

                if (error != AttributeError.None)
                {
                    view.Error = error;
                    return view;
                }
            }

            if (view.Multiplier < FP64.Zero)
            {
                view.Multiplier = FP64.Zero;
            }

            return view;
        }

        /// <summary>从同一只读快照重建依赖边；Frozen 来源与周期读取不产生持续边。</summary>
        public static IReadOnlyList<DependencyView> Dependencies(EffectRuntimeState state)
        {
            List<DependencyView> edges = new List<DependencyView>();
            foreach (EffectEntityState entity in state.Entities)
            {
                foreach (AttributeEntryState entry in entity.Attributes.Entries)
                {
                    DependencyNode baseNode = new DependencyNode(entity.EntityId, entry.Configuration.Id, AttributeReadKind.Base);
                    edges.Add(new DependencyView(baseNode, new DependencyNode(entity.EntityId, entry.Configuration.Id, AttributeReadKind.Current)));
                    if (entry.Configuration.MaximumAttribute.IsValid)
                    {
                        edges.Add(new DependencyView(new DependencyNode(entity.EntityId, entry.Configuration.MaximumAttribute,
                            AttributeReadKind.Current), baseNode));
                    }
                }

                foreach (ActiveEffect effect in entity.Effects)
                {
                    if (effect.Spec.Definition.PeriodFrames == 0)
                    {
                        foreach (EffectModifier modifier in effect.Spec.Definition.Modifiers)
                        {
                            Reads(edges, effect, modifier.Value, modifier.AttributeId);
                        }
                    }
                }
            }

            edges.Sort((a, b) =>
            {
                int from = a.From.CompareTo(b.From);
                int to = a.To.CompareTo(b.To);
                return from != 0 ? from : to != 0 ? to : a.InstanceId.Value.CompareTo(b.InstanceId.Value);
            });
            return edges.AsReadOnly();
        }

        private static void Reads(List<DependencyView> edges, ActiveEffect effect, EffectValue value, AttributeId destination)
        {
            if (value.Kind == EffectValueKind.Arithmetic)
            {
                Reads(edges, effect, value.Expression.Left, destination);
                Reads(edges, effect, value.Expression.Right, destination);
            }
            else if (value.Kind >= EffectValueKind.SourceBase && value.Kind <= EffectValueKind.TargetCurrent)
            {
                bool source = value.Kind == EffectValueKind.SourceBase || value.Kind == EffectValueKind.SourceCurrent;
                bool current = value.Kind == EffectValueKind.SourceCurrent || value.Kind == EffectValueKind.TargetCurrent;
                if (!source || !effect.IsSourceFrozen)
                {
                    edges.Add(new DependencyView(new DependencyNode(source ? effect.Spec.SourceId : effect.TargetId,
                        value.AttributeId, current ? AttributeReadKind.Current : AttributeReadKind.Base),
                        new DependencyNode(effect.TargetId, destination, AttributeReadKind.Current), effect.Id));
                }
            }
        }
    }
}
