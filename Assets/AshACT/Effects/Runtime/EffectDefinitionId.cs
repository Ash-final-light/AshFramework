using System;
using System.Globalization;

namespace AshACT.Effects
{
    /// <summary>显式稳定编号；0 无效，实例编号仅在所属模拟域内有效。</summary>
    public readonly struct EffectDefinitionId : IEquatable<EffectDefinitionId>, IComparable<EffectDefinitionId>
    {
        public uint Value { get; }
        public bool IsValid => Value != 0;

        public EffectDefinitionId(uint value)
        {
            Value = value;
        }

        public bool Equals(EffectDefinitionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EffectDefinitionId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EffectDefinitionId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(EffectDefinitionId left, EffectDefinitionId right) => left.Equals(right);
        public static bool operator !=(EffectDefinitionId left, EffectDefinitionId right) => !left.Equals(right);
    }
}
