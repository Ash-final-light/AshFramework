using System.Numerics;

namespace AshFramework.Math
{
    /// <summary>提供通用几何计算，独立于场景和碰撞框架。</summary>
    public static class FixGeometry
    {
        /// <summary>逆时针返回正数，顺时针返回负数，共线返回零。</summary>
        public static int Orientation(FixVector2 a, FixVector2 b, FixVector2 c)
        {
            BigInteger abX = (BigInteger)b.X.RawValue - a.X.RawValue;
            BigInteger abY = (BigInteger)b.Y.RawValue - a.Y.RawValue;
            BigInteger acX = (BigInteger)c.X.RawValue - a.X.RawValue;
            BigInteger acY = (BigInteger)c.Y.RawValue - a.Y.RawValue;
            return (abX * acY - abY * acX).Sign;
        }

        public static FixVector2 ClosestPointOnSegment(FixVector2 point, FixVector2 start, FixVector2 end)
        {
            BigInteger dx = (BigInteger)end.X.RawValue - start.X.RawValue;
            BigInteger dy = (BigInteger)end.Y.RawValue - start.Y.RawValue;
            BigInteger denominator = dx * dx + dy * dy;
            if (denominator.IsZero)
            {
                return start;
            }

            BigInteger numerator = ((BigInteger)point.X.RawValue - start.X.RawValue) * dx
                + ((BigInteger)point.Y.RawValue - start.Y.RawValue) * dy;
            numerator = BigInteger.Max(BigInteger.Zero, BigInteger.Min(denominator, numerator));
            return new FixVector2(
                Fix64.FromWideRaw(start.X.RawValue + dx * numerator / denominator),
                Fix64.FromWideRaw(start.Y.RawValue + dy * numerator / denominator));
        }

        public static FixVector3 ClosestPointOnSegment(FixVector3 point, FixVector3 start, FixVector3 end)
        {
            BigInteger dx = (BigInteger)end.X.RawValue - start.X.RawValue;
            BigInteger dy = (BigInteger)end.Y.RawValue - start.Y.RawValue;
            BigInteger dz = (BigInteger)end.Z.RawValue - start.Z.RawValue;
            BigInteger denominator = dx * dx + dy * dy + dz * dz;
            if (denominator.IsZero)
            {
                return start;
            }

            BigInteger numerator = ((BigInteger)point.X.RawValue - start.X.RawValue) * dx
                + ((BigInteger)point.Y.RawValue - start.Y.RawValue) * dy
                + ((BigInteger)point.Z.RawValue - start.Z.RawValue) * dz;
            numerator = BigInteger.Max(BigInteger.Zero, BigInteger.Min(denominator, numerator));
            return new FixVector3(
                Fix64.FromWideRaw(start.X.RawValue + dx * numerator / denominator),
                Fix64.FromWideRaw(start.Y.RawValue + dy * numerator / denominator),
                Fix64.FromWideRaw(start.Z.RawValue + dz * numerator / denominator));
        }

        public static Fix64 DistanceToSegment(FixVector2 point, FixVector2 start, FixVector2 end)
        {
            return FixVector2.Distance(point, ClosestPointOnSegment(point, start, end));
        }

        /// <summary>返回顶点 a、b、c 对应的重心权重；三点严格共线时返回 false。</summary>
        public static bool TryBarycentric(FixVector2 point, FixVector2 a, FixVector2 b,
            FixVector2 c, out FixVector3 weights)
        {
            BigInteger abX = (BigInteger)b.X.RawValue - a.X.RawValue;
            BigInteger abY = (BigInteger)b.Y.RawValue - a.Y.RawValue;
            BigInteger acX = (BigInteger)c.X.RawValue - a.X.RawValue;
            BigInteger acY = (BigInteger)c.Y.RawValue - a.Y.RawValue;
            BigInteger apX = (BigInteger)point.X.RawValue - a.X.RawValue;
            BigInteger apY = (BigInteger)point.Y.RawValue - a.Y.RawValue;
            BigInteger denominator = abX * acY - abY * acX;
            if (denominator.IsZero)
            {
                weights = FixVector3.Zero;
                return false;
            }

            Fix64 v = Fix64.FromWideRaw((apX * acY - apY * acX) * Fix64.Scale / denominator);
            Fix64 w = Fix64.FromWideRaw((abX * apY - abY * apX) * Fix64.Scale / denominator);
            weights = new FixVector3(1 - v - w, v, w);
            return true;
        }

        public static FixVector2 QuadraticBezier(FixVector2 a, FixVector2 b, FixVector2 c, Fix64 t)
        {
            t = FixMath.Clamp01(t);
            return FixVector2.LerpUnclamped(FixVector2.LerpUnclamped(a, b, t),
                FixVector2.LerpUnclamped(b, c, t), t);
        }

        public static FixVector3 CubicBezier(FixVector3 a, FixVector3 b, FixVector3 c, FixVector3 d, Fix64 t)
        {
            t = FixMath.Clamp01(t);
            FixVector3 ab = FixVector3.LerpUnclamped(a, b, t);
            FixVector3 bc = FixVector3.LerpUnclamped(b, c, t);
            FixVector3 cd = FixVector3.LerpUnclamped(c, d, t);
            return FixVector3.LerpUnclamped(FixVector3.LerpUnclamped(ab, bc, t),
                FixVector3.LerpUnclamped(bc, cd, t), t);
        }
    }
}
