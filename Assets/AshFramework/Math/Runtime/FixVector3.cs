using System;
using System.Numerics;

namespace AshFramework.Math
{
    [Serializable]
    public readonly struct FixVector3 : IEquatable<FixVector3>
    {
        public static readonly FixVector3 Zero = new FixVector3(Fix64.Zero, Fix64.Zero, Fix64.Zero);
        public static readonly FixVector3 One = new FixVector3(Fix64.One, Fix64.One, Fix64.One);
        public static readonly FixVector3 Right = new FixVector3(Fix64.One, Fix64.Zero, Fix64.Zero);
        public static readonly FixVector3 Up = new FixVector3(Fix64.Zero, Fix64.One, Fix64.Zero);
        public static readonly FixVector3 Forward = new FixVector3(Fix64.Zero, Fix64.Zero, Fix64.One);

        public Fix64 X { get; }
        public Fix64 Y { get; }
        public Fix64 Z { get; }

        public FixVector3(Fix64 x, Fix64 y, Fix64 z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal BigInteger SquaredRaw => (BigInteger)X.RawValue * X.RawValue + (BigInteger)Y.RawValue * Y.RawValue + (BigInteger)Z.RawValue * Z.RawValue;
        public Fix64 SqrMagnitude => Fix64.FromWideRaw(SquaredRaw / Fix64.Scale);
        public Fix64 Magnitude => Fix64.FromWideRaw(FixMath.IntegerSqrt(SquaredRaw));
        public FixVector3 Normalized => NormalizeOrZero();

        /// <summary>支持原始分量极小的向量归一化，避免先量化长度造成精度损失。</summary>
        public bool TryNormalize(out FixVector3 result)
        {
            BigInteger squared = SquaredRaw;
            if (squared.IsZero)
            {
                result = Zero;
                return false;
            }

            BigInteger length = FixMath.IntegerSqrt(squared * Fix64.Scale * Fix64.Scale);
            result = new FixVector3(
                Fix64.FromWideRaw((BigInteger)X.RawValue * Fix64.Scale * Fix64.Scale / length),
                Fix64.FromWideRaw((BigInteger)Y.RawValue * Fix64.Scale * Fix64.Scale / length),
                Fix64.FromWideRaw((BigInteger)Z.RawValue * Fix64.Scale * Fix64.Scale / length));
            return true;
        }

        public FixVector3 NormalizeOrZero()
        {
            return TryNormalize(out FixVector3 result) ? result : Zero;
        }

        public FixVector3 NormalizeOrFallback(FixVector3 fallback)
        {
            return TryNormalize(out FixVector3 result) ? result : fallback;
        }

        public static Fix64 Dot(FixVector3 left, FixVector3 right)
        {
            return Fix64.FromWideRaw((
                (BigInteger)left.X.RawValue * right.X.RawValue +
                (BigInteger)left.Y.RawValue * right.Y.RawValue +
                (BigInteger)left.Z.RawValue * right.Z.RawValue) / Fix64.Scale);
        }

        public static FixVector3 Cross(FixVector3 left, FixVector3 right)
        {
            return new FixVector3(
                Fix64.FromWideRaw(((BigInteger)left.Y.RawValue * right.Z.RawValue - (BigInteger)left.Z.RawValue * right.Y.RawValue) / Fix64.Scale),
                Fix64.FromWideRaw(((BigInteger)left.Z.RawValue * right.X.RawValue - (BigInteger)left.X.RawValue * right.Z.RawValue) / Fix64.Scale),
                Fix64.FromWideRaw(((BigInteger)left.X.RawValue * right.Y.RawValue - (BigInteger)left.Y.RawValue * right.X.RawValue) / Fix64.Scale));
        }

        public static FixVector3 ProjectOnPlane(FixVector3 value, FixVector3 normal)
        {
            return value - Project(value, normal);
        }

        public static Fix64 Angle(FixVector3 from, FixVector3 to)
        {
            if (!from.TryNormalize(out from) || !to.TryNormalize(out to))
            {
                return Fix64.Zero;
            }

            return FixMath.Atan2(Cross(from, to).Magnitude, Dot(from, to));
        }

        public static Fix64 SignedAngle(FixVector3 from, FixVector3 to, FixVector3 axis)
        {
            Fix64 sign = Dot(axis, Cross(from, to));
            return sign < Fix64.Zero ? -Angle(from, to) : Angle(from, to);
        }

        public static Fix64 Distance(FixVector3 left, FixVector3 right)
        {
            return (left - right).Magnitude;
        }

        public static Fix64 DistanceSquared(FixVector3 left, FixVector3 right)
        {
            return (left - right).SqrMagnitude;
        }

        /// <summary>直接比较原始距离平方，不将平方结果收窄为 Fix64。</summary>
        public static bool WithinDistance(FixVector3 left, FixVector3 right, Fix64 distance)
        {
            if (distance < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }

            BigInteger squared = BigInteger.Zero;
            BigInteger deltaX = (BigInteger)left.X.RawValue - right.X.RawValue;
            squared += deltaX * deltaX;
            BigInteger deltaY = (BigInteger)left.Y.RawValue - right.Y.RawValue;
            squared += deltaY * deltaY;
            BigInteger deltaZ = (BigInteger)left.Z.RawValue - right.Z.RawValue;
            squared += deltaZ * deltaZ;

            return squared <= (BigInteger)distance.RawValue * distance.RawValue;
        }

        public static FixVector3 ClampMagnitude(FixVector3 value, Fix64 maxLength)
        {
            if (maxLength < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength));
            }

            return value.SquaredRaw <= (BigInteger)maxLength.RawValue * maxLength.RawValue
                ? value
                : value.Normalized * maxLength;
        }

        public static FixVector3 MoveTowards(FixVector3 current, FixVector3 target, Fix64 maxDelta)
        {
            if (maxDelta < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDelta));
            }

            FixVector3 delta = target - current;
            if (delta.SquaredRaw <= (BigInteger)maxDelta.RawValue * maxDelta.RawValue)
            {
                return target;
            }

            return current + delta.Normalized * maxDelta;
        }

        public static FixVector3 Project(FixVector3 value, FixVector3 normal)
        {
            return normal.TryNormalize(out FixVector3 unit) ? unit * Dot(value, unit) : Zero;
        }

        public static FixVector3 Reflect(FixVector3 direction, FixVector3 normal)
        {
            return direction - 2 * Project(direction, normal);
        }

        public static FixVector3 Scale(FixVector3 left, FixVector3 right)
        {
            return new FixVector3(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        }

        public static FixVector3 Lerp(FixVector3 from, FixVector3 to, Fix64 t)
        {
            return LerpUnclamped(from, to, FixMath.Clamp01(t));
        }

        public static FixVector3 LerpUnclamped(FixVector3 from, FixVector3 to, Fix64 t)
        {
            return new FixVector3(FixMath.LerpUnclamped(from.X, to.X, t), FixMath.LerpUnclamped(from.Y, to.Y, t), FixMath.LerpUnclamped(from.Z, to.Z, t));
        }

        public bool Equals(FixVector3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is FixVector3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }

        public static FixVector3 operator +(FixVector3 left, FixVector3 right)
        {
            return new FixVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static FixVector3 operator -(FixVector3 left, FixVector3 right)
        {
            return new FixVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static FixVector3 operator -(FixVector3 value)
        {
            return new FixVector3(-value.X, -value.Y, -value.Z);
        }

        public static FixVector3 operator *(FixVector3 value, Fix64 scalar)
        {
            return new FixVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        public static FixVector3 operator *(Fix64 scalar, FixVector3 value)
        {
            return value * scalar;
        }

        public static FixVector3 operator /(FixVector3 value, Fix64 scalar)
        {
            return new FixVector3(value.X / scalar, value.Y / scalar, value.Z / scalar);
        }

        public static bool operator ==(FixVector3 left, FixVector3 right) => left.Equals(right);
        public static bool operator !=(FixVector3 left, FixVector3 right) => !left.Equals(right);
    }
}
