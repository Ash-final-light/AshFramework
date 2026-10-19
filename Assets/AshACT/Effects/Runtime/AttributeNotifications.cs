using System;
using System.Collections.Generic;
using AshACT.Attributes;

namespace AshACT.Effects
{
    [Flags]
    public enum AttributeChangeChannels
    {
        Base = 1,
        Current = 2,
        Both = Base | Current
    }

    public enum AttributeChangeReason
    {
        DirectCommand,
        EffectApplied,
        EffectRemoved,
        EffectExpired,
        PeriodicExecution,
        EntityDeactivated
    }

    /// <summary>触发整笔结算的操作上下文，不表示每个连带变化的唯一修改器来源。</summary>
    public readonly struct AttributeChangeCause
    {
        public AttributeChangeReason Reason { get; }
        public EntityId TriggerEntityId { get; }
        public EntityId SourceId { get; }
        public EffectDefinitionId DefinitionId { get; }
        public EffectInstanceId InstanceId { get; }

        internal AttributeChangeCause(AttributeChangeReason reason, EntityId triggerEntityId,
            EntityId sourceId = default, EffectDefinitionId definitionId = default, EffectInstanceId instanceId = default)
        {
            Reason = reason;
            TriggerEntityId = triggerEntityId;
            SourceId = sourceId;
            DefinitionId = definitionId;
            InstanceId = instanceId;
        }
    }

    /// <summary>同一事务内某个属性提交前后的净变化，数值保留完整定点精度。</summary>
    public readonly struct AttributeChange
    {
        public EntityId EntityId { get; }
        public AttributeId AttributeId { get; }
        public ulong Lifecycle { get; }
        public AttributeValue OldValue { get; }
        public AttributeValue NewValue { get; }
        public AttributeChangeChannels ChangedChannels { get; }
        public long Frame { get; }
        public AttributeChangeCause Cause { get; }

        internal AttributeChange(EntityId entityId, AttributeId attributeId, ulong lifecycle,
            AttributeValue oldValue, AttributeValue newValue, AttributeChangeChannels changedChannels,
            long frame, AttributeChangeCause cause)
        {
            EntityId = entityId;
            AttributeId = attributeId;
            Lifecycle = lifecycle;
            OldValue = oldValue;
            NewValue = newValue;
            ChangedChannels = changedChannels;
            Frame = frame;
            Cause = cause;
        }
    }

    /// <summary>整域恢复成功后的读取失效提示；恢复不冒充正常的属性修改。</summary>
    public readonly struct RuntimeStateReset
    {
        public long PreviousFrame { get; }
        public long Frame { get; }
        public EffectFramePhase Phase { get; }

        internal RuntimeStateReset(long previousFrame, long frame, EffectFramePhase phase)
        {
            PreviousFrame = previousFrame;
            Frame = frame;
            Phase = phase;
        }
    }

    public sealed partial class EffectRuntime
    {
        private sealed class Observer<T> : IDisposable
        {
            private readonly List<Observer<T>> m_Owners;
            internal Action<T> Callback;
            internal readonly AttributeChangeChannels Channels;

            internal Observer(List<Observer<T>> owners, Action<T> callback,
                AttributeChangeChannels channels = AttributeChangeChannels.Both)
            {
                m_Owners = owners;
                Callback = callback;
                Channels = channels;
                owners.Add(this);
            }

            public void Dispose()
            {
                Callback = null;
                m_Owners.Remove(this);
            }
        }

        private sealed class ChangeBatch
        {
            internal readonly List<AttributeChange> Changes;
            internal readonly RuntimeStateReset? Reset;

            internal ChangeBatch(List<AttributeChange> changes, RuntimeStateReset? reset = null)
            {
                Changes = changes;
                Reset = reset;
            }
        }

        private readonly Dictionary<EntityId, Dictionary<AttributeId, List<Observer<AttributeChange>>>> m_Observers =
            new Dictionary<EntityId, Dictionary<AttributeId, List<Observer<AttributeChange>>>>();
        private readonly List<Observer<RuntimeStateReset>> m_ResetObservers = new List<Observer<RuntimeStateReset>>();
        private readonly List<ChangeBatch> m_ChangeBatches = new List<ChangeBatch>();
        private int m_OperationDepth;

        public bool IsPublishingNotifications { get; private set; }
        public Exception LastNotificationException { get; private set; }

        /// <summary>监听异常的独立诊断出口；诊断监听者自己的异常也不会影响模拟提交。</summary>
        public event Action<Exception> NotificationFailed;

        /// <summary>订阅本域中实体实际拥有的属性；令牌跨离场和复活保留，释放后停止回调。</summary>
        public IDisposable Subscribe(EntityId entityId, AttributeId attributeId, Action<AttributeChange> callback,
            AttributeChangeChannels channels = AttributeChangeChannels.Current)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (channels != AttributeChangeChannels.Base && channels != AttributeChangeChannels.Current &&
                channels != AttributeChangeChannels.Both)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            if (!m_Controllers.TryGetValue(entityId, out EffectController controller))
            {
                throw new ArgumentException("实体未注册到本运行域。", nameof(entityId));
            }

            if (!controller.Attributes.TryGetValue(attributeId, out _))
            {
                throw new ArgumentException("实体没有该属性。", nameof(attributeId));
            }

            if (!m_Observers.TryGetValue(entityId, out Dictionary<AttributeId, List<Observer<AttributeChange>>> attributes))
            {
                attributes = new Dictionary<AttributeId, List<Observer<AttributeChange>>>();
                m_Observers.Add(entityId, attributes);
            }

            if (!attributes.TryGetValue(attributeId, out List<Observer<AttributeChange>> observers))
            {
                observers = new List<Observer<AttributeChange>>();
                attributes.Add(attributeId, observers);
            }

            return new Observer<AttributeChange>(observers, callback, channels);
        }

        public IDisposable SubscribeStateReset(Action<RuntimeStateReset> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            return new Observer<RuntimeStateReset>(m_ResetObservers, callback);
        }

        private T RunOperation<T>(Func<T> action)
        {
            m_OperationDepth++;
            try
            {
                return action();
            }
            finally
            {
                m_OperationDepth--;
                if (m_OperationDepth == 0)
                {
                    PublishChanges();
                }
            }
        }

        private EffectResult RunWrite(Func<EffectResult> action)
        {
            if (IsPublishingNotifications)
            {
                return new EffectResult(EffectError.NotificationReentrancy);
            }

            return RunOperation(action);
        }

        private List<AttributeChange> CollectChanges(EffectRuntime candidate, AttributeChangeCause cause)
        {
            List<AttributeChange> changes = new List<AttributeChange>();
            foreach (EffectController controller in m_Controllers.Values)
            {
                EffectController next = candidate.m_Controllers[controller.EntityId];
                foreach (AttributeId id in controller.Attributes.AttributeIds)
                {
                    controller.Attributes.TryGetValue(id, out AttributeValue oldValue);
                    next.Attributes.TryGetValue(id, out AttributeValue newValue);
                    AttributeChangeChannels channels = 0;
                    if (oldValue.BaseValue.Raw != newValue.BaseValue.Raw)
                    {
                        channels |= AttributeChangeChannels.Base;
                    }

                    if (oldValue.CurrentValue.Raw != newValue.CurrentValue.Raw)
                    {
                        channels |= AttributeChangeChannels.Current;
                    }

                    if (channels != 0)
                    {
                        changes.Add(new AttributeChange(controller.EntityId, id, next.Lifecycle,
                            oldValue, newValue, channels, candidate.CurrentFrame, cause));
                    }
                }
            }

            return changes;
        }

        private void PublishChanges()
        {
            if (m_ChangeBatches.Count == 0)
            {
                return;
            }

            IsPublishingNotifications = true;
            foreach (EffectController controller in m_Controllers.Values)
            {
                controller.Attributes.SetManagedWrites(true);
            }

            try
            {
                foreach (ChangeBatch batch in m_ChangeBatches)
                {
                    if (batch.Reset.HasValue)
                    {
                        foreach (Observer<RuntimeStateReset> observer in m_ResetObservers.ToArray())
                        {
                            Notify(observer, batch.Reset.Value);
                        }

                        continue;
                    }

                    // 在本批次第一次回调前固定全部接收者，回调中新增订阅只参与后续批次。
                    Observer<AttributeChange>[][] receivers = new Observer<AttributeChange>[batch.Changes.Count][];
                    for (int i = 0; i < batch.Changes.Count; i++)
                    {
                        AttributeChange change = batch.Changes[i];
                        receivers[i] = m_Observers.TryGetValue(change.EntityId, out var attributes) &&
                            attributes.TryGetValue(change.AttributeId, out var observers) ?
                            observers.ToArray() : Array.Empty<Observer<AttributeChange>>();
                    }

                    for (int i = 0; i < batch.Changes.Count; i++)
                    {
                        AttributeChange change = batch.Changes[i];
                        foreach (Observer<AttributeChange> observer in receivers[i])
                        {
                            if ((observer.Channels & change.ChangedChannels) != 0)
                            {
                                Notify(observer, change);
                            }
                        }
                    }
                }
            }
            finally
            {
                m_ChangeBatches.Clear();
                foreach (EffectController controller in m_Controllers.Values)
                {
                    controller.Attributes.SetManagedWrites(CurrentFrame >= 0);
                }

                IsPublishingNotifications = false;
            }
        }

        private void Notify<T>(Observer<T> observer, T value)
        {
            try
            {
                observer.Callback?.Invoke(value);
            }
            catch (Exception exception)
            {
                LastNotificationException = exception;
                if (NotificationFailed == null)
                {
                    return;
                }

                foreach (Action<Exception> report in NotificationFailed.GetInvocationList())
                {
                    try
                    {
                        report(exception);
                    }
                    catch (Exception reportingException)
                    {
                        LastNotificationException = reportingException;
                    }
                }
            }
        }

        private static AttributeChangeCause CauseFor(AttributeChangeReason reason, ActiveEffect effect)
        {
            return new AttributeChangeCause(reason, effect.TargetId, effect.Spec.SourceId, effect.Spec.Definition.Id, effect.Id);
        }
    }
}
