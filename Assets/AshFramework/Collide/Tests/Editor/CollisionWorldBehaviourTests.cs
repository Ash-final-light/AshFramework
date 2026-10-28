using System;
using System.Collections.Generic;
using AshFramework.Collide.Editor;
using AshFramework.Collide.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionWorldBehaviourTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<CollisionPair> _results = new List<CollisionPair>();

        private GameObject Create(string name)
        {
            GameObject item = new GameObject(name);
            _objects.Add(item);
            return item;
        }

        private CollisionWorldBehaviour World(CollisionPlane plane = CollisionPlane.XZ)
        {
            CollisionWorldBehaviour world = Create("Test World").AddComponent<CollisionWorldBehaviour>();
            world.Configure(plane, Vector2.zero, new Vector2(20, 20));
            return world;
        }

        private CollisionBodyBehaviour Body(CollisionWorldBehaviour world = null)
        {
            GameObject item = Create("Test Body");
            CollisionBodyBehaviour body = item.AddComponent<CollisionBodyBehaviour>();
            item.AddComponent<CircleShapeBehaviour>();
            body.BindWorld(world);
            return body;
        }

        private static CollisionBodyId Id(CollisionWorldBehaviour world, CollisionBodyBehaviour body)
        {
            Assert.That(world.TryGetBodyId(body, out CollisionBodyId id), Is.True);
            return id;
        }

        private static void SetSerializedWorld(CollisionBodyBehaviour body, CollisionWorldBehaviour world)
        {
            SerializedObject serialized = new SerializedObject(body);
            serialized.FindProperty("_world").objectReferenceValue = world;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
            _results.Clear();
        }

        [Test]
        public void UninitializedWorldDoesNotPublishSuccessOrInventIds()
        {
            CollisionWorldBehaviour world = World();
            CollisionBodyBehaviour body = Body(world);
            Assert.That(world.BoundBodyCount, Is.EqualTo(1));
            Assert.That(world.TryGetBodyId(body, out CollisionBodyId id), Is.False);
            Assert.That(id.IsValid, Is.False);
            _results.Add(default);
            Assert.That(world.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain("not initialized"));
            Assert.That(world.LastError, Is.EqualTo(error));
            Assert.That(_results, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InitializationAndBindingOrderRegisterExactlyOnce(bool bindFirst)
        {
            CollisionWorldBehaviour world = World();
            CollisionBodyBehaviour body = Body();
            StubBroadPhase2D index = new StubBroadPhase2D();
            if (bindFirst)
            {
                body.BindWorld(world);
            }

            world.Initialize(index);
            body.BindWorld(world);
            CollisionBodyId id = Id(world, body);
            body.BindWorld(world);
            Assert.That(Id(world, body), Is.EqualTo(id));
            Assert.That(world.BoundBodyCount, Is.EqualTo(1));
            Assert.That(world.IsBodyActive(body), Is.False);
            Assert.That(world.Step(_results, out string error), Is.True, error);
            Assert.That(index.Entries.ContainsKey(id), Is.True);
        }

        [Test]
        public void InitializationDiscoversSerializedBindingsIncludingNeverEnabledBodies()
        {
            CollisionWorldBehaviour world = World();
            GameObject item = Create("Initially Inactive Body");
            item.SetActive(false);
            CollisionBodyBehaviour body = item.AddComponent<CollisionBodyBehaviour>();
            item.AddComponent<CircleShapeBehaviour>();
            SetSerializedWorld(body, world);
            Assert.That(world.BoundBodyCount, Is.Zero);
            StubBroadPhase2D index = new StubBroadPhase2D();
            world.Initialize(index);
            CollisionBodyId id = Id(world, body);
            Assert.That(world.Step(_results, out string error), Is.True, error);
            Assert.That(index.Entries, Is.Empty);
            item.SetActive(true);
            Assert.That(world.Step(_results, out error), Is.True, error);
            Assert.That(Id(world, body), Is.EqualTo(id));
            Assert.That(index.Entries.ContainsKey(id), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DisableAndEnableKeepIdentityAndRemoveOldProxy(bool disableComponent)
        {
            CollisionWorldBehaviour world = World();
            StubBroadPhase2D index = new StubBroadPhase2D();
            world.Initialize(index);
            CollisionBodyBehaviour body = Body(world);
            CollisionBodyId id = Id(world, body);
            Assert.That(world.Step(_results, out _), Is.True);
            if (disableComponent)
            {
                body.enabled = false;
            }
            else
            {
                body.gameObject.SetActive(false);
            }

            Assert.That(world.IsBodyActive(body), Is.False);
            Assert.That(Id(world, body), Is.EqualTo(id));
            Assert.That(world.Step(_results, out _), Is.True);
            Assert.That(index.Entries, Is.Empty);
            body.enabled = true;
            body.gameObject.SetActive(true);
            Assert.That(world.Step(_results, out _), Is.True);
            Assert.That(Id(world, body), Is.EqualTo(id));
            Assert.That(index.Entries.ContainsKey(id), Is.True);
        }

        [Test]
        public void UnbindRebindAndTransferInvalidateOldWorldIdentity()
        {
            CollisionWorldBehaviour a = World();
            CollisionWorldBehaviour b = World();
            StubBroadPhase2D indexA = new StubBroadPhase2D();
            StubBroadPhase2D indexB = new StubBroadPhase2D();
            a.Initialize(indexA);
            b.Initialize(indexB);
            CollisionBodyBehaviour body = Body(a);
            CollisionBodyId first = Id(a, body);
            Assert.That(a.Step(_results, out _), Is.True);
            body.BindWorld(null);
            Assert.That(a.TryGetBodyId(body, out _), Is.False);
            body.BindWorld(a);
            CollisionBodyId second = Id(a, body);
            Assert.That(second, Is.Not.EqualTo(first));
            body.BindWorld(b);
            Assert.That(a.TryGetBodyId(body, out _), Is.False);
            Assert.That(Id(b, body), Is.Not.EqualTo(second));
            Assert.That(a.Step(_results, out _), Is.True);
            Assert.That(b.Step(_results, out _), Is.True);
            Assert.That(indexA.Entries, Is.Empty);
            Assert.That(indexB.Entries.Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DestroyedBodiesAreRemovedBeforeNextQuery(bool initiallyInactive)
        {
            CollisionWorldBehaviour world = World();
            StubBroadPhase2D index = new StubBroadPhase2D();
            world.Initialize(index);
            CollisionBodyBehaviour body = Body(world);
            if (initiallyInactive)
            {
                body.gameObject.SetActive(false);
            }

            Assert.That(world.Step(_results, out _), Is.True);
            UnityEngine.Object.DestroyImmediate(body.gameObject);
            Assert.That(world.Step(_results, out string error), Is.True, error);
            Assert.That(world.BoundBodyCount, Is.Zero);
            Assert.That(index.Entries, Is.Empty);
        }

        [Test]
        public void DestroyWorldClearsExclusiveIndexAndBodyCanBindAnotherWorld()
        {
            CollisionWorldBehaviour world = World();
            StubBroadPhase2D index = new StubBroadPhase2D();
            world.Initialize(index);
            CollisionBodyBehaviour body = Body(world);
            Assert.That(world.Step(_results, out _), Is.True);
            UnityEngine.Object.DestroyImmediate(world.gameObject);
            Assert.That(index.Entries, Is.Empty);
            CollisionWorldBehaviour replacement = World();
            replacement.Initialize(new StubBroadPhase2D());
            body.BindWorld(replacement);
            Assert.That(replacement.Step(_results, out string error), Is.True, error);
            Assert.That(replacement.IsBodyActive(body), Is.True);
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void StepRefreshesAllGeometryBeforeQueryAndPreservesLogicalHeightData(CollisionPlane plane)
        {
            CollisionWorldBehaviour world = World(plane);
            StubBroadPhase2D index = new StubBroadPhase2D();
            world.Initialize(index);
            CollisionBodyBehaviour a = Body(world);
            CollisionBodyBehaviour b = Body(world);
            a.Plane = plane;
            b.Plane = plane;
            a.LogicalHeight = LogicalHeight.Level1;
            b.LogicalHeight = LogicalHeight.Level3;
            index.Candidates.Add(Id(world, a));
            index.Candidates.Add(Id(world, b));
            Assert.That(world.Step(_results, out string error), Is.True, error);
            Assert.That(_results.Count, Is.EqualTo(1));
            Assert.That(index.Calls.IndexOf("Query"), Is.GreaterThan(index.Calls.LastIndexOf("Insert")));
            b.transform.position = plane == CollisionPlane.XY ? new Vector3(0, 8, 0) : new Vector3(0, 0, 8);
            index.Calls.Clear();
            Assert.That(world.Step(_results, out error), Is.True, error);
            Assert.That(_results, Is.Empty);
            Assert.That(index.Calls.IndexOf("Query"), Is.GreaterThan(index.Calls.LastIndexOf("Update")));
        }

        [TestCase("plane")]
        [TestCase("bounds")]
        [TestCase("geometry")]
        public void InvalidBodyIsPausedAndRecoversWithSameIdWhileOtherBodiesContinue(string cause)
        {
            CollisionWorldBehaviour world = World();
            StubBroadPhase2D index = new StubBroadPhase2D();
            world.Initialize(index);
            CollisionBodyBehaviour bad = Body(world);
            CollisionBodyBehaviour good = Body(world);
            CollisionBodyId id = Id(world, bad);
            Assert.That(world.Step(_results, out _), Is.True);
            if (cause == "plane")
            {
                bad.Plane = CollisionPlane.XY;
            }
            else if (cause == "bounds")
            {
                bad.transform.position = new Vector3(30, 0, 0);
            }
            else
            {
                bad.GetComponent<CircleShapeBehaviour>().enabled = false;
            }

            Assert.That(world.Step(_results, out string error), Is.True, error);
            Assert.That(world.IsBodyActive(bad), Is.False);
            Assert.That(world.GetBodyError(bad), Is.Not.Empty);
            Assert.That(index.Entries.ContainsKey(id), Is.False);
            Assert.That(world.IsBodyActive(good), Is.True);
            bad.Plane = world.Plane;
            bad.transform.position = Vector3.zero;
            bad.GetComponent<CircleShapeBehaviour>().enabled = true;
            Assert.That(world.Step(_results, out error), Is.True, error);
            Assert.That(Id(world, bad), Is.EqualTo(id));
            Assert.That(world.GetBodyError(bad), Is.Empty);
            Assert.That(world.IsBodyActive(bad), Is.True);
        }

        [Test]
        public void IndexFailurePublishesNoPairsAndNextStepRebuilds()
        {
            CollisionWorldBehaviour world = World();
            StubBroadPhase2D index = new StubBroadPhase2D();
            world.Initialize(index);
            CollisionBodyBehaviour a = Body(world);
            CollisionBodyBehaviour b = Body(world);
            index.Candidates.Add(Id(world, a));
            index.Candidates.Add(Id(world, b));
            Assert.That(world.Step(_results, out _), Is.True);
            index.FailOperation = "Query";
            Assert.That(world.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain("Query"));
            Assert.That(_results, Is.Empty);
            index.Calls.Clear();
            Assert.That(world.Step(_results, out error), Is.True, error);
            Assert.That(index.Calls[0], Is.EqualTo("Clear"));
            Assert.That(_results.Count, Is.EqualTo(1));
        }

        [Test]
        public void DisabledWorldPausesBodiesAndRejectsStepUntilEnabled()
        {
            CollisionWorldBehaviour world = World();
            world.Initialize(new StubBroadPhase2D());
            CollisionBodyBehaviour body = Body(world);
            CollisionBodyId id = Id(world, body);
            Assert.That(world.Step(_results, out _), Is.True);
            world.enabled = false;
            Assert.That(world.IsBodyActive(body), Is.False);
            Assert.That(world.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain("disabled"));
            world.enabled = true;
            Assert.That(world.Step(_results, out error), Is.True, error);
            Assert.That(Id(world, body), Is.EqualTo(id));
        }

        [Test]
        public void FixedConfigurationCannotBeChangedSilentlyAfterInitialization()
        {
            CollisionWorldBehaviour world = World();
            world.Initialize(new StubBroadPhase2D());
            CollisionBodyBehaviour body = Body(world);
            Assert.That(world.Step(_results, out _), Is.True);
            Assert.Throws<InvalidOperationException>(() => world.Configure(CollisionPlane.XY, Vector2.zero, Vector2.one));
            Assert.Throws<InvalidOperationException>(() => world.Initialize(new StubBroadPhase2D()));
            SerializedObject serialized = new SerializedObject(world);
            serialized.FindProperty("_plane").enumValueIndex = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(world.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain("configuration changed"));
            Assert.That(world.IsBodyActive(body), Is.False);
            serialized.FindProperty("_plane").enumValueIndex = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(world.Step(_results, out error), Is.True, error);
        }

        [Test]
        public void InvalidConfigurationOrNullIndexLeavesWorldUninitialized()
        {
            CollisionWorldBehaviour world = World();
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Configure((CollisionPlane)99, Vector2.zero, Vector2.one));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Configure(CollisionPlane.XZ, new Vector2(float.NaN, 0), Vector2.one));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Configure(CollisionPlane.XZ, Vector2.zero, Vector2.zero));
            Assert.Throws<OverflowException>(() => world.Configure(CollisionPlane.XZ, new Vector2(3e9f, 0), Vector2.one));
            Assert.Throws<ArgumentNullException>(() => world.Initialize(null));
            Assert.That(world.IsInitialized, Is.False);
            Assert.Throws<ArgumentNullException>(() => world.Step(null, out _));
        }

        [Test]
        public void ExplicitDiscoveryHandlesInactiveSerializedBindingAndTrackedTransfers()
        {
            CollisionWorldBehaviour a = World();
            CollisionWorldBehaviour b = World();
            a.Initialize(new StubBroadPhase2D());
            b.Initialize(new StubBroadPhase2D());
            CollisionBodyBehaviour body = Body();
            body.enabled = false;
            SetSerializedWorld(body, a);
            a.RefreshBindings();
            Assert.That(Id(a, body).IsValid, Is.True);
            SetSerializedWorld(body, b);
            Assert.That(a.Step(_results, out _), Is.True);
            Assert.That(a.TryGetBodyId(body, out _), Is.False);
            Assert.That(Id(b, body).IsValid, Is.True);
            Assert.That(b.IsBodyActive(body), Is.False);
        }

        [Test]
        public void PlaneCheckIncludesDisabledBodiesAndDoesNotInitializeOrMutateGeometry()
        {
            CollisionWorldBehaviour world = World();
            CollisionBodyBehaviour good = Body(world);
            CollisionBodyBehaviour bad = Body(world);
            CollisionBodyBehaviour unrelated = Body();
            bad.Plane = CollisionPlane.XY;
            bad.enabled = false;
            unrelated.Plane = CollisionPlane.XY;
            Assert.That(good.RefreshGeometry(), Is.True);
            CompoundShape2D geometry = good.Geometry;
            int count = world.BoundBodyCount;
            List<CollisionBodyBehaviour> mismatches = new List<CollisionBodyBehaviour>();
            Assert.That(CollisionWorldValidation.CheckPlaneConsistency(world, mismatches, out int checkedCount, out string message), Is.False);
            Assert.That(checkedCount, Is.EqualTo(2));
            Assert.That(mismatches, Is.EqualTo(new[] { bad }));
            Assert.That(message, Does.Contain("2"));
            Assert.That(world.IsInitialized, Is.False);
            Assert.That(world.BoundBodyCount, Is.EqualTo(count));
            Assert.That(good.Geometry, Is.SameAs(geometry));
            Assert.That(geometry.Count, Is.EqualTo(1));
            Assert.That(good.LastError, Is.Empty);
            bad.Plane = world.Plane;
            Assert.That(CollisionWorldValidation.CheckPlaneConsistency(world, mismatches, out _, out _), Is.True);
            Assert.That(mismatches, Is.Empty);
        }

        [Test]
        public void EmptyPlaneCheckReportsZeroInsteadOfImplyingBodiesWereChecked()
        {
            CollisionWorldBehaviour world = World();
            List<CollisionBodyBehaviour> mismatches = new List<CollisionBodyBehaviour>();
            Assert.That(CollisionWorldValidation.CheckPlaneConsistency(world, mismatches, out int count, out string message), Is.True);
            Assert.That(count, Is.Zero);
            Assert.That(message, Does.Contain("检查数量为 0"));
            Assert.That(world.IsInitialized, Is.False);
        }

        [Test]
        public void BindingMutationDuringQueryIsRejectedBeforeChangingEitherWorld()
        {
            CollisionWorldBehaviour a = World();
            CollisionWorldBehaviour b = World();
            StubBroadPhase2D index = new StubBroadPhase2D();
            a.Initialize(index);
            b.Initialize(new StubBroadPhase2D());
            CollisionBodyBehaviour body = Body(a);
            CollisionBodyId id = Id(a, body);
            index.QueryCallback = () => body.BindWorld(b);
            Assert.That(a.Step(_results, out string error), Is.False);
            Assert.That(error, Does.Contain("during Step"));
            Assert.That(body.World, Is.SameAs(a));
            Assert.That(Id(a, body), Is.EqualTo(id));
            Assert.That(b.BoundBodyCount, Is.Zero);
            index.QueryCallback = null;
            Assert.That(a.Step(_results, out error), Is.True, error);
        }
    }
}
