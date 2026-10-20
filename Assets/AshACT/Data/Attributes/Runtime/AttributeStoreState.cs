using System;
using System.Collections.Generic;
using Framework.FixedPoint;

namespace AshACT.Attributes
{
    public readonly struct AttributeModifierState
    {
        public AttributeCommand Command { get; }
        public ulong Sequence { get; }

        internal AttributeModifierState(AttributeCommand command, ulong sequence)
        {
            Command = command;
            Sequence = sequence;
        }
    }

    public sealed class AttributeEntryState
    {
        public AttributeConfiguration Configuration { get; }
        public AttributeValue Value { get; }
        public IReadOnlyList<AttributeModifierState> Modifiers { get; }

        internal AttributeEntryState(AttributeConfiguration configuration, AttributeValue value,
            AttributeModifierState[] modifiers)
        {
            Configuration = configuration;
            Value = value;
            Modifiers = Array.AsReadOnly(modifiers);
        }
    }

    /// <summary>完整只读容器状态；配置的全部字段共同标识配置身份。</summary>
    public sealed class AttributeStoreState
    {
        public IReadOnlyList<AttributeEntryState> Entries { get; }
        public ulong LastApplicationSequence { get; }

        internal AttributeStoreState(AttributeEntryState[] entries, ulong sequence)
        {
            Entries = Array.AsReadOnly(entries);
            LastApplicationSequence = sequence;
        }
    }

    public sealed partial class AttributeStore
    {
        private object m_DomainOwner;
        private bool m_ManagedWrites;

        internal bool TryClaim(object owner)
        {
            if (m_DomainOwner != null && !ReferenceEquals(m_DomainOwner, owner))
            {
                return false;
            }

            m_DomainOwner = owner;
            return true;
        }

        internal void SetManagedWrites(bool managed)
        {
            m_ManagedWrites = managed;
        }

        public AttributeStoreState CaptureState()
        {
            AttributeEntryState[] entries = new AttributeEntryState[m_Entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = m_Entries[i];
                AttributeModifierState[] modifiers = new AttributeModifierState[entry.Modifiers.Count];
                for (int j = 0; j < modifiers.Length; j++)
                {
                    PersistentModifier modifier = entry.Modifiers[j];
                    modifiers[j] = new AttributeModifierState(modifier.Command, modifier.Sequence);
                }

                entries[i] = new AttributeEntryState(entry.Configuration,
                    new AttributeValue(entry.BaseValue, entry.CurrentValue), modifiers);
            }

            return new AttributeStoreState(entries, LastApplicationSequence);
        }

        // 以下能力仅向域协调器开放；候选容器不向调用方发布。
        internal AttributeStore Fork()
        {
            Entry[] entries = new Entry[m_Entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new Entry(m_Entries[i]);
            }

            return new AttributeStore(entries, m_Indices)
            {
                LastApplicationSequence = LastApplicationSequence
            };
        }

        internal void CommitFrom(AttributeStore candidate)
        {
            m_Entries = candidate.m_Entries;
            LastApplicationSequence = candidate.LastApplicationSequence;
        }

        internal AttributeConfiguration Configuration(AttributeId id)
        {
            return m_Entries[m_Indices[id]].Configuration;
        }

        internal AttributeResult ApplyUncalculated(AttributeCommand command)
        {
            ulong sequence = LastApplicationSequence;
            AttributeError error = Apply(m_Entries, command, ref sequence);
            if (error == AttributeError.None)
            {
                LastApplicationSequence = sequence;
            }

            return new AttributeResult(error, command.AttributeId);
        }

        internal AttributeResult ReplaceOperand(AttributeCommand command)
        {
            Entry entry = m_Entries[m_Indices[command.AttributeId]];
            for (int i = 0; i < entry.Modifiers.Count; i++)
            {
                PersistentModifier modifier = entry.Modifiers[i];
                if (modifier.Command.ModifierHandle == command.ModifierHandle)
                {
                    AttributeError error = ValidateOperand(command.Operation, command.Operand);
                    if (error != AttributeError.None)
                    {
                        return new AttributeResult(error, command.AttributeId);
                    }

                    entry.Modifiers[i] = new PersistentModifier(command, modifier.Sequence);
                    return new AttributeResult(AttributeError.None);
                }
            }

            return new AttributeResult(AttributeError.MissingModifier, command.AttributeId);
        }

        internal AttributeResult RecalculateNode(AttributeId id, bool current)
        {
            Entry entry = m_Entries[m_Indices[id]];
            AttributeConfiguration configuration = entry.Configuration;
            if (configuration.Kind == AttributeKind.Resource)
            {
                if (!current)
                {
                    FP64 maximum = configuration.Maximum;
                    if (configuration.MaximumAttribute.IsValid)
                    {
                        FP64 dynamicMaximum = m_Entries[m_Indices[configuration.MaximumAttribute]].CurrentValue;
                        if (dynamicMaximum < maximum)
                        {
                            maximum = dynamicMaximum;
                        }
                    }

                    if (maximum < configuration.Minimum)
                    {
                        return new AttributeResult(AttributeError.InvalidConfiguration, id);
                    }

                    entry.BaseValue = AttributeMath.Clamp(entry.BaseValue, configuration.Minimum, maximum);
                }
                else
                {
                    entry.CurrentValue = entry.BaseValue;
                }
            }
            else if (current)
            {
                AttributeError error = AttributeAggregator.Evaluate(entry.BaseValue, entry.Modifiers, out FP64 value);
                if (error != AttributeError.None)
                {
                    return new AttributeResult(error, id);
                }

                entry.CurrentValue = AttributeMath.Clamp(value, configuration.Minimum, configuration.Maximum);
            }

            return new AttributeResult(AttributeError.None);
        }

        internal bool LoadState(AttributeStoreState state)
        {
            if (state == null || state.Entries.Count != m_Entries.Length)
            {
                return false;
            }

            HashSet<ulong> handles = new HashSet<ulong>();
            HashSet<ulong> sequences = new HashSet<ulong>();
            for (int i = 0; i < m_Entries.Length; i++)
            {
                AttributeEntryState saved = state.Entries[i];
                AttributeConfiguration configuration = m_Entries[i].Configuration;
                if (!configuration.Equals(saved.Configuration) ||
                    saved.Value.BaseValue < configuration.Minimum || saved.Value.BaseValue > configuration.Maximum ||
                    saved.Value.CurrentValue < configuration.Minimum || saved.Value.CurrentValue > configuration.Maximum)
                {
                    return false;
                }

                Entry entry = new Entry(configuration);
                entry.BaseValue = saved.Value.BaseValue;
                entry.CurrentValue = saved.Value.CurrentValue;
                ulong previous = 0;
                foreach (AttributeModifierState modifier in saved.Modifiers)
                {
                    AttributeCommand command = modifier.Command;
                    if (configuration.Kind != AttributeKind.Stat || command.Kind != AttributeCommandKind.AddPersistent ||
                        command.AttributeId != configuration.Id || command.ModifierHandle == 0 ||
                        !handles.Add(command.ModifierHandle) || modifier.Sequence <= previous ||
                        modifier.Sequence > state.LastApplicationSequence || !sequences.Add(modifier.Sequence) ||
                        ValidateOperand(command.Operation, command.Operand) != AttributeError.None)
                    {
                        return false;
                    }

                    previous = modifier.Sequence;
                    entry.Modifiers.Add(new PersistentModifier(command, modifier.Sequence));
                }

                m_Entries[i] = entry;
            }

            LastApplicationSequence = state.LastApplicationSequence;
            return true;
        }
    }
}
