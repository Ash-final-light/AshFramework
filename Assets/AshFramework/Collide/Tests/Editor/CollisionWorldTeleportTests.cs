using System.Collections.Generic;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionWorldTeleportTests
    {
        private StubBroadPhase2D _index;
        private CollisionWorld _world;
        private List<CollisionContact> _contacts;
        private List<CollisionPair> _pairs;

        [SetUp]
        public void SetUp()
        {
            _index = new StubBroadPhase2D();
            _world = new CollisionWorld(new Aabb2D(FixVector2.Zero, new FixVector2(10, 10)), _index);
            _contacts = new List<CollisionContact>();
            _pairs = new List<CollisionPair>();
        }

        [Test]
        public void SuccessfulTeleportSynchronizesProxyAndReturnsLandingContacts()
        {
            CollisionBodyId moving = AddCircle(0, 1);
            CollisionBodyId wall = AddCircle(5, 1);
            _index.Candidates.Add(wall);
            Step();

            Assert.That(_world.TryTeleportTo(moving, Circle(4, 1), _contacts, out string error), Is.True, error);

            Assert.That(_world.TryGetBodyBounds(moving, out Aabb2D bounds), Is.True);
            Assert.That(bounds.Center, Is.EqualTo(new FixVector2(4, 0)));
            Assert.That(_index.Entries[moving].Center, Is.EqualTo(new FixVector2(4, 0)));
            Assert.That(_contacts.Count, Is.EqualTo(1));
            Assert.That(_contacts[0].Pair, Is.EqualTo(new CollisionPair(moving, wall)));
        }

        [Test]
        public void TeleportOfHigherIdKeepsContactShapeIndicesInCanonicalPairOrder()
        {
            CollisionBodyId wall = AddCompound(5, 7);
            CollisionBodyId moving = AddCircle(0, 1);
            _index.Candidates.Add(wall);
            Step();

            Assert.That(_world.TryTeleportTo(moving, Circle(6, 1), _contacts, out string error), Is.True, error);

            Assert.That(_contacts.Count, Is.EqualTo(2));
            Assert.That(_contacts[0].Pair.A, Is.EqualTo(wall));
            Assert.That(_contacts[0].Shapes.ShapeIndexA, Is.EqualTo(0));
            Assert.That(_contacts[0].Shapes.ShapeIndexB, Is.EqualTo(0));
            Assert.That(_contacts[1].Shapes.ShapeIndexA, Is.EqualTo(1));
            Assert.That(_contacts[1].Shapes.ShapeIndexB, Is.EqualTo(0));
        }

        [Test]
        public void InvalidTargetPreservesGeometryAndProxy()
        {
            CollisionBodyId moving = AddCircle(0, 1);
            Step();
            _contacts.Add(default);

            Assert.That(_world.TryTeleportTo(moving, Circle(10, 1), _contacts, out string error), Is.False);

            Assert.That(error, Does.Contain("coverage"));
            Assert.That(_contacts, Is.Empty);
            AssertOriginalPosition(moving);
        }

        [TestCase("Update")]
        [TestCase("Query")]
        public void IndexFailureRollsBackAuthorityAndNextStepRebuilds(string operation)
        {
            CollisionBodyId moving = AddCircle(0, 1);
            _index.Candidates.Add(moving);
            Step();
            _index.FailOperation = operation;
            _contacts.Add(default);

            Assert.That(_world.TryTeleportTo(moving, Circle(4, 1), _contacts, out string error), Is.False);

            Assert.That(error, Does.Contain(operation));
            Assert.That(_contacts, Is.Empty);
            AssertOriginalPosition(moving);
            _index.Calls.Clear();
            Assert.That(_world.Step(_pairs, out error), Is.True, error);
            Assert.That(_index.Calls[0], Is.EqualTo("Clear"));
            Assert.That(_index.Entries[moving].Center, Is.EqualTo(FixVector2.Zero));
        }

        private CollisionBodyId AddCircle(int x, int radius)
        {
            CollisionBodyId id = _world.RegisterBody();
            Assert.That(_world.TrySubmitGeometry(id, Circle(x, radius), LogicalHeight.Level1, out string error), Is.True, error);
            return id;
        }

        private CollisionBodyId AddCompound(int firstX, int secondX)
        {
            CollisionBodyId id = _world.RegisterBody();
            CompoundShape2D geometry = new CompoundShape2D();
            geometry.SetShapes(new[]
            {
                WorldShape2D.FromCircle(new Circle2D(new FixVector2(firstX, 0), 1)),
                WorldShape2D.FromCircle(new Circle2D(new FixVector2(secondX, 0), 1))
            });
            Assert.That(_world.TrySubmitGeometry(id, geometry, LogicalHeight.Level1, out string error), Is.True, error);
            return id;
        }

        private static CompoundShape2D Circle(int x, int radius)
        {
            CompoundShape2D geometry = new CompoundShape2D();
            geometry.SetShapes(new[]
            {
                WorldShape2D.FromCircle(new Circle2D(new FixVector2(x, 0), radius))
            });
            return geometry;
        }

        private void Step()
        {
            Assert.That(_world.Step(_pairs, out string error), Is.True, error);
        }

        private void AssertOriginalPosition(CollisionBodyId moving)
        {
            Assert.That(_world.TryGetBodyBounds(moving, out Aabb2D bounds), Is.True);
            Assert.That(bounds.Center, Is.EqualTo(FixVector2.Zero));
            Assert.That(_index.Entries[moving].Center, Is.EqualTo(FixVector2.Zero));
        }
    }
}
