using System.Collections.Generic;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionWorldAreaQueryTests
    {
        private StubBroadPhase2D _index;
        private CollisionWorld _world;
        private List<CollisionBodyId> _results;

        [SetUp]
        public void SetUp()
        {
            _index = new StubBroadPhase2D();
            _world = new CollisionWorld(new Aabb2D(FixVector2.Zero, new FixVector2(20, 20)), _index);
            _results = new List<CollisionBodyId>();
        }

        [Test]
        public void CircleQueryRefinesAllShapeKindsAndIncludesTangency()
        {
            CollisionBodyId circle = Add(WorldShape2D.FromCircle(new Circle2D(new FixVector2(-2, 0), 1)));
            CollisionBodyId aabb = Add(WorldShape2D.FromAabb(new Aabb2D(new FixVector2(2, 0), FixVector2.One)));
            CollisionBodyId obb = Add(WorldShape2D.FromObb(new Obb2D(new FixVector2(0, 2), FixVector2.One, FixMath.Pi / 4)));
            Add(WorldShape2D.FromCircle(new Circle2D(new FixVector2(10, 0), 1)));

            Assert.That(_world.TryQueryCircle(new Circle2D(FixVector2.Zero, 1), _results, out string error), Is.True, error);

            Assert.That(_results, Is.EqualTo(new[] { circle, aabb, obb }));
        }

        [Test]
        public void AabbQueryCanExtendOutsideCoverageAndUseNegativeCoordinates()
        {
            CollisionBodyId inside = Add(WorldShape2D.FromAabb(
                new Aabb2D(new FixVector2(-19, -19), FixVector2.One)));
            Add(WorldShape2D.FromCircle(new Circle2D(new FixVector2(10, 10), 1)));

            Assert.That(_world.TryQueryAabb(
                new Aabb2D(new FixVector2(-20, -20), new FixVector2(2, 2)), _results, out string error), Is.True, error);

            Assert.That(_results, Is.EqualTo(new[] { inside }));
        }

        [Test]
        public void CompoundBodyAppearsOnceAndResultsIgnoreCandidateOrderAndDuplicates()
        {
            CollisionBodyId first = Add(WorldShape2D.FromCircle(new Circle2D(new FixVector2(0, -1), 1)));
            CollisionBodyId compound = Add(
                WorldShape2D.FromCircle(new Circle2D(new FixVector2(-1, 0), 1)),
                WorldShape2D.FromCircle(new Circle2D(new FixVector2(1, 0), 1)));
            _index.Candidates.Reverse();
            _index.Candidates.Add(compound);

            Assert.That(_world.TryQueryAabb(
                new Aabb2D(FixVector2.Zero, new FixVector2(2, 2)), _results, out string error), Is.True, error);

            Assert.That(_results, Is.EqualTo(new[] { first, compound }));
        }

        [Test]
        public void QuerySynchronizesLatestSubmissionBeforeLookingUpCandidates()
        {
            CollisionBodyId moving = Add(WorldShape2D.FromCircle(new Circle2D(new FixVector2(-10, 0), 1)));
            Assert.That(_world.TryQueryCircle(new Circle2D(FixVector2.Zero, 2), _results, out string error), Is.True, error);
            Assert.That(_results, Is.Empty);
            _index.Calls.Clear();
            Assert.That(_world.TrySubmitGeometry(moving,
                Geometry(WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1))),
                LogicalHeight.Level1, out error), Is.True, error);

            Assert.That(_world.TryQueryCircle(new Circle2D(FixVector2.Zero, 2), _results, out error), Is.True, error);

            Assert.That(_index.Calls[0], Is.EqualTo("Update"));
            Assert.That(_results, Is.EqualTo(new[] { moving }));
        }

        [Test]
        public void SuspendedAndRemovedBodiesLeaveNoQueryResults()
        {
            CollisionBodyId suspended = Add(WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1)));
            CollisionBodyId removed = Add(WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1)));
            Assert.That(_world.TryQueryCircle(new Circle2D(FixVector2.Zero, 2), _results, out string error), Is.True, error);
            _world.SuspendBody(suspended);
            Assert.That(_world.UnregisterBody(removed), Is.True);

            Assert.That(_world.TryQueryCircle(new Circle2D(FixVector2.Zero, 2), _results, out error), Is.True, error);

            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void QueryFailureClearsOutputAndNextQueryRebuildsIndex()
        {
            CollisionBodyId id = Add(WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1)));
            _results.Add(id);
            _index.FailOperation = "Query";

            Assert.That(_world.TryQueryCircle(new Circle2D(FixVector2.Zero, 2), _results, out string error), Is.False);
            Assert.That(error, Does.Contain("Query"));
            Assert.That(_results, Is.Empty);
            _index.Calls.Clear();

            Assert.That(_world.TryQueryCircle(new Circle2D(FixVector2.Zero, 2), _results, out error), Is.True, error);
            Assert.That(_index.Calls[0], Is.EqualTo("Clear"));
            Assert.That(_results, Is.EqualTo(new[] { id }));
        }

        [Test]
        public void InvalidCandidateFailsInsteadOfPublishingPartialResults()
        {
            Add(WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1)));
            CollisionWorld other = new CollisionWorld(
                new Aabb2D(FixVector2.Zero, new FixVector2(20, 20)), new StubBroadPhase2D());
            _index.InvalidCandidates.Add(other.RegisterBody());

            Assert.That(_world.TryQueryAabb(
                new Aabb2D(FixVector2.Zero, new FixVector2(2, 2)), _results, out string error), Is.False);

            Assert.That(error, Does.Contain("unknown or inactive"));
            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void InvalidAreaReturnsFailureAndClearsCallerList()
        {
            CollisionBodyId id = Add(WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1)));
            _results.Add(id);

            Assert.That(_world.TryQueryCircle(default, _results, out string error), Is.False);

            Assert.That(error, Is.Not.Empty);
            Assert.That(_results, Is.Empty);
        }

        private CollisionBodyId Add(params WorldShape2D[] shapes)
        {
            CollisionBodyId id = _world.RegisterBody();
            Assert.That(_world.TrySubmitGeometry(id, Geometry(shapes), LogicalHeight.Level1,
                out string error), Is.True, error);
            _index.Candidates.Add(id);
            return id;
        }

        private static CompoundShape2D Geometry(params WorldShape2D[] shapes)
        {
            CompoundShape2D geometry = new CompoundShape2D();
            geometry.SetShapes(shapes);
            return geometry;
        }
    }
}
