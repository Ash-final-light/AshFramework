using System;
using System.Globalization;

namespace AshACT.Effects
{
    /// <summary>显式稳定编号；0 无效，实例编号仅在所属模拟域内有效。</summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public ulong Value { get; }
        public bool IsValid => Value != 0;

        public EntityId(ulong value)
        {
            Value = value;
        }

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }
}
