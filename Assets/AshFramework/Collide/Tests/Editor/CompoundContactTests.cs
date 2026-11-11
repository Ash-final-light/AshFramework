using System;
using System.Collections.Generic;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CompoundContactTests
    {
        private readonly List<CompoundContact2D> _shapes = new List<CompoundContact2D>();
        private readonly List<CollisionPair> _pairs = new List<CollisionPair>();
        private readonly List<CollisionContact> _contacts = new List<CollisionContact>();

        [Test]
        public void CollectsBothWallShapesAndPreservesArgumentOrientation()
        {
            CompoundShape2D circle = Circle(Fix64.One + Fix64.Half);
            CompoundShape2D wall = Wall();

            CompoundCollision2D.CollectContacts(circle, wall, _shapes);
            Assert.That(_shapes.Count, Is.EqualTo(2));
            AssertShapeContact(_shapes[0], 0, 0, FixVector2.Right, Fix64.Half);
            AssertShapeContact(_shapes[1], 0, 1, FixVector2.Right, Fix64.Half);

            CompoundCollision2D.CollectContacts(wall, circle, _shapes);
            Assert.That(_shapes.Count, Is.EqualTo(2));
            AssertShapeContact(_shapes[0], 0, 0, -FixVector2.Right, Fix64.Half);
            AssertShapeContact(_shapes[1], 1, 0, -FixVector2.Right, Fix64.Half);
        }

        [Test]
        public void CompoundBoundsAloneDoNotCreateContactsAndTangencyStillCounts()
        {
            CompoundShape2D wall = Wall();
            CompoundCollision2D.CollectContacts(Circle(Fix64.One), wall, _shapes);
            Assert.That(_shapes.Count, Is.EqualTo(2));
            AssertShapeContact(_shapes[0], 0, 0, FixVector2.Right, Fix64.Zero);
            AssertShapeContact(_shapes[1], 0, 1, FixVector2.Right, Fix64.Zero);

            CompoundShape2D corner = Compound(WorldShape2D.FromCircle(new Circle2D(new FixVector2(1, 3), 1)));
            Assert.That(corner.TryGetBounds(out Aabb2D circleBounds), Is.True);
            Assert.That(wall.TryGetBounds(out Aabb2D wallBounds), Is.True);
            Assert.That(Collision2D.Overlaps(circleBounds, wallBounds), Is.True);
            CompoundCollision2D.CollectContacts(corner, wall, _shapes);
            Assert.That(_shapes, Is.Empty);
        }

        [Test]
        public void WorldSortsPairsAndContactsRegardlessOfTraversalOrder()
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CollisionWorld world = World(index);
            CollisionBodyId removed = world.RegisterBody();
            CollisionBodyId a = Add(world, Circle(Fix64.One + Fix64.Half));
            CollisionBodyId b = Add(world, Wall());
            CollisionBodyId c = Add(world, Circle(Fix64.One + Fix64.Half));
            Assert.That(world.UnregisterBody(removed), Is.True);
            index.Candidates.AddRange(new[] { c, b, a });

            Assert.That(world.Step(_pairs, _contacts, out string error), Is.True, error);
            CollisionPair ab = new CollisionPair(a, b);
            CollisionPair ac = new CollisionPair(a, c);
            CollisionPair bc = new CollisionPair(b, c);
            Assert.That(_pairs, Is.EqualTo(new[] { ab, ac, bc }));
            Assert.That(_contacts.Count, Is.EqualTo(5));
            Assert.That(_contacts[0].Pair, Is.EqualTo(ab));
            Assert.That(_contacts[1].Pair, Is.EqualTo(ab));
            Assert.That(_contacts[2].Pair, Is.EqualTo(ac));
            Assert.That(_contacts[3].Pair, Is.EqualTo(bc));
            Assert.That(_contacts[4].Pair, Is.EqualTo(bc));
            AssertShapeContact(_contacts[0].Shapes, 0, 0, FixVector2.Right, Fix64.Half);
            AssertShapeContact(_contacts[1].Shapes, 0, 1, FixVector2.Right, Fix64.Half);
            AssertShapeContact(_contacts[3].Shapes, 0, 0, -FixVector2.Right, Fix64.Half);
            AssertShapeContact(_contacts[4].Shapes, 1, 0, -FixVector2.Right, Fix64.Half);
        }

        [Test]
        public void QueryFailureClearsBothOutputsAndNextStepRecovers()
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CollisionWorld world = World(index);
            CollisionBodyId a = Add(world, Circle(Fix64.One + Fix64.Half));
            CollisionBodyId b = Add(world, Wall());
            index.Candidates.AddRange(new[] { a, b });
            Assert.That(world.Step(_pairs, _contacts, out string error), Is.True, error);
            Assert.That(_contacts.Count, Is.EqualTo(2));

            index.FailOperation = "Query";
            index.FailureDelay = 1;
            Assert.That(world.Step(_pairs, _contacts, out error), Is.False);
            Assert.That(error, Does.Contain("Injected Query"));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);

            Assert.That(world.Step(_pairs, _contacts, out error), Is.True, error);
            Assert.That(index.Calls, Does.Contain("Clear"));
            Assert.That(_pairs.Count, Is.EqualTo(1));
            Assert.That(_contacts.Count, Is.EqualTo(2));
            world.SuspendBody(b);
            Assert.That(world.Step(_pairs, _contacts, out error), Is.True, error);
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
        }

        [Test]
        public void DepthOverflowDiscardsPartialContactsAndPairOnlyStepStillWorks()
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            Aabb2D bounds = new Aabb2D(FixVector2.Zero, new FixVector2(1600000000, 1600000000));
            CollisionWorld world = new CollisionWorld(bounds, index);
            WorldShape2D small = WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1));
            WorldShape2D large = WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, 1500000000));
            CompoundShape2D a = Compound(small, large);
            CompoundShape2D b = Compound(large);
            index.Candidates.Add(Add(world, a));
            index.Candidates.Add(Add(world, b));

            Assert.Throws<OverflowException>(() => CompoundCollision2D.CollectContacts(a, b, _shapes));
            Assert.That(_shapes, Is.Empty);
            Assert.That(world.Step(_pairs, _contacts, out string error), Is.False);
            Assert.That(error, Does.Contain("Contact depth"));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
            Assert.That(world.Step(_pairs, out error), Is.True, error);
            Assert.That(_pairs.Count, Is.EqualTo(1));
        }

        private static CollisionWorld World(IBroadPhase2D index)
        {
            return new CollisionWorld(new Aabb2D(FixVector2.Zero, new FixVector2(10, 10)), index);
        }

        private static CollisionBodyId Add(CollisionWorld world, CompoundShape2D compound)
        {
            CollisionBodyId id = world.RegisterBody();
            Assert.That(world.TrySubmitGeometry(id, compound, LogicalHeight.Level1, out string error), Is.True, error);
            return id;
        }

        private static CompoundShape2D Circle(Fix64 x)
        {
            return Compound(WorldShape2D.FromCircle(new Circle2D(new FixVector2(x, 0), 1)));
        }

        private static CompoundShape2D Wall()
        {
            return Compound(WorldShape2D.FromAabb(new Aabb2D(new FixVector2(3, 1), FixVector2.One)),
                WorldShape2D.FromAabb(new Aabb2D(new FixVector2(3, -1), FixVector2.One)));
        }

        private static CompoundShape2D Compound(params WorldShape2D[] shapes)
        {
            CompoundShape2D compound = new CompoundShape2D();
            compound.SetShapes(shapes);
            return compound;
        }

        private static void AssertShapeContact(CompoundContact2D actual, int a, int b, FixVector2 normal, Fix64 depth)
        {
            Assert.That(actual.ShapeIndexA, Is.EqualTo(a));
            Assert.That(actual.ShapeIndexB, Is.EqualTo(b));
            Assert.That(actual.Contact.Normal, Is.EqualTo(normal));
            Assert.That(actual.Contact.Depth, Is.EqualTo(depth));
        }
    }
}
