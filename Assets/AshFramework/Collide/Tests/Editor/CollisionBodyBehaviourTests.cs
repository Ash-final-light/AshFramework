using System;
using System.Collections.Generic;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionBodyBehaviourTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        private GameObject Create(string name, Transform parent = null)
        {
            GameObject gameObject = new GameObject(name);
            _objects.Add(gameObject);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            return gameObject;
        }

        private CollisionBodyBehaviour Body(CollisionPlane plane)
        {
            CollisionBodyBehaviour body = Create("Body").AddComponent<CollisionBodyBehaviour>();
            body.Plane = plane;
            return body;
        }

        private CircleShapeBehaviour Circle(CollisionBodyBehaviour body, float x = 0)
        {
            GameObject gameObject = Create("Circle", body.transform);
            gameObject.transform.localPosition = new Vector3(x, 0, 0);
            return gameObject.AddComponent<CircleShapeBehaviour>();
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
        }

        private static void Near(Fix64 actual, double expected)
        {
            Assert.That((double)actual.ToDecimal(), Is.EqualTo(expected).Within(0.000003));
        }

        [Test]
        public void EmptyBodyHasNoGeometryAndDefaultsToXZAndHeightOne()
        {
            CollisionBodyBehaviour body = Create("Empty").AddComponent<CollisionBodyBehaviour>();
            Assert.That(body.Plane, Is.EqualTo(CollisionPlane.XZ));
            Assert.That(body.LogicalHeight, Is.EqualTo(LogicalHeight.Level1));
            Assert.That(body.RefreshGeometry(), Is.False);
            Assert.That(body.HasGeometry, Is.False);
            Assert.That(body.TryGetBounds(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => body.Geometry.TryGetBounds(out _));
            Assert.That(body.LastError, Does.Contain("no active"));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void CollectsSameObjectAndGrandchildShapesButNotVisualObjects(CollisionPlane plane)
        {
            CollisionBodyBehaviour body = Body(plane);
            CircleShapeBehaviour own = body.gameObject.AddComponent<CircleShapeBehaviour>();
            GameObject group = Create("Group", body.transform);
            BoxShapeBehaviour box = Create("Box", group.transform).AddComponent<BoxShapeBehaviour>();
            Create("VisualOnly", body.transform);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.ShapeCount, Is.EqualTo(2));
            Assert.That(body.GetShape(0), Is.SameAs(own));
            Assert.That(body.GetShape(1), Is.SameAs(box));
            Assert.That(own.Owner, Is.SameAs(body));
            Assert.That(box.Owner, Is.SameAs(body));
            Assert.That(body.Geometry.Count, Is.EqualTo(2));
        }

        [Test]
        public void NearestBodyBoundaryIncludesDisabledNestedBodies()
        {
            CollisionBodyBehaviour parent = Body(CollisionPlane.XZ);
            Circle(parent);
            CollisionBodyBehaviour nested = Create("Nested", parent.transform).AddComponent<CollisionBodyBehaviour>();
            CircleShapeBehaviour child = Circle(nested);
            Assert.That(parent.RefreshGeometry(), Is.True, parent.LastError);
            Assert.That(nested.RefreshGeometry(), Is.True, nested.LastError);
            Assert.That(parent.ShapeCount, Is.EqualTo(1));
            Assert.That(child.Owner, Is.SameAs(nested));
            nested.enabled = false;
            parent.RebuildShapes();
            Assert.That(parent.RefreshGeometry(), Is.True, parent.LastError);
            Assert.That(parent.ShapeCount, Is.EqualTo(1));
            Assert.That(nested.HasGeometry, Is.False);
        }

        [Test]
        public void SiblingOrderChangesPartOrderWithoutChangingOwner()
        {
            CollisionBodyBehaviour body = Body(CollisionPlane.XZ);
            CircleShapeBehaviour a = Circle(body, 0);
            CircleShapeBehaviour b = Circle(body, 4);
            b.transform.SetSiblingIndex(0);
            body.RebuildShapes();
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.GetShape(0), Is.SameAs(b));
            Assert.That(body.GetShape(1), Is.SameAs(a));
            Assert.That(a.Owner, Is.SameAs(body));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void ParentRotationAndScaledLocalCenterPrepareCorrectWorldCircle(CollisionPlane plane)
        {
            CollisionBodyBehaviour body = Body(plane);
            body.transform.position = plane == CollisionPlane.XY ? new Vector3(10, 20, 5) : new Vector3(10, 5, 20);
            body.transform.rotation = Quaternion.AngleAxis(90, plane == CollisionPlane.XY ? Vector3.forward : Vector3.up);
            CircleShapeBehaviour shape = Circle(body, 2);
            shape.transform.localScale = Vector3.one * 2;
            shape.LocalCenter = new Vector2(1, 0);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Circle2D world = body.Geometry[0].Circle;
            Near(world.Center.X, 10);
            Near(world.Center.Y, plane == CollisionPlane.XY ? 24 : 16);
            Near(world.Radius, 1);
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void BoxBakesPositiveOrthogonalScaleAndPreservesRotation(CollisionPlane plane)
        {
            CollisionBodyBehaviour body = Body(plane);
            BoxShapeBehaviour shape = Create("Box", body.transform).AddComponent<BoxShapeBehaviour>();
            shape.HalfExtents = new Vector2(1, 2);
            shape.transform.localScale = plane == CollisionPlane.XY ? new Vector3(2, 3, 1) : new Vector3(2, 1, 3);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.Geometry[0].Kind, Is.EqualTo(ShapeKind2D.Aabb));
            Assert.That(body.Geometry[0].Aabb.HalfExtents, Is.EqualTo(new FixVector2(2, 6)));
            shape.transform.localRotation = Quaternion.AngleAxis(35, plane == CollisionPlane.XY ? Vector3.forward : Vector3.up);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.Geometry[0].Kind, Is.EqualTo(ShapeKind2D.Obb));
            Near(body.Geometry[0].Obb.HalfExtents.X, 2);
            Near(body.Geometry[0].Obb.HalfExtents.Y, 6);
        }

        [Test]
        public void WholeBoundsCoverSecondPartAndRefreshReusesOwnerContainer()
        {
            CollisionBodyBehaviour body = Body(CollisionPlane.XZ);
            Circle(body, -5);
            CircleShapeBehaviour moving = Circle(body, 5);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            CompoundShape2D geometry = body.Geometry;
            body.TryGetBounds(out Aabb2D bounds);
            Near(bounds.Min.X, -5.5);
            Near(bounds.Max.X, 5.5);
            moving.transform.localPosition = new Vector3(10, 0, 0);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.Geometry, Is.SameAs(geometry));
            body.TryGetBounds(out bounds);
            Near(bounds.Max.X, 10.5);
        }

        [Test]
        public void DisableInactiveAndDestroyedShapesLeaveNoResidualGeometry()
        {
            CollisionBodyBehaviour body = Body(CollisionPlane.XZ);
            CircleShapeBehaviour a = Circle(body);
            CircleShapeBehaviour b = Circle(body, 10);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            b.enabled = false;
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.ShapeCount, Is.EqualTo(1));
            b.enabled = true;
            b.gameObject.SetActive(false);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.ShapeCount, Is.EqualTo(1));
            b.gameObject.SetActive(true);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.ShapeCount, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(b);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.ShapeCount, Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(a);
            Assert.That(body.RefreshGeometry(), Is.False);
            Assert.That(body.TryGetBounds(out _), Is.False);
        }

        [Test]
        public void ExplicitRebuildHandlesDeepAncestorReparenting()
        {
            CollisionBodyBehaviour a = Body(CollisionPlane.XZ);
            CollisionBodyBehaviour b = Body(CollisionPlane.XZ);
            Transform containerA = Create("ContainerA", a.transform).transform;
            Transform containerB = Create("ContainerB", b.transform).transform;
            Transform group = Create("MovingGroup", containerA).transform;
            CircleShapeBehaviour shape = Create("Circle", group).AddComponent<CircleShapeBehaviour>();
            Assert.That(a.RefreshGeometry(), Is.True, a.LastError);
            group.SetParent(containerB, false);
            a.RebuildShapes();
            b.RebuildShapes();
            Assert.That(a.RefreshGeometry(), Is.False);
            Assert.That(b.RefreshGeometry(), Is.True, b.LastError);
            Assert.That(shape.Owner, Is.SameAs(b));
            Assert.That(b.ShapeCount, Is.EqualTo(1));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void ShearedBoxFailsEntireRefreshAndClearsPriorCache(CollisionPlane plane)
        {
            CollisionBodyBehaviour body = Body(plane);
            Circle(body);
            BoxShapeBehaviour box = Create("Box", body.transform).AddComponent<BoxShapeBehaviour>();
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            CompoundShape2D oldReference = body.Geometry;
            // 用普通分组制造剪切，确保圆本身保持有效，失败来自矩形。
            Transform group = Create("NonUniformGroup", body.transform).transform;
            group.localScale = new Vector3(2, 1, 1);
            box.transform.SetParent(group, false);
            box.transform.localRotation = Quaternion.AngleAxis(45, plane == CollisionPlane.XY ? Vector3.forward : Vector3.up);
            body.RebuildShapes();
            Assert.That(body.RefreshGeometry(), Is.False);
            Assert.That(body.LastError, Does.Contain("orthogonal"));
            Assert.That(body.HasGeometry, Is.False);
            Assert.That(oldReference.Count, Is.Zero);
            Assert.That(body.TryGetBounds(out _), Is.False);
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void NonUniformCircleScaleIsRejected(CollisionPlane plane)
        {
            CollisionBodyBehaviour body = Body(plane);
            CircleShapeBehaviour circle = Circle(body);
            circle.transform.localScale = new Vector3(2, 1, 1);
            Assert.That(body.RefreshGeometry(), Is.False);
            Assert.That(body.LastError, Does.Contain("uniform"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveAncestorScaleIsRejected(float scale)
        {
            CollisionBodyBehaviour body = Body(CollisionPlane.XZ);
            Circle(body);
            body.transform.localScale = new Vector3(scale, 1, 1);
            Assert.That(body.RefreshGeometry(), Is.False);
            Assert.That(body.LastError, Does.Contain("positive"));
        }

        [Test]
        public void UnsupportedTiltAndUnrepresentableGeometryAreRejected()
        {
            CollisionBodyBehaviour body = Body(CollisionPlane.XZ);
            CircleShapeBehaviour circle = Circle(body);
            circle.transform.localRotation = Quaternion.Euler(20, 0, 0);
            Assert.That(body.RefreshGeometry(), Is.False);
            circle.transform.localRotation = Quaternion.identity;
            circle.transform.localPosition = new Vector3(3000000000f, 0, 0);
            Assert.That(body.RefreshGeometry(), Is.False);
            circle.transform.localPosition = Vector3.zero;
            circle.Radius = 3000000000f;
            Assert.That(body.RefreshGeometry(), Is.False);
        }

        [Test]
        public void InvalidSerializedConfigurationFailsWithoutDroppingOnlyOnePart()
        {
            CollisionBodyBehaviour body = Body(CollisionPlane.XZ);
            Circle(body);
            CircleShapeBehaviour invalid = Circle(body, 5);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            SerializedObject serialized = new SerializedObject(invalid);
            serialized.FindProperty("_radius").floatValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(body.RefreshGeometry(), Is.False);
            Assert.That(body.HasGeometry, Is.False);
            invalid.enabled = false;
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            Assert.That(body.Geometry.Count, Is.EqualTo(1));
        }

        [Test]
        public void LogicalHeightIsSingleValidatedDataAndDoesNotFilterGeometry()
        {
            CollisionBodyBehaviour a = Body(CollisionPlane.XZ);
            CollisionBodyBehaviour b = Body(CollisionPlane.XZ);
            Circle(a);
            Circle(b);
            a.LogicalHeight = LogicalHeight.Level1;
            b.LogicalHeight = LogicalHeight.Level3;
            Assert.That(a.RefreshGeometry(), Is.True, a.LastError);
            Assert.That(b.RefreshGeometry(), Is.True, b.LastError);
            Assert.That(CompoundCollision2D.Overlaps(a.Geometry, b.Geometry), Is.True);
            a.LogicalHeight = LogicalHeight.Level2;
            Assert.That(a.HasGeometry, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.LogicalHeight = (LogicalHeight)0);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.LogicalHeight = (LogicalHeight)4);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Plane = (CollisionPlane)99);
        }

        [Test]
        public void BodyDisableClearsCacheAndReenableCanPrepareAgain()
        {
            CollisionBodyBehaviour body = Body(CollisionPlane.XZ);
            Circle(body);
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
            CompoundShape2D cache = body.Geometry;
            body.enabled = false;
            Assert.That(cache.Count, Is.Zero);
            Assert.That(body.RefreshGeometry(), Is.False);
            body.enabled = true;
            Assert.That(body.RefreshGeometry(), Is.True, body.LastError);
        }

        [Test]
        public void ShapePropertySettersRejectInvalidDimensionsAndOffsets()
        {
            CircleShapeBehaviour circle = Create("Circle").AddComponent<CircleShapeBehaviour>();
            BoxShapeBehaviour box = Create("Box").AddComponent<BoxShapeBehaviour>();
            Assert.That(circle.Owner, Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => circle.Radius = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => circle.Radius = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => box.HalfExtents = new Vector2(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => box.HalfExtents = new Vector2(float.PositiveInfinity, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => circle.LocalCenter = new Vector2(float.NaN, 0));
        }
    }
}
