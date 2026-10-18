using System;
using System.Collections.Generic;
using AshACT.Attributes;

namespace AshACT.Effects
{
    /// <summary>每个目标的效果索引；写入统一经过所属运行时，标记不占用属性。</summary>
    public sealed class EffectController
    {
        private readonly SortedDictionary<EffectInstanceId, ActiveEffect> m_Active =
            new SortedDictionary<EffectInstanceId, ActiveEffect>();

        public EntityId EntityId { get; }
        public AttributeStore Attributes { get; }
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

        public int GetMarkerCount(MarkerId marker)
        {
            int count = 0;
            foreach (ActiveEffect effect in m_Active.Values)
            {
                foreach (MarkerId candidate in effect.Spec.Definition.Markers)
                {
                    if (candidate == marker)
                    {
                        count++;
                    }
                }
            }

            return count;
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
