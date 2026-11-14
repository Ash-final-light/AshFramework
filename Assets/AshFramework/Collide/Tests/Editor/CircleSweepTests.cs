using System.Collections.Generic;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CircleSweepTests
    {
        [Test]
        public void StopsRadiusOneCircleAtWallLeftSurface()
        {
            Circle2D moving = Circle(0, 0, 1);
            Aabb2D wall = new Aabb2D(V(3, 0), V(1, 4));

            CircleSweepStatus status = CircleSweep2D.Sweep(moving, V(5, 0), wall,
                out CircleSweepGeometryHit2D hit);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            AssertRawNear(hit.Time, Fix64.FromRatio(1, 5), 1);
            AssertRawNear(hit.Center.X, Fix64.One, 1);
            Assert.That(hit.Center.Y, Is.EqualTo(Fix64.Zero));
            Assert.That(hit.Normal.X, Is.LessThan(Fix64.Zero));
        }

        [Test]
        public void ThinWallCannotBeSkippedByLongMovement()
        {
            Fix64 halfRaw = Fix64.Epsilon;
            Aabb2D wall = new Aabb2D(V(50, 0), new FixVector2(halfRaw, 10));
            CircleSweepStatus status = CircleSweep2D.Sweep(Circle(0, 0, 1), V(100, 0), wall,
                out CircleSweepGeometryHit2D hit);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Fix64 contactCenter = Fix64.FromInt(49) - halfRaw;
            Assert.That(hit.Center.X, Is.LessThanOrEqualTo(contactCenter));
            AssertRawNear(hit.Center.X, contactCenter, 4);
        }

        [Test]
        public void SweepsAgainstCircleAndRotatedBox()
        {
            CircleSweepStatus circleStatus = CircleSweep2D.Sweep(Circle(0, 0, 1), V(10, 0),
                Circle(5, 0, 1), out CircleSweepGeometryHit2D circleHit);
            Obb2D box = new Obb2D(V(5, 0), V(2, 1), FixMath.Pi / 4);
            CircleSweepStatus boxStatus = CircleSweep2D.Sweep(Circle(0, 0, 1), V(10, 0),
                box, out CircleSweepGeometryHit2D boxHit);

            Assert.That(circleStatus, Is.EqualTo(CircleSweepStatus.Hit));
            AssertRawNear(circleHit.Center.X, Fix64.FromInt(3), 16);
            Assert.That(circleHit.Normal.X, Is.LessThan(Fix64.Zero));
            Assert.That(boxStatus, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(boxHit.Time, Is.GreaterThan(Fix64.Zero));
            Assert.That(boxHit.Time, Is.LessThan(Fix64.One));
            Assert.That(boxHit.Normal.X, Is.LessThan(Fix64.Zero));
        }

        [Test]
        public void EndpointTangencyIsAHit()
        {
            CircleSweepStatus status = CircleSweep2D.Sweep(Circle(0, 0, 1), V(3, 0),
                Circle(5, 0, 1), out CircleSweepGeometryHit2D hit);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(hit.Time, Is.EqualTo(Fix64.One));
            Assert.That(hit.Center, Is.EqualTo(V(3, 0)));
        }

        [Test]
        public void TangentStartOnlyBlocksMovementIntoObstacle()
        {
            Circle2D moving = Circle(0, 0, 1);
            Aabb2D wall = new Aabb2D(V(2, 0), V(1, 4));

            Assert.That(CircleSweep2D.Sweep(moving, V(-3, 0), wall, out _),
                Is.EqualTo(CircleSweepStatus.NoHit));
            Assert.That(CircleSweep2D.Sweep(moving, V(0, 3), wall, out _),
                Is.EqualTo(CircleSweepStatus.NoHit));
            Assert.That(CircleSweep2D.Sweep(moving, V(3, 0), wall, out CircleSweepGeometryHit2D hit),
                Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(hit.Time, Is.EqualTo(Fix64.Zero));
        }

        [Test]
        public void InitialPenetrationAndZeroMovementHaveDistinctStatuses()
        {
            Aabb2D wall = new Aabb2D(V(0, 0), V(1, 1));
            Assert.That(CircleSweep2D.Sweep(Circle(0, 0, 1), FixVector2.Zero, wall, out _),
                Is.EqualTo(CircleSweepStatus.InitialOverlap));
            Assert.That(CircleSweep2D.Sweep(Circle(5, 0, 1), FixVector2.Zero, wall, out _),
                Is.EqualTo(CircleSweepStatus.NoHit));
        }

        [Test]
        public void CompoundReturnsAllShapesAtEarliestTimeInIndexOrder()
        {
            CompoundShape2D compound = new CompoundShape2D();
            compound.SetShapes(new[]
            {
                WorldShape2D.FromAabb(new Aabb2D(V(5, 1), V(1, 1))),
                WorldShape2D.FromAabb(new Aabb2D(V(5, -1), V(1, 1))),
                WorldShape2D.FromCircle(Circle(8, 0, 1))
            });
            List<CircleSweepShapeHit2D> hits = new List<CircleSweepShapeHit2D>();

            CircleSweepStatus status = CircleSweep2D.Sweep(Circle(0, 0, 1), V(10, 0), compound, hits);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits[0].ShapeIndex, Is.EqualTo(0));
            Assert.That(hits[1].ShapeIndex, Is.EqualTo(1));
            Assert.That(hits[0].Time, Is.EqualTo(hits[1].Time));
        }

        [Test]
        public void OneRawUnitGapAndTangencyRemainDistinct()
        {
            Circle2D moving = new Circle2D(FixVector2.Zero, Fix64.Epsilon);
            Circle2D touching = new Circle2D(new FixVector2(Fix64.FromRaw(3), 0), Fix64.Epsilon);
            CircleSweepStatus status = CircleSweep2D.Sweep(moving,
                new FixVector2(Fix64.Epsilon, 0), touching, out CircleSweepGeometryHit2D hit);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(hit.Time, Is.EqualTo(Fix64.One));
            Assert.That(hit.Center.X, Is.EqualTo(Fix64.Epsilon));
        }

        [Test]
        public void DefaultShapesAreRejected()
        {
            Circle2D circle = Circle(0, 0, 1);
            Assert.Throws<System.ArgumentException>(() => CircleSweep2D.Sweep(default, V(1, 0), circle, out _));
            Assert.Throws<System.ArgumentException>(() => CircleSweep2D.Sweep(circle, V(1, 0), default(Circle2D), out _));
            Assert.Throws<System.ArgumentException>(() => CircleSweep2D.Sweep(circle, V(1, 0), default(Aabb2D), out _));
            Assert.Throws<System.ArgumentException>(() => CircleSweep2D.Sweep(circle, V(1, 0), default(Obb2D), out _));
            Assert.Throws<System.ArgumentException>(() => CircleSweep2D.Sweep(circle, V(1, 0), default(WorldShape2D), out _));
        }

        private static Circle2D Circle(int x, int y, int radius)
        {
            return new Circle2D(V(x, y), radius);
        }

        private static FixVector2 V(int x, int y)
        {
            return new FixVector2(x, y);
        }

        private static void AssertRawNear(Fix64 actual, Fix64 expected, long tolerance)
        {
            long difference = actual.RawValue - expected.RawValue;
            Assert.That(System.Math.Abs(difference), Is.LessThanOrEqualTo(tolerance));
        }
    }
}
