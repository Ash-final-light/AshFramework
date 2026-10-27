using System;
using System.Numerics;

namespace AshFramework.Math
{
    /// <summary>元素按行列命名，使用列向量；left * right 先应用 right。</summary>
    [Serializable]
    public readonly struct FixMatrix3x3 : IEquatable<FixMatrix3x3>
    {
        public static readonly FixMatrix3x3 Zero = default;
        public static readonly FixMatrix3x3 Identity = new FixMatrix3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public Fix64 M00 { get; }
        public Fix64 M01 { get; }
        public Fix64 M02 { get; }
        public Fix64 M10 { get; }
        public Fix64 M11 { get; }
        public Fix64 M12 { get; }
        public Fix64 M20 { get; }
        public Fix64 M21 { get; }
        public Fix64 M22 { get; }

        public FixMatrix3x3(Fix64 m00, Fix64 m01, Fix64 m02,
            Fix64 m10, Fix64 m11, Fix64 m12, Fix64 m20, Fix64 m21, Fix64 m22)
        {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M20 = m20;
            M21 = m21;
            M22 = m22;
        }

        public FixVector3 Row0 => new FixVector3(M00, M01, M02);
        public FixVector3 Row1 => new FixVector3(M10, M11, M12);
        public FixVector3 Row2 => new FixVector3(M20, M21, M22);
        public FixVector3 Column0 => new FixVector3(M00, M10, M20);
        public FixVector3 Column1 => new FixVector3(M01, M11, M21);
        public FixVector3 Column2 => new FixVector3(M02, M12, M22);

        private BigInteger DeterminantRaw => (BigInteger)M00.RawValue
            * ((BigInteger)M11.RawValue * M22.RawValue - (BigInteger)M12.RawValue * M21.RawValue)
            - (BigInteger)M01.RawValue
            * ((BigInteger)M10.RawValue * M22.RawValue - (BigInteger)M12.RawValue * M20.RawValue)
            + (BigInteger)M02.RawValue
            * ((BigInteger)M10.RawValue * M21.RawValue - (BigInteger)M11.RawValue * M20.RawValue);

        public Fix64 Determinant => Fix64.FromWideRaw(DeterminantRaw / ((BigInteger)Fix64.Scale * Fix64.Scale));

        public static FixMatrix3x3 FromColumns(FixVector3 x, FixVector3 y, FixVector3 z)
        {
            return new FixMatrix3x3(x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z);
        }

        public static FixMatrix3x3 FromQuaternion(FixQuaternion rotation)
        {
            FixQuaternion q = rotation.Normalized;
            Fix64 xx = q.X * q.X;
            Fix64 yy = q.Y * q.Y;
            Fix64 zz = q.Z * q.Z;
            Fix64 xy = q.X * q.Y;
            Fix64 xz = q.X * q.Z;
            Fix64 yz = q.Y * q.Z;
            Fix64 wx = q.W * q.X;
            Fix64 wy = q.W * q.Y;
            Fix64 wz = q.W * q.Z;
            return new FixMatrix3x3(
                1 - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy),
                2 * (xy + wz), 1 - 2 * (xx + zz), 2 * (yz - wx),
                2 * (xz - wy), 2 * (yz + wx), 1 - 2 * (xx + yy));
        }

        public FixMatrix3x3 Transpose()
        {
            return new FixMatrix3x3(M00, M10, M20, M01, M11, M21, M02, M12, M22);
        }

        /// <summary>仅对严格奇异矩阵返回 false；逆矩阵无法表示时抛出溢出异常。</summary>
        public bool TryInverse(out FixMatrix3x3 inverse)
        {
            BigInteger determinant = DeterminantRaw;
            if (determinant.IsZero)
            {
                inverse = Zero;
                return false;
            }

            BigInteger factor = (BigInteger)Fix64.Scale * Fix64.Scale;
            inverse = new FixMatrix3x3(
                Cofactor(M11, M22, M12, M21, factor, determinant),
                Cofactor(M02, M21, M01, M22, factor, determinant),
                Cofactor(M01, M12, M02, M11, factor, determinant),
                Cofactor(M12, M20, M10, M22, factor, determinant),
                Cofactor(M00, M22, M02, M20, factor, determinant),
                Cofactor(M02, M10, M00, M12, factor, determinant),
                Cofactor(M10, M21, M11, M20, factor, determinant),
                Cofactor(M01, M20, M00, M21, factor, determinant),
                Cofactor(M00, M11, M01, M10, factor, determinant));
            return true;
        }

        private static Fix64 Cofactor(Fix64 a, Fix64 b, Fix64 c, Fix64 d,
            BigInteger factor, BigInteger determinant)
        {
            BigInteger numerator = (BigInteger)a.RawValue * b.RawValue - (BigInteger)c.RawValue * d.RawValue;
            return Fix64.FromWideRaw(numerator * factor / determinant);
        }

        public FixMatrix3x3 Inverse()
        {
            if (!TryInverse(out FixMatrix3x3 inverse))
            {
                throw new InvalidOperationException("A singular matrix has no inverse.");
            }

            return inverse;
        }

        /// <summary>对二维点执行齐次变换，并除以变换后的 W 分量。</summary>
        public FixVector2 TransformPoint(FixVector2 point)
        {
            FixVector3 result = this * new FixVector3(point.X, point.Y, 1);
            return new FixVector2(result.X / result.Z, result.Y / result.Z);
        }

        public FixVector2 TransformVector(FixVector2 vector)
        {
            FixVector3 result = this * new FixVector3(vector.X, vector.Y, 0);
            return new FixVector2(result.X, result.Y);
        }

        public bool Equals(FixMatrix3x3 other)
        {
            return Row0 == other.Row0 && Row1 == other.Row1 && Row2 == other.Row2;
        }

        public override bool Equals(object obj)
        {
            return obj is FixMatrix3x3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((Row0.GetHashCode() * 31 + Row1.GetHashCode()) * 31 + Row2.GetHashCode());
        }

        public static FixVector3 operator *(FixMatrix3x3 matrix, FixVector3 vector)
        {
            return new FixVector3(FixVector3.Dot(matrix.Row0, vector),
                FixVector3.Dot(matrix.Row1, vector), FixVector3.Dot(matrix.Row2, vector));
        }

        public static FixMatrix3x3 operator *(FixMatrix3x3 left, FixMatrix3x3 right)
        {
            return FromColumns(left * right.Column0, left * right.Column1, left * right.Column2);
        }

        public static bool operator ==(FixMatrix3x3 left, FixMatrix3x3 right) => left.Equals(right);
        public static bool operator !=(FixMatrix3x3 left, FixMatrix3x3 right) => !left.Equals(right);
    }
}
