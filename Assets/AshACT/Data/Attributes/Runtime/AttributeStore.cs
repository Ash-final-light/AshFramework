using System;
using System.Collections.Generic;
using Framework.FixedPoint;

namespace AshACT.Attributes
{
    /// <summary>
    /// 单个角色的固定属性集合；在模拟线程上按确定性的命令顺序调用。
    /// 事务中不触发回调，只有全部命令成功后才原子替换内部状态。
    /// </summary>
    public sealed partial class AttributeStore
    {
        private sealed class Entry
        {
            internal readonly AttributeConfiguration Configuration;
            internal readonly List<PersistentModifier> Modifiers;
            internal FP64 BaseValue;
            internal FP64 CurrentValue;

            internal Entry(AttributeConfiguration configuration)
            {
                Configuration = configuration;
                Modifiers = new List<PersistentModifier>();
                BaseValue = AttributeMath.Clamp(configuration.InitialBaseValue, configuration.Minimum, configuration.Maximum);
                CurrentValue = BaseValue;
            }

            internal Entry(Entry source)
            {
                Configuration = source.Configuration;
                Modifiers = new List<PersistentModifier>(source.Modifiers);
                BaseValue = source.BaseValue;
                CurrentValue = source.CurrentValue;
            }
        }

        private Entry[] m_Entries;
        private readonly Dictionary<AttributeId, int> m_Indices;

        public IReadOnlyList<AttributeId> AttributeIds { get; }
        public ulong LastApplicationSequence { get; private set; }

        private AttributeStore(Entry[] entries, Dictionary<AttributeId, int> indices)
        {
            m_Entries = entries;
            m_Indices = indices;
            AttributeId[] ids = new AttributeId[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                ids[i] = entries[i].Configuration.Id;
            }

            AttributeIds = Array.AsReadOnly(ids);
        }

        public static AttributeResult TryCreate(IReadOnlyList<AttributeConfiguration> configurations, out AttributeStore store)
        {
            store = null;
            if (configurations == null)
            {
                return new AttributeResult(AttributeError.InvalidConfiguration);
            }

            Entry[] entries = new Entry[configurations.Count];
            Dictionary<AttributeId, int> indices = new Dictionary<AttributeId, int>();
            for (int i = 0; i < configurations.Count; i++)
            {
                AttributeConfiguration configuration = configurations[i];
                AttributeError error = ValidateConfiguration(configuration);
                if (error != AttributeError.None)
                {
                    return new AttributeResult(error, configuration.Id);
                }

                if (indices.ContainsKey(configuration.Id))
                {
                    return new AttributeResult(AttributeError.DuplicateAttribute, configuration.Id);
                }

                indices.Add(configuration.Id, i);
                entries[i] = new Entry(configuration);
            }

            // 按属性编号排序，避免调用方的配置顺序或字典遍历顺序影响结算。
            Array.Sort(entries, (left, right) => left.Configuration.Id.CompareTo(right.Configuration.Id));
            indices.Clear();
            for (int i = 0; i < entries.Length; i++)
            {
                indices.Add(entries[i].Configuration.Id, i);
            }

            foreach (Entry entry in entries)
            {
                AttributeId maximumId = entry.Configuration.MaximumAttribute;
                if (!maximumId.IsValid)
                {
                    continue;
                }

                if (!indices.TryGetValue(maximumId, out int maximumIndex))
                {
                    return new AttributeResult(AttributeError.MissingAttribute, maximumId);
                }

                if (entries[maximumIndex].Configuration.Kind != AttributeKind.Stat)
                {
                    return new AttributeResult(AttributeError.InvalidConfiguration, entry.Configuration.Id);
                }
            }

            AttributeStore candidate = new AttributeStore(entries, indices);
            AttributeResult calculation = candidate.Recalculate(entries, -1);
            if (!calculation.Succeeded)
            {
                return calculation;
            }

            store = candidate;
            return new AttributeResult(AttributeError.None);
        }

        public bool TryGetValue(AttributeId id, out AttributeValue value)
        {
            value = default;
            if (!m_Indices.TryGetValue(id, out int index))
            {
                return false;
            }

            Entry entry = m_Entries[index];
            value = new AttributeValue(entry.BaseValue, entry.CurrentValue);
            return true;
        }

        public bool HasModifier(ulong handle)
        {
            return FindModifier(m_Entries, handle);
        }

        /// <summary>
        /// 按顺序执行命令，每条命令结束后立即结算属性边界和资源上限。
        /// 任一失败均丢弃整批修改，包括修改器移除和应用序号分配。
        /// </summary>
        public AttributeResult TryExecute(IReadOnlyList<AttributeCommand> commands)
        {
            if (m_ManagedWrites)
            {
                return new AttributeResult(AttributeError.StoreOwnedByRuntime);
            }

            if (commands == null)
            {
                return new AttributeResult(AttributeError.InvalidConfiguration);
            }

            Entry[] working = new Entry[m_Entries.Length];
            for (int i = 0; i < working.Length; i++)
            {
                working[i] = new Entry(m_Entries[i]);
            }

            ulong sequence = LastApplicationSequence;
            for (int i = 0; i < commands.Count; i++)
            {
                AttributeCommand command = commands[i];
                AttributeError error = Apply(working, command, ref sequence);
                if (error != AttributeError.None)
                {
                    return new AttributeResult(error, command.AttributeId, i);
                }

                AttributeResult calculation = Recalculate(working, i);
                if (!calculation.Succeeded)
                {
                    return calculation;
                }
            }

            m_Entries = working;
            LastApplicationSequence = sequence;
            return new AttributeResult(AttributeError.None);
        }

        private static AttributeError ValidateConfiguration(AttributeConfiguration configuration)
        {
            if (!configuration.Id.IsValid)
            {
                return AttributeError.InvalidAttributeId;
            }

            if ((configuration.Kind != AttributeKind.Stat && configuration.Kind != AttributeKind.Resource) ||
                !AttributeMath.IsConfigurationValue(configuration.Minimum) ||
                !AttributeMath.IsConfigurationValue(configuration.Maximum) ||
                !AttributeMath.IsConfigurationValue(configuration.InitialBaseValue) ||
                configuration.Minimum > configuration.Maximum ||
                (configuration.Kind == AttributeKind.Stat && configuration.MaximumAttribute.IsValid))
            {
                return AttributeError.InvalidConfiguration;
            }

            return AttributeError.None;
        }

        private AttributeError Apply(Entry[] entries, AttributeCommand command, ref ulong sequence)
        {
            if (!command.AttributeId.IsValid)
            {
                return AttributeError.InvalidAttributeId;
            }

            if (!m_Indices.TryGetValue(command.AttributeId, out int index))
            {
                return AttributeError.MissingAttribute;
            }

            Entry entry = entries[index];
            if (command.Kind == AttributeCommandKind.RemovePersistent)
            {
                if (command.ModifierHandle == 0)
                {
                    return AttributeError.InvalidModifierHandle;
                }

                for (int i = 0; i < entry.Modifiers.Count; i++)
                {
                    if (entry.Modifiers[i].Command.ModifierHandle == command.ModifierHandle)
                    {
                        entry.Modifiers.RemoveAt(i);
                        return AttributeError.None;
                    }
                }

                return AttributeError.MissingModifier;
            }

            if (command.Kind != AttributeCommandKind.Instant && command.Kind != AttributeCommandKind.AddPersistent)
            {
                return AttributeError.InvalidOperation;
            }

            AttributeError validation = ValidateOperand(command.Operation, command.Operand);
            if (validation != AttributeError.None)
            {
                return validation;
            }

            if (command.Kind == AttributeCommandKind.Instant)
            {
                AttributeError error = ApplyInstant(entry.BaseValue, command.Operation, command.Operand, out FP64 value);
                if (error != AttributeError.None)
                {
                    return error;
                }

                entry.BaseValue = AttributeMath.Clamp(value, entry.Configuration.Minimum, entry.Configuration.Maximum);
                return AttributeError.None;
            }

            if (entry.Configuration.Kind == AttributeKind.Resource)
            {
                return AttributeError.ResourceModifierNotAllowed;
            }

            if (command.ModifierHandle == 0)
            {
                return AttributeError.InvalidModifierHandle;
            }

            if (FindModifier(entries, command.ModifierHandle))
            {
                return AttributeError.DuplicateModifier;
            }

            if (sequence == ulong.MaxValue)
            {
                return AttributeError.Overflow;
            }

            sequence++;
            entry.Modifiers.Add(new PersistentModifier(command, sequence));
            return AttributeError.None;
        }

        private static AttributeError ValidateOperand(AttributeOperation operation, FP64 operand)
        {
            if (operation < AttributeOperation.Add || operation > AttributeOperation.Override)
            {
                return AttributeError.InvalidOperation;
            }

            if (!AttributeMath.IsConfigurationValue(operand))
            {
                return AttributeError.InvalidOperand;
            }

            if (operation == AttributeOperation.Divide && operand.Raw == 0)
            {
                return AttributeError.DivideByZero;
            }

            if ((operation == AttributeOperation.Multiply && operand < FP64.Zero) ||
                (operation == AttributeOperation.Divide && operand < FP64.One))
            {
                return AttributeError.InvalidOperand;
            }

            return AttributeError.None;
        }

        private static AttributeError ApplyInstant(FP64 value, AttributeOperation operation, FP64 operand, out FP64 result)
        {
            switch (operation)
            {
                case AttributeOperation.Add:
                    return AttributeMath.Add(value, operand, out result);
                case AttributeOperation.Multiply:
                    return AttributeMath.Multiply(value, operand, out result);
                case AttributeOperation.Divide:
                    return AttributeMath.Divide(value, operand, out result);
                case AttributeOperation.Override:
                    result = operand;
                    return AttributeError.None;
                default:
                    result = default;
                    return AttributeError.InvalidOperation;
            }
        }

        private AttributeResult Recalculate(Entry[] entries, int commandIndex)
        {
            // 资源上限只能引用数值属性，因此按固定顺序分两轮结算不会产生依赖环。
            foreach (Entry entry in entries)
            {
                if (entry.Configuration.Kind != AttributeKind.Stat)
                {
                    continue;
                }

                AttributeError error = AttributeAggregator.Evaluate(entry.BaseValue, entry.Modifiers, out FP64 current);
                if (error != AttributeError.None)
                {
                    return new AttributeResult(error, entry.Configuration.Id, commandIndex);
                }

                entry.CurrentValue = AttributeMath.Clamp(current, entry.Configuration.Minimum, entry.Configuration.Maximum);
            }

            foreach (Entry entry in entries)
            {
                AttributeConfiguration configuration = entry.Configuration;
                if (configuration.Kind != AttributeKind.Resource)
                {
                    continue;
                }

                FP64 maximum = configuration.Maximum;
                if (configuration.MaximumAttribute.IsValid)
                {
                    FP64 dynamicMaximum = entries[m_Indices[configuration.MaximumAttribute]].CurrentValue;
                    if (dynamicMaximum < maximum)
                    {
                        maximum = dynamicMaximum;
                    }
                }

                if (maximum < configuration.Minimum)
                {
                    return new AttributeResult(AttributeError.InvalidConfiguration, configuration.Id, commandIndex);
                }

                entry.BaseValue = AttributeMath.Clamp(entry.BaseValue, configuration.Minimum, maximum);
                entry.CurrentValue = entry.BaseValue;
            }

            return new AttributeResult(AttributeError.None);
        }

        private static bool FindModifier(Entry[] entries, ulong handle)
        {
            foreach (Entry entry in entries)
            {
                foreach (PersistentModifier modifier in entry.Modifiers)
                {
                    if (modifier.Command.ModifierHandle == handle)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
