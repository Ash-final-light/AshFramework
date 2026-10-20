using System;
using System.Globalization;

namespace AshACT.Attributes
{
    /// <summary>显式分配并持久保存的配置编号；0 无效，属性名称不参与身份判定。</summary>
    public readonly struct AttributeId : IEquatable<AttributeId>, IComparable<AttributeId>
    {
        public uint Value { get; }
        public bool IsValid => Value != 0;

        public AttributeId(uint value)
        {
            Value = value;
        }

        public bool Equals(AttributeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AttributeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(AttributeId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(AttributeId left, AttributeId right) => left.Equals(right);
        public static bool operator !=(AttributeId left, AttributeId right) => !left.Equals(right);
    }
}
