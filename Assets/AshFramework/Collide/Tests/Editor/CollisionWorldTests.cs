using System;
using System.Collections.Generic;
using System.Reflection;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionWorldTests
    {
        private StubBroadPhase2D _index;
        private CollisionWorld _world;
        private List<CollisionPair> _results;

        [SetUp]
        public void SetUp()
        {
            _index = new StubBroadPhase2D();
            _world = CreateWorld(_index);
            _results = new List<CollisionPair>();
        }

        private static CollisionWorld CreateWorld(IBroadPhase2D index)
        {
            return new CollisionWorld(new Aabb2D(FixVector2.Zero, new FixVector2(100, 100)), index);
        }

        private static CompoundShape2D Geometry(int x, int radius = 1)
        {
            CompoundShape2D geometry = new CompoundShape2D();
            geometry.SetShapes(new[] { WorldShape2D.FromCircle(new Circle2D(new FixVector2(x, 0), radius)) });
            return geometry;
        }

        private CollisionBodyId Add(int x, LogicalHeight height = LogicalHeight.Level1)
        {
            CollisionBodyId id = _world.RegisterBody();
            Assert.That(_world.TrySubmitGeometry(id, Geometry(x), height, out string error), Is.True, error);
            _index.Candidates.Add(id);
            return id;
        }

        private void Step()
        {
            Assert.That(_world.Step(_results, out string error), Is.True, error);
        }

        [Test]
        public void ConstructorRequiresValidBoundsAndExplicitIndex()
        {
            Assert.Throws<ArgumentException>(() => new CollisionWorld(default, _index));
            Assert.Throws<ArgumentNullException>(() => CreateWorld(null));
            Step();
            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void RegistrationStartsSuspendedAndWorldIdentitiesAreIsolated()
        {
            CollisionBodyId a = _world.RegisterBody();
            CollisionWorld other = CreateWorld(new StubBroadPhase2D());
            CollisionBodyId b = other.RegisterBody();
            Assert.That(a.Value, Is.EqualTo(b.Value));
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(default(CollisionBodyId).IsValid, Is.False);
            Assert.That(_world.ContainsBody(a), Is.True);
            Assert.That(_world.IsBodyActive(a), Is.False);
            Assert.That(_world.ContainsBody(b), Is.False);
            Assert.That(_world.UnregisterBody(b), Is.False);
            Assert.Throws<ArgumentException>(() => _world.SuspendBody(b));
            Assert.Throws<ArgumentException>(() => _world.TrySubmitGeometry(b, Geometry(0), LogicalHeight.Level1, out _));
            Step();
            Assert.That(_index.Entries, Is.Empty);
        }

        [Test]
        public void SubmittedGeometryIsIndependentOfSourceAndHeightDoesNotFilter()
        {
            CompoundShape2D source = Geometry(0);
            CollisionBodyId a = _world.RegisterBody();
            Assert.That(_world.TrySubmitGeometry(a, source, LogicalHeight.Level3, out _), Is.True);
            _index.Candidates.Add(a);
            CollisionBodyId b = Add(0);
            source.SetShapes(new[] { WorldShape2D.FromCircle(new Circle2D(new FixVector2(90, 0), 1)) });
            source.Clear();
            Step();
            Assert.That(_results, Is.EqualTo(new[] { new CollisionPair(a, b) }));
            Assert.That(_world.TryGetBodyBounds(a, out Aabb2D bounds), Is.True);
            Assert.That(bounds.Center, Is.EqualTo(FixVector2.Zero));
            Assert.That(_world.TryGetLogicalHeight(a, out LogicalHeight height), Is.True);
            Assert.That(height, Is.EqualTo(LogicalHeight.Level3));
        }

        [Test]
        public void SynchronizesAllRangesBeforeQueryAndUpdatesOnlyChangedBounds()
        {
            CollisionBodyId a = Add(0);
            CollisionBodyId b = Add(2);
            Step();
            Assert.That(_index.Calls, Is.EqualTo(new[] { "Insert", "Insert", "Query", "Query" }));
            Assert.That(_results, Has.Count.EqualTo(1), "Tangency must count as overlap.");
            _index.Calls.Clear();
            Assert.That(_world.TrySubmitGeometry(a, Geometry(10), LogicalHeight.Level1, out _), Is.True);
            Assert.That(_world.TrySubmitGeometry(b, Geometry(12), LogicalHeight.Level2, out _), Is.True);
            Step();
            Assert.That(_index.Calls, Is.EqualTo(new[] { "Update", "Update", "Query", "Query" }));
            _index.Calls.Clear();
            Step();
            Assert.That(_index.Calls, Is.EqualTo(new[] { "Query", "Query" }));
            Assert.That(_results, Has.Count.EqualTo(1));
        }

        [Test]
        public void SameBoundsCanHaveDifferentNarrowPhaseGeometry()
        {
            CompoundShape2D split = new CompoundShape2D();
            split.SetShapes(new[] { Geometry(-5)[0], Geometry(5)[0] });
            CollisionBodyId a = _world.RegisterBody();
            _index.Candidates.Add(a);
            Assert.That(_world.TrySubmitGeometry(a, split, LogicalHeight.Level1, out _), Is.True);
            Add(0);
            Step();
            Assert.That(_results, Is.Empty);
            split.TryGetBounds(out Aabb2D bounds);
            split.SetShapes(new[] { WorldShape2D.FromAabb(bounds) });
            _index.Calls.Clear();
            Assert.That(_world.TrySubmitGeometry(a, split, LogicalHeight.Level1, out _), Is.True);
            Step();
            Assert.That(_index.Calls, Does.Not.Contain("Update"));
            Assert.That(_results, Has.Count.EqualTo(1));
        }

        [Test]
        public void CandidateDirectionAndOrderDoNotDuplicateResults()
        {
            CollisionBodyId a = Add(0);
            CollisionBodyId b = Add(0);
            CollisionBodyId c = Add(0);
            Add(50);
            _index.Candidates.Reverse();
            Step();
            Assert.That(_results, Is.EqualTo(new[]
            {
                new CollisionPair(a, b), new CollisionPair(a, c), new CollisionPair(b, c)
            }));
        }

        [Test]
        public void SuspensionRemovesProxyAndResumesSameIdentity()
        {
            CollisionBodyId a = Add(0);
            Add(0);
            Step();
            _world.SuspendBody(a);
            Assert.That(_world.ContainsBody(a), Is.True);
            Assert.That(_world.TryGetBodyBounds(a, out _), Is.False);
            _index.Calls.Clear();
            Step();
            Assert.That(_index.Entries.ContainsKey(a), Is.False);
            Assert.That(_index.Calls[0], Is.EqualTo("Remove"));
            Assert.That(_results, Is.Empty);
            Assert.That(_world.TrySubmitGeometry(a, Geometry(0), LogicalHeight.Level1, out _), Is.True);
            Step();
            Assert.That(_results, Has.Count.EqualTo(1));
            Assert.That(_world.BodyCount, Is.EqualTo(2));
        }

        [TestCase("null")]
        [TestCase("empty")]
        [TestCase("outside")]
        [TestCase("height")]
        public void BadSubmissionSuspendsOnlyThatBodyAndReportsReason(string kind)
        {
            CollisionBodyId a = Add(0);
            Add(0);
            Add(0);
            Step();
            CompoundShape2D source = kind == "null" ? null : kind == "empty" ? new CompoundShape2D() : Geometry(kind == "outside" ? 100 : 0);
            LogicalHeight height = kind == "height" ? (LogicalHeight)4 : LogicalHeight.Level1;
            Assert.That(_world.TrySubmitGeometry(a, source, height, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(_world.GetBodyError(a), Is.EqualTo(error));
            Step();
            Assert.That(_results, Has.Count.EqualTo(1));
            Assert.That(_index.Entries.ContainsKey(a), Is.False);
            Assert.That(_world.TrySubmitGeometry(a, Geometry(99), LogicalHeight.Level2, out _), Is.True);
            Step();
            Assert.That(_world.GetBodyError(a), Is.Empty);
        }

        [Test]
        public void RemovalInvalidatesIdImmediatelyAndReusesSlotOnlyAfterCleanup()
        {
            CollisionBodyId a = Add(0);
            Step();
            FieldInfo field = typeof(CollisionWorld).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic);
            Array slots = (Array)field.GetValue(_world);
            object first = slots.GetValue(0);
            Assert.That(_world.UnregisterBody(a), Is.True);
            Assert.That(_world.UnregisterBody(a), Is.False);
            Assert.That(_world.ContainsBody(a), Is.False);
            CollisionBodyId b = Add(0);
            Assert.That(slots.GetValue(1), Is.Not.SameAs(first));
            Step();
            CollisionBodyId c = Add(0);
            Assert.That(slots.GetValue(0), Is.SameAs(first));
            Assert.That(c, Is.Not.EqualTo(a));
            Assert.That(c.Value, Is.GreaterThan(b.Value));
            Assert.Throws<ArgumentException>(() => _world.SuspendBody(a));
            Step();
            Assert.That(_results, Is.EqualTo(new[] { new CollisionPair(b, c) }));
        }

        [TestCase("Insert")]
        [TestCase("Update")]
        [TestCase("Remove")]
        [TestCase("Query")]
        public void IndexFailurePublishesNothingAndNextStepRebuilds(string operation)
        {
            CollisionBodyId a = Add(0);
            Add(0);
            Step();
            if (operation == "Insert")
            {
                Add(0);
            }
            else if (operation == "Update")
            {
                Assert.That(_world.TrySubmitGeometry(a, Geometry(1), LogicalHeight.Level1, out _), Is.True);
            }
            else if (operation == "Remove")
            {
                _world.UnregisterBody(a);
            }

            _index.FailOperation = operation;
            Assert.That(_world.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain(operation));
            Assert.That(_results, Is.Empty);
            _index.Calls.Clear();
            _index.FailOperation = "Clear";
            Assert.That(_world.Step(_results, out error), Is.False);
            Assert.That(_index.Calls, Is.EqualTo(new[] { "Clear" }));
            Assert.That(_results, Is.Empty);
            _index.Calls.Clear();
            Step();
            Assert.That(_index.Calls[0], Is.EqualTo("Clear"));
            Assert.That(_results.Count, Is.EqualTo(operation == "Remove" ? 0 : operation == "Insert" ? 3 : 1));
        }

        [TestCase("register")]
        [TestCase("submit")]
        [TestCase("suspend")]
        [TestCase("remove")]
        [TestCase("step")]
        public void MutationAndReentrantStepFromIndexAreRejected(string operation)
        {
            CollisionBodyId a = Add(0);
            _index.QueryCallback = () =>
            {
                switch (operation)
                {
                    case "register":
                        _world.RegisterBody();
                        break;
                    case "submit":
                        _world.TrySubmitGeometry(a, Geometry(0), LogicalHeight.Level1, out _);
                        break;
                    case "suspend":
                        _world.SuspendBody(a);
                        break;
                    case "remove":
                        _world.UnregisterBody(a);
                        break;
                    default:
                        _world.Step(_results, out _);
                        break;
                }
            };
            Assert.That(_world.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain("forbidden"));
            Assert.That(_world.BodyCount, Is.EqualTo(1));
            _index.QueryCallback = null;
            Step();
        }

        [Test]
        public void InvalidIndexCandidatesFailInsteadOfBeingSilentlySkipped()
        {
            Add(0);
            _index.InvalidCandidates.Add(CreateWorld(new StubBroadPhase2D()).RegisterBody());
            Assert.That(_world.Step(_results, out _), Is.False);
            Assert.That(_results, Is.Empty);
            _index.InvalidCandidates.Clear();
            Step();
        }

        [Test]
        public void GrowthAndSwapRemovalPreserveRemainingRecords()
        {
            List<CollisionBodyId> ids = new List<CollisionBodyId>();
            for (int i = 0; i < 40; i++)
            {
                ids.Add(Add(i * 2));
            }

            Step();
            Assert.That(_results.Count, Is.EqualTo(39));
            for (int i = 0; i < 40; i += 2)
            {
                _world.UnregisterBody(ids[i]);
            }

            Step();
            Assert.That(_world.BodyCount, Is.EqualTo(20));
            Assert.That(_index.Entries.Count, Is.EqualTo(20));
            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void PairIsCanonicalAndRejectsInvalidSelfAndForeignIds()
        {
            CollisionBodyId a = _world.RegisterBody();
            CollisionBodyId b = _world.RegisterBody();
            Assert.That(new CollisionPair(a, b), Is.EqualTo(new CollisionPair(b, a)));
            Assert.That(new CollisionPair(a, b).GetHashCode(), Is.EqualTo(new CollisionPair(b, a).GetHashCode()));
            Assert.Throws<ArgumentException>(() => new CollisionPair(a, a));
            Assert.Throws<ArgumentException>(() => new CollisionPair(default, a));
            Assert.Throws<ArgumentException>(() => new CollisionPair(a, CreateWorld(new StubBroadPhase2D()).RegisterBody()));
        }

        [Test]
        public void LaterQueryFailureDiscardsAlreadyComputedPairs()
        {
            Add(0);
            Add(0);
            Add(0);
            _index.FailOperation = "Query";
            _index.FailureDelay = 1;
            Assert.That(_world.Step(_results, out _), Is.False);
            Assert.That(_index.Calls.FindAll(call => call == "Query"), Has.Count.EqualTo(2));
            Assert.That(_results, Is.Empty);
            Step();
            Assert.That(_results, Has.Count.EqualTo(3));
        }

        [Test]
        public void LostProxyDuringSuspensionFailsAndRebuildsBeforeRecovery()
        {
            CollisionBodyId a = Add(0);
            Step();
            _index.Entries.Clear();
            _world.SuspendBody(a);
            Assert.That(_world.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain("lost"));
            _index.Calls.Clear();
            Step();
            Assert.That(_index.Calls, Is.EqualTo(new[] { "Clear" }));
            Assert.That(_world.ContainsBody(a), Is.True);
        }

        [Test]
        public void WorldBuffersAndIndexAreIndependent()
        {
            Add(0);
            Add(0);
            Step();
            StubBroadPhase2D secondIndex = new StubBroadPhase2D();
            CollisionWorld second = CreateWorld(secondIndex);
            List<CollisionPair> secondResults = new List<CollisionPair>();
            Assert.That(second.Step(secondResults, out _), Is.True);
            Assert.That(secondResults, Is.Empty);
            Assert.That(_results, Has.Count.EqualTo(1));
            Assert.That(_index.Entries, Has.Count.EqualTo(2));
            Assert.Throws<ArgumentNullException>(() => _world.Step(null, out _));
            Step();
        }
    }
}
