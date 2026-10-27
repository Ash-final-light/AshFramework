using System;
using System.Collections.Generic;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using NUnit.Framework;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class UnityCollisionPlaneAdapterTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        private Transform CreateTransform()
        {
            GameObject gameObject = new GameObject("CollidePlaneAdapterTest");
            _objects.Add(gameObject);
            return gameObject.transform;
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

        private static Vector3 Normal(CollisionPlane plane)
        {
            return plane == CollisionPlane.XY ? Vector3.forward : Vector3.up;
        }

        private static void AssertAngle(Fix64 actual, int degrees)
        {
            Fix64 expected = FixMath.DegreesToRadians(degrees);
            decimal delta = System.Math.Abs(FixMath.DeltaAngle(expected, actual).ToDecimal());
            Assert.That(delta, Is.LessThan(0.000002m));
        }

        [TestCase(CollisionPlane.XY, 0)]
        [TestCase(CollisionPlane.XY, 90)]
        [TestCase(CollisionPlane.XY, -90)]
        [TestCase(CollisionPlane.XY, 180)]
        [TestCase(CollisionPlane.XY, 37)]
        [TestCase(CollisionPlane.XZ, 0)]
        [TestCase(CollisionPlane.XZ, 90)]
        [TestCase(CollisionPlane.XZ, -90)]
        [TestCase(CollisionPlane.XZ, 180)]
        [TestCase(CollisionPlane.XZ, 37)]
        public void ReadsWorldPositionAndSignedPlanarRotation(CollisionPlane plane, int degrees)
        {
            Transform transform = CreateTransform();
            transform.position = new Vector3(10, 5, 20);
            transform.rotation = Quaternion.AngleAxis(degrees, Normal(plane));
            Vector3 originalPosition = transform.position;
            Quaternion originalRotation = transform.rotation;
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out FixTransform2D pose), Is.True);
            Assert.That(pose.Position, Is.EqualTo(new FixVector2(10, plane == CollisionPlane.XY ? 5 : 20)));
            Assert.That(pose.Scale, Is.EqualTo(FixVector2.One));
            AssertAngle(pose.Rotation, plane == CollisionPlane.XY ? degrees : -degrees);
            Assert.That(transform.position, Is.EqualTo(originalPosition));
            Assert.That(transform.rotation, Is.EqualTo(originalRotation));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void UnusedCoordinateIsNotQuantizedOrTreatedAsLogicalHeight(CollisionPlane plane)
        {
            Transform transform = CreateTransform();
            transform.position = plane == CollisionPlane.XY
                ? new Vector3(10, 20, 10000000000f)
                : new Vector3(10, 10000000000f, 20);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out FixTransform2D pose), Is.True);
            Assert.That(pose.Position, Is.EqualTo(new FixVector2(10, 20)));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void IncludesParentPositionAndPlanarRotation(CollisionPlane plane)
        {
            Transform parent = CreateTransform();
            Transform child = CreateTransform();
            parent.position = new Vector3(3, 4, 5);
            parent.rotation = Quaternion.AngleAxis(40, Normal(plane));
            child.SetParent(parent, false);
            child.localPosition = new Vector3(2, 0, 0);
            child.localRotation = Quaternion.AngleAxis(20, Normal(plane));
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(child, plane, out FixTransform2D pose), Is.True);
            double radians = 40 * System.Math.PI / 180;
            Assert.That((double)pose.Position.X.ToDecimal(), Is.EqualTo(3 + 2 * System.Math.Cos(radians)).Within(0.000002));
            double expectedY = plane == CollisionPlane.XY
                ? 4 + 2 * System.Math.Sin(radians)
                : 5 - 2 * System.Math.Sin(radians);
            Assert.That((double)pose.Position.Y.ToDecimal(), Is.EqualTo(expectedY).Within(0.000002));
            AssertAngle(pose.Rotation, plane == CollisionPlane.XY ? 60 : -60);
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void ToleranceOnlyAllowsSmallNormalDeviation(CollisionPlane plane)
        {
            Transform transform = CreateTransform();
            transform.rotation = Quaternion.AngleAxis(0.0001f, Vector3.right);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out _), Is.True);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out _, 0), Is.False);
            transform.rotation = Quaternion.AngleAxis(0.1f, Vector3.right);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out FixTransform2D failed), Is.False);
            Assert.That(failed.Scale, Is.EqualTo(FixVector2.One));
            Assert.That(failed.Position, Is.EqualTo(FixVector2.Zero));
            Assert.That(failed.Rotation, Is.EqualTo(Fix64.Zero));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void InvertedNormalIsNotAcceptedAsAnInPlanePose(CollisionPlane plane)
        {
            Transform transform = CreateTransform();
            transform.rotation = Quaternion.AngleAxis(180, Vector3.right);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out _), Is.False);
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void RigidPoseDoesNotBakeOrModifyScale(CollisionPlane plane)
        {
            Transform transform = CreateTransform();
            transform.localScale = new Vector3(2, 3, 4);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out FixTransform2D pose), Is.True);
            Assert.That(pose.Scale, Is.EqualTo(FixVector2.One));
            Assert.That(transform.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void PositionOutsideFixedRangeReturnsFalse(CollisionPlane plane)
        {
            Transform transform = CreateTransform();
            transform.position = new Vector3(3000000000f, 0, 0);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, plane, out FixTransform2D pose), Is.False);
            Assert.That(pose.Scale, Is.EqualTo(FixVector2.One));
        }

        [Test]
        public void NullAndDestroyedTransformsReturnFalse()
        {
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(null, CollisionPlane.XZ, out _), Is.False);
            Transform transform = CreateTransform();
            UnityEngine.Object.DestroyImmediate(transform.gameObject);
            Assert.That(UnityCollisionPlaneAdapter.TryReadRigidPose(transform, CollisionPlane.XY, out _), Is.False);
        }

        [TestCase(-0.1f)]
        [TestCase(1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidToleranceIsRejected(float tolerance)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => UnityCollisionPlaneAdapter.TryReadRigidPose(
                null, CollisionPlane.XY, out _, tolerance));
        }

        [Test]
        public void InvalidPlaneIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => UnityCollisionPlaneAdapter.TryReadRigidPose(
                null, (CollisionPlane)99, out _));
        }
    }
}
