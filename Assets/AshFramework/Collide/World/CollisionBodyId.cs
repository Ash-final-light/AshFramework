using System;
using System.Runtime.CompilerServices;

namespace AshFramework.Collide
{
    /// <summary>世界内身份。数值递增不复用；不同世界的相同数值不是同一身份。</summary>
    public readonly struct CollisionBodyId : IEquatable<CollisionBodyId>
    {
        private readonly object _owner;

        public long Value { get; }
        public bool IsValid => _owner != null && Value > 0;

        internal CollisionBodyId(object owner, long value)
        {
            _owner = owner;
            Value = value;
        }

        internal bool BelongsTo(object owner) => ReferenceEquals(_owner, owner);
        internal bool SharesWorld(CollisionBodyId other) => IsValid && other.IsValid && ReferenceEquals(_owner, other._owner);
        public bool Equals(CollisionBodyId other) => Value == other.Value && ReferenceEquals(_owner, other._owner);
        public override bool Equals(object obj) => obj is CollisionBodyId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_owner == null ? 0 : RuntimeHelpers.GetHashCode(_owner)) * 397) ^ Value.GetHashCode();
            }
        }

        public override string ToString() => IsValid ? Value.ToString() : "Invalid";
        public static bool operator ==(CollisionBodyId a, CollisionBodyId b) => a.Equals(b);
        public static bool operator !=(CollisionBodyId a, CollisionBodyId b) => !a.Equals(b);
    }
}
