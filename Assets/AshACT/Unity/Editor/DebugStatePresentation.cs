using System.Collections.Generic;
using AshACT.Attributes;
using AshACT.Effects;
using Framework.FixedPoint;

namespace AshACT.Unity.Editor
{
    /// <summary>只在采样时构建显示文本；重绘复用结果，不重新计算快照、哈希或数值格式。</summary>
    public sealed class DebugStatePresentation
    {
        public IReadOnlyList<string> Lines { get; }
        public string Hash { get; }
        public int MaximumLineLength { get; }

        public DebugStatePresentation(EffectRuntimeState state)
        {
            Hash = state.Hash;
            List<string> lines = new List<string>
            {
                $"当前帧 {state.CurrentFrame} / 完成帧 {state.LastCompletedFrame} / {state.Phase}",
                $"Hash {state.Hash}"
            };
            if (!state.Fault.Succeeded)
            {
                lines.Add("结算失败: " + AttributeSession.Describe(state.Fault));
            }

            foreach (EffectEntityState entity in state.Entities)
            {
                lines.Add($"实体 {entity.EntityId} / {(entity.IsActive ? "Active" : "Inactive")} / 生命周期 {entity.Lifecycle}");
                foreach (AttributeEntryState entry in entity.Attributes.Entries)
                {
                    AttributeConfiguration configuration = entry.Configuration;
                    AttributeAggregateView aggregate = RuntimeDiagnostics.Aggregate(entry);
                    lines.Add($"  属性 {configuration.Id} / {configuration.Kind}    Base {Value(entry.Value.BaseValue)}    Current {Value(entry.Value.CurrentValue)}");
                    lines.Add($"  边界 [{Value(configuration.Minimum)}, {Value(configuration.Maximum)}]    动态上限属性 {configuration.MaximumAttribute}");
                    lines.Add($"  A {Value(aggregate.Additive)}    M {Value(aggregate.Multiplier)}    D {Value(aggregate.Divisor)}");
                    lines.Add($"  Override 胜者 handle={aggregate.OverrideHandle} / priority={aggregate.OverridePriority} / sequence={aggregate.OverrideSequence} / {aggregate.Error}");
                    foreach (AttributeModifierState modifier in entry.Modifiers)
                    {
                        lines.Add($"  句柄 {modifier.Command.ModifierHandle} / 顺序 {modifier.Sequence} / {modifier.Command.Operation} {Value(modifier.Command.Operand)}");
                    }
                }

                foreach (ActiveEffect effect in entity.Effects)
                {
                    lines.Add($"  实例 {effect.Id} / 定义 {effect.Spec.Definition.Id} / 来源 {effect.Spec.SourceId} " +
                        $"{(effect.IsSourceFrozen ? "Frozen" : "Live")} / 生命周期 {effect.SourceLifecycle}");
                    lines.Add($"  开始 {effect.StartFrame} / 到期 {effect.EndFrame?.ToString() ?? "∞"} / 下次周期 {effect.NextPeriodFrame?.ToString() ?? "无"} / 间隔 {effect.Spec.Definition.PeriodFrames}");
                    List<string> markers = new List<string>();
                    foreach (MarkerId marker in effect.Spec.Definition.Markers)
                    {
                        markers.Add(marker.ToString());
                    }

                    lines.Add("  标记: " + string.Join(", ", markers));
                    if (effect.FrozenSource != null)
                    {
                        foreach (AttributeEntryState frozen in effect.FrozenSource.Attributes.Entries)
                        {
                            lines.Add($"  Frozen 属性 {frozen.Configuration.Id}    Base {Value(frozen.Value.BaseValue)}    Current {Value(frozen.Value.CurrentValue)}");
                        }
                    }
                }
            }

            lines.Add("实时依赖路径（Frozen 不产生来源实时边）");
            foreach (DependencyView edge in RuntimeDiagnostics.Dependencies(state))
            {
                lines.Add(edge.ToString());
            }

            foreach (string line in lines)
            {
                MaximumLineLength = System.Math.Max(MaximumLineLength, line.Length);
            }

            Lines = lines.AsReadOnly();
        }

        private static string Value(FP64 value) => $"{DecimalValue.Format(value)} [Raw {value.Raw}]";
    }
}
