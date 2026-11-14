using System;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class SpatialOperationContractTests
    {
        [Test]
        public void SweepStatusValuesAreStable()
        {
            Assert.That((int)CircleSweepStatus.NoHit, Is.EqualTo(0));
            Assert.That((int)CircleSweepStatus.Hit, Is.EqualTo(1));
            Assert.That((int)CircleSweepStatus.InitialOverlap, Is.EqualTo(2));
        }

        [Test]
        public void SweepHitRejectsInvalidFields()
        {
            CollisionWorld world = CreateWorld();
            CollisionBodyId id = world.RegisterBody();

            Assert.Throws<ArgumentException>(() => new CircleSweepHit2D(default, 0, Fix64.Zero,
                FixVector2.Zero, FixVector2.Right));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CircleSweepHit2D(id, -1, Fix64.Zero,
                FixVector2.Zero, FixVector2.Right));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CircleSweepHit2D(id, 0, -Fix64.Epsilon,
                FixVector2.Zero, FixVector2.Right));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CircleSweepHit2D(id, 0, Fix64.One + Fix64.Epsilon,
                FixVector2.Zero, FixVector2.Right));
            Assert.Throws<ArgumentException>(() => new CircleSweepHit2D(id, 0, Fix64.Zero,
                FixVector2.Zero, FixVector2.Zero));
        }

        [Test]
        public void SweepHitsOrderByTimeBodyAndShape()
        {
            CollisionWorld world = CreateWorld();
            CollisionBodyId first = world.RegisterBody();
            CollisionBodyId second = world.RegisterBody();
            CircleSweepHit2D later = Hit(first, 0, Fix64.One);
            CircleSweepHit2D secondBody = Hit(second, 0, Fix64.Half);
            CircleSweepHit2D secondShape = Hit(first, 1, Fix64.Half);
            CircleSweepHit2D firstShape = Hit(first, 0, Fix64.Half);

            Assert.That(firstShape.CompareTo(secondShape), Is.LessThan(0));
            Assert.That(secondShape.CompareTo(secondBody), Is.LessThan(0));
            Assert.That(secondBody.CompareTo(later), Is.LessThan(0));
            Assert.That(firstShape.IsValid, Is.True);
            Assert.That(default(CircleSweepHit2D).IsValid, Is.False);
        }

        [Test]
        public void SweepHitsFromDifferentWorldsCannotBeOrdered()
        {
            CircleSweepHit2D first = Hit(CreateWorld().RegisterBody(), 0, Fix64.Zero);
            CircleSweepHit2D second = Hit(CreateWorld().RegisterBody(), 0, Fix64.Zero);

            Assert.Throws<ArgumentException>(() => first.CompareTo(second));
        }

        private static CollisionWorld CreateWorld()
        {
            Aabb2D bounds = new Aabb2D(FixVector2.Zero, new FixVector2(10, 10));
            return new CollisionWorld(bounds, new UniformGridBroadPhase2D(bounds, 1));
        }

        private static CircleSweepHit2D Hit(CollisionBodyId body, int shapeIndex, Fix64 time)
        {
            return new CircleSweepHit2D(body, shapeIndex, time, FixVector2.Zero, FixVector2.Right);
        }
    }
}
