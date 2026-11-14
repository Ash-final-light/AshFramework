using System.Collections.Generic;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using NUnit.Framework;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionWorldBehaviourTeleportTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<CollisionContact> _contacts = new List<CollisionContact>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
            _contacts.Clear();
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void SuccessfulTeleportCommitsTransformAndLandingGeometry(CollisionPlane plane)
        {
            CollisionWorldBehaviour world = CreateWorld(plane, out StubBroadPhase2D index);
            CollisionBodyBehaviour moving = CreateCircle(world, plane, Point(plane, 0, 0));
            CollisionBodyBehaviour wall = CreateCircle(world, plane, Point(plane, 5, 0));
            Assert.That(world.TryGetBodyId(moving, out CollisionBodyId movingId), Is.True);
            Assert.That(world.TryGetBodyId(wall, out CollisionBodyId wallId), Is.True);
            index.Candidates.Add(movingId);
            index.Candidates.Add(wallId);
            Assert.That(world.Step(new List<CollisionPair>(), out string error), Is.True, error);
            Vector3 target = Point(plane, 4, 0, 3);

            Assert.That(world.TryTeleportTo(moving, target, _contacts, out error), Is.True, error);

            Assert.That(moving.transform.position, Is.EqualTo(target));
            Assert.That(moving.TryGetBounds(out Aabb2D bounds), Is.True);
            Assert.That(bounds.Center, Is.EqualTo(new FixVector2(4, 0)));
            Assert.That(_contacts.Count, Is.EqualTo(1));
        }

        [Test]
        public void FailedTeleportPreservesTransformAndCachedGeometry()
        {
            CollisionWorldBehaviour world = CreateWorld(CollisionPlane.XZ, out StubBroadPhase2D index);
            CollisionBodyBehaviour moving = CreateCircle(world, CollisionPlane.XZ, Vector3.zero);
            Assert.That(world.TryGetBodyId(moving, out CollisionBodyId movingId), Is.True);
            index.Candidates.Add(movingId);
            Assert.That(world.Step(new List<CollisionPair>(), out string error), Is.True, error);
            index.FailOperation = "Update";
            _contacts.Add(default);

            Assert.That(world.TryTeleportTo(moving, new Vector3(4, 0, 0), _contacts, out error), Is.False);

            Assert.That(error, Does.Contain("Update"));
            Assert.That(moving.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(moving.TryGetBounds(out Aabb2D bounds), Is.True);
            Assert.That(bounds.Center, Is.EqualTo(FixVector2.Zero));
            Assert.That(_contacts, Is.Empty);
        }

        [Test]
        public void SuccessfulTeleportStopsOldGradualCorrectionButFailurePreservesIt()
        {
            CollisionWorldBehaviour world = CreateWorld(CollisionPlane.XZ, out StubBroadPhase2D index);
            PositionCorrectionManager manager = world.gameObject.AddComponent<PositionCorrectionManager>();
            CollisionBodySettings actor = CreateSettings(world, CollisionPlane.XZ, "Character", Vector3.zero,
                CollisionLayer.Character);
            actor.gameObject.AddComponent<CircleShapeBehaviour>().Radius = 1;
            CharacterMovement character = actor.gameObject.AddComponent<CharacterMovement>();
            character.Body = actor.Body;
            character.Mode = CorrectionMode.Gradual;
            character.CorrectionSpeed = 1;
            CollisionBodySettings wall = CreateSettings(world, CollisionPlane.XZ, "Wall", Vector3.zero,
                CollisionLayer.WorldSolid);
            wall.gameObject.AddComponent<BoxShapeBehaviour>().HalfExtents = Vector2.one;
            actor.Body.BindWorld(world);
            wall.Body.BindWorld(world);
            manager.Configure(world, new[] { actor, wall });
            Assert.That(world.TryGetBodyId(actor.Body, out CollisionBodyId actorId), Is.True);
            Assert.That(world.TryGetBodyId(wall.Body, out CollisionBodyId wallId), Is.True);
            index.Candidates.Add(actorId);
            index.Candidates.Add(wallId);
            List<CollisionPair> pairs = new List<CollisionPair>();
            Assert.That(manager.TryMoveAndCorrect(character, Vector3.zero, 0, 0.25, pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));

            index.FailOperation = "Update";
            Assert.That(world.TryTeleportTo(actor.Body, new Vector3(4, 0, 0), _contacts, out error), Is.False);
            Vector3 afterFailedTeleport = actor.transform.position;
            Assert.That(manager.TryContinueGradualCorrection(character, 1, 0.25, pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(actor.transform.position, Is.Not.EqualTo(afterFailedTeleport));

            Assert.That(world.TryTeleportTo(actor.Body, new Vector3(4, 0, 0), _contacts, out error), Is.True, error);
            Vector3 teleported = actor.transform.position;
            Assert.That(manager.TryContinueGradualCorrection(character, 2, 0.25, pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(actor.transform.position, Is.EqualTo(teleported));
        }

        private CollisionWorldBehaviour CreateWorld(CollisionPlane plane, out StubBroadPhase2D index)
        {
            GameObject item = Create("World");
            CollisionWorldBehaviour world = item.AddComponent<CollisionWorldBehaviour>();
            world.Configure(plane, Vector2.zero, new Vector2(10, 10));
            index = new StubBroadPhase2D();
            world.Initialize(index);
            return world;
        }

        private CollisionBodyBehaviour CreateCircle(CollisionWorldBehaviour world, CollisionPlane plane, Vector3 position)
        {
            GameObject item = Create("Circle");
            item.transform.position = position;
            CollisionBodyBehaviour body = item.AddComponent<CollisionBodyBehaviour>();
            body.Plane = plane;
            item.AddComponent<CircleShapeBehaviour>().Radius = 1;
            body.BindWorld(world);
            return body;
        }

        private CollisionBodySettings CreateSettings(CollisionWorldBehaviour world, CollisionPlane plane, string name,
            Vector3 position, CollisionLayer layer)
        {
            GameObject item = Create(name);
            item.transform.position = position;
            CollisionBodySettings settings = item.AddComponent<CollisionBodySettings>();
            settings.Body.Plane = plane;
            settings.Layer = layer;
            return settings;
        }

        private GameObject Create(string name)
        {
            GameObject item = new GameObject(name);
            _objects.Add(item);
            return item;
        }

        private static Vector3 Point(CollisionPlane plane, float x, float y, float orthogonal = 0)
        {
            return plane == CollisionPlane.XY ? new Vector3(x, y, orthogonal) : new Vector3(x, orthogonal, y);
        }
    }
}
