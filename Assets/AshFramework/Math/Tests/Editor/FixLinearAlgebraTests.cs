using System;
using NUnit.Framework;

namespace AshFramework.Math.Tests
{
    public sealed class FixLinearAlgebraTests
    {
        private static void Near(FixVector2 actual, FixVector2 expected, double tolerance = 0.0000001)
        {
            FixMathTests.Near(actual.X, (double)expected.X.ToDecimal(), tolerance);
            FixMathTests.Near(actual.Y, (double)expected.Y.ToDecimal(), tolerance);
        }

        private static void Near(FixVector3 actual, FixVector3 expected, double tolerance = 0.0000001)
        {
            FixMathTests.Near(actual.X, (double)expected.X.ToDecimal(), tolerance);
            FixMathTests.Near(actual.Y, (double)expected.Y.ToDecimal(), tolerance);
            FixMathTests.Near(actual.Z, (double)expected.Z.ToDecimal(), tolerance);
        }

        [Test]
        public void NormalizeHandlesZeroTinyAndLargeVectors()
        {
            Assert.That(FixVector2.Zero.TryNormalize(out FixVector2 zero), Is.False);
            Assert.That(zero, Is.EqualTo(FixVector2.Zero));
            Assert.That(FixVector3.Zero.NormalizeOrFallback(FixVector3.Up), Is.EqualTo(FixVector3.Up));
            FixVector2 tiny = new FixVector2(Fix64.Epsilon, Fix64.Epsilon).Normalized;
            FixMathTests.Near(tiny.X, 1 / System.Math.Sqrt(2));
            FixMathTests.Near(tiny.Magnitude, 1);
            FixVector3 large = new FixVector3(Fix64.MinValue, Fix64.MaxValue, Fix64.MaxValue).Normalized;
            FixMathTests.Near(large.Magnitude, 1);
            Near(new FixVector3(3, 0, 4).Normalized,
                new FixVector3(Fix64.FromRatio(3, 5), 0, Fix64.FromRatio(4, 5)));
        }

        [Test]
        public void DotPreservesCancellationAcrossWideIntermediateProducts()
        {
            FixVector2 a = new FixVector2(1000000000, 1000000000);
            FixVector2 b = new FixVector2(1000000000, -1000000000);
            Assert.That(FixVector2.Dot(a, b), Is.EqualTo(Fix64.Zero));
            Assert.That(FixVector3.Cross(FixVector3.Right, FixVector3.Up), Is.EqualTo(FixVector3.Forward));
            Assert.That(FixVector2.Cross(FixVector2.Right, FixVector2.Up), Is.EqualTo(Fix64.One));
        }

        [Test]
        public void DistanceComparisonDoesNotOverflowSquaredFixedRange()
        {
            Assert.That(FixVector2.WithinDistance(new FixVector2(1000000000, 0),
                new FixVector2(-1000000000, 0), 2000000000), Is.True);
            Assert.That(FixVector3.WithinDistance(FixVector3.Zero, new FixVector3(3, 0, 4), 4), Is.False);
            Assert.That(FixVector3.Distance(FixVector3.Zero, new FixVector3(3, 0, 4)), Is.EqualTo(Fix64.FromInt(5)));
        }

        [Test]
        public void MovementProjectionAndReflectionMatchKnownGeometry()
        {
            Near(FixVector2.ClampMagnitude(new FixVector2(3, 4), 2),
                new FixVector2(Fix64.FromRatio(6, 5), Fix64.FromRatio(8, 5)));
            Assert.That(FixVector2.MoveTowards(FixVector2.Zero, FixVector2.Right, 2), Is.EqualTo(FixVector2.Right));
            Near(FixVector3.MoveTowards(FixVector3.Zero, new FixVector3(0, 0, 10), 2), new FixVector3(0, 0, 2));
            Near(FixVector3.Project(new FixVector3(3, 4, 5), FixVector3.Up), new FixVector3(0, 4, 0));
            Near(FixVector3.ProjectOnPlane(new FixVector3(3, 4, 5), FixVector3.Up), new FixVector3(3, 0, 5));
            Near(FixVector2.Reflect(new FixVector2(1, -1), FixVector2.Up), new FixVector2(1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixVector2.ClampMagnitude(FixVector2.One, -1));
        }

        [Test]
        public void VectorAnglesAndRotationUseRadians()
        {
            Near(FixVector2.Right.Rotate(FixMath.HalfPi), FixVector2.Up);
            FixMathTests.Near(FixVector2.SignedAngle(FixVector2.Up, FixVector2.Right), -System.Math.PI / 2);
            FixMathTests.Near(FixVector3.Angle(FixVector3.Right, -FixVector3.Right), System.Math.PI);
            FixMathTests.Near(FixVector3.SignedAngle(FixVector3.Right, FixVector3.Up, FixVector3.Forward), System.Math.PI / 2);
            Assert.That(FixVector2.Angle(FixVector2.Zero, FixVector2.Up), Is.EqualTo(Fix64.Zero));
        }

        [Test]
        public void QuaternionIdentityAndInversePreserveVectors()
        {
            FixVector3 point = new FixVector3(2, 3, 4);
            Assert.That(FixQuaternion.Identity.W, Is.EqualTo(Fix64.One));
            Assert.That(FixQuaternion.Identity * point, Is.EqualTo(point));
            FixQuaternion rotation = FixQuaternion.Euler(FixMath.DegreesToRadians(20),
                FixMath.DegreesToRadians(40), FixMath.DegreesToRadians(-10));
            Near(rotation.Inverse() * (rotation * point), point);
            Assert.Throws<InvalidOperationException>(() => default(FixQuaternion).Inverse());
            Assert.Throws<ArgumentException>(() => FixQuaternion.AngleAxis(1, FixVector3.Zero));
        }

        [Test]
        public void QuaternionAxisAndFromToCoverParallelAndOppositeDirections()
        {
            Near(FixQuaternion.AngleAxis(FixMath.HalfPi, FixVector3.Forward) * FixVector3.Right, FixVector3.Up);
            Near(FixQuaternion.FromToRotation(FixVector3.Right, -FixVector3.Right) * FixVector3.Right, -FixVector3.Right);
            Near(FixQuaternion.FromToRotation(FixVector3.Right, FixVector3.Up) * FixVector3.Right, FixVector3.Up);
            Assert.That(FixQuaternion.FromToRotation(FixVector3.Up, FixVector3.Up), Is.EqualTo(FixQuaternion.Identity));
        }

        [Test]
        public void QuaternionInterpolationTreatsOppositeSignsAsTheSameRotation()
        {
            FixQuaternion q = FixQuaternion.AngleAxis(FixMath.HalfPi, FixVector3.Forward);
            Near(FixQuaternion.Slerp(q, -q, Fix64.Half) * FixVector3.Right, FixVector3.Up);
            FixQuaternion half = FixQuaternion.Slerp(FixQuaternion.Identity, q, Fix64.Half);
            FixMathTests.Near(FixQuaternion.Angle(FixQuaternion.Identity, half), System.Math.PI / 4);
            Near(FixQuaternion.RotateTowards(FixQuaternion.Identity, q, FixMath.Pi) * FixVector3.Right, FixVector3.Up);
        }

        [Test]
        public void MatrixAndQuaternionRepresentTheSameRotation()
        {
            FixQuaternion q = FixQuaternion.Euler(FixMath.DegreesToRadians(25),
                FixMath.DegreesToRadians(100), FixMath.DegreesToRadians(-30));
            FixMatrix3x3 matrix = FixMatrix3x3.FromQuaternion(q);
            FixVector3 point = new FixVector3(2, -3, 4);
            Near(matrix * point, q * point);
            Near(FixQuaternion.FromRotationMatrix(matrix) * point, q * point);
            Near(matrix.Transpose() * (matrix * point), point);
            Near(FixQuaternion.LookRotation(FixVector3.Right, FixVector3.Up) * FixVector3.Forward, FixVector3.Right);
            Assert.Throws<ArgumentException>(() => FixQuaternion.LookRotation(FixVector3.Up, FixVector3.Up));
        }

        [Test]
        public void GeneralMatrixInverseHandlesNonuniformScaleAndSingularity()
        {
            FixMatrix3x3 matrix = new FixMatrix3x3(2, 1, 3, 0, 3, 4, 0, 0, 1);
            FixVector3 point = new FixVector3(4, -2, 1);
            Near(matrix.Inverse() * (matrix * point), point);
            Assert.That(matrix.Determinant, Is.EqualTo(Fix64.FromInt(6)));
            Assert.That(FixMatrix3x3.Zero.TryInverse(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => FixMatrix3x3.Zero.Inverse());
            FixMatrix3x3 tiny = new FixMatrix3x3(Fix64.FromRatio(1, 10000), 0, 0,
                0, Fix64.FromRatio(1, 10000), 0, 0, 0, 1);
            Near(tiny.Inverse() * (tiny * FixVector3.One), FixVector3.One, 0.00001);
        }

        [Test]
        public void TransformCompositionPreservesShearInsteadOfAddingAnglesAndPositions()
        {
            FixTransform2D parent = new FixTransform2D(new FixVector2(10, 20),
                FixMath.HalfPi, new FixVector2(2, 3));
            FixTransform2D child = new FixTransform2D(new FixVector2(1, 2),
                FixMath.DegreesToRadians(30), new FixVector2(4, 5));
            FixVector2 point = new FixVector2(3, -2);
            Near(parent.Compose(child).TransformPoint(point), parent.TransformPoint(child.TransformPoint(point)));
            Near(parent.InverseTransformPoint(parent.TransformPoint(point)), point);
            Near(parent.ToMatrix().Inverse().TransformPoint(parent.TransformPoint(point)), point);
            Near(parent.TransformDirection(FixVector2.Right), FixVector2.Up);
            Assert.Throws<InvalidOperationException>(() => new FixTransform2D(FixVector2.Zero, 0,
                FixVector2.Zero).InverseTransformPoint(point));
        }

        [Test]
        public void GeometryHandlesDegenerateSegmentsAndTriangleWeights()
        {
            Assert.That(FixGeometry.Orientation(FixVector2.Zero, FixVector2.Right, FixVector2.Up), Is.EqualTo(1));
            Assert.That(FixGeometry.ClosestPointOnSegment(new FixVector2(2, 3), FixVector2.Zero,
                new FixVector2(4, 0)), Is.EqualTo(new FixVector2(2, 0)));
            Assert.That(FixGeometry.ClosestPointOnSegment(new FixVector2(2, 3), FixVector2.One,
                FixVector2.One), Is.EqualTo(FixVector2.One));
            Near(FixGeometry.ClosestPointOnSegment(new FixVector3(3, 4, 2), FixVector3.Zero,
                new FixVector3(0, 0, 4)), new FixVector3(0, 0, 2));
            Assert.That(FixGeometry.TryBarycentric(new FixVector2(Fix64.Half, Fix64.Half),
                FixVector2.Zero, new FixVector2(2, 0), new FixVector2(0, 2), out FixVector3 weights), Is.True);
            Assert.That(weights, Is.EqualTo(new FixVector3(Fix64.Half, Fix64.FromRatio(1, 4), Fix64.FromRatio(1, 4))));
            Assert.That(FixGeometry.TryBarycentric(FixVector2.One, FixVector2.Zero,
                FixVector2.Right, new FixVector2(2, 0), out _), Is.False);
        }

        [Test]
        public void BezierCurvesPreserveEndpointsAndKnownMidpoints()
        {
            Assert.That(FixGeometry.QuadraticBezier(FixVector2.Zero, FixVector2.Up,
                FixVector2.Right, 0), Is.EqualTo(FixVector2.Zero));
            Assert.That(FixGeometry.QuadraticBezier(FixVector2.Zero, FixVector2.Up,
                FixVector2.Right, 1), Is.EqualTo(FixVector2.Right));
            Assert.That(FixGeometry.CubicBezier(FixVector3.Zero, new FixVector3(1, 0, 0),
                new FixVector3(2, 0, 0), new FixVector3(3, 0, 0), Fix64.Half),
                Is.EqualTo(new FixVector3(Fix64.FromRatio(3, 2), 0, 0)));
        }
    }
}
