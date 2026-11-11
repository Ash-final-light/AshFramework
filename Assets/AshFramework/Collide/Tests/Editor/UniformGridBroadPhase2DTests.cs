using System;
using System.Collections.Generic;
using System.Reflection;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class UniformGridBroadPhase2DTests
    {
        private static readonly Aabb2D Coverage = Box(0, 0, 10, 10);
        private CollisionWorld _identities;
        private UniformGridBroadPhase2D _grid;
        private readonly List<CollisionBodyId> _results = new List<CollisionBodyId>();

        [SetUp]
        public void SetUp()
        {
            // 只借用真实世界分配合法身份，单独验证索引接口，不向 Stub 提交几何。
            _identities = new CollisionWorld(Coverage, new StubBroadPhase2D());
            _grid = new UniformGridBroadPhase2D(Coverage, 2);
            _results.Clear();
        }

        private static Aabb2D Box(int x, int y, int halfX = 1, int halfY = 1)
        {
            return new Aabb2D(new FixVector2(x, y), new FixVector2(halfX, halfY));
        }

        private CollisionBodyId Add(Aabb2D bounds)
        {
            CollisionBodyId id = _identities.RegisterBody();
            _grid.Insert(id, bounds);
            return id;
        }

        private void Expect(Aabb2D query, params CollisionBodyId[] expected)
        {
            _results.Clear();
            _grid.Query(query, _results);
            Assert.That(_results, Is.EquivalentTo(expected));
            Assert.That(new HashSet<CollisionBodyId>(_results).Count, Is.EqualTo(_results.Count));
        }

        [Test]
        public void ConstructorRejectsInvalidBoundsSizeAndExcessiveCellCount()
        {
            Assert.Throws<ArgumentException>(() => new UniformGridBroadPhase2D(default, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniformGridBroadPhase2D(Coverage, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniformGridBroadPhase2D(Coverage, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniformGridBroadPhase2D(Coverage, 2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniformGridBroadPhase2D(Coverage, Fix64.Epsilon));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniformGridBroadPhase2D(Coverage, 2, 99));
            Assert.That(_grid.CellCount, Is.EqualTo(100));
        }

        [Test]
        public void EmptyIndexAndRepeatedClearAreValid()
        {
            Expect(Coverage);
            Assert.That(_grid.Remove(default), Is.False);
            _grid.Clear();
            _grid.Clear();
            Expect(Coverage);
            Assert.That(_grid.BodyCount, Is.Zero);
        }

        [Test]
        public void LargeBodySpansAllCellsAndEachQueryReturnsItOnce()
        {
            CollisionBodyId id = Add(Coverage);
            Expect(Box(-9, -9), id);
            Expect(Box(9, 9), id);
            Expect(Coverage, id);
            Assert.That(_grid.Remove(id), Is.True);
            Expect(Coverage);
        }

        [TestCase(-3, -1)]
        [TestCase(1, 3)]
        public void TangentBoundsOnInternalCellBoundariesAreIncluded(int a, int b)
        {
            CollisionBodyId first = Add(Box(a, a));
            CollisionBodyId second = Add(Box(b, b));
            Expect(Box(a, a), first, second);
            Expect(Box(b, b), first, second);
        }

        [Test]
        public void NegativeCoordinatesAndNonDivisibleCoverageUseCorrectLastCells()
        {
            _grid = new UniformGridBroadPhase2D(Box(-3, -4, 5, 3), 3);
            Assert.That(_grid.Columns, Is.EqualTo(4));
            Assert.That(_grid.Rows, Is.EqualTo(2));
            CollisionBodyId left = Add(Box(-7, -6));
            CollisionBodyId right = Add(Box(1, -2));
            Expect(Box(-7, -6), left);
            Expect(Box(1, -2), right);
            Expect(Box(-3, -4, 5, 3), left, right);
        }

        [Test]
        public void QueryCanExtendOutsideCoverageAndTouchItsMaximumEdge()
        {
            CollisionBodyId right = Add(Box(9, 0));
            Expect(Box(11, 0), right);
            Expect(Box(13, 0));
            Expect(Box(0, 0, 20, 20), right);
            Expect(Box(-11, 0));
        }

        [Test]
        public void QueryAppendsWithoutClearingSortingOrDeduplicatingExistingItems()
        {
            CollisionBodyId a = Add(Box(0, 0));
            CollisionBodyId b = Add(Box(0, 0));
            _results.Add(b);
            _results.Add(a);
            _grid.Query(Coverage, _results);
            Assert.That(_results[0], Is.EqualTo(b));
            Assert.That(_results[1], Is.EqualTo(a));
            Assert.That(_results.GetRange(2, 2), Is.EquivalentTo(new[] { a, b }));
        }

        [Test]
        public void UpdateMovesGrowsShrinksAndFiltersOldPositionEvenInsideSameCell()
        {
            CollisionBodyId id = Add(Box(-6, -6));
            _grid.Update(id, Box(6, 6));
            Expect(Box(-6, -6));
            Expect(Box(6, 6), id);
            _grid.Update(id, Coverage);
            Expect(Box(-6, -6), id);
            _grid.Update(id, Box(6, 6));
            Expect(Box(-6, -6));
            Aabb2D small = new Aabb2D(new FixVector2(Fix64.FromRatio(1, 4), Fix64.FromRatio(1, 4)),
                new FixVector2(Fix64.FromRatio(1, 10), Fix64.FromRatio(1, 10)));
            _grid.Update(id, small);
            Expect(Box(6, 6));
            Expect(small, id);
            _grid.Update(id, new Aabb2D(new FixVector2(Fix64.FromRatio(7, 4), Fix64.FromRatio(7, 4)), small.HalfExtents));
            Expect(small);
        }

        [Test]
        public void InvalidInsertAndDuplicateDoNotChangeExistingRegistration()
        {
            CollisionBodyId a = Add(Box(0, 0));
            CollisionBodyId b = _identities.RegisterBody();
            Assert.Throws<ArgumentException>(() => _grid.Insert(default, Box(0, 0)));
            Assert.Throws<ArgumentException>(() => _grid.Insert(a, Box(6, 6)));
            Assert.Throws<ArgumentException>(() => _grid.Insert(b, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => _grid.Insert(b, Box(10, 0)));
            Assert.That(_grid.BodyCount, Is.EqualTo(1));
            Expect(Box(0, 0), a);
            Expect(Box(6, 6));
            _grid.Insert(b, Box(6, 6));
            Expect(Box(6, 6), b);
        }

        [Test]
        public void InvalidUpdateKeepsOldBoundsAndDoesNotImplicitlyInsert()
        {
            CollisionBodyId a = Add(Box(0, 0));
            CollisionBodyId missing = _identities.RegisterBody();
            Assert.Throws<KeyNotFoundException>(() => _grid.Update(missing, Box(0, 0)));
            Assert.Throws<ArgumentException>(() => _grid.Update(default, Box(0, 0)));
            Assert.Throws<ArgumentException>(() => _grid.Update(a, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => _grid.Update(a, Box(10, 0)));
            Assert.That(_grid.BodyCount, Is.EqualTo(1));
            Expect(Box(0, 0), a);
            Expect(Box(9, 0));
        }

        [Test]
        public void InvalidQueryPreservesCallerListAndIndex()
        {
            CollisionBodyId id = Add(Box(0, 0));
            _results.Add(id);
            Assert.Throws<ArgumentException>(() => _grid.Query(default, _results));
            Assert.Throws<ArgumentNullException>(() => _grid.Query(Coverage, null));
            Assert.That(_results, Is.EqualTo(new[] { id }));
            Expect(Coverage, id);
        }

        [Test]
        public void RemoveAndClearLeaveNoGhostsAndAllowReuse()
        {
            CollisionBodyId a = Add(Coverage);
            CollisionBodyId b = Add(Box(0, 0));
            Assert.That(_grid.Remove(a), Is.True);
            Assert.That(_grid.Remove(a), Is.False);
            Expect(Coverage, b);
            _grid.Insert(a, Box(8, 8));
            _grid.Clear();
            Expect(Coverage);
            Assert.That(_grid.BodyCount, Is.Zero);
            _grid.Insert(a, Box(-8, -8));
            Expect(Coverage, a);
            Expect(Box(8, 8));
        }

        [Test]
        public void SameNumericIdFromDifferentWorldsIsNotDeduplicated()
        {
            CollisionBodyId a = Add(Box(0, 0));
            CollisionBodyId b = new CollisionWorld(Coverage, new StubBroadPhase2D()).RegisterBody();
            Assert.That(a.Value, Is.EqualTo(b.Value));
            _grid.Insert(b, Box(0, 0));
            Expect(Coverage, a, b);
        }

        [Test]
        public void QueryGenerationWrapDoesNotLosePreviouslyVisitedBodies()
        {
            CollisionBodyId a = Add(Coverage);
            Expect(Coverage, a);
            typeof(UniformGridBroadPhase2D).GetField("_queryGeneration", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_grid, uint.MaxValue);
            Expect(Coverage, a);
            Expect(Coverage, a);
        }

        [Test]
        public void RawUnitCellsPreserveTangencyAndOneRawUnitSeparation()
        {
            Fix64 half = Fix64.FromRaw(3);
            _grid = new UniformGridBroadPhase2D(new Aabb2D(FixVector2.Zero, new FixVector2(half, half)), Fix64.Epsilon);
            Aabb2D left = new Aabb2D(new FixVector2(Fix64.FromRaw(-1), 0), new FixVector2(Fix64.Epsilon, Fix64.Epsilon));
            CollisionBodyId a = Add(left);
            Aabb2D touching = new Aabb2D(new FixVector2(Fix64.Epsilon, 0), left.HalfExtents);
            Expect(touching, a);
            Expect(new Aabb2D(new FixVector2(Fix64.FromRaw(2), 0), left.HalfExtents));
        }

        [Test]
        public void WideRawCoverageDoesNotOverflowSignedDistance()
        {
            Aabb2D wide = new Aabb2D(new FixVector2(Fix64.FromRaw(-1), 0), new FixVector2(Fix64.MaxValue, 1));
            _grid = new UniformGridBroadPhase2D(wide, Fix64.MaxValue);
            Assert.That(_grid.Columns, Is.EqualTo(2));
            Aabb2D left = new Aabb2D(new FixVector2(Fix64.FromRaw(long.MinValue + 2), 0), new FixVector2(Fix64.Epsilon, Fix64.Epsilon));
            Aabb2D right = new Aabb2D(new FixVector2(Fix64.FromRaw(long.MaxValue - 2), 0), left.HalfExtents);
            CollisionBodyId a = Add(left);
            CollisionBodyId b = Add(right);
            Expect(left, a);
            Expect(right, b);
            Expect(wide, a, b);
        }

        [Test]
        public void RandomMutationQueriesMatchIndependentClosedIntervalReference()
        {
            Random random = new Random(417);
            Dictionary<CollisionBodyId, Aabb2D> reference = new Dictionary<CollisionBodyId, Aabb2D>();
            CollisionBodyId[] ids = new CollisionBodyId[24];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = _identities.RegisterBody();
            }

            for (int step = 0; step < 240; step++)
            {
                CollisionBodyId id = ids[random.Next(ids.Length)];
                if (step % 7 == 0)
                {
                    Assert.That(_grid.Remove(id), Is.EqualTo(reference.Remove(id)));
                }
                else
                {
                    Aabb2D bounds = Box(random.Next(-6, 7), random.Next(-6, 7), random.Next(1, 4), random.Next(1, 4));
                    if (reference.ContainsKey(id))
                    {
                        _grid.Update(id, bounds);
                    }
                    else
                    {
                        _grid.Insert(id, bounds);
                    }

                    reference[id] = bounds;
                }

                Aabb2D query = Box(random.Next(-12, 13), random.Next(-12, 13), random.Next(1, 5), random.Next(1, 5));
                List<CollisionBodyId> expected = new List<CollisionBodyId>();
                foreach (KeyValuePair<CollisionBodyId, Aabb2D> entry in reference)
                {
                    if (ClosedOverlap(entry.Value, query))
                    {
                        expected.Add(entry.Key);
                    }
                }

                Expect(query, expected.ToArray());
            }
        }

        private static bool ClosedOverlap(Aabb2D a, Aabb2D b)
        {
            return a.Min.X.RawValue <= b.Max.X.RawValue && b.Min.X.RawValue <= a.Max.X.RawValue
                && a.Min.Y.RawValue <= b.Max.Y.RawValue && b.Min.Y.RawValue <= a.Max.Y.RawValue;
        }

        [Test]
        public void RealWorldUsesGridForPairsPauseResumeAndRemoval()
        {
            CollisionWorld world = new CollisionWorld(Coverage, _grid);
            CollisionBodyId a = world.RegisterBody();
            CollisionBodyId b = world.RegisterBody();
            CompoundShape2D geometry = new CompoundShape2D();
            geometry.SetShapes(new[] { WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1)) });
            Assert.That(world.TrySubmitGeometry(a, geometry, LogicalHeight.Level1, out _), Is.True);
            Assert.That(world.TrySubmitGeometry(b, geometry, LogicalHeight.Level3, out _), Is.True);
            List<CollisionPair> pairs = new List<CollisionPair>();
            Assert.That(world.Step(pairs, out string error), Is.True, error);
            Assert.That(pairs, Is.EqualTo(new[] { new CollisionPair(a, b) }));
            world.SuspendBody(b);
            Assert.That(world.Step(pairs, out error), Is.True, error);
            Assert.That(pairs, Is.Empty);
            Assert.That(world.TrySubmitGeometry(b, geometry, LogicalHeight.Level3, out _), Is.True);
            Assert.That(world.Step(pairs, out error), Is.True, error);
            Assert.That(pairs.Count, Is.EqualTo(1));
            world.UnregisterBody(a);
            Assert.That(world.Step(pairs, out error), Is.True, error);
            Assert.That(pairs, Is.Empty);
            Assert.That(_grid.BodyCount, Is.EqualTo(1));
        }
    }
}
