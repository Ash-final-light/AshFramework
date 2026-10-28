using System;

namespace AshFramework.Collide
{
    /// <summary>同一世界中两个不同 Body 的规范化无序对，A.Value 小于 B.Value。</summary>
    public readonly struct CollisionPair : IEquatable<CollisionPair>, IComparable<CollisionPair>
    {
        public CollisionBodyId A { get; }
        public CollisionBodyId B { get; }

        public CollisionPair(CollisionBodyId a, CollisionBodyId b)
        {
            if (!a.SharesWorld(b) || a == b)
            {
                throw new ArgumentException("A pair requires two distinct valid bodies from the same world.");
            }

            A = a.Value < b.Value ? a : b;
            B = a.Value < b.Value ? b : a;
        }

        public int CompareTo(CollisionPair other)
        {
            if (!A.SharesWorld(other.A))
            {
                throw new ArgumentException("Only pairs from the same world can be ordered.", nameof(other));
            }

            int first = A.Value.CompareTo(other.A.Value);
            return first != 0 ? first : B.Value.CompareTo(other.B.Value);
        }

        public bool Equals(CollisionPair other) => A == other.A && B == other.B;
        public override bool Equals(object obj) => obj is CollisionPair other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (A.GetHashCode() * 397) ^ B.GetHashCode();
            }
        }

        public static bool operator ==(CollisionPair a, CollisionPair b) => a.Equals(b);
        public static bool operator !=(CollisionPair a, CollisionPair b) => !a.Equals(b);
    }
}
