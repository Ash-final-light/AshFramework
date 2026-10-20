using System;
using System.Globalization;

namespace AshACT.Effects
{
    /// <summary>显式稳定编号；0 无效，实例编号仅在所属模拟域内有效。</summary>
    public readonly struct EffectInstanceId : IEquatable<EffectInstanceId>, IComparable<EffectInstanceId>
    {
        public ulong Value { get; }
        public bool IsValid => Value != 0;

        public EffectInstanceId(ulong value)
        {
            Value = value;
        }

        public bool Equals(EffectInstanceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EffectInstanceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EffectInstanceId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(EffectInstanceId left, EffectInstanceId right) => left.Equals(right);
        public static bool operator !=(EffectInstanceId left, EffectInstanceId right) => !left.Equals(right);
    }
}
