using System;
using System.Numerics;
using AshFramework.Math;

namespace AshFramework.Collide
{
    internal static class ContactGeometry2D
    {
        private static readonly BigInteger ScaleSquared = (BigInteger)Fix64.Scale * Fix64.Scale;

        internal static bool CircleCircle(Circle2D a, Circle2D b, out Contact2D contact)
        {
            BigInteger x = (BigInteger)b.Center.X.RawValue - a.Center.X.RawValue;
            BigInteger y = (BigInteger)b.Center.Y.RawValue - a.Center.Y.RawValue;
            BigInteger distanceSquared = x * x + y * y;
            BigInteger radius = (BigInteger)a.Radius.RawValue + b.Radius.RawValue;
            if (distanceSquared > radius * radius)
            {
                contact = default;
                return false;
            }

            // Subtracting the floor of the distance rounds the remaining depth up.
            BigInteger rawDepth = radius - FloorSqrt(distanceSquared);
            FixVector2 normal = distanceSquared.IsZero ? FixVector2.Right : UnitNormal(x, y, distanceSquared);
            contact = new Contact2D(normal, ToDepth(rawDepth));
            return true;
        }

        internal static bool AabbAabb(Aabb2D a, Aabb2D b, out Contact2D contact)
        {
            BigInteger x = (BigInteger)b.Center.X.RawValue - a.Center.X.RawValue;
            BigInteger y = (BigInteger)b.Center.Y.RawValue - a.Center.Y.RawValue;
            BigInteger depthX = (BigInteger)a.HalfExtents.X.RawValue + b.HalfExtents.X.RawValue - BigInteger.Abs(x);
            BigInteger depthY = (BigInteger)a.HalfExtents.Y.RawValue + b.HalfExtents.Y.RawValue - BigInteger.Abs(y);
            if (depthX.Sign < 0 || depthY.Sign < 0)
            {
                contact = default;
                return false;
            }

            if (depthX <= depthY)
            {
                contact = new Contact2D(x.Sign < 0 ? -FixVector2.Right : FixVector2.Right, ToDepth(depthX));
            }
            else
            {
                contact = new Contact2D(y.Sign < 0 ? -FixVector2.Up : FixVector2.Up, ToDepth(depthY));
            }

            return true;
        }

        internal static bool CircleBox(Circle2D circle, BoxData box, out Contact2D contact)
        {
            BigInteger x = (BigInteger)circle.Center.X.RawValue - box.Center.X.RawValue;
            BigInteger y = (BigInteger)circle.Center.Y.RawValue - box.Center.Y.RawValue;
            BigInteger axisLengthSquared = SquaredLength(box.AxisX);
            BigInteger distanceDenominator = ScaleSquared * axisLengthSquared;
            BigInteger projectedX = Project(x, y, box.AxisX) * Fix64.Scale;
            BigInteger projectedY = Project(x, y, box.AxisY) * Fix64.Scale;
            BigInteger extentX = box.HalfExtents.X.RawValue * axisLengthSquared;
            BigInteger extentY = box.HalfExtents.Y.RawValue * axisLengthSquared;
            BigInteger offsetX = projectedX - BigInteger.Max(-extentX, BigInteger.Min(projectedX, extentX));
            BigInteger offsetY = projectedY - BigInteger.Max(-extentY, BigInteger.Min(projectedY, extentY));
            BigInteger radius = circle.Radius.RawValue;

            if (offsetX.IsZero && offsetY.IsZero)
            {
                // The center lies in the box. Exit through the nearest face, then clear the radius.
                BigInteger faceX = extentX - BigInteger.Abs(projectedX);
                BigInteger faceY = extentY - BigInteger.Abs(projectedY);
                bool useX = faceX < faceY || (faceX == faceY
                    && PrefersAxis(box.AxisX, axisLengthSquared, box.AxisY, axisLengthSquared));
                BigInteger faceDistance = useX ? faceX : faceY;
                FixVector2 axis = useX ? OrientedAxis(box.AxisX, -projectedX) : OrientedAxis(box.AxisY, -projectedY);
                BigInteger depth = radius + CeilingSqrtRatio(faceDistance * faceDistance, distanceDenominator);
                contact = new Contact2D(UnitNormal(axis.X.RawValue, axis.Y.RawValue, axisLengthSquared), ToDepth(depth));
                return true;
            }

            BigInteger offsetSquared = offsetX * offsetX + offsetY * offsetY;
            if (offsetSquared > radius * radius * distanceDenominator)
            {
                contact = default;
                return false;
            }

            // Transform the direction from the circle toward the closest box point back to world space.
            BigInteger normalX = -offsetX * box.AxisX.X.RawValue - offsetY * box.AxisY.X.RawValue;
            BigInteger normalY = -offsetX * box.AxisX.Y.RawValue - offsetY * box.AxisY.Y.RawValue;
            FixVector2 normal = UnitNormal(normalX, normalY, normalX * normalX + normalY * normalY);
            BigInteger rawDepth = radius - FloorSqrt(offsetSquared / distanceDenominator);
            contact = new Contact2D(normal, ToDepth(rawDepth));
            return true;
        }

        internal static bool Boxes(BoxData a, BoxData b, out Contact2D contact)
        {
            BigInteger x = (BigInteger)b.Center.X.RawValue - a.Center.X.RawValue;
            BigInteger y = (BigInteger)b.Center.Y.RawValue - a.Center.Y.RawValue;
            if (!TryBoxAxis(a, b, a.AxisX, Project(x, y, a.AxisX), out BoxAxisContact ax)
                || !TryBoxAxis(a, b, a.AxisY, Project(x, y, a.AxisY), out BoxAxisContact ay)
                || !TryBoxAxis(a, b, b.AxisX, Project(x, y, b.AxisX), out BoxAxisContact bx)
                || !TryBoxAxis(a, b, b.AxisY, Project(x, y, b.AxisY), out BoxAxisContact by))
            {
                contact = default;
                return false;
            }

            BoxAxisContact best = Shallower(Shallower(ax, ay), Shallower(bx, by));
            FixVector2 normal = UnitNormal(best.Direction.X.RawValue, best.Direction.Y.RawValue, best.LengthSquared);
            BigInteger depth = CeilingSqrtRatio(best.OverlapSquared, ScaleSquared * best.LengthSquared);
            contact = new Contact2D(normal, ToDepth(depth));
            return true;
        }

        private static bool TryBoxAxis(BoxData a, BoxData b, FixVector2 axis, BigInteger projection,
            out BoxAxisContact contact)
        {
            BigInteger overlap = GeometryPredicates.ProjectionRadius(a, axis) + GeometryPredicates.ProjectionRadius(b, axis)
                - BigInteger.Abs(projection) * Fix64.Scale;
            if (overlap.Sign < 0)
            {
                contact = default;
                return false;
            }

            contact = new BoxAxisContact(OrientedAxis(axis, projection), SquaredLength(axis), overlap * overlap);
            return true;
        }

        private static BoxAxisContact Shallower(BoxAxisContact a, BoxAxisContact b)
        {
            // Compare squared world depths before rounding, accounting for each axis's actual length.
            int comparison = (a.OverlapSquared * b.LengthSquared).CompareTo(b.OverlapSquared * a.LengthSquared);
            if (comparison == 0)
            {
                return PrefersAxis(a.Direction, a.LengthSquared, b.Direction, b.LengthSquared) ? a : b;
            }

            return comparison < 0 ? a : b;
        }

        private static bool PrefersAxis(FixVector2 a, BigInteger lengthA, FixVector2 b, BigInteger lengthB)
        {
            // Prefer the axis closest to world X. For equal slopes prefer the canonical axis with positive Y.
            BigInteger xA = a.X.RawValue;
            BigInteger xB = b.X.RawValue;
            int comparison = (xA * xA * lengthB).CompareTo(xB * xB * lengthA);
            if (comparison != 0)
            {
                return comparison > 0;
            }

            return CanonicalAxis(a).Y >= Fix64.Zero || CanonicalAxis(b).Y < Fix64.Zero;
        }

        private static FixVector2 OrientedAxis(FixVector2 axis, BigInteger projection)
        {
            if (projection.IsZero)
            {
                return CanonicalAxis(axis);
            }

            return projection.Sign > 0 ? axis : -axis;
        }

        private static FixVector2 CanonicalAxis(FixVector2 axis)
        {
            bool reverse = axis.X < Fix64.Zero || (axis.X == Fix64.Zero && axis.Y < Fix64.Zero);
            return reverse ? -axis : axis;
        }

        private static BigInteger Project(BigInteger x, BigInteger y, FixVector2 axis)
        {
            return x * axis.X.RawValue + y * axis.Y.RawValue;
        }

        private static BigInteger SquaredLength(FixVector2 axis)
        {
            return Project(axis.X.RawValue, axis.Y.RawValue, axis);
        }

        private static FixVector2 UnitNormal(BigInteger x, BigInteger y, BigInteger lengthSquared)
        {
            return new FixVector2(RoundUnitComponent(x, lengthSquared), RoundUnitComponent(y, lengthSquared));
        }

        private static Fix64 RoundUnitComponent(BigInteger component, BigInteger lengthSquared)
        {
            BigInteger scaledSquared = component * component * ScaleSquared;
            BigInteger rounded = FloorSqrt(scaledSquared / lengthSquared);
            BigInteger twiceMidpoint = 2 * rounded + 1;
            if (4 * scaledSquared >= twiceMidpoint * twiceMidpoint * lengthSquared)
            {
                rounded++;
            }

            return Fix64.FromRaw((long)(component.Sign < 0 ? -rounded : rounded));
        }

        private static Fix64 ToDepth(BigInteger rawDepth)
        {
            if (rawDepth > long.MaxValue)
            {
                throw new OverflowException("Contact depth exceeds the Fix64 range.");
            }

            return Fix64.FromRaw((long)rawDepth);
        }

        private static BigInteger CeilingSqrtRatio(BigInteger numerator, BigInteger denominator)
        {
            BigInteger floor = FloorSqrt(numerator / denominator);
            return floor * floor * denominator == numerator ? floor : floor + 1;
        }

        private static BigInteger FloorSqrt(BigInteger value)
        {
            if (value <= BigInteger.One)
            {
                return value;
            }

            // Start above the root and use integer Newton iteration until it stops decreasing.
            BigInteger current = BigInteger.One << (value.ToByteArray().Length * 4);
            BigInteger next = (current + value / current) / 2;
            while (next < current)
            {
                current = next;
                next = (current + value / current) / 2;
            }

            return current;
        }

        private readonly struct BoxAxisContact
        {
            internal readonly FixVector2 Direction;
            internal readonly BigInteger LengthSquared;
            internal readonly BigInteger OverlapSquared;

            internal BoxAxisContact(FixVector2 direction, BigInteger lengthSquared, BigInteger overlapSquared)
            {
                Direction = direction;
                LengthSquared = lengthSquared;
                OverlapSquared = overlapSquared;
            }
        }
    }
}
