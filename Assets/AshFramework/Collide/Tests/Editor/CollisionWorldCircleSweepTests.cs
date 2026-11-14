using System.Collections.Generic;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionWorldCircleSweepTests
    {
        private StubBroadPhase2D _index;
        private CollisionWorld _world;
        private List<CircleSweepHit2D> _hits;

        [SetUp]
        public void SetUp()
        {
            _index = new StubBroadPhase2D();
            _world = new CollisionWorld(new Aabb2D(FixVector2.Zero, V(100, 100)), _index);
            _hits = new List<CircleSweepHit2D>();
        }

        [Test]
        public void StopsRadiusOneCircleAtWallLeftSurface()
        {
            CollisionBodyId wall = Add(WorldShape2D.FromAabb(new Aabb2D(V(3, 0), V(1, 4))));

            Assert.That(_world.TrySweepCircle(Circle(0, 0, 1), V(5, 0), _hits,
                out CircleSweepStatus status, out string error), Is.True, error);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(_hits.Count, Is.EqualTo(1));
            Assert.That(_hits[0].Body, Is.EqualTo(wall));
            AssertRawNear(_hits[0].Center.X, Fix64.One, 1);
        }

        [Test]
        public void SweptBoundsCrossGridCellsAndFindThinRotatedWalls()
        {
            Aabb2D bounds = new Aabb2D(FixVector2.Zero, V(100, 100));
            CollisionWorld world = new CollisionWorld(bounds, new UniformGridBroadPhase2D(bounds, 2));
            CollisionBodyId thin = Add(world,
                WorldShape2D.FromAabb(new Aabb2D(V(20, 0), new FixVector2(Fix64.Epsilon, 5))));
            Add(world, WorldShape2D.FromObb(new Obb2D(V(40, 0), V(2, 1), FixMath.Pi / 4)));

            Assert.That(world.TrySweepCircle(Circle(0, 0, 1), V(50, 0), _hits,
                out CircleSweepStatus status, out string error), Is.True, error);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(_hits.Count, Is.EqualTo(1));
            Assert.That(_hits[0].Body, Is.EqualTo(thin));
            Assert.That(_hits[0].Center.X, Is.LessThanOrEqualTo(Fix64.FromInt(19) + Fix64.Epsilon));
        }

        [Test]
        public void ReturnsAllEarliestBodiesAndShapesInStableOrder()
        {
            CollisionBodyId first = Add(
                WorldShape2D.FromAabb(new Aabb2D(V(5, 1), V(1, 1))),
                WorldShape2D.FromAabb(new Aabb2D(V(5, -1), V(1, 1))));
            CollisionBodyId second = Add(WorldShape2D.FromAabb(new Aabb2D(V(5, 0), V(1, 4))));
            _index.Candidates.Reverse();

            Assert.That(_world.TrySweepCircle(Circle(0, 0, 1), V(10, 0), _hits,
                out CircleSweepStatus status, out string error), Is.True, error);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(_hits.Count, Is.EqualTo(3));
            Assert.That(_hits[0].Body, Is.EqualTo(first));
            Assert.That(_hits[0].ShapeIndex, Is.EqualTo(0));
            Assert.That(_hits[1].Body, Is.EqualTo(first));
            Assert.That(_hits[1].ShapeIndex, Is.EqualTo(1));
            Assert.That(_hits[2].Body, Is.EqualTo(second));
        }

        [Test]
        public void InitialOverlapOverridesEarlierCandidateHits()
        {
            Add(WorldShape2D.FromAabb(new Aabb2D(V(5, 0), V(1, 4))));
            Add(WorldShape2D.FromCircle(Circle(0, 0, 2)));

            Assert.That(_world.TrySweepCircle(Circle(0, 0, 1), V(10, 0), _hits,
                out CircleSweepStatus status, out string error), Is.True, error);

            Assert.That(status, Is.EqualTo(CircleSweepStatus.InitialOverlap));
            Assert.That(_hits, Is.Empty);
        }

        [Test]
        public void EndpointTangencyAndZeroMovementHaveStableStatuses()
        {
            Add(WorldShape2D.FromCircle(Circle(5, 0, 1)));

            Assert.That(_world.TrySweepCircle(Circle(0, 0, 1), V(3, 0), _hits,
                out CircleSweepStatus status, out string error), Is.True, error);
            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
            Assert.That(_hits[0].Time, Is.EqualTo(Fix64.One));

            Assert.That(_world.TrySweepCircle(Circle(0, 10, 1), FixVector2.Zero, _hits,
                out status, out error), Is.True, error);
            Assert.That(status, Is.EqualTo(CircleSweepStatus.NoHit));
            Assert.That(_hits, Is.Empty);
        }

        [Test]
        public void QueryFailureClearsOutputAndNextSweepRebuildsIndex()
        {
            CollisionBodyId wall = Add(WorldShape2D.FromAabb(new Aabb2D(V(3, 0), V(1, 4))));
            _hits.Add(new CircleSweepHit2D(wall, 0, Fix64.Zero, V(0, 0), V(-1, 0)));
            _index.FailOperation = "Query";

            Assert.That(_world.TrySweepCircle(Circle(0, 0, 1), V(5, 0), _hits,
                out CircleSweepStatus status, out string error), Is.False);
            Assert.That(error, Does.Contain("Query"));
            Assert.That(status, Is.EqualTo(CircleSweepStatus.NoHit));
            Assert.That(_hits, Is.Empty);
            _index.Calls.Clear();

            Assert.That(_world.TrySweepCircle(Circle(0, 0, 1), V(5, 0), _hits,
                out status, out error), Is.True, error);
            Assert.That(_index.Calls[0], Is.EqualTo("Clear"));
            Assert.That(status, Is.EqualTo(CircleSweepStatus.Hit));
        }

        [Test]
        public void InvalidCandidateFailsWithoutPublishingPartialHits()
        {
            Add(WorldShape2D.FromAabb(new Aabb2D(V(3, 0), V(1, 4))));
            CollisionWorld other = new CollisionWorld(
                new Aabb2D(FixVector2.Zero, V(10, 10)), new StubBroadPhase2D());
            _index.InvalidCandidates.Add(other.RegisterBody());

            Assert.That(_world.TrySweepCircle(Circle(0, 0, 1), V(5, 0), _hits,
                out CircleSweepStatus status, out string error), Is.False);

            Assert.That(error, Does.Contain("unknown or inactive"));
            Assert.That(status, Is.EqualTo(CircleSweepStatus.NoHit));
            Assert.That(_hits, Is.Empty);
        }

        private CollisionBodyId Add(params WorldShape2D[] shapes)
        {
            CollisionBodyId id = Add(_world, shapes);
            _index.Candidates.Add(id);
            return id;
        }

        private static CollisionBodyId Add(CollisionWorld world, params WorldShape2D[] shapes)
        {
            CollisionBodyId id = world.RegisterBody();
            CompoundShape2D geometry = new CompoundShape2D();
            geometry.SetShapes(shapes);
            Assert.That(world.TrySubmitGeometry(id, geometry, LogicalHeight.Level1,
                out string error), Is.True, error);
            return id;
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
