using System;
using System.Globalization;

namespace AshACT.Effects
{
    /// <summary>显式稳定编号；0 无效，实例编号仅在所属模拟域内有效。</summary>
    public readonly struct MarkerId : IEquatable<MarkerId>, IComparable<MarkerId>
    {
        public uint Value { get; }
        public bool IsValid => Value != 0;

        public MarkerId(uint value)
        {
            Value = value;
        }

        public bool Equals(MarkerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is MarkerId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(MarkerId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(MarkerId left, MarkerId right) => left.Equals(right);
        public static bool operator !=(MarkerId left, MarkerId right) => !left.Equals(right);
    }
}
