using System;
using System.Numerics;

namespace AshFramework.Math
{
    /// <summary>按右手定则旋转向量，角度使用弧度；默认结构体值不是单位旋转。</summary>
    [Serializable]
    public readonly struct FixQuaternion : IEquatable<FixQuaternion>
    {
        public static readonly FixQuaternion Identity = new FixQuaternion(0, 0, 0, 1);

        public Fix64 X { get; }
        public Fix64 Y { get; }
        public Fix64 Z { get; }
        public Fix64 W { get; }

        public FixQuaternion(Fix64 x, Fix64 y, Fix64 z, Fix64 w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        private BigInteger SquaredRaw => (BigInteger)X.RawValue * X.RawValue
            + (BigInteger)Y.RawValue * Y.RawValue + (BigInteger)Z.RawValue * Z.RawValue
            + (BigInteger)W.RawValue * W.RawValue;

        public bool TryNormalize(out FixQuaternion result)
        {
            BigInteger squared = SquaredRaw;
            if (squared.IsZero)
            {
                result = Identity;
                return false;
            }

            BigInteger length = FixMath.IntegerSqrt(squared * Fix64.Scale * Fix64.Scale);
            result = new FixQuaternion(
                Fix64.FromWideRaw((BigInteger)X.RawValue * Fix64.Scale * Fix64.Scale / length),
                Fix64.FromWideRaw((BigInteger)Y.RawValue * Fix64.Scale * Fix64.Scale / length),
                Fix64.FromWideRaw((BigInteger)Z.RawValue * Fix64.Scale * Fix64.Scale / length),
                Fix64.FromWideRaw((BigInteger)W.RawValue * Fix64.Scale * Fix64.Scale / length));
            return true;
        }

        public FixQuaternion Normalized
        {
            get
            {
                if (!TryNormalize(out FixQuaternion result))
                {
                    throw new InvalidOperationException("A zero quaternion has no rotation.");
                }

                return result;
            }
        }

        public FixQuaternion Conjugate => new FixQuaternion(-X, -Y, -Z, W);

        public FixQuaternion Inverse()
        {
            BigInteger squared = SquaredRaw;
            if (squared.IsZero)
            {
                throw new InvalidOperationException("A zero quaternion has no inverse.");
            }

            BigInteger factor = (BigInteger)Fix64.Scale * Fix64.Scale;
            return new FixQuaternion(
                Fix64.FromWideRaw(-(BigInteger)X.RawValue * factor / squared),
                Fix64.FromWideRaw(-(BigInteger)Y.RawValue * factor / squared),
                Fix64.FromWideRaw(-(BigInteger)Z.RawValue * factor / squared),
                Fix64.FromWideRaw((BigInteger)W.RawValue * factor / squared));
        }

        public static Fix64 Dot(FixQuaternion left, FixQuaternion right)
        {
            return Fix64.FromWideRaw(((BigInteger)left.X.RawValue * right.X.RawValue
                + (BigInteger)left.Y.RawValue * right.Y.RawValue
                + (BigInteger)left.Z.RawValue * right.Z.RawValue
                + (BigInteger)left.W.RawValue * right.W.RawValue) / Fix64.Scale);
        }

        public static FixQuaternion AngleAxis(Fix64 radians, FixVector3 axis)
        {
            if (!axis.TryNormalize(out FixVector3 unit))
            {
                throw new ArgumentException("Rotation axis must be nonzero.", nameof(axis));
            }

            FixMath.SinCos(radians * Fix64.Half, out Fix64 sin, out Fix64 cos);
            return new FixQuaternion(unit.X * sin, unit.Y * sin, unit.Z * sin, cos).Normalized;
        }

        /// <summary>先绕 Z 轴旋转，再绕 X 轴，最后绕 Y 轴；组合顺序为 qY * qX * qZ。</summary>
        public static FixQuaternion Euler(Fix64 xRadians, Fix64 yRadians, Fix64 zRadians)
        {
            return (AngleAxis(yRadians, FixVector3.Up)
                * AngleAxis(xRadians, FixVector3.Right)
                * AngleAxis(zRadians, FixVector3.Forward)).Normalized;
        }

        public static FixQuaternion FromToRotation(FixVector3 from, FixVector3 to)
        {
            if (!from.TryNormalize(out from) || !to.TryNormalize(out to))
            {
                throw new ArgumentException("Both directions must be nonzero.");
            }

            FixVector3 cross = FixVector3.Cross(from, to);
            Fix64 dot = FixMath.Clamp(FixVector3.Dot(from, to), -Fix64.One, Fix64.One);
            if (cross == FixVector3.Zero)
            {
                if (dot >= Fix64.Zero)
                {
                    return Identity;
                }

                FixVector3 basis = FixMath.Abs(from.X) <= FixMath.Abs(from.Y)
                    ? FixVector3.Right
                    : FixVector3.Up;
                return AngleAxis(FixMath.Pi, FixVector3.Cross(from, basis));
            }

            return AngleAxis(FixMath.Atan2(cross.Magnitude, dot), cross);
        }

        public static FixQuaternion LookRotation(FixVector3 forward, FixVector3 up)
        {
            if (!forward.TryNormalize(out forward)
                || !FixVector3.Cross(up.Normalized, forward).TryNormalize(out FixVector3 right))
            {
                throw new ArgumentException("Forward and up must be nonzero and nonparallel.");
            }

            FixVector3 correctedUp = FixVector3.Cross(forward, right).Normalized;
            return FromRotationMatrix(FixMatrix3x3.FromColumns(right, correctedUp, forward));
        }

        /// <summary>输入必须是标准正交旋转矩阵，不包含缩放或反射。</summary>
        public static FixQuaternion FromRotationMatrix(FixMatrix3x3 matrix)
        {
            Fix64 trace = matrix.M00 + matrix.M11 + matrix.M22;
            FixQuaternion result;
            if (trace > Fix64.Zero)
            {
                Fix64 s = FixMath.Sqrt(trace + 1) * 2;
                result = new FixQuaternion((matrix.M21 - matrix.M12) / s,
                    (matrix.M02 - matrix.M20) / s, (matrix.M10 - matrix.M01) / s, s / 4);
            }
            else if (matrix.M00 > matrix.M11 && matrix.M00 > matrix.M22)
            {
                Fix64 s = FixMath.Sqrt(FixMath.Max(0, 1 + matrix.M00 - matrix.M11 - matrix.M22)) * 2;
                result = new FixQuaternion(s / 4, (matrix.M01 + matrix.M10) / s,
                    (matrix.M02 + matrix.M20) / s, (matrix.M21 - matrix.M12) / s);
            }
            else if (matrix.M11 > matrix.M22)
            {
                Fix64 s = FixMath.Sqrt(FixMath.Max(0, 1 + matrix.M11 - matrix.M00 - matrix.M22)) * 2;
                result = new FixQuaternion((matrix.M01 + matrix.M10) / s, s / 4,
                    (matrix.M12 + matrix.M21) / s, (matrix.M02 - matrix.M20) / s);
            }
            else
            {
                Fix64 s = FixMath.Sqrt(FixMath.Max(0, 1 + matrix.M22 - matrix.M00 - matrix.M11)) * 2;
                result = new FixQuaternion((matrix.M02 + matrix.M20) / s,
                    (matrix.M12 + matrix.M21) / s, s / 4, (matrix.M10 - matrix.M01) / s);
            }

            return result.Normalized;
        }

        public FixVector3 Rotate(FixVector3 point)
        {
            FixQuaternion unit = Normalized;
            FixVector3 vector = new FixVector3(unit.X, unit.Y, unit.Z);
            FixVector3 twiceCross = 2 * FixVector3.Cross(vector, point);
            return point + unit.W * twiceCross + FixVector3.Cross(vector, twiceCross);
        }

        public static Fix64 Angle(FixQuaternion from, FixQuaternion to)
        {
            FixQuaternion relative = (from.Normalized.Conjugate * to.Normalized).Normalized;
            Fix64 vectorLength = new FixVector3(relative.X, relative.Y, relative.Z).Magnitude;
            return 2 * FixMath.Atan2(vectorLength, FixMath.Abs(relative.W));
        }

        public static FixQuaternion Nlerp(FixQuaternion from, FixQuaternion to, Fix64 t)
        {
            from = from.Normalized;
            to = to.Normalized;
            t = FixMath.Clamp01(t);
            if (Dot(from, to) < Fix64.Zero)
            {
                to = -to;
            }

            return (from * (Fix64.One - t) + to * t).Normalized;
        }

        public static FixQuaternion Slerp(FixQuaternion from, FixQuaternion to, Fix64 t)
        {
            from = from.Normalized;
            to = to.Normalized;
            t = FixMath.Clamp01(t);
            Fix64 dot = Dot(from, to);
            if (dot < Fix64.Zero)
            {
                dot = -dot;
                to = -to;
            }

            dot = FixMath.Clamp01(dot);
            if (dot > Fix64.FromRatio(9995, 10000))
            {
                return Nlerp(from, to, t);
            }

            Fix64 angle = FixMath.Acos(dot);
            Fix64 denominator = FixMath.Sin(angle);
            Fix64 left = FixMath.Sin((Fix64.One - t) * angle) / denominator;
            Fix64 right = FixMath.Sin(t * angle) / denominator;
            return (from * left + to * right).Normalized;
        }

        public static FixQuaternion RotateTowards(FixQuaternion from, FixQuaternion to, Fix64 maxRadians)
        {
            if (maxRadians < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRadians));
            }

            Fix64 angle = Angle(from, to);
            return angle == Fix64.Zero ? to.Normalized : Slerp(from, to, FixMath.Min(1, maxRadians / angle));
        }

        public bool Equals(FixQuaternion other)
        {
            return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        }

        public override bool Equals(object obj)
        {
            return obj is FixQuaternion other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked(((X.GetHashCode() * 31 + Y.GetHashCode()) * 31 + Z.GetHashCode()) * 31 + W.GetHashCode());
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z}, {W})";
        }

        public static FixQuaternion operator *(FixQuaternion left, FixQuaternion right)
        {
            return new FixQuaternion(
                Fix64.FromWideRaw(((BigInteger)left.W.RawValue * right.X.RawValue + (BigInteger)left.X.RawValue * right.W.RawValue + (BigInteger)left.Y.RawValue * right.Z.RawValue - (BigInteger)left.Z.RawValue * right.Y.RawValue) / Fix64.Scale),
                Fix64.FromWideRaw(((BigInteger)left.W.RawValue * right.Y.RawValue - (BigInteger)left.X.RawValue * right.Z.RawValue + (BigInteger)left.Y.RawValue * right.W.RawValue + (BigInteger)left.Z.RawValue * right.X.RawValue) / Fix64.Scale),
                Fix64.FromWideRaw(((BigInteger)left.W.RawValue * right.Z.RawValue + (BigInteger)left.X.RawValue * right.Y.RawValue - (BigInteger)left.Y.RawValue * right.X.RawValue + (BigInteger)left.Z.RawValue * right.W.RawValue) / Fix64.Scale),
                Fix64.FromWideRaw(((BigInteger)left.W.RawValue * right.W.RawValue - (BigInteger)left.X.RawValue * right.X.RawValue - (BigInteger)left.Y.RawValue * right.Y.RawValue - (BigInteger)left.Z.RawValue * right.Z.RawValue) / Fix64.Scale));
        }

        public static FixVector3 operator *(FixQuaternion rotation, FixVector3 point) => rotation.Rotate(point);
        public static FixQuaternion operator -(FixQuaternion value) => new FixQuaternion(-value.X, -value.Y, -value.Z, -value.W);
        public static FixQuaternion operator +(FixQuaternion left, FixQuaternion right) => new FixQuaternion(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
        public static FixQuaternion operator *(FixQuaternion value, Fix64 scalar) => new FixQuaternion(value.X * scalar, value.Y * scalar, value.Z * scalar, value.W * scalar);
        public static bool operator ==(FixQuaternion left, FixQuaternion right) => left.Equals(right);
        public static bool operator !=(FixQuaternion left, FixQuaternion right) => !left.Equals(right);
    }
}
