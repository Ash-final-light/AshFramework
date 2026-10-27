using System;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CompoundShapeTests
    {
        private static WorldShape2D Circle(int x, int y)
        {
            return WorldShape2D.FromCircle(new Circle2D(new FixVector2(x, y), 1));
        }

        private static WorldShape2D Create(ShapeKind2D kind, int x)
        {
            FixVector2 center = new FixVector2(x, 0);
            switch (kind)
            {
                case ShapeKind2D.Circle:
                    return WorldShape2D.FromCircle(new Circle2D(center, 1));
                case ShapeKind2D.Aabb:
                    return WorldShape2D.FromAabb(new Aabb2D(center, FixVector2.One));
                default:
                    return WorldShape2D.FromObb(new Obb2D(center, FixVector2.One, FixMath.Pi / 4));
            }
        }

        [TestCase(ShapeKind2D.Circle, ShapeKind2D.Circle)]
        [TestCase(ShapeKind2D.Circle, ShapeKind2D.Aabb)]
        [TestCase(ShapeKind2D.Circle, ShapeKind2D.Obb)]
        [TestCase(ShapeKind2D.Aabb, ShapeKind2D.Circle)]
        [TestCase(ShapeKind2D.Aabb, ShapeKind2D.Aabb)]
        [TestCase(ShapeKind2D.Aabb, ShapeKind2D.Obb)]
        [TestCase(ShapeKind2D.Obb, ShapeKind2D.Circle)]
        [TestCase(ShapeKind2D.Obb, ShapeKind2D.Aabb)]
        [TestCase(ShapeKind2D.Obb, ShapeKind2D.Obb)]
        public void AllNineWorldShapePairDirectionsDispatch(ShapeKind2D kindA, ShapeKind2D kindB)
        {
            WorldShape2D a = Create(kindA, 0);
            Assert.That(CompoundCollision2D.Overlaps(a, Create(kindB, 0)), Is.True);
            Assert.That(CompoundCollision2D.Overlaps(a, Create(kindB, 10)), Is.False);
        }

        [Test]
        public void WorldShapeFactoriesRejectDefaultsAndWrongAccessors()
        {
            Assert.That(default(WorldShape2D).IsValid, Is.False);
            Assert.Throws<ArgumentException>(() => WorldShape2D.FromCircle(default));
            Assert.Throws<ArgumentException>(() => WorldShape2D.FromAabb(default));
            Assert.Throws<ArgumentException>(() => WorldShape2D.FromObb(default));
            Assert.Throws<ArgumentException>(() => default(WorldShape2D).GetBounds());
            Assert.Throws<InvalidOperationException>(() => Circle(0, 0).Aabb.GetBounds());
            Assert.Throws<InvalidOperationException>(() => Create(ShapeKind2D.Aabb, 0).Obb.GetBounds());
            Assert.Throws<InvalidOperationException>(() => Create(ShapeKind2D.Obb, 0).Circle.GetBounds());
            Assert.Throws<ArgumentException>(() => CompoundCollision2D.Overlaps(default(WorldShape2D), Circle(0, 0)));
        }

        [Test]
        public void WholeBoundsIncludeEveryPartAndSecondPartCanHit()
        {
            CompoundShape2D a = new CompoundShape2D();
            a.SetShapes(new[] { Circle(-5, 0), Circle(5, 0) });
            Assert.That(a.TryGetBounds(out Aabb2D bounds), Is.True);
            Assert.That(bounds.Min, Is.EqualTo(new FixVector2(-6, -1)));
            Assert.That(bounds.Max, Is.EqualTo(new FixVector2(6, 1)));
            CompoundShape2D b = new CompoundShape2D();
            b.SetShapes(new[] { Circle(5, 0) });
            Assert.That(CompoundCollision2D.Overlaps(a, b), Is.True);
            Assert.That(CompoundCollision2D.Overlaps(b, a), Is.True);
        }

        [Test]
        public void OverallBoundsOverlapDoesNotFillGapsBetweenParts()
        {
            CompoundShape2D a = new CompoundShape2D();
            a.SetShapes(new[] { Circle(-5, 0), Circle(5, 0) });
            CompoundShape2D b = new CompoundShape2D();
            b.SetShapes(new[] { Circle(0, 0) });
            a.TryGetBounds(out Aabb2D boundsA);
            b.TryGetBounds(out Aabb2D boundsB);
            Assert.That(Collision2D.Overlaps(boundsA, boundsB), Is.True);
            Assert.That(CompoundCollision2D.Overlaps(a, b), Is.False);
        }

        [Test]
        public void CompoundCopiesInputsAndClearRemovesBoundsAndOldEntries()
        {
            WorldShape2D[] input = { Circle(0, 0), Circle(10, 0) };
            CompoundShape2D compound = new CompoundShape2D();
            compound.SetShapes(input);
            input[0] = Circle(100, 0);
            Assert.That(compound[0].Circle.Center.X, Is.EqualTo(Fix64.Zero));
            compound.SetShapes(new[] { Circle(2, 0) });
            Assert.That(compound.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => compound[1].GetBounds());
            compound.Clear();
            Assert.That(compound.Count, Is.Zero);
            Assert.That(compound.TryGetBounds(out Aabb2D bounds), Is.False);
            Assert.That(bounds.IsValid, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => compound[-1].GetBounds());
            Assert.That(CompoundCollision2D.Overlaps(compound, new CompoundShape2D()), Is.False);
        }

        [Test]
        public void InvalidReplacementIsAtomicAndNullInputsAreExplicit()
        {
            CompoundShape2D compound = new CompoundShape2D();
            compound.SetShapes(new[] { Circle(7, 0) });
            Assert.Throws<ArgumentException>(() => compound.SetShapes(new[] { Circle(3, 0), default(WorldShape2D) }));
            Assert.That(compound.Count, Is.EqualTo(1));
            Assert.That(compound[0].Circle.Center.X, Is.EqualTo(Fix64.FromInt(7)));
            Assert.Throws<ArgumentNullException>(() => compound.SetShapes(null));
            Assert.Throws<ArgumentNullException>(() => CompoundCollision2D.Overlaps(null, compound));
            Assert.Throws<ArgumentNullException>(() => CompoundCollision2D.Overlaps(compound, null));
            compound.SetShapes(Array.Empty<WorldShape2D>());
            Assert.That(compound.Count, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OddRawUnionRoundsOutwardWithoutOverflowAtCoordinateEdges(bool high)
        {
            long first = high ? long.MaxValue - 1 : long.MinValue + 1;
            long second = high ? long.MaxValue - 2 : long.MinValue + 2;
            WorldShape2D a = WorldShape2D.FromCircle(new Circle2D(
                new FixVector2(Fix64.FromRaw(first), 0), Fix64.Epsilon));
            WorldShape2D b = WorldShape2D.FromCircle(new Circle2D(
                new FixVector2(Fix64.FromRaw(second), 0), Fix64.Epsilon));
            CompoundShape2D compound = new CompoundShape2D();
            compound.SetShapes(new[] { a, b });
            compound.TryGetBounds(out Aabb2D bounds);
            Assert.That(bounds.Min.X <= a.GetBounds().Min.X && bounds.Min.X <= b.GetBounds().Min.X, Is.True);
            Assert.That(bounds.Max.X >= a.GetBounds().Max.X && bounds.Max.X >= b.GetBounds().Max.X, Is.True);
            Assert.That(bounds.HalfExtents.X.RawValue, Is.EqualTo(2));
        }

        [Test]
        public void UnrepresentableWholeRangeIsRejectedWithoutChangingOldData()
        {
            CompoundShape2D compound = new CompoundShape2D();
            compound.SetShapes(new[] { Circle(7, 0) });
            WorldShape2D left = WorldShape2D.FromCircle(new Circle2D(
                new FixVector2(Fix64.FromRaw(long.MinValue + 1), 0), Fix64.Epsilon));
            WorldShape2D right = WorldShape2D.FromCircle(new Circle2D(
                new FixVector2(Fix64.FromRaw(long.MaxValue - 1), 0), Fix64.Epsilon));
            Assert.Throws<OverflowException>(() => compound.SetShapes(new[] { left, right }));
            Assert.That(compound[0].Circle.Center.X, Is.EqualTo(Fix64.FromInt(7)));
        }
    }
}
