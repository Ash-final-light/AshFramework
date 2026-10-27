using System;
using System.Collections.Generic;
using AshFramework.Collide.Editor;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using NUnit.Framework;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class CollisionGizmoPreviewTests
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

        [TestCase(CollisionPlane.XY, ShapeKind2D.Circle)]
        [TestCase(CollisionPlane.XZ, ShapeKind2D.Circle)]
        [TestCase(CollisionPlane.XY, ShapeKind2D.Aabb)]
        [TestCase(CollisionPlane.XZ, ShapeKind2D.Aabb)]
        [TestCase(CollisionPlane.XY, ShapeKind2D.Obb)]
        [TestCase(CollisionPlane.XZ, ShapeKind2D.Obb)]
        public void OutlinesAreClosedAndUseTheRequestedWorldPlane(CollisionPlane plane, ShapeKind2D kind)
        {
            FixVector2 center = new FixVector2(Fix64.FromInt(3), Fix64.FromInt(-2));
            FixVector2 extents = new FixVector2(Fix64.FromInt(2), Fix64.One);
            WorldShape2D shape;
            if (kind == ShapeKind2D.Circle)
            {
                shape = WorldShape2D.FromCircle(new Circle2D(center, Fix64.FromInt(2)));
            }
            else if (kind == ShapeKind2D.Aabb)
            {
                shape = WorldShape2D.FromAabb(new Aabb2D(center, extents));
            }
            else
            {
                shape = WorldShape2D.FromObb(new Obb2D(center, extents, FixMath.Pi / Fix64.FromInt(5)));
            }

            List<Vector3> points = new List<Vector3>();
            CollisionGizmoPreview.BuildOutline(shape, plane, 7, points);
            Assert.That(points.Count, Is.EqualTo(kind == ShapeKind2D.Circle ? 65 : 5));
            Assert.That(points[points.Count - 1], Is.EqualTo(points[0]));
            Aabb2D bounds = shape.GetBounds();
            foreach (Vector3 point in points)
            {
                Assert.That(plane == CollisionPlane.XY ? point.z : point.y, Is.EqualTo(7));
                double x = point.x;
                double y = plane == CollisionPlane.XY ? point.y : point.z;
                Assert.That(x, Is.InRange((double)bounds.Min.X.ToDecimal() - 0.000001,
                    (double)bounds.Max.X.ToDecimal() + 0.000001));
                Assert.That(y, Is.InRange((double)bounds.Min.Y.ToDecimal() - 0.000001,
                    (double)bounds.Max.Y.ToDecimal() + 0.000001));
                if (kind == ShapeKind2D.Circle)
                {
                    Assert.That((x - 3) * (x - 3) + (y + 2) * (y + 2), Is.EqualTo(4).Within(0.000002));
                }
            }

            if (kind == ShapeKind2D.Obb)
            {
                Obb2D box = shape.Obb;
                double expectedX = 3 - (double)box.AxisX.X.ToDecimal() * 2 - (double)box.AxisY.X.ToDecimal();
                double expectedY = -2 - (double)box.AxisX.Y.ToDecimal() * 2 - (double)box.AxisY.Y.ToDecimal();
                Assert.That(points[0].x, Is.EqualTo(expectedX).Within(0.000001));
                Assert.That(plane == CollisionPlane.XY ? points[0].y : points[0].z,
                    Is.EqualTo(expectedY).Within(0.000001));
            }
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void PreviewUsesCurrentOffsetsAndPoseWithoutChangingTheBodyCache(CollisionPlane plane)
        {
            CollisionBodyBehaviour body = Create("Body").AddComponent<CollisionBodyBehaviour>();
            body.Plane = plane;
            body.transform.position = new Vector3(2, 4, 6);
            BoxShapeBehaviour shape = Create("Box", body.transform).AddComponent<BoxShapeBehaviour>();
            shape.LocalCenter = new Vector2(1, 2);
            Assert.That(body.RefreshGeometry(), Is.True);
            CompoundShape2D original = body.Geometry;
            Aabb2D originalBounds = original[0].GetBounds();
            shape.transform.localPosition = plane == CollisionPlane.XY
                ? new Vector3(3, 5, 0) : new Vector3(3, 0, 5);
            shape.transform.localRotation = Quaternion.AngleAxis(30, plane == CollisionPlane.XY ? Vector3.forward : Vector3.up);

            CollisionGizmoPreview preview = new CollisionGizmoPreview();
            Assert.That(preview.Capture(body), Is.True, preview.Error);
            Assert.That(preview.NormalCoordinate, Is.EqualTo(plane == CollisionPlane.XY ? 6 : 4));
            Assert.That(shape.TryBuildWorldShape(plane, out WorldShape2D expected, out _), Is.True);
            Assert.That(preview.Geometry[0].Kind, Is.EqualTo(ShapeKind2D.Obb));
            Assert.That(preview.Geometry[0].Obb.Center, Is.EqualTo(expected.Obb.Center));
            Assert.That(preview.Geometry[0].Obb.AxisX, Is.EqualTo(expected.Obb.AxisX));
            Assert.That(body.Geometry, Is.SameAs(original));
            Assert.That(body.Geometry[0].GetBounds().Center, Is.EqualTo(originalBounds.Center));
            Assert.That(body.LastError, Is.Empty);
        }

        [Test]
        public void PreviewRecollectsDeepHierarchyAndRespectsDisabledNestedBodies()
        {
            CollisionBodyBehaviour body = Create("Body").AddComponent<CollisionBodyBehaviour>();
            Create("Own", body.transform).AddComponent<CircleShapeBehaviour>();
            GameObject group = Create("Group", body.transform);
            GameObject deep = Create("Deep", group.transform);
            Create("Box", deep.transform).AddComponent<BoxShapeBehaviour>();
            Assert.That(body.RefreshGeometry(), Is.True);
            Assert.That(body.ShapeCount, Is.EqualTo(2));
            CollisionBodyBehaviour nested = deep.AddComponent<CollisionBodyBehaviour>();
            nested.enabled = false;

            CollisionGizmoPreview preview = new CollisionGizmoPreview();
            Assert.That(preview.Capture(body), Is.True, preview.Error);
            Assert.That(preview.Geometry.Count, Is.EqualTo(1));
            Assert.That(body.ShapeCount, Is.EqualTo(2));
            Assert.That(preview.Capture(nested), Is.False);
            Assert.That(preview.Geometry.Count, Is.Zero);
        }

        [Test]
        public void FailureClearsPreviewButLeavesDetectionCacheAndErrorUntouched()
        {
            CollisionBodyBehaviour body = Create("Body").AddComponent<CollisionBodyBehaviour>();
            CircleShapeBehaviour shape = Create("Circle", body.transform).AddComponent<CircleShapeBehaviour>();
            Assert.That(body.RefreshGeometry(), Is.True);
            CompoundShape2D original = body.Geometry;
            CollisionGizmoPreview preview = new CollisionGizmoPreview();
            Assert.That(preview.Capture(body), Is.True);
            shape.transform.localScale = new Vector3(1, 1, 2);
            Assert.That(preview.Capture(body), Is.False);
            Assert.That(preview.Error, Is.Not.Empty);
            Assert.That(preview.Geometry.Count, Is.Zero);
            Assert.That(body.Geometry, Is.SameAs(original));
            Assert.That(body.LastError, Is.Empty);
            shape.enabled = false;
            Assert.That(preview.Capture(body), Is.False);
            Assert.That(preview.Error, Does.Contain("no active"));
        }

        [Test]
        public void LogicalHeightDoesNotMoveThePreviewPlaneAndUnionMatchesDetection()
        {
            CollisionBodyBehaviour body = Create("Body").AddComponent<CollisionBodyBehaviour>();
            body.transform.position = new Vector3(0, 8, 0);
            Create("Circle", body.transform).AddComponent<CircleShapeBehaviour>();
            BoxShapeBehaviour shape = Create("Box", body.transform).AddComponent<BoxShapeBehaviour>();
            shape.transform.localPosition = new Vector3(5, 13, 2);
            Assert.That(body.RefreshGeometry(), Is.True);
            Assert.That(body.TryGetBounds(out Aabb2D expected), Is.True);
            CollisionGizmoPreview preview = new CollisionGizmoPreview();
            body.LogicalHeight = LogicalHeight.Level3;
            Assert.That(preview.Capture(body), Is.True, preview.Error);
            Assert.That(preview.NormalCoordinate, Is.EqualTo(8));
            Assert.That(preview.Geometry.TryGetBounds(out Aabb2D actual), Is.True);
            Assert.That(actual.Center, Is.EqualTo(expected.Center));
            Assert.That(actual.HalfExtents, Is.EqualTo(expected.HalfExtents));
            Assert.That(preview.Capture(null), Is.False);
            Assert.That(preview.Geometry.TryGetBounds(out _), Is.False);
        }

        [Test]
        public void InvalidOutlineArgumentsDoNotLeaveStaleVertices()
        {
            List<Vector3> points = new List<Vector3> { Vector3.one };
            WorldShape2D circle = WorldShape2D.FromCircle(new Circle2D(FixVector2.Zero, Fix64.One));
            Assert.Throws<ArgumentException>(() => CollisionGizmoPreview.BuildOutline(default, CollisionPlane.XZ, 0, points));
            Assert.That(points, Is.Empty);
            Assert.Throws<ArgumentException>(() => CollisionGizmoPreview.BuildOutline(circle, (CollisionPlane)9, 0, points));
            Assert.Throws<ArgumentException>(() => CollisionGizmoPreview.BuildOutline(circle, CollisionPlane.XZ, float.NaN, points));
            Assert.Throws<ArgumentException>(() => CollisionGizmoPreview.BuildOutline(circle, CollisionPlane.XZ, 0, points, 7));
            Assert.Throws<ArgumentNullException>(() => CollisionGizmoPreview.BuildOutline(circle, CollisionPlane.XZ, 0, null));
        }
    }
}
