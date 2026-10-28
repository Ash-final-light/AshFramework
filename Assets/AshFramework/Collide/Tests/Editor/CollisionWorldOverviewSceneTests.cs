using System.Collections.Generic;
using AshFramework.Collide.Editor;
using AshFramework.Collide.Samples;
using AshFramework.Collide.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionWorldOverviewSceneTests
    {
        private const string ScenePath = "Assets/AshFramework/Collide/Samples/Scenes/CollideWorldOverview.unity";
        private Scene _scene;
        private Scene _previousActiveScene;
        private bool _openedByTest;
        private CollisionWorldOverview _overview;

        [SetUp]
        public void SetUp()
        {
            _previousActiveScene = SceneManager.GetActiveScene();
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _openedByTest = !_scene.IsValid() || !_scene.isLoaded;
            if (_openedByTest)
            {
                _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }
            foreach (UnityEngine.GameObject root in _scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out CollisionWorldOverview overview))
                {
                    _overview = overview;
                }
            }

            Assert.That(_overview, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_openedByTest && _scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }

            if (_previousActiveScene.IsValid() && _previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(_previousActiveScene);
            }

            _overview = null;
        }

        [Test]
        public void SavedSceneContainsExplicitBindingsDisabledMismatchAndOutOfBoundsCase()
        {
            CollisionWorldBehaviour world = _overview.World;
            Assert.That(world, Is.Not.Null);
            Assert.That(world.Plane, Is.EqualTo(CollisionPlane.XY));
            Assert.That(world.BoundsCenter, Is.EqualTo(UnityEngine.Vector2.zero));
            Assert.That(world.BoundsHalfExtents, Is.EqualTo(new UnityEngine.Vector2(9, 4)));
            Assert.That(world.IsInitialized, Is.False);
            Assert.That(_overview.CaseCount, Is.EqualTo(6));
            for (int i = 0; i < 5; i++)
            {
                Assert.That(_overview.GetCase(i).Body.World, Is.SameAs(world));
                Assert.That(world.TryGetBodyId(_overview.GetCase(i).Body, out _), Is.False);
            }

            Assert.That(_overview.GetCase(5).Body.World, Is.Null);
            Assert.That(_overview.GetCase(2).Body.enabled, Is.False);
            Assert.That(_overview.GetCase(3).Body.enabled, Is.False);
            List<CollisionBodyBehaviour> mismatches = new List<CollisionBodyBehaviour>();
            Assert.That(CollisionWorldValidation.CheckPlaneConsistency(world, mismatches, out int count, out _), Is.False);
            Assert.That(count, Is.EqualTo(5));
            Assert.That(mismatches, Is.EqualTo(new[] { _overview.GetCase(3).Body }));
            CollisionBodyBehaviour outside = _overview.GetCase(4).Body;
            Assert.That(outside.RefreshGeometry(), Is.True, outside.LastError);
            Assert.That(outside.TryGetBounds(out Aabb2D bounds), Is.True);
            Assert.That(bounds.Max.X.ToDecimal(), Is.GreaterThan(9));
        }

        [Test]
        public void ExampleRefreshDoesNotInitializeWorldAndStepReportsFailure()
        {
            _overview.RefreshExampleGeometry();
            CollisionBodyBehaviour a = _overview.GetCase(0).Body;
            CollisionBodyBehaviour b = _overview.GetCase(1).Body;
            Assert.That(a.Geometry.Count, Is.EqualTo(2));
            Assert.That(CompoundCollision2D.Overlaps(a.Geometry, b.Geometry), Is.True);
            Assert.That(a.LogicalHeight, Is.EqualTo(LogicalHeight.Level1));
            Assert.That(b.LogicalHeight, Is.EqualTo(LogicalHeight.Level3));
            Assert.That(_overview.AttemptStep(), Is.False);
            Assert.That(_overview.StepMessage, Does.Contain("not initialized"));
            Assert.That(_overview.World.IsInitialized, Is.False);
            Assert.That(_overview.World.TryGetBodyId(a, out _), Is.False);
        }
    }
}
