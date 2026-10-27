using System;
using System.Numerics;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>宽整数几何谓词；比较结束前不把平方或投影收窄为 Fix64。</summary>
    internal static class GeometryPredicates
    {
        internal static void ValidateHalfExtents(FixVector2 halfExtents)
        {
            if (halfExtents.X <= Fix64.Zero || halfExtents.Y <= Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(halfExtents), "Both half extents must be positive.");
            }
        }

        internal static void ValidateBounds(FixVector2 center, FixVector2 halfExtents)
        {
            ValidateAxisBounds(center.X.RawValue, halfExtents.X.RawValue);
            ValidateAxisBounds(center.Y.RawValue, halfExtents.Y.RawValue);
        }

        private static void ValidateAxisBounds(long center, long extent)
        {
            if ((BigInteger)center - extent < long.MinValue || (BigInteger)center + extent > long.MaxValue)
            {
                throw new OverflowException("Shape bounds exceed the Fix64 coordinate range.");
            }
        }

        internal static Aabb2D GetObbBounds(FixVector2 center, FixVector2 halfExtents, FixVector2 axisX)
        {
            BigInteger cos = BigInteger.Abs(axisX.X.RawValue);
            BigInteger sin = BigInteger.Abs(axisX.Y.RawValue);
            BigInteger x = halfExtents.X.RawValue * cos + halfExtents.Y.RawValue * sin;
            BigInteger y = halfExtents.X.RawValue * sin + halfExtents.Y.RawValue * cos;
            FixVector2 extents = new FixVector2(CeilingExtent(x), CeilingExtent(y));
            return new Aabb2D(center, extents);
        }

        private static Fix64 CeilingExtent(BigInteger scaledExtent)
        {
            BigInteger raw = (scaledExtent + Fix64.Scale - 1) / Fix64.Scale;
            return Fix64.FromRaw((long)raw);
        }

        internal static bool CircleCircle(Circle2D a, Circle2D b)
        {
            BigInteger dx = (BigInteger)a.Center.X.RawValue - b.Center.X.RawValue;
            BigInteger dy = (BigInteger)a.Center.Y.RawValue - b.Center.Y.RawValue;
            BigInteger radius = (BigInteger)a.Radius.RawValue + b.Radius.RawValue;
            return dx * dx + dy * dy <= radius * radius;
        }

        internal static bool CircleAabb(Circle2D circle, Aabb2D box)
        {
            BigInteger dx = BigInteger.Abs((BigInteger)circle.Center.X.RawValue - box.Center.X.RawValue);
            BigInteger dy = BigInteger.Abs((BigInteger)circle.Center.Y.RawValue - box.Center.Y.RawValue);
            BigInteger x = BigInteger.Max(BigInteger.Zero, dx - box.HalfExtents.X.RawValue);
            BigInteger y = BigInteger.Max(BigInteger.Zero, dy - box.HalfExtents.Y.RawValue);
            BigInteger radius = circle.Radius.RawValue;
            return x * x + y * y <= radius * radius;
        }

        internal static bool CircleObb(Circle2D circle, Obb2D box)
        {
            BigInteger dx = (BigInteger)circle.Center.X.RawValue - box.Center.X.RawValue;
            BigInteger dy = (BigInteger)circle.Center.Y.RawValue - box.Center.Y.RawValue;
            BigInteger ux = box.AxisX.X.RawValue;
            BigInteger uy = box.AxisX.Y.RawValue;
            BigInteger normSquared = ux * ux + uy * uy;

            // 用相同的轴长度补偿投影与距离，避免把量化轴错误地视为精确单位轴。
            BigInteger projectionX = BigInteger.Abs(dx * ux + dy * uy) * Fix64.Scale;
            BigInteger projectionY = BigInteger.Abs(-dx * uy + dy * ux) * Fix64.Scale;
            BigInteger x = BigInteger.Max(BigInteger.Zero, projectionX - box.HalfExtents.X.RawValue * normSquared);
            BigInteger y = BigInteger.Max(BigInteger.Zero, projectionY - box.HalfExtents.Y.RawValue * normSquared);
            BigInteger radius = circle.Radius.RawValue;
            BigInteger scale = Fix64.Scale;
            return x * x + y * y <= radius * radius * scale * scale * normSquared;
        }

        internal static bool AabbAabb(Aabb2D a, Aabb2D b)
        {
            BigInteger dx = BigInteger.Abs((BigInteger)a.Center.X.RawValue - b.Center.X.RawValue);
            BigInteger dy = BigInteger.Abs((BigInteger)a.Center.Y.RawValue - b.Center.Y.RawValue);
            BigInteger x = (BigInteger)a.HalfExtents.X.RawValue + b.HalfExtents.X.RawValue;
            BigInteger y = (BigInteger)a.HalfExtents.Y.RawValue + b.HalfExtents.Y.RawValue;
            return dx <= x && dy <= y;
        }

        internal static bool Boxes(BoxData a, BoxData b)
        {
            return OverlapOnAxis(a, b, a.AxisX)
                && OverlapOnAxis(a, b, a.AxisY)
                && OverlapOnAxis(a, b, b.AxisX)
                && OverlapOnAxis(a, b, b.AxisY);
        }

        private static bool OverlapOnAxis(BoxData a, BoxData b, FixVector2 axis)
        {
            BigInteger dx = (BigInteger)b.Center.X.RawValue - a.Center.X.RawValue;
            BigInteger dy = (BigInteger)b.Center.Y.RawValue - a.Center.Y.RawValue;
            BigInteger distance = BigInteger.Abs(dx * axis.X.RawValue + dy * axis.Y.RawValue) * Fix64.Scale;
            return distance <= ProjectionRadius(a, axis) + ProjectionRadius(b, axis);
        }

        private static BigInteger ProjectionRadius(BoxData box, FixVector2 axis)
        {
            BigInteger dotX = (BigInteger)box.AxisX.X.RawValue * axis.X.RawValue
                + (BigInteger)box.AxisX.Y.RawValue * axis.Y.RawValue;
            BigInteger dotY = (BigInteger)box.AxisY.X.RawValue * axis.X.RawValue
                + (BigInteger)box.AxisY.Y.RawValue * axis.Y.RawValue;
            return box.HalfExtents.X.RawValue * BigInteger.Abs(dotX)
                + box.HalfExtents.Y.RawValue * BigInteger.Abs(dotY);
        }
    }

    internal readonly struct BoxData
    {
        internal FixVector2 Center { get; }
        internal FixVector2 HalfExtents { get; }
        internal FixVector2 AxisX { get; }
        internal FixVector2 AxisY { get; }

        internal BoxData(Aabb2D box)
        {
            Center = box.Center;
            HalfExtents = box.HalfExtents;
            AxisX = FixVector2.Right;
            AxisY = FixVector2.Up;
        }

        internal BoxData(Obb2D box)
        {
            Center = box.Center;
            HalfExtents = box.HalfExtents;
            AxisX = box.AxisX;
            AxisY = box.AxisY;
        }
    }
}
