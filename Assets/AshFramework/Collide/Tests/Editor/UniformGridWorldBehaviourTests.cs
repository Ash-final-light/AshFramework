using System.Collections.Generic;
using AshFramework.Collide.Samples;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class UniformGridWorldBehaviourTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private CollisionWorldBehaviour _world;
        private CollisionWorldOverview _overview;
        private CollisionBodyBehaviour[] _bodies;

        private GameObject Create(string name)
        {
            GameObject item = new GameObject(name);
            _objects.Add(item);
            return item;
        }

        [SetUp]
        public void SetUp()
        {
            _world = Create("Grid World").AddComponent<CollisionWorldBehaviour>();
            _world.Configure(CollisionPlane.XY, Vector2.zero, new Vector2(9, 4));
            _overview = Create("Grid Example").AddComponent<CollisionWorldOverview>();
            _bodies = new CollisionBodyBehaviour[6];
            Vector3[] positions =
            {
                new Vector3(-4, 1, 0), new Vector3(-3, 1, 0), new Vector3(3, 1, 0),
                new Vector3(-5, -2, 0), new Vector3(9, -2, 0), new Vector3(1, -2, 0)
            };
            CollisionWorldOverview.Entry[] entries = new CollisionWorldOverview.Entry[6];
            for (int i = 0; i < _bodies.Length; i++)
            {
                GameObject item = Create("Body " + i);
                item.transform.position = positions[i];
                CollisionBodyBehaviour body = item.AddComponent<CollisionBodyBehaviour>();
                body.Plane = CollisionPlane.XY;
                item.AddComponent<CircleShapeBehaviour>().Radius = 1;
                body.BindWorld(i == 5 ? null : _world);
                _bodies[i] = body;
                entries[i] = new CollisionWorldOverview.Entry(body, "Case " + i, Vector3.zero);
            }

            _bodies[1].LogicalHeight = LogicalHeight.Level3;
            _bodies[2].enabled = false;
            _bodies[3].Plane = CollisionPlane.XZ;
            _bodies[3].enabled = false;
            _overview.Configure(_world, entries);
        }

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
        }

        private CollisionBodyId Id(int index)
        {
            Assert.That(_world.TryGetBodyId(_bodies[index], out CollisionBodyId id), Is.True);
            return id;
        }

        private void InitializeAndStep()
        {
            Assert.That(_overview.InitializeGrid(), Is.True, _overview.IndexMessage);
            Assert.That(_overview.AttemptStep(), Is.True, _overview.StepMessage);
        }

        [Test]
        public void InjectedGridProducesNamedPairAndPerBodyErrors()
        {
            InitializeAndStep();
            Assert.That(_overview.GridColumns, Is.EqualTo(9));
            Assert.That(_overview.GridRows, Is.EqualTo(4));
            Assert.That(_overview.PairCount, Is.EqualTo(1));
            Assert.That(_overview.GetPair(0), Is.EqualTo(new CollisionPair(Id(0), Id(1))));
            Assert.That(_overview.GetPairDescription(0), Does.Contain("Body 0").And.Contain("Body 1"));
            Assert.That(_world.IsBodyActive(_bodies[0]), Is.True);
            Assert.That(_world.IsBodyActive(_bodies[1]), Is.True);
            Assert.That(_overview.GetBodyStatus(2), Does.Contain("disabled"));
            Assert.That(_overview.GetBodyStatus(4), Does.Contain("exceed"));
            Assert.That(_overview.GetBodyStatus(5), Does.Contain("未绑定"));
            Assert.That(_world.TryGetBodyId(_bodies[5], out _), Is.False);
        }

        [Test]
        public void CircleAgainstTwoWallShapesProducesTwoHalfUnitContacts()
        {
            CollisionBodyBehaviour wall = _bodies[0];
            wall.GetComponent<CircleShapeBehaviour>().enabled = false;
            wall.transform.position = new Vector3(3, 0, 0);
            for (int i = 0; i < 2; i++)
            {
                GameObject part = Create("Wall " + i);
                part.transform.SetParent(wall.transform, false);
                part.transform.localPosition = new Vector3(0, i == 0 ? 1 : -1, 0);
                part.AddComponent<BoxShapeBehaviour>().HalfExtents = Vector2.one;
            }

            _bodies[1].transform.position = new Vector3(1.5f, 0, 0);
            InitializeAndStep();
            Assert.That(_overview.PairCount, Is.EqualTo(1));
            Assert.That(_overview.ContactCount, Is.EqualTo(2));
            CollisionPair pair = _overview.GetPair(0);
            bool wallIsA = pair.A == Id(0);
            for (int i = 0; i < _overview.ContactCount; i++)
            {
                CollisionContact contact = _overview.GetContact(i);
                Assert.That(contact.Pair, Is.EqualTo(pair));
                Assert.That(contact.Shapes.ShapeIndexA, Is.EqualTo(wallIsA ? i : 0));
                Assert.That(contact.Shapes.ShapeIndexB, Is.EqualTo(wallIsA ? 0 : i));
                Assert.That(contact.Shapes.Contact.Normal, Is.EqualTo(wallIsA ? -FixVector2.Right : FixVector2.Right));
                Assert.That(contact.Shapes.Contact.Depth, Is.EqualTo(Fix64.Half));
                Assert.That(_overview.TryGetContactGizmo(i, out _, out Vector3 direction, out string label), Is.True);
                Assert.That(direction, Is.EqualTo(wallIsA ? Vector3.left : Vector3.right));
                Assert.That(label, Does.Contain("0.5"));
            }
        }

        [Test]
        public void MovingAndRestoringBChangesPairsOnlyAfterStep()
        {
            InitializeAndStep();
            CollisionBodyId id = Id(1);
            Vector3 original = _bodies[1].transform.position;
            _overview.ToggleExampleBPosition();
            Assert.That(_overview.PairCount, Is.EqualTo(1), "Stored pairs describe the preceding successful Step.");
            Assert.That(_overview.ContactCount, Is.EqualTo(1));
            Assert.That(_overview.AttemptStep(), Is.True);
            Assert.That(_overview.PairCount, Is.Zero);
            Assert.That(_overview.ContactCount, Is.Zero);
            _overview.ToggleExampleBPosition();
            Assert.That(_bodies[1].transform.position, Is.EqualTo(original));
            Assert.That(_overview.AttemptStep(), Is.True);
            Assert.That(_overview.PairCount, Is.EqualTo(1));
            Assert.That(_overview.ContactCount, Is.EqualTo(1));
            Assert.That(Id(1), Is.EqualTo(id));
        }

        [Test]
        public void DisableAndEnableBKeepIdAndRecoverPair()
        {
            InitializeAndStep();
            CollisionBodyId id = Id(1);
            _overview.ToggleExampleBEnabled();
            Assert.That(_world.IsBodyActive(_bodies[1]), Is.False);
            Assert.That(_overview.AttemptStep(), Is.True);
            Assert.That(_overview.PairCount, Is.Zero);
            Assert.That(_overview.ContactCount, Is.Zero);
            Assert.That(Id(1), Is.EqualTo(id));
            _overview.ToggleExampleBEnabled();
            Assert.That(_overview.AttemptStep(), Is.True);
            Assert.That(_overview.PairCount, Is.EqualTo(1));
            Assert.That(_overview.ContactCount, Is.EqualTo(1));
            Assert.That(Id(1), Is.EqualTo(id));
        }

        [Test]
        public void CorrectingPlaneAndCoverageResumesBodiesWithOriginalIds()
        {
            InitializeAndStep();
            CollisionBodyId wrongPlane = Id(3);
            CollisionBodyId outside = Id(4);
            _bodies[3].enabled = true;
            Assert.That(_overview.AttemptStep(), Is.True);
            Assert.That(_world.GetBodyError(_bodies[3]), Does.Contain("plane"));
            _bodies[3].Plane = CollisionPlane.XY;
            _bodies[4].transform.position = new Vector3(7, -2, 0);
            Assert.That(_overview.AttemptStep(), Is.True);
            Assert.That(_world.IsBodyActive(_bodies[3]), Is.True);
            Assert.That(_world.IsBodyActive(_bodies[4]), Is.True);
            Assert.That(Id(3), Is.EqualTo(wrongPlane));
            Assert.That(Id(4), Is.EqualTo(outside));
        }

        [Test]
        public void InvalidGridConfigurationDoesNotInitializeOrInventIdentity()
        {
            SerializedObject serialized = new SerializedObject(_overview);
            serialized.FindProperty("_cellSize").floatValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_overview.InitializeGrid(), Is.False);
            Assert.That(_world.IsInitialized, Is.False);
            Assert.That(_world.TryGetBodyId(_bodies[0], out _), Is.False);
            Assert.That(_overview.AttemptStep(), Is.False);
            Assert.That(_overview.PairCount, Is.Zero);
            Assert.That(_overview.StepMessage, Does.Contain("not initialized"));
            serialized.FindProperty("_cellSize").floatValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            InitializeAndStep();
            CollisionBodyId id = Id(0);
            Assert.That(_overview.InitializeGrid(), Is.False);
            Assert.That(Id(0), Is.EqualTo(id));
            Assert.That(_overview.AttemptStep(), Is.True);
        }

        [Test]
        public void FailedStepClearsDisplayedPairsAndCanRecover()
        {
            InitializeAndStep();
            List<CollisionPair> pairs = new List<CollisionPair>();
            List<CollisionContact> contacts = new List<CollisionContact>();
            Assert.That(_world.Step(pairs, contacts, out string error), Is.True, error);
            Assert.That(contacts.Count, Is.EqualTo(1));
            _world.enabled = false;
            Assert.That(_world.Step(pairs, contacts, out error), Is.False);
            Assert.That(pairs, Is.Empty);
            Assert.That(contacts, Is.Empty);
            Assert.That(_overview.AttemptStep(), Is.False);
            Assert.That(_overview.PairCount, Is.Zero);
            Assert.That(_overview.ContactCount, Is.Zero);
            Assert.That(_overview.LastStepSucceeded, Is.False);
            _world.enabled = true;
            Assert.That(_overview.AttemptStep(), Is.True);
            Assert.That(_overview.PairCount, Is.EqualTo(1));
            Assert.That(_overview.ContactCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyedBodyLeavesNoGridCandidateOrDisplayedPair()
        {
            InitializeAndStep();
            CollisionBodyId old = Id(1);
            Object.DestroyImmediate(_bodies[1].gameObject);
            Assert.That(_overview.AttemptStep(), Is.True, _overview.StepMessage);
            Assert.That(_overview.PairCount, Is.Zero);
            Assert.That(_overview.FindBody(old), Is.Null);
            Assert.That(_overview.GetBodyStatus(1), Does.Contain("销毁"));
        }
    }
}
