using System;
using System.Collections.Generic;
using AshACT.Attributes;

namespace AshACT.Effects
{
    /// <summary>每个目标的效果索引；写入统一经过所属运行时，不管理独立标记状态。</summary>
    public sealed class EffectController
    {
        private readonly SortedDictionary<EffectInstanceId, ActiveEffect> m_Active =
            new SortedDictionary<EffectInstanceId, ActiveEffect>();

        public EntityId EntityId { get; }
        public AttributeStore Attributes { get; }
        public bool IsActive { get; internal set; } = true;
        public ulong Lifecycle { get; internal set; } = 1;
        public int ActiveCount => m_Active.Count;

        internal EffectController(EntityId entityId, AttributeStore attributes)
        {
            EntityId = entityId;
            Attributes = attributes;
        }

        public bool TryGetActive(EffectInstanceId id, out ActiveEffect effect)
        {
            return m_Active.TryGetValue(id, out effect);
        }

        public IReadOnlyList<ActiveEffect> GetActiveEffects()
        {
            ActiveEffect[] snapshot = new ActiveEffect[m_Active.Count];
            m_Active.Values.CopyTo(snapshot, 0);
            return Array.AsReadOnly(snapshot);
        }

        internal void CopyFrom(EffectController candidate)
        {
            Attributes.CommitFrom(candidate.Attributes);
            IsActive = candidate.IsActive;
            Lifecycle = candidate.Lifecycle;
            m_Active.Clear();
            foreach (ActiveEffect effect in candidate.m_Active.Values)
            {
                m_Active.Add(effect.Id, effect);
            }
        }

        internal void Commit(ActiveEffect effect)
        {
            m_Active[effect.Id] = effect;
        }

        internal void Forget(EffectInstanceId id)
        {
            m_Active.Remove(id);
        }
    }
}
