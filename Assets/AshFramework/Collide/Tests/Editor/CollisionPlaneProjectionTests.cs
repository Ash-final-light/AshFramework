using System;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionPlaneProjectionTests
    {
        [TestCase(CollisionPlane.XY, 5)]
        [TestCase(CollisionPlane.XZ, 20)]
        public void WorldPositionMapsToLogicalXY(CollisionPlane plane, int expectedY)
        {
            FixVector2 position = CollisionPlaneProjection.Project(new FixVector3(10, 5, 20), plane);
            Assert.That(position, Is.EqualTo(new FixVector2(10, expectedY)));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void EmbedAndProjectPreserveAllRawBits(CollisionPlane plane)
        {
            FixVector2 position = new FixVector2(Fix64.MinValue, Fix64.FromRaw(-17));
            Fix64 normal = Fix64.MaxValue;
            FixVector3 world = CollisionPlaneProjection.Embed(position, normal, plane);
            Assert.That(CollisionPlaneProjection.Project(world, plane), Is.EqualTo(position));
            Assert.That(plane == CollisionPlane.XY ? world.Z : world.Y, Is.EqualTo(normal));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void SupplyingOriginalNormalCoordinateRestoresWorldPosition(CollisionPlane plane)
        {
            FixVector3 original = new FixVector3(Fix64.FromRaw(-123), Fix64.FromRaw(987), Fix64.MinValue);
            Fix64 normal = plane == CollisionPlane.XY ? original.Z : original.Y;
            FixVector3 restored = CollisionPlaneProjection.Embed(
                CollisionPlaneProjection.Project(original, plane), normal, plane);
            Assert.That(restored, Is.EqualTo(original));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void DiscardedCoordinateDoesNotAffectPlanarData(CollisionPlane plane)
        {
            FixVector2 position = new FixVector2(-7, 13);
            FixVector3 a = CollisionPlaneProjection.Embed(position, Fix64.MinValue, plane);
            FixVector3 b = CollisionPlaneProjection.Embed(position, Fix64.MaxValue, plane);
            Assert.That(CollisionPlaneProjection.Project(a, plane), Is.EqualTo(position));
            Assert.That(CollisionPlaneProjection.Project(b, plane), Is.EqualTo(position));
        }

        [TestCase(CollisionPlane.XY, 1, 0, 0, 0)]
        [TestCase(CollisionPlane.XY, 0, 1, 0, 90)]
        [TestCase(CollisionPlane.XY, -1, 0, 0, 180)]
        [TestCase(CollisionPlane.XY, 0, -1, 0, -90)]
        [TestCase(CollisionPlane.XZ, 1, 0, 0, 0)]
        [TestCase(CollisionPlane.XZ, 0, 0, 1, 90)]
        [TestCase(CollisionPlane.XZ, -1, 0, 0, 180)]
        [TestCase(CollisionPlane.XZ, 0, 0, -1, -90)]
        public void CardinalAxisDirectionsProduceCounterClockwiseRadians(
            CollisionPlane plane, int x, int y, int z, int degrees)
        {
            Assert.That(CollisionPlaneProjection.TryGetRotation(new FixVector3(x, y, z), plane,
                out Fix64 rotation), Is.True);
            Fix64 expected = FixMath.DegreesToRadians(degrees);
            Assert.That(System.Math.Abs(FixMath.DeltaAngle(expected, rotation).ToDecimal()), Is.LessThan(0.00000001m));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void ZeroProjectionFailsEvenWhenWorldDirectionIsNonzero(CollisionPlane plane)
        {
            FixVector3 direction = plane == CollisionPlane.XY ? FixVector3.Forward : FixVector3.Up;
            Assert.That(CollisionPlaneProjection.TryGetRotation(direction, plane, out Fix64 angle), Is.False);
            Assert.That(angle, Is.EqualTo(Fix64.Zero));
            Assert.That(CollisionPlaneProjection.TryGetRotation(FixVector3.Zero, plane, out angle), Is.False);
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void TinyAndFullRangeDirectionsDoNotNeedSquaredLength(CollisionPlane plane)
        {
            FixVector3 tiny = CollisionPlaneProjection.Embed(new FixVector2(0, Fix64.Epsilon), 0, plane);
            Assert.That(CollisionPlaneProjection.TryGetRotation(tiny, plane, out Fix64 angle), Is.True);
            Assert.That(angle, Is.EqualTo(FixMath.HalfPi));
            FixVector3 large = CollisionPlaneProjection.Embed(new FixVector2(Fix64.MinValue, 0), 0, plane);
            Assert.That(CollisionPlaneProjection.TryGetRotation(large, plane, out angle), Is.True);
            Assert.That(FixMath.Abs(FixMath.DeltaAngle(FixMath.Pi, angle)), Is.LessThanOrEqualTo(Fix64.Epsilon));
        }

        [Test]
        public void InvalidPlaneIsRejectedByEveryEntryPoint()
        {
            CollisionPlane invalid = (CollisionPlane)123;
            Assert.Throws<ArgumentOutOfRangeException>(() => CollisionPlaneProjection.Project(FixVector3.Zero, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => CollisionPlaneProjection.Embed(FixVector2.Zero, 0, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => CollisionPlaneProjection.TryGetRotation(FixVector3.Right, invalid, out _));
        }

        [Test]
        public void BothPlanesFeedTheSameFineDetectionGeometry()
        {
            foreach (CollisionPlane plane in new[] { CollisionPlane.XY, CollisionPlane.XZ })
            {
                FixVector3 worldA = CollisionPlaneProjection.Embed(new FixVector2(10, 20), 5, plane);
                FixVector3 worldB = CollisionPlaneProjection.Embed(new FixVector2(12, 20), -99, plane);
                FixVector2 a = CollisionPlaneProjection.Project(worldA, plane);
                FixVector2 b = CollisionPlaneProjection.Project(worldB, plane);
                Circle2D circle = new Circle2D(a, 1);
                Aabb2D box = new Aabb2D(b, FixVector2.One);
                Obb2D rotated = new Obb2D(b, FixVector2.One, FixMath.HalfPi);
                Assert.That(Collision2D.Overlaps(circle, box), Is.True);
                Assert.That(Collision2D.Overlaps(circle, rotated), Is.True);
                Assert.That(Collision2D.Overlaps(box, rotated), Is.True);
            }
        }
    }
}
