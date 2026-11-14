using System;
using System.Collections.Generic;
using System.Numerics;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>单圆对固定简单形状或复合体的连续平移扫掠。</summary>
    public static class CircleSweep2D
    {
        private static readonly BigInteger TimeScale = Fix64.Scale;

        public static CircleSweepStatus Sweep(Circle2D moving, FixVector2 translation,
            Circle2D obstacle, out CircleSweepGeometryHit2D hit)
        {
            moving.Validate();
            obstacle.Validate();
            BigInteger radius = (BigInteger)moving.Radius.RawValue + obstacle.Radius.RawValue;
            return SweepRoundedPoint(moving, translation,
                (BigInteger)moving.Center.X.RawValue - obstacle.Center.X.RawValue,
                (BigInteger)moving.Center.Y.RawValue - obstacle.Center.Y.RawValue,
                translation.X.RawValue, translation.Y.RawValue,
                BigInteger.Zero, BigInteger.Zero, radius * radius,
                FixVector2.Right, FixVector2.Up, out hit);
        }

        public static CircleSweepStatus Sweep(Circle2D moving, FixVector2 translation,
            Aabb2D obstacle, out CircleSweepGeometryHit2D hit)
        {
            moving.Validate();
            obstacle.Validate();
            BigInteger radius = moving.Radius.RawValue;
            return SweepRoundedPoint(moving, translation,
                (BigInteger)moving.Center.X.RawValue - obstacle.Center.X.RawValue,
                (BigInteger)moving.Center.Y.RawValue - obstacle.Center.Y.RawValue,
                translation.X.RawValue, translation.Y.RawValue,
                obstacle.HalfExtents.X.RawValue, obstacle.HalfExtents.Y.RawValue,
                radius * radius, FixVector2.Right, FixVector2.Up, out hit);
        }

        public static CircleSweepStatus Sweep(Circle2D moving, FixVector2 translation,
            Obb2D obstacle, out CircleSweepGeometryHit2D hit)
        {
            moving.Validate();
            obstacle.Validate();
            BigInteger dx = (BigInteger)moving.Center.X.RawValue - obstacle.Center.X.RawValue;
            BigInteger dy = (BigInteger)moving.Center.Y.RawValue - obstacle.Center.Y.RawValue;
            BigInteger ux = obstacle.AxisX.X.RawValue;
            BigInteger uy = obstacle.AxisX.Y.RawValue;
            BigInteger lengthSquared = ux * ux + uy * uy;
            BigInteger startX = (dx * ux + dy * uy) * Fix64.Scale;
            BigInteger startY = (-dx * uy + dy * ux) * Fix64.Scale;
            BigInteger moveX = ((BigInteger)translation.X.RawValue * ux
                + (BigInteger)translation.Y.RawValue * uy) * Fix64.Scale;
            BigInteger moveY = (-(BigInteger)translation.X.RawValue * uy
                + (BigInteger)translation.Y.RawValue * ux) * Fix64.Scale;
            BigInteger extentX = (BigInteger)obstacle.HalfExtents.X.RawValue * lengthSquared;
            BigInteger extentY = (BigInteger)obstacle.HalfExtents.Y.RawValue * lengthSquared;
            BigInteger radiusSquared = (BigInteger)moving.Radius.RawValue * moving.Radius.RawValue
                * Fix64.Scale * Fix64.Scale * lengthSquared;
            return SweepRoundedPoint(moving, translation, startX, startY, moveX, moveY,
                extentX, extentY, radiusSquared, obstacle.AxisX, obstacle.AxisY, out hit);
        }

        public static CircleSweepStatus Sweep(Circle2D moving, FixVector2 translation,
            WorldShape2D obstacle, out CircleSweepGeometryHit2D hit)
        {
            if (!obstacle.IsValid)
            {
                throw new ArgumentException("An uninitialized world shape is invalid.", nameof(obstacle));
            }

            switch (obstacle.Kind)
            {
                case ShapeKind2D.Circle:
                    return Sweep(moving, translation, obstacle.Circle, out hit);
                case ShapeKind2D.Aabb:
                    return Sweep(moving, translation, obstacle.Aabb, out hit);
                case ShapeKind2D.Obb:
                    return Sweep(moving, translation, obstacle.Obb, out hit);
                default:
                    throw new ArgumentException("Unsupported world shape kind.", nameof(obstacle));
            }
        }

        /// <summary>清空 hits，返回最早时刻的全部子形状命中，按子形状索引排序。</summary>
        public static CircleSweepStatus Sweep(Circle2D moving, FixVector2 translation,
            CompoundShape2D obstacle, List<CircleSweepShapeHit2D> hits)
        {
            if (obstacle == null)
            {
                throw new ArgumentNullException(nameof(obstacle));
            }

            if (hits == null)
            {
                throw new ArgumentNullException(nameof(hits));
            }

            hits.Clear();
            CircleSweepStatus result = CircleSweepStatus.NoHit;
            Fix64 earliest = Fix64.One;
            for (int i = 0; i < obstacle.Count; i++)
            {
                CircleSweepStatus status = Sweep(moving, translation, obstacle[i], out CircleSweepGeometryHit2D hit);
                if (status == CircleSweepStatus.InitialOverlap)
                {
                    hits.Clear();
                    return status;
                }

                if (status != CircleSweepStatus.Hit)
                {
                    continue;
                }

                if (result != CircleSweepStatus.Hit || hit.Time < earliest)
                {
                    hits.Clear();
                    earliest = hit.Time;
                    result = CircleSweepStatus.Hit;
                }

                if (hit.Time == earliest)
                {
                    hits.Add(new CircleSweepShapeHit2D(i, hit));
                }
            }

            return result;
        }

        private static CircleSweepStatus SweepRoundedPoint(Circle2D moving, FixVector2 translation,
            BigInteger startX, BigInteger startY, BigInteger moveX, BigInteger moveY,
            BigInteger extentX, BigInteger extentY, BigInteger radiusSquared,
            FixVector2 axisX, FixVector2 axisY, out CircleSweepGeometryHit2D hit)
        {
            Rational zero = Rational.Zero;
            BigInteger startDistance = DistanceNumerator(startX, startY, moveX, moveY,
                extentX, extentY, zero, out BigInteger offsetX, out BigInteger offsetY);
            int startComparison = startDistance.CompareTo(radiusSquared);
            if (startComparison < 0)
            {
                hit = default;
                return CircleSweepStatus.InitialOverlap;
            }

            if (translation == FixVector2.Zero)
            {
                hit = default;
                return CircleSweepStatus.NoHit;
            }

            if (startComparison == 0)
            {
                BigInteger derivative = offsetX * moveX + offsetY * moveY;
                if (derivative < 0)
                {
                    FixVector2 normal = GetNormal(offsetX, offsetY, axisX, axisY);
                    hit = new CircleSweepGeometryHit2D(Fix64.Zero, moving.Center, normal);
                    return CircleSweepStatus.Hit;
                }

                hit = default;
                return CircleSweepStatus.NoHit;
            }

            List<Rational> boundaries = new List<Rational> { Rational.Zero, Rational.One };
            AddBoundary(startX, moveX, -extentX, boundaries);
            AddBoundary(startX, moveX, extentX, boundaries);
            AddBoundary(startY, moveY, -extentY, boundaries);
            AddBoundary(startY, moveY, extentY, boundaries);
            boundaries.Sort();
            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                Rational from = boundaries[i];
                Rational to = boundaries[i + 1];
                if (from == to)
                {
                    continue;
                }

                Rational minimum = MinimumInSegment(startX, startY, moveX, moveY,
                    extentX, extentY, from, to);
                if (!IsWithin(startX, startY, moveX, moveY, extentX, extentY, minimum, radiusSquared))
                {
                    continue;
                }

                Rational contact = FindFirstContact(startX, startY, moveX, moveY,
                    extentX, extentY, from, minimum, radiusSquared);
                Fix64 time = ToSafeTime(contact);
                FixVector2 center = CenterAt(moving.Center, translation, time);
                DistanceNumerator(startX, startY, moveX, moveY, extentX, extentY,
                    contact, out offsetX, out offsetY);
                FixVector2 normal = GetNormal(offsetX, offsetY, axisX, axisY);
                hit = new CircleSweepGeometryHit2D(time, center, normal);
                return CircleSweepStatus.Hit;
            }

            hit = default;
            return CircleSweepStatus.NoHit;
        }

        private static Rational MinimumInSegment(BigInteger startX, BigInteger startY,
            BigInteger moveX, BigInteger moveY, BigInteger extentX, BigInteger extentY,
            Rational from, Rational to)
        {
            Rational middle = Rational.Midpoint(from, to);
            GetRegion(startX, moveX, extentX, middle, out BigInteger baseX, out BigInteger velocityX);
            GetRegion(startY, moveY, extentY, middle, out BigInteger baseY, out BigInteger velocityY);
            BigInteger denominator = velocityX * velocityX + velocityY * velocityY;
            if (denominator.IsZero)
            {
                return from;
            }

            Rational stationary = new Rational(-(baseX * velocityX + baseY * velocityY), denominator);
            if (stationary < from)
            {
                return from;
            }

            return stationary > to ? to : stationary;
        }

        private static void GetRegion(BigInteger start, BigInteger movement, BigInteger extent,
            Rational sample, out BigInteger offsetBase, out BigInteger velocity)
        {
            BigInteger value = start * sample.Denominator + movement * sample.Numerator;
            BigInteger boundary = extent * sample.Denominator;
            if (value < -boundary)
            {
                offsetBase = start + extent;
                velocity = movement;
            }
            else if (value > boundary)
            {
                offsetBase = start - extent;
                velocity = movement;
            }
            else
            {
                offsetBase = BigInteger.Zero;
                velocity = BigInteger.Zero;
            }
        }

        private static Rational FindFirstContact(BigInteger startX, BigInteger startY,
            BigInteger moveX, BigInteger moveY, BigInteger extentX, BigInteger extentY,
            Rational separated, Rational touching, BigInteger radiusSquared)
        {
            if (IsWithin(startX, startY, moveX, moveY, extentX, extentY, separated, radiusSquared))
            {
                return separated;
            }

            for (int i = 0; i < 96; i++)
            {
                Rational middle = Rational.Midpoint(separated, touching);
                if (IsWithin(startX, startY, moveX, moveY, extentX, extentY, middle, radiusSquared))
                {
                    touching = middle;
                }
                else
                {
                    separated = middle;
                }
            }

            return touching;
        }

        private static bool IsWithin(BigInteger startX, BigInteger startY,
            BigInteger moveX, BigInteger moveY, BigInteger extentX, BigInteger extentY,
            Rational time, BigInteger radiusSquared)
        {
            BigInteger distance = DistanceNumerator(startX, startY, moveX, moveY,
                extentX, extentY, time, out _, out _);
            return distance <= radiusSquared * time.Denominator * time.Denominator;
        }

        private static BigInteger DistanceNumerator(BigInteger startX, BigInteger startY,
            BigInteger moveX, BigInteger moveY, BigInteger extentX, BigInteger extentY,
            Rational time, out BigInteger offsetX, out BigInteger offsetY)
        {
            offsetX = OffsetNumerator(startX, moveX, extentX, time);
            offsetY = OffsetNumerator(startY, moveY, extentY, time);
            return offsetX * offsetX + offsetY * offsetY;
        }

        private static BigInteger OffsetNumerator(BigInteger start, BigInteger movement,
            BigInteger extent, Rational time)
        {
            BigInteger value = start * time.Denominator + movement * time.Numerator;
            BigInteger boundary = extent * time.Denominator;
            if (value < -boundary)
            {
                return value + boundary;
            }

            return value > boundary ? value - boundary : BigInteger.Zero;
        }

        private static void AddBoundary(BigInteger start, BigInteger movement,
            BigInteger boundary, List<Rational> boundaries)
        {
            if (movement.IsZero)
            {
                return;
            }

            Rational time = new Rational(boundary - start, movement);
            if (time > Rational.Zero && time < Rational.One && !boundaries.Contains(time))
            {
                boundaries.Add(time);
            }
        }

        private static Fix64 ToSafeTime(Rational contact)
        {
            BigInteger raw = contact.Numerator * TimeScale / contact.Denominator;
            if (raw < 0)
            {
                raw = BigInteger.Zero;
            }
            else if (raw > Fix64.Scale)
            {
                raw = Fix64.Scale;
            }

            return Fix64.FromRaw((long)raw);
        }

        private static FixVector2 CenterAt(FixVector2 start, FixVector2 translation, Fix64 time)
        {
            BigInteger x = (BigInteger)translation.X.RawValue * time.RawValue / Fix64.Scale;
            BigInteger y = (BigInteger)translation.Y.RawValue * time.RawValue / Fix64.Scale;
            return new FixVector2(Fix64.FromRaw(checked(start.X.RawValue + (long)x)),
                Fix64.FromRaw(checked(start.Y.RawValue + (long)y)));
        }

        private static FixVector2 GetNormal(BigInteger offsetX, BigInteger offsetY,
            FixVector2 axisX, FixVector2 axisY)
        {
            BigInteger worldX = offsetX * axisX.X.RawValue + offsetY * axisY.X.RawValue;
            BigInteger worldY = offsetX * axisX.Y.RawValue + offsetY * axisY.Y.RawValue;
            FixVector2 direction = new FixVector2(ToScaledComponent(worldX), ToScaledComponent(worldY));
            return direction.NormalizeOrFallback(FixVector2.Right);
        }

        private static Fix64 ToScaledComponent(BigInteger value)
        {
            BigInteger absolute = BigInteger.Abs(value);
            while (absolute > long.MaxValue)
            {
                value /= 2;
                absolute /= 2;
            }

            return Fix64.FromRaw((long)value);
        }

        private readonly struct Rational : IComparable<Rational>, IEquatable<Rational>
        {
            internal static readonly Rational Zero = new Rational(BigInteger.Zero, BigInteger.One);
            internal static readonly Rational One = new Rational(BigInteger.One, BigInteger.One);

            internal BigInteger Numerator { get; }
            internal BigInteger Denominator { get; }

            internal Rational(BigInteger numerator, BigInteger denominator)
            {
                if (denominator.IsZero)
                {
                    throw new DivideByZeroException();
                }

                if (denominator.Sign < 0)
                {
                    numerator = -numerator;
                    denominator = -denominator;
                }

                BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
                if (divisor > BigInteger.One)
                {
                    numerator /= divisor;
                    denominator /= divisor;
                }

                Numerator = numerator;
                Denominator = denominator;
            }

            internal static Rational Midpoint(Rational a, Rational b)
            {
                return new Rational(a.Numerator * b.Denominator + b.Numerator * a.Denominator,
                    2 * a.Denominator * b.Denominator);
            }

            public int CompareTo(Rational other)
            {
                return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
            }

            public bool Equals(Rational other)
            {
                return CompareTo(other) == 0;
            }

            public override bool Equals(object obj)
            {
                return obj is Rational other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Numerator.GetHashCode() ^ Denominator.GetHashCode();
            }

            public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
            public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
            public static bool operator ==(Rational a, Rational b) => a.Equals(b);
            public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
        }
    }
}
