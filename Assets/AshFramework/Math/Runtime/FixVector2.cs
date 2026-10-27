using System;
using System.Numerics;

namespace AshFramework.Math
{
    [Serializable]
    public readonly struct FixVector2 : IEquatable<FixVector2>
    {
        public static readonly FixVector2 Zero = new FixVector2(Fix64.Zero, Fix64.Zero);
        public static readonly FixVector2 One = new FixVector2(Fix64.One, Fix64.One);
        public static readonly FixVector2 Right = new FixVector2(Fix64.One, Fix64.Zero);
        public static readonly FixVector2 Up = new FixVector2(Fix64.Zero, Fix64.One);

        public Fix64 X { get; }
        public Fix64 Y { get; }

        public FixVector2(Fix64 x, Fix64 y)
        {
            X = x;
            Y = y;
        }

        internal BigInteger SquaredRaw => (BigInteger)X.RawValue * X.RawValue + (BigInteger)Y.RawValue * Y.RawValue;
        public Fix64 SqrMagnitude => Fix64.FromWideRaw(SquaredRaw / Fix64.Scale);
        public Fix64 Magnitude => Fix64.FromWideRaw(FixMath.IntegerSqrt(SquaredRaw));
        public FixVector2 Normalized => NormalizeOrZero();

        /// <summary>支持原始分量极小的向量归一化，避免先量化长度造成精度损失。</summary>
        public bool TryNormalize(out FixVector2 result)
        {
            BigInteger squared = SquaredRaw;
            if (squared.IsZero)
            {
                result = Zero;
                return false;
            }

            BigInteger length = FixMath.IntegerSqrt(squared * Fix64.Scale * Fix64.Scale);
            result = new FixVector2(
                Fix64.FromWideRaw((BigInteger)X.RawValue * Fix64.Scale * Fix64.Scale / length),
                Fix64.FromWideRaw((BigInteger)Y.RawValue * Fix64.Scale * Fix64.Scale / length));
            return true;
        }

        public FixVector2 NormalizeOrZero()
        {
            return TryNormalize(out FixVector2 result) ? result : Zero;
        }

        public FixVector2 NormalizeOrFallback(FixVector2 fallback)
        {
            return TryNormalize(out FixVector2 result) ? result : fallback;
        }

        public static Fix64 Dot(FixVector2 left, FixVector2 right)
        {
            return Fix64.FromWideRaw((
                (BigInteger)left.X.RawValue * right.X.RawValue +
                (BigInteger)left.Y.RawValue * right.Y.RawValue) / Fix64.Scale);
        }

        public static Fix64 Cross(FixVector2 left, FixVector2 right)
        {
            return Fix64.FromWideRaw(((BigInteger)left.X.RawValue * right.Y.RawValue
                - (BigInteger)left.Y.RawValue * right.X.RawValue) / Fix64.Scale);
        }

        public FixVector2 Perpendicular()
        {
            return new FixVector2(-Y, X);
        }

        public FixVector2 Rotate(Fix64 radians)
        {
            FixMath.SinCos(radians, out Fix64 sin, out Fix64 cos);
            return new FixVector2(
                Fix64.FromWideRaw(((BigInteger)X.RawValue * cos.RawValue - (BigInteger)Y.RawValue * sin.RawValue) / Fix64.Scale),
                Fix64.FromWideRaw(((BigInteger)X.RawValue * sin.RawValue + (BigInteger)Y.RawValue * cos.RawValue) / Fix64.Scale));
        }

        public static FixVector2 FromAngle(Fix64 radians)
        {
            FixMath.SinCos(radians, out Fix64 sin, out Fix64 cos);
            return new FixVector2(cos, sin);
        }

        public static Fix64 SignedAngle(FixVector2 from, FixVector2 to)
        {
            if (!from.TryNormalize(out from) || !to.TryNormalize(out to))
            {
                return Fix64.Zero;
            }

            return FixMath.Atan2(Cross(from, to), Dot(from, to));
        }

        public static Fix64 Angle(FixVector2 from, FixVector2 to)
        {
            return FixMath.Abs(SignedAngle(from, to));
        }

        public static Fix64 Distance(FixVector2 left, FixVector2 right)
        {
            return (left - right).Magnitude;
        }

        public static Fix64 DistanceSquared(FixVector2 left, FixVector2 right)
        {
            return (left - right).SqrMagnitude;
        }

        /// <summary>直接比较原始距离平方，不将平方结果收窄为 Fix64。</summary>
        public static bool WithinDistance(FixVector2 left, FixVector2 right, Fix64 distance)
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

            return squared <= (BigInteger)distance.RawValue * distance.RawValue;
        }

        public static FixVector2 ClampMagnitude(FixVector2 value, Fix64 maxLength)
        {
            if (maxLength < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength));
            }

            return value.SquaredRaw <= (BigInteger)maxLength.RawValue * maxLength.RawValue
                ? value
                : value.Normalized * maxLength;
        }

        public static FixVector2 MoveTowards(FixVector2 current, FixVector2 target, Fix64 maxDelta)
        {
            if (maxDelta < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDelta));
            }

            FixVector2 delta = target - current;
            if (delta.SquaredRaw <= (BigInteger)maxDelta.RawValue * maxDelta.RawValue)
            {
                return target;
            }

            return current + delta.Normalized * maxDelta;
        }

        public static FixVector2 Project(FixVector2 value, FixVector2 normal)
        {
            return normal.TryNormalize(out FixVector2 unit) ? unit * Dot(value, unit) : Zero;
        }

        public static FixVector2 Reflect(FixVector2 direction, FixVector2 normal)
        {
            return direction - 2 * Project(direction, normal);
        }

        public static FixVector2 Scale(FixVector2 left, FixVector2 right)
        {
            return new FixVector2(left.X * right.X, left.Y * right.Y);
        }

        public static FixVector2 Lerp(FixVector2 from, FixVector2 to, Fix64 t)
        {
            return LerpUnclamped(from, to, FixMath.Clamp01(t));
        }

        public static FixVector2 LerpUnclamped(FixVector2 from, FixVector2 to, Fix64 t)
        {
            return new FixVector2(FixMath.LerpUnclamped(from.X, to.X, t), FixMath.LerpUnclamped(from.Y, to.Y, t));
        }

        public bool Equals(FixVector2 other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is FixVector2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }

        public static FixVector2 operator +(FixVector2 left, FixVector2 right)
        {
            return new FixVector2(left.X + right.X, left.Y + right.Y);
        }

        public static FixVector2 operator -(FixVector2 left, FixVector2 right)
        {
            return new FixVector2(left.X - right.X, left.Y - right.Y);
        }

        public static FixVector2 operator -(FixVector2 value)
        {
            return new FixVector2(-value.X, -value.Y);
        }

        public static FixVector2 operator *(FixVector2 value, Fix64 scalar)
        {
            return new FixVector2(value.X * scalar, value.Y * scalar);
        }

        public static FixVector2 operator *(Fix64 scalar, FixVector2 value)
        {
            return value * scalar;
        }

        public static FixVector2 operator /(FixVector2 value, Fix64 scalar)
        {
            return new FixVector2(value.X / scalar, value.Y / scalar);
        }

        public static bool operator ==(FixVector2 left, FixVector2 right) => left.Equals(right);
        public static bool operator !=(FixVector2 left, FixVector2 right) => !left.Equals(right);
    }
}
