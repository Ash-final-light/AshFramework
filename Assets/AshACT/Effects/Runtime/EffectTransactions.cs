using System;
using System.Collections.Generic;
using AshACT.Attributes;
using Framework.FixedPoint;

namespace AshACT.Effects
{
    /// <summary>绑定一次来源生命周期的不可变快照，复活不会改变已有引用。</summary>
    public sealed class SourceSnapshot
    {
        public EntityId EntityId { get; }
        public ulong Lifecycle { get; }
        public AttributeStoreState Attributes { get; }

        internal SourceSnapshot(EntityId entityId, ulong lifecycle, AttributeStoreState attributes)
        {
            EntityId = entityId;
            Lifecycle = lifecycle;
            Attributes = attributes;
        }

        internal bool TryRead(AttributeId id, bool current, out FP64 value)
        {
            foreach (AttributeEntryState entry in Attributes.Entries)
            {
                if (entry.Configuration.Id == id)
                {
                    value = current ? entry.Value.CurrentValue : entry.Value.BaseValue;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }

    public sealed partial class EffectRuntime
    {
        /// <summary>已注册容器的模拟写入入口；每条命令后传播依赖，整批共同提交。</summary>
        public EffectResult TryExecute(EntityId entityId, IReadOnlyList<AttributeCommand> commands)
        {
            return RunWrite(() => ExecuteCore(entityId, commands));
        }

        private EffectResult ExecuteCore(EntityId entityId, IReadOnlyList<AttributeCommand> commands)
        {
            EffectResult writable = RequireInput(entityId);
            if (!writable.Succeeded)
            {
                return writable;
            }

            return Atomic(candidate => candidate.Execute(entityId, commands),
                new AttributeChangeCause(AttributeChangeReason.DirectCommand, entityId));
        }

        public EffectResult TryDeactivate(EntityId entityId)
        {
            return RunWrite(() => DeactivateCore(entityId));
        }

        private EffectResult DeactivateCore(EntityId entityId)
        {
            EffectResult writable = RequireInput(entityId);
            return writable.Succeeded ? Atomic(candidate => candidate.Deactivate(entityId),
                new AttributeChangeCause(AttributeChangeReason.EntityDeactivated, entityId)) : writable;
        }

        public EffectResult TryReactivate(EntityId entityId)
        {
            return RunWrite(() => ReactivateCore(entityId));
        }

        private EffectResult ReactivateCore(EntityId entityId)
        {
            EffectResult writable = RequireInput(entityId);
            if (!writable.Succeeded)
            {
                return writable;
            }

            if (!m_Controllers.TryGetValue(entityId, out EffectController controller))
            {
                return new EffectResult(EffectError.MissingTarget, entityId);
            }

            if (controller.IsActive)
            {
                return new EffectResult(EffectError.InvalidPhase, entityId);
            }

            if (controller.Lifecycle == ulong.MaxValue)
            {
                return new EffectResult(EffectError.IdExhausted, entityId);
            }

            controller.Lifecycle++;
            controller.IsActive = true;
            return new EffectResult(EffectError.None, entityId);
        }

        private EffectRuntime ForkDomain()
        {
            EffectRuntime candidate = new EffectRuntime
            {
                CurrentFrame = CurrentFrame,
                LastCompletedFrame = LastCompletedFrame,
                LastInstanceId = LastInstanceId,
                LastModifierHandle = LastModifierHandle,
                Phase = Phase,
                Fault = Fault
            };
            foreach (EffectController controller in m_Controllers.Values)
            {
                EffectController copy = new EffectController(controller.EntityId, controller.Attributes.Fork())
                {
                    IsActive = controller.IsActive,
                    Lifecycle = controller.Lifecycle
                };
                foreach (ActiveEffect effect in controller.GetActiveEffects())
                {
                    copy.Commit(effect);
                }

                candidate.m_Controllers.Add(copy.EntityId, copy);
            }

            return candidate;
        }

        private void CommitDomain(EffectRuntime candidate)
        {
            foreach (EffectController controller in m_Controllers.Values)
            {
                controller.CopyFrom(candidate.m_Controllers[controller.EntityId]);
                controller.Attributes.SetManagedWrites(candidate.CurrentFrame >= 0);
            }

            CurrentFrame = candidate.CurrentFrame;
            LastCompletedFrame = candidate.LastCompletedFrame;
            LastInstanceId = candidate.LastInstanceId;
            LastModifierHandle = candidate.LastModifierHandle;
            Phase = candidate.Phase;
            Fault = candidate.Fault;
        }

        private EffectResult Atomic(Func<EffectRuntime, EffectResult> action, AttributeChangeCause cause)
        {
            EffectRuntime candidate = ForkDomain();
            EffectResult result = action(candidate);
            if (result.Succeeded)
            {
                if (cause.Reason == AttributeChangeReason.EffectApplied)
                {
                    cause = new AttributeChangeCause(cause.Reason, cause.TriggerEntityId,
                        cause.SourceId, cause.DefinitionId, result.InstanceId);
                }

                List<AttributeChange> changes = CollectChanges(candidate, cause);
                CommitDomain(candidate);
                if (changes.Count != 0)
                {
                    m_ChangeBatches.Add(new ChangeBatch(changes));
                }
            }

            return result;
        }

        private EffectResult Execute(EntityId entityId, IReadOnlyList<AttributeCommand> commands)
        {
            if (!m_Controllers.TryGetValue(entityId, out EffectController controller))
            {
                return new EffectResult(EffectError.MissingTarget, entityId);
            }

            if (!controller.IsActive)
            {
                return new EffectResult(EffectError.EntityInactive, entityId);
            }

            if (commands == null)
            {
                return new EffectResult(EffectError.InvalidSpec, entityId);
            }

            for (int i = 0; i < commands.Count; i++)
            {
                AttributeCommand command = commands[i];
                foreach (ActiveEffect effect in controller.GetActiveEffects())
                {
                    if (command.Kind == AttributeCommandKind.RemovePersistent &&
                        ContainsHandle(effect, command.ModifierHandle))
                    {
                        return new EffectResult(EffectError.ModifierOwnedByEffect, entityId, effect.Id, i);
                    }
                }

                AttributeResult applied = controller.Attributes.ApplyUncalculated(command);
                if (!applied.Succeeded)
                {
                    return AttributeFailure(entityId, default,
                        new AttributeResult(applied.Error, applied.AttributeId, i));
                }

                EffectResult calculation = Recalculate();
                if (!calculation.Succeeded)
                {
                    return calculation;
                }
            }

            return new EffectResult(EffectError.None, entityId);
        }

        private static bool ContainsHandle(ActiveEffect effect, ulong handle)
        {
            foreach (ulong candidate in effect.ModifierHandles)
            {
                if (candidate == handle)
                {
                    return true;
                }
            }

            return false;
        }

        private EffectResult Apply(EntityId targetId, EffectSpec spec)
        {
            if (spec == null)
            {
                return new EffectResult(EffectError.InvalidSpec, targetId);
            }

            if (!m_Controllers.TryGetValue(targetId, out EffectController controller))
            {
                return new EffectResult(EffectError.MissingTarget, targetId);
            }

            if (!controller.IsActive)
            {
                return new EffectResult(EffectError.EntityInactive, targetId);
            }

            ulong lifecycle = 0;
            if (m_Controllers.TryGetValue(spec.SourceId, out EffectController source))
            {
                if (!source.IsActive)
                {
                    return new EffectResult(EffectError.EntityInactive, spec.SourceId);
                }

                lifecycle = source.Lifecycle;
            }

            if (LastInstanceId == ulong.MaxValue)
            {
                return new EffectResult(EffectError.IdExhausted, targetId);
            }

            EffectDefinition definition = spec.Definition;
            long? end = null;
            if (definition.DurationKind == EffectDurationKind.Finite)
            {
                if (CurrentFrame > long.MaxValue - definition.DurationFrames)
                {
                    return new EffectResult(EffectError.FrameOverflow, targetId);
                }

                end = CurrentFrame + definition.DurationFrames;
            }

            bool persistent = definition.DurationKind != EffectDurationKind.Instant && definition.PeriodFrames == 0;
            AttributeCommand[] commands = new AttributeCommand[definition.Modifiers.Count];
            ulong handle = LastModifierHandle;
            for (int i = 0; i < commands.Length; i++)
            {
                EffectModifier modifier = definition.Modifiers[i];
                if (!controller.Attributes.TryGetValue(modifier.AttributeId, out _))
                {
                    return new EffectResult(EffectError.MissingAttribute, targetId, modifierIndex: i,
                        attributeId: modifier.AttributeId);
                }

                EffectResult input = Evaluate(modifier.Value, spec, targetId, null, out FP64 operand);
                if (!input.Succeeded)
                {
                    return AtModifier(input, targetId, default, i);
                }

                if (EffectDefinition.ValidateOperand(modifier.Operation, operand) != EffectError.None)
                {
                    return new EffectResult(EffectError.InvalidParameter, targetId, modifierIndex: i,
                        parameterId: modifier.Value.ParameterId);
                }

                if (persistent)
                {
                    do
                    {
                        if (handle == ulong.MaxValue)
                        {
                            return new EffectResult(EffectError.IdExhausted, targetId, modifierIndex: i);
                        }

                        handle++;
                    }
                    while (controller.Attributes.HasModifier(handle));
                }

                commands[i] = new AttributeCommand(persistent ? AttributeCommandKind.AddPersistent : AttributeCommandKind.Instant,
                    modifier.AttributeId, modifier.Operation, operand, persistent ? handle : 0, modifier.Priority);
            }

            EffectInstanceId id = new EffectInstanceId(LastInstanceId + 1);
            ActiveEffect active = new ActiveEffect(id, spec, targetId, CurrentFrame, end,
                NextPeriod(CurrentFrame, definition.PeriodFrames, end), commands, lifecycle);
            if (definition.DurationKind != EffectDurationKind.Instant)
            {
                controller.Commit(active);
            }

            // 先检查完整新效果的图，连同尚未物化的修改器一起拒绝环。
            EffectResult graph = BuildOrder(out _);
            if (!graph.Succeeded)
            {
                return AtModifier(graph, targetId, default, graph.ModifierIndex);
            }

            if (persistent)
            {
                for (int i = 0; i < commands.Length; i++)
                {
                    AttributeResult applied = controller.Attributes.ApplyUncalculated(commands[i]);
                    if (!applied.Succeeded)
                    {
                        return AttributeFailure(targetId, default,
                            new AttributeResult(applied.Error, applied.AttributeId, i));
                    }

                    EffectResult calculation = Recalculate(true);
                    if (!calculation.Succeeded)
                    {
                        return calculation;
                    }
                }
            }
            else if (definition.DurationKind == EffectDurationKind.Instant)
            {
                EffectResult applied = ExecuteEffect(active);
                if (!applied.Succeeded)
                {
                    return applied;
                }
            }

            LastInstanceId = id.Value;
            LastModifierHandle = handle;
            return new EffectResult(EffectError.None, targetId, id);
        }

        private EffectResult ExecuteEffect(ActiveEffect effect)
        {
            EffectController controller = m_Controllers[effect.TargetId];
            for (int i = 0; i < effect.Spec.Definition.Modifiers.Count; i++)
            {
                EffectModifier modifier = effect.Spec.Definition.Modifiers[i];
                EffectResult evaluated = Evaluate(modifier.Value, effect.Spec, effect.TargetId, effect.FrozenSource, out FP64 value);
                if (!evaluated.Succeeded)
                {
                    return AtModifier(evaluated, effect.TargetId, effect.Id, i);
                }

                AttributeResult applied = controller.Attributes.ApplyUncalculated(new AttributeCommand(
                    AttributeCommandKind.Instant, modifier.AttributeId, modifier.Operation, value));
                if (!applied.Succeeded)
                {
                    return AttributeFailure(effect.TargetId, effect.Id,
                        new AttributeResult(applied.Error, applied.AttributeId, i));
                }

                EffectResult calculation = Recalculate();
                if (!calculation.Succeeded)
                {
                    return calculation;
                }
            }

            return new EffectResult(EffectError.None, effect.TargetId, effect.Id);
        }

        private EffectResult Tick(EffectInstanceId id, EntityId target)
        {
            EffectController controller = m_Controllers[target];
            controller.TryGetActive(id, out ActiveEffect effect);
            EffectResult result = ExecuteEffect(effect);
            if (result.Succeeded)
            {
                controller.Commit(new ActiveEffect(effect.Id, effect.Spec, effect.TargetId, effect.StartFrame,
                    effect.EndFrame, NextPeriod(CurrentFrame, effect.Spec.Definition.PeriodFrames, effect.EndFrame),
                    effect.Commands, effect.SourceLifecycle, effect.FrozenSource));
            }

            return result;
        }

        private EffectResult RemoveCore(ActiveEffect effect)
        {
            EffectController controller = m_Controllers[effect.TargetId];
            // 整个实例先退出候选图，防止已经移除的修改器被实时重算重新物化。
            controller.Forget(effect.Id);
            foreach (AttributeCommand command in effect.Commands)
            {
                if (command.Kind != AttributeCommandKind.AddPersistent)
                {
                    continue;
                }

                AttributeResult applied = controller.Attributes.ApplyUncalculated(new AttributeCommand(
                    AttributeCommandKind.RemovePersistent, command.AttributeId, modifierHandle: command.ModifierHandle));
                if (!applied.Succeeded)
                {
                    return AttributeFailure(effect.TargetId, effect.Id, applied);
                }

                EffectResult calculation = Recalculate();
                if (!calculation.Succeeded)
                {
                    return calculation;
                }
            }

            return new EffectResult(EffectError.None, effect.TargetId, effect.Id);
        }

        private EffectResult Deactivate(EntityId entityId)
        {
            if (!m_Controllers.TryGetValue(entityId, out EffectController controller))
            {
                return new EffectResult(EffectError.MissingTarget, entityId);
            }

            if (!controller.IsActive)
            {
                return new EffectResult(EffectError.EntityInactive, entityId);
            }

            EffectResult settled = Recalculate();
            if (!settled.Succeeded)
            {
                return settled;
            }

            SourceSnapshot frozen = new SourceSnapshot(entityId, controller.Lifecycle, controller.Attributes.CaptureState());
            foreach (ActiveEffect effect in OrderedEffects())
            {
                if (effect.TargetId != entityId && effect.Spec.SourceId == entityId && !effect.IsSourceFrozen)
                {
                    m_Controllers[effect.TargetId].Commit(new ActiveEffect(effect.Id, effect.Spec, effect.TargetId,
                        effect.StartFrame, effect.EndFrame, effect.NextPeriodFrame, effect.Commands,
                        effect.SourceLifecycle, frozen));
                }
            }

            // 目标清理是一次生命周期事务，清除全部后统一重算，避免中间清理顺序造成假失败。
            foreach (ActiveEffect effect in controller.GetActiveEffects())
            {
                controller.Forget(effect.Id);
                foreach (AttributeCommand command in effect.Commands)
                {
                    if (command.Kind == AttributeCommandKind.AddPersistent)
                    {
                        AttributeResult removed = controller.Attributes.ApplyUncalculated(new AttributeCommand(
                            AttributeCommandKind.RemovePersistent, command.AttributeId, modifierHandle: command.ModifierHandle));
                        if (!removed.Succeeded)
                        {
                            return AttributeFailure(entityId, effect.Id, removed);
                        }
                    }
                }
            }

            controller.IsActive = false;
            return Recalculate();
        }

        private static EffectResult AtModifier(EffectResult result, EntityId target, EffectInstanceId id, int index)
        {
            return new EffectResult(result.Error, target, id, index, result.ParameterId,
                result.AttributeResult, result.AttributeId);
        }
    }
}
