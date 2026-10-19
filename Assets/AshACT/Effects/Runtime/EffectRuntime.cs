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
    public sealed partial class EffectRuntime
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
            if (IsPublishingNotifications)
            {
                return new EffectResult(EffectError.NotificationReentrancy, entityId);
            }

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

            if (!attributes.TryClaim(this))
            {
                return new EffectResult(EffectError.StoreAlreadyRegistered, entityId);
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
            return RunWrite(() => BeginFrameCore(frame));
        }

        private EffectResult BeginFrameCore(long frame)
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

            foreach (EffectController controller in m_Controllers.Values)
            {
                controller.Attributes.SetManagedWrites(true);
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
            return RunWrite(EndFrameCore);
        }

        private EffectResult EndFrameCore()
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

                EffectResult transaction = Atomic(candidate => candidate.Tick(effect.Id, effect.TargetId),
                    CauseFor(AttributeChangeReason.PeriodicExecution, effect));
                if (!transaction.Succeeded)
                {
                    return Stop(transaction);
                }
            }

            LastCompletedFrame = CurrentFrame;
            Phase = EffectFramePhase.BetweenFrames;
            return new EffectResult(EffectError.None);
        }

        public EffectResult TryApply(EntityId targetId, EffectSpec spec)
        {
            return RunWrite(() => ApplyCore(targetId, spec));
        }

        private EffectResult ApplyCore(EntityId targetId, EffectSpec spec)
        {
            EffectResult writable = RequireInput(targetId);
            if (!writable.Succeeded)
            {
                return writable;
            }

            EffectResult result = Atomic(candidate => candidate.Apply(targetId, spec),
                new AttributeChangeCause(AttributeChangeReason.EffectApplied, targetId,
                    spec?.SourceId ?? default, spec?.Definition.Id ?? default));
            if (!result.Succeeded && result.InstanceId.Value > LastInstanceId)
            {
                return new EffectResult(result.Error, result.TargetId, modifierIndex: result.ModifierIndex,
                    parameterId: result.ParameterId, attributeResult: result.AttributeResult, attributeId: result.AttributeId);
            }

            return result;
        }

        /// <summary>去重并按实体编号升序执行，每个目标独立提交；结果顺序与执行顺序一致。</summary>
        public IReadOnlyList<EffectResult> ApplyMany(IReadOnlyList<EntityId> targets, EffectSpec spec)
        {
            if (IsPublishingNotifications)
            {
                return Array.AsReadOnly(new[] { new EffectResult(EffectError.NotificationReentrancy) });
            }

            return RunOperation(() => ApplyManyCore(targets, spec));
        }

        private IReadOnlyList<EffectResult> ApplyManyCore(IReadOnlyList<EntityId> targets, EffectSpec spec)
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
            return RunWrite(() => RemoveCore(targetId, instanceId));
        }

        private EffectResult RemoveCore(EntityId targetId, EffectInstanceId instanceId)
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

            return Atomic(candidate => candidate.RemoveCore(effect), CauseFor(AttributeChangeReason.EffectRemoved, effect));
        }

        private EffectResult Remove(ActiveEffect effect)
        {
            return Atomic(candidate => candidate.RemoveCore(effect), CauseFor(AttributeChangeReason.EffectExpired, effect));
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
