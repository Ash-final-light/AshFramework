using System.Collections.Generic;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using NUnit.Framework;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class PositionCorrectionTests
    {
        private readonly List<CollisionPair> _pairs = new List<CollisionPair>();
        private readonly List<CollisionContact> _contacts = new List<CollisionContact>();
        private GameObject _root;
        private CollisionWorldBehaviour _world;
        private PositionCorrectionManager _manager;
        private CharacterMovement _character;
        private CollisionBodyBehaviour _wall;
        private CollisionBodyBehaviour _trigger;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [TestCase(CollisionPlane.XY, true)]
        [TestCase(CollisionPlane.XZ, false)]
        public void MovesOnlyCharacterOnceAndReturnsTangentContacts(CollisionPlane plane, bool characterFirst)
        {
            CreateWorld(plane, characterFirst);
            Vector3 wallPosition = _wall.transform.position;
            Vector3 target = Point(1.5f, 0, 2);
            Vector3 corrected = Point(1, 0, 2);
            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts, out string error), Is.True, error);
            Assert.That(_character.Body.transform.position, Is.EqualTo(corrected));
            Assert.That(_wall.transform.position, Is.EqualTo(wallPosition));
            Assert.That(_pairs.Count, Is.EqualTo(1));
            Assert.That(_contacts.Count, Is.EqualTo(2));
            Assert.That(_contacts[0].Shapes.Contact.Depth, Is.EqualTo(Fix64.Zero));
            Assert.That(_contacts[1].Shapes.Contact.Depth, Is.EqualTo(Fix64.Zero));

            Assert.That(_manager.TryMoveAndCorrect(_character, corrected, _pairs, _contacts, out error), Is.True, error);
            Assert.That(_character.Body.transform.position, Is.EqualTo(corrected));
        }

        [Test]
        public void TriggerOnlyContactPreservesTargetAndIsReturned()
        {
            CreateWorld(CollisionPlane.XY, true);
            Vector3 target = Point(-2.5f, 0);
            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts, out string error), Is.True, error);
            Assert.That(_character.Body.transform.position, Is.EqualTo(target));
            Assert.That(_pairs, Is.EqualTo(new[] { new CollisionPair(Id(_character.Body), Id(_trigger)) }));
            Assert.That(_contacts.Count, Is.EqualTo(1));
            Assert.That(_contacts[0].Shapes.Contact.Depth, Is.GreaterThan(Fix64.Zero));
        }

        [Test]
        public void DeepTriggerOverlapDoesNotOverrideWallCorrection()
        {
            CreateWorld(CollisionPlane.XY, true);
            Vector3 target = Point(1.5f, 0);
            _trigger.transform.position = target;
            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts, out string error), Is.True, error);
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1, 0)));
            Assert.That(_pairs, Does.Contain(new CollisionPair(Id(_character.Body), Id(_trigger))));
            Assert.That(_pairs, Does.Contain(new CollisionPair(Id(_character.Body), Id(_wall))));
        }

        [TestCase(CorrectionTiming.Deferred, CorrectionMode.Snap)]
        [TestCase(CorrectionTiming.Immediate, CorrectionMode.Gradual)]
        public void ReservedPoliciesFailBeforeMoving(CorrectionTiming timing, CorrectionMode mode)
        {
            CreateWorld(CollisionPlane.XY, true);
            _character.Timing = timing;
            _character.Mode = mode;
            Vector3 original = _character.Body.transform.position;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), _pairs, _contacts, out string error), Is.False);
            Assert.That(error, Does.Contain("Immediate + Snap"));
            Assert.That(_character.Body.transform.position, Is.EqualTo(original));
        }

        [TestCase(0)]
        [TestCase(3)]
        public void FailedInitialOrVerificationStepRollsBackAndNextStepResynchronizes(int successfulQueries)
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CreateWorld(CollisionPlane.XY, true, index);
            index.Candidates.AddRange(new[] { Id(_character.Body), Id(_wall), Id(_trigger) });
            index.FailOperation = "Query";
            index.FailureDelay = successfulQueries;
            Vector3 original = _character.Body.transform.position;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), _pairs, _contacts, out string error), Is.False);
            Assert.That(error, Does.Contain("Injected Query"));
            Assert.That(_character.Body.transform.position, Is.EqualTo(original));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
            Assert.That(_world.Step(_pairs, _contacts, out error), Is.True, error);
            Assert.That(_pairs, Is.Empty);
        }

        [Test]
        public void ATargetOutsideWorldRollsBackEvenWhenStepSucceedsForOtherBodies()
        {
            CreateWorld(CollisionPlane.XY, true);
            Vector3 original = _character.Body.transform.position;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(12, 0), _pairs, _contacts, out string error), Is.False);
            Assert.That(error, Does.Contain("did not participate"));
            Assert.That(_character.Body.transform.position, Is.EqualTo(original));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
        }

        private void CreateWorld(CollisionPlane plane, bool characterFirst, IBroadPhase2D index = null)
        {
            _root = new GameObject("Position correction test world");
            _world = _root.AddComponent<CollisionWorldBehaviour>();
            _world.Configure(plane, Vector2.zero, new Vector2(9, 4));
            _manager = _root.AddComponent<PositionCorrectionManager>();
            CollisionBodySettings actor = CreateBody("Character", CollisionLayer.Character, Point(0, 0));
            _character = actor.gameObject.AddComponent<CharacterMovement>();
            _character.Body = actor.Body;
            CollisionBodySettings wall = CreateBody("Wall", CollisionLayer.WorldSolid, Point(3, 0));
            _wall = wall.Body;
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject part = new GameObject("Wall part");
                part.transform.SetParent(_wall.transform, false);
                part.transform.localPosition = Point(0, i);
                part.AddComponent<BoxShapeBehaviour>().HalfExtents = Vector2.one;
            }

            CollisionBodySettings trigger = CreateBody("Trigger", CollisionLayer.Trigger, Point(-3, 0));
            _trigger = trigger.Body;
            _character.gameObject.AddComponent<CircleShapeBehaviour>().Radius = 1;
            _trigger.gameObject.AddComponent<CircleShapeBehaviour>().Radius = 1;
            _world.Initialize(index ?? new UniformGridBroadPhase2D(new Aabb2D(FixVector2.Zero, new FixVector2(9, 4)), 2));
            (characterFirst ? _character.Body : _wall).BindWorld(_world);
            (characterFirst ? _wall : _character.Body).BindWorld(_world);
            _trigger.BindWorld(_world);
            _manager.Configure(_world, new[] { actor, wall, trigger });
        }

        private CollisionBodySettings CreateBody(string name, CollisionLayer layer, Vector3 position)
        {
            GameObject body = new GameObject(name);
            body.transform.SetParent(_root.transform, false);
            body.transform.position = position;
            CollisionBodySettings settings = body.AddComponent<CollisionBodySettings>();
            settings.Body.Plane = _world.Plane;
            settings.Layer = layer;
            return settings;
        }

        private Vector3 Point(float x, float y, float orthogonal = 0)
        {
            return _world.Plane == CollisionPlane.XY ? new Vector3(x, y, orthogonal) : new Vector3(x, orthogonal, y);
        }

        private CollisionBodyId Id(CollisionBodyBehaviour body)
        {
            Assert.That(_world.TryGetBodyId(body, out CollisionBodyId id), Is.True);
            return id;
        }
    }
}
