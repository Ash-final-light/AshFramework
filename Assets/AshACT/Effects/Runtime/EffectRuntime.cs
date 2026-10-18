using System;
using System.Collections.Generic;
using AshACT.Attributes;
using Framework.FixedPoint;

namespace AshACT.Effects
{
    public enum EffectFramePhase
    {
        BetweenFrames,
        Input,
        Faulted
    }

    /// <summary>
    /// 单模拟域的串行入口。帧开始移除到期项，调用方提交输入后再于帧结束触发周期。
    /// 系统步骤失败后锁定运行时，保留已提交的较早事务供诊断，不能重试半帧。
    /// </summary>
    public sealed class EffectRuntime
    {
        private readonly SortedDictionary<EntityId, EffectController> m_Controllers =
            new SortedDictionary<EntityId, EffectController>();

        public long CurrentFrame { get; private set; } = -1;
        public long LastCompletedFrame { get; private set; } = -1;
        public ulong LastInstanceId { get; private set; }
        public ulong LastModifierHandle { get; private set; }
        public EffectFramePhase Phase { get; private set; }
        public EffectResult Fault { get; private set; }

        public EffectResult TryRegister(EntityId entityId, AttributeStore attributes, out EffectController controller)
        {
            controller = null;
            if (Phase == EffectFramePhase.Faulted)
            {
                return new EffectResult(EffectError.Faulted, entityId);
            }

            if (CurrentFrame != -1 || Phase != EffectFramePhase.BetweenFrames)
            {
                return new EffectResult(EffectError.InvalidPhase, entityId);
            }

            if (!entityId.IsValid || attributes == null)
            {
                return new EffectResult(EffectError.InvalidEntity, entityId);
            }

            if (m_Controllers.ContainsKey(entityId))
            {
                return new EffectResult(EffectError.DuplicateEntity, entityId);
            }

            foreach (EffectController registered in m_Controllers.Values)
            {
                if (ReferenceEquals(registered.Attributes, attributes))
                {
                    return new EffectResult(EffectError.StoreAlreadyRegistered, entityId);
                }
            }

            controller = new EffectController(entityId, attributes);
            m_Controllers.Add(entityId, controller);
            return new EffectResult(EffectError.None, entityId);
        }

        public bool TryGetController(EntityId entityId, out EffectController controller)
        {
            return m_Controllers.TryGetValue(entityId, out controller);
        }

        public EffectResult TryBeginFrame(long frame)
        {
            if (Phase == EffectFramePhase.Faulted)
            {
                return new EffectResult(EffectError.Faulted);
            }

            if (Phase != EffectFramePhase.BetweenFrames)
            {
                return new EffectResult(EffectError.InvalidPhase);
            }

            if (frame < 0 || LastCompletedFrame == long.MaxValue || frame != LastCompletedFrame + 1)
            {
                return new EffectResult(EffectError.InvalidFrame);
            }

            CurrentFrame = frame;
            foreach (ActiveEffect effect in OrderedEffects())
            {
                if (effect.EndFrame.HasValue && effect.EndFrame.Value <= frame)
                {
                    EffectResult result = Remove(effect);
                    if (!result.Succeeded)
                    {
                        return Stop(result);
                    }
                }
            }

            Phase = EffectFramePhase.Input;
            return new EffectResult(EffectError.None);
        }

        public EffectResult TryEndFrame()
        {
            EffectResult writable = RequireInput();
            if (!writable.Succeeded)
            {
                return writable;
            }

            foreach (ActiveEffect effect in OrderedEffects())
            {
                if (!effect.NextPeriodFrame.HasValue || effect.NextPeriodFrame.Value != CurrentFrame)
                {
                    continue;
                }

                EffectController controller = m_Controllers[effect.TargetId];
                AttributeResult transaction = controller.Attributes.TryExecute(effect.Commands);
                if (!transaction.Succeeded)
                {
                    return Stop(AttributeFailure(effect.TargetId, effect.Id, transaction));
                }

                controller.Commit(new ActiveEffect(effect.Id, effect.Spec, effect.TargetId,
                    effect.StartFrame, effect.EndFrame,
                    NextPeriod(CurrentFrame, effect.Spec.Definition.PeriodFrames, effect.EndFrame), effect.Commands));
            }

            LastCompletedFrame = CurrentFrame;
            Phase = EffectFramePhase.BetweenFrames;
            return new EffectResult(EffectError.None);
        }

        public EffectResult TryApply(EntityId targetId, EffectSpec spec)
        {
            EffectResult writable = RequireInput(targetId);
            if (!writable.Succeeded)
            {
                return writable;
            }

            if (spec == null)
            {
                return new EffectResult(EffectError.InvalidSpec, targetId);
            }

            if (!m_Controllers.TryGetValue(targetId, out EffectController controller))
            {
                return new EffectResult(EffectError.MissingTarget, targetId);
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

            EffectInstanceId instanceId = new EffectInstanceId(LastInstanceId + 1);
            bool persistent = definition.DurationKind != EffectDurationKind.Instant && definition.PeriodFrames == 0;
            AttributeCommand[] commands = new AttributeCommand[definition.Modifiers.Count];
            ulong handle = LastModifierHandle;
            for (int i = 0; i < commands.Length; i++)
            {
                EffectModifier modifier = definition.Modifiers[i];
                FP64 operand = modifier.Value.Constant;
                if (modifier.Value.Kind == EffectValueKind.SetByCaller &&
                    !spec.Parameters.TryGetValue(modifier.Value.ParameterId, out operand))
                {
                    return new EffectResult(EffectError.MissingParameter, targetId, modifierIndex: i,
                        parameterId: modifier.Value.ParameterId);
                }

                if (EffectDefinition.ValidateOperand(modifier.Operation, operand) != EffectError.None)
                {
                    return new EffectResult(EffectError.InvalidParameter, targetId, modifierIndex: i,
                        parameterId: modifier.Value.ParameterId);
                }

                if (!controller.Attributes.TryGetValue(modifier.AttributeId, out _))
                {
                    return new EffectResult(EffectError.MissingAttribute, targetId, modifierIndex: i,
                        attributeId: modifier.AttributeId);
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

            // 周期效果在施加时只验证，不执行伤害或治疗；第一次触发严格晚于开始帧。
            if (persistent || definition.DurationKind == EffectDurationKind.Instant)
            {
                AttributeResult transaction = controller.Attributes.TryExecute(commands);
                if (!transaction.Succeeded)
                {
                    return AttributeFailure(targetId, default, transaction);
                }
            }

            if (definition.DurationKind != EffectDurationKind.Instant)
            {
                controller.Commit(new ActiveEffect(instanceId, spec, targetId, CurrentFrame, end,
                    NextPeriod(CurrentFrame, definition.PeriodFrames, end), commands));
            }

            LastInstanceId = instanceId.Value;
            LastModifierHandle = handle;
            return new EffectResult(EffectError.None, targetId, instanceId);
        }

        /// <summary>去重并按实体编号升序执行，每个目标独立提交；结果顺序与执行顺序一致。</summary>
        public IReadOnlyList<EffectResult> ApplyMany(IReadOnlyList<EntityId> targets, EffectSpec spec)
        {
            if (targets == null)
            {
                return Array.AsReadOnly(new[] { new EffectResult(EffectError.InvalidEntity) });
            }

            SortedSet<EntityId> ordered = new SortedSet<EntityId>();
            foreach (EntityId target in targets)
            {
                ordered.Add(target);
            }

            EffectResult[] results = new EffectResult[ordered.Count];
            int index = 0;
            foreach (EntityId target in ordered)
            {
                results[index] = TryApply(target, spec);
                index++;
            }

            return Array.AsReadOnly(results);
        }

        public EffectResult TryRemove(EntityId targetId, EffectInstanceId instanceId)
        {
            EffectResult writable = RequireInput(targetId);
            if (!writable.Succeeded)
            {
                return writable;
            }

            if (!m_Controllers.TryGetValue(targetId, out EffectController controller))
            {
                return new EffectResult(EffectError.MissingTarget, targetId, instanceId);
            }

            if (!controller.TryGetActive(instanceId, out ActiveEffect effect))
            {
                return new EffectResult(EffectError.MissingInstance, targetId, instanceId);
            }

            return Remove(effect);
        }

        private EffectResult Remove(ActiveEffect effect)
        {
            EffectController controller = m_Controllers[effect.TargetId];
            AttributeCommand[] commands = new AttributeCommand[effect.ModifierHandles.Count];
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = new AttributeCommand(AttributeCommandKind.RemovePersistent,
                    effect.Commands[i].AttributeId, modifierHandle: effect.ModifierHandles[i]);
            }

            AttributeResult transaction = controller.Attributes.TryExecute(commands);
            if (!transaction.Succeeded)
            {
                return AttributeFailure(effect.TargetId, effect.Id, transaction);
            }

            controller.Forget(effect.Id);
            return new EffectResult(EffectError.None, effect.TargetId, effect.Id);
        }

        private List<ActiveEffect> OrderedEffects()
        {
            List<ActiveEffect> effects = new List<ActiveEffect>();
            foreach (EffectController controller in m_Controllers.Values)
            {
                effects.AddRange(controller.GetActiveEffects());
            }

            effects.Sort((left, right) => left.Id.CompareTo(right.Id));
            return effects;
        }

        private static long? NextPeriod(long frame, long period, long? end)
        {
            if (period == 0 || frame > long.MaxValue - period)
            {
                return null;
            }

            long next = frame + period;
            return end.HasValue && next >= end.Value ? (long?)null : next;
        }

        private EffectResult RequireInput(EntityId targetId = default)
        {
            return new EffectResult(Phase == EffectFramePhase.Faulted ? EffectError.Faulted :
                Phase == EffectFramePhase.Input ? EffectError.None : EffectError.InvalidPhase, targetId);
        }

        private EffectResult Stop(EffectResult result)
        {
            Phase = EffectFramePhase.Faulted;
            Fault = result;
            return result;
        }

        private static EffectResult AttributeFailure(EntityId target, EffectInstanceId instance, AttributeResult result)
        {
            return new EffectResult(EffectError.AttributeFailure, target, instance,
                result.CommandIndex, attributeResult: result);
        }
    }
}
