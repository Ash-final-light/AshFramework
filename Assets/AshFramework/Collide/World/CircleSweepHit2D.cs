using System;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>
    /// 单圆扫掠在最早接触时刻的一条固定障碍命中。Normal 指向将移动圆移离障碍的方向。
    /// </summary>
    public readonly struct CircleSweepHit2D : IEquatable<CircleSweepHit2D>, IComparable<CircleSweepHit2D>
    {
        public CollisionBodyId Body { get; }
        public int ShapeIndex { get; }
        public Fix64 Time { get; }
        public FixVector2 Center { get; }
        public FixVector2 Normal { get; }
        public bool IsValid => Body.IsValid && ShapeIndex >= 0
            && Time >= Fix64.Zero && Time <= Fix64.One && Normal != FixVector2.Zero;

        public CircleSweepHit2D(CollisionBodyId body, int shapeIndex, Fix64 time,
            FixVector2 center, FixVector2 normal)
        {
            if (!body.IsValid)
            {
                throw new ArgumentException("A sweep hit requires a valid body.", nameof(body));
            }

            if (shapeIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(shapeIndex), "Shape index must be non-negative.");
            }

            if (time < Fix64.Zero || time > Fix64.One)
            {
                throw new ArgumentOutOfRangeException(nameof(time), "Sweep time must be in the closed interval [0, 1].");
            }

            if (normal == FixVector2.Zero)
            {
                throw new ArgumentException("A sweep hit requires a non-zero normal.", nameof(normal));
            }

            Body = body;
            ShapeIndex = shapeIndex;
            Time = time;
            Center = center;
            Normal = normal;
        }

        public int CompareTo(CircleSweepHit2D other)
        {
            if (!Body.SharesWorld(other.Body))
            {
                throw new ArgumentException("Only sweep hits from the same world can be ordered.", nameof(other));
            }

            int timeOrder = Time.CompareTo(other.Time);
            if (timeOrder != 0)
            {
                return timeOrder;
            }

            int bodyOrder = Body.Value.CompareTo(other.Body.Value);
            return bodyOrder != 0 ? bodyOrder : ShapeIndex.CompareTo(other.ShapeIndex);
        }

        public bool Equals(CircleSweepHit2D other)
        {
            return Body == other.Body && ShapeIndex == other.ShapeIndex && Time == other.Time
                && Center == other.Center && Normal == other.Normal;
        }

        public override bool Equals(object obj)
        {
            return obj is CircleSweepHit2D other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Body.GetHashCode();
                hashCode = (hashCode * 397) ^ ShapeIndex;
                hashCode = (hashCode * 397) ^ Time.GetHashCode();
                hashCode = (hashCode * 397) ^ Center.GetHashCode();
                return (hashCode * 397) ^ Normal.GetHashCode();
            }
        }

        public static bool operator ==(CircleSweepHit2D a, CircleSweepHit2D b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(CircleSweepHit2D a, CircleSweepHit2D b)
        {
            return !a.Equals(b);
        }
    }
}
