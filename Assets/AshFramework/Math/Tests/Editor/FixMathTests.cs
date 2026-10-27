using System;
using NUnit.Framework;

namespace AshFramework.Math.Tests
{
    public sealed class FixMathTests
    {
        internal static void Near(Fix64 actual, double expected, double tolerance = 0.00000002)
        {
            Assert.That((double)actual.ToDecimal(), Is.EqualTo(expected).Within(tolerance));
        }

        [TestCase(0)]
        [TestCase(30)]
        [TestCase(-30)]
        [TestCase(45)]
        [TestCase(60)]
        [TestCase(90)]
        [TestCase(-90)]
        [TestCase(180)]
        [TestCase(-180)]
        [TestCase(270)]
        [TestCase(360)]
        [TestCase(10000)]
        public void TrigonometryMatchesIndependentReferenceAcrossQuadrants(int degrees)
        {
            Fix64 radians = FixMath.DegreesToRadians(degrees);
            FixMath.SinCos(radians, out Fix64 sin, out Fix64 cos);
            double reference = degrees * System.Math.PI / 180;
            Near(sin, System.Math.Sin(reference));
            Near(cos, System.Math.Cos(reference));
            Near(FixMath.RadiansToDegrees(radians), degrees, 0.0000001);
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(0, -1)]
        [TestCase(-1, 0)]
        [TestCase(3, 4)]
        [TestCase(3, -4)]
        [TestCase(-3, -4)]
        [TestCase(-3, 4)]
        public void Atan2HandlesAxesAndQuadrants(int y, int x)
        {
            Near(FixMath.Atan2(y, x), System.Math.Atan2(y, x));
        }

        [Test]
        public void Atan2HandlesZeroAndExtremeRawValues()
        {
            Assert.That(FixMath.Atan2(0, 0), Is.EqualTo(Fix64.Zero));
            Near(FixMath.Atan2(Fix64.MinValue, Fix64.MaxValue), -System.Math.PI / 4);
            Near(FixMath.Atan2(Fix64.Epsilon, Fix64.Epsilon), System.Math.PI / 4);
        }

        [TestCase("-1")]
        [TestCase("-0.5")]
        [TestCase("0")]
        [TestCase("0.5")]
        [TestCase("1")]
        public void InverseTrigonometryHandlesDomainEndpoints(string text)
        {
            Fix64 value = Fix64.Parse(text);
            Near(FixMath.Asin(value), System.Math.Asin((double)value.ToDecimal()));
            Near(FixMath.Acos(value), System.Math.Acos((double)value.ToDecimal()));
        }

        [TestCase("0")]
        [TestCase("0.25")]
        [TestCase("2")]
        [TestCase("1000000000")]
        public void SqrtMatchesReferenceWithinOneRawUnit(string text)
        {
            Fix64 value = Fix64.Parse(text);
            Near(FixMath.Sqrt(value), System.Math.Sqrt((double)value.ToDecimal()), 0.000000001);
        }

        [Test]
        public void ScalarHelpersHaveDefinedBoundaryBehavior()
        {
            Assert.That(FixMath.Lerp(0, 10, 2), Is.EqualTo(Fix64.FromInt(10)));
            Assert.That(FixMath.LerpUnclamped(0, 10, 2), Is.EqualTo(Fix64.FromInt(20)));
            Assert.That(FixMath.Lerp(Fix64.MinValue, Fix64.MaxValue, Fix64.One), Is.EqualTo(Fix64.MaxValue));
            Assert.That(FixMath.InverseLerp(10, 0, 5), Is.EqualTo(Fix64.Half));
            Assert.That(FixMath.InverseLerp(1, 1, 3), Is.EqualTo(Fix64.Zero));
            Assert.That(FixMath.Repeat(-1, 3), Is.EqualTo(Fix64.FromInt(2)));
            Assert.That(FixMath.PingPong(-1, 3), Is.EqualTo(Fix64.One));
            Assert.That(FixMath.SmoothStep(0, 10, Fix64.Half), Is.EqualTo(Fix64.FromInt(5)));
            Assert.That(FixMath.MoveTowards(0, 2, 3), Is.EqualTo(Fix64.FromInt(2)));
            Assert.That(FixMath.Pow(2, -3), Is.EqualTo(Fix64.FromRatio(1, 8)));
            Assert.That(FixMath.Pow(0, 0), Is.EqualTo(Fix64.One));
            Assert.That(FixMath.Approximately(Fix64.MinValue, Fix64.MaxValue, 1), Is.False);
        }

        [Test]
        public void AngleHelpersCrossTheWrapBoundaryAlongTheShortestArc()
        {
            Fix64 from = FixMath.DegreesToRadians(350);
            Fix64 to = FixMath.DegreesToRadians(10);
            Near(FixMath.RadiansToDegrees(FixMath.DeltaAngle(from, to)), 20, 0.000001);
            Near(FixMath.LerpAngle(from, to, Fix64.Half), 0);
            Near(FixMath.RadiansToDegrees(FixMath.MoveTowardsAngle(from, to,
                FixMath.DegreesToRadians(5))), -5, 0.000001);
            Assert.That(FixMath.NormalizeDegrees(-10), Is.EqualTo(Fix64.FromInt(350)));
            Fix64 extremeTarget = FixMath.NormalizeRadians(Fix64.MaxValue) + 1;
            Fix64 extremeExpected = FixMath.NormalizeRadians(extremeTarget);
            Assert.That(FixMath.LerpAngle(Fix64.MaxValue, extremeTarget, 1), Is.EqualTo(extremeExpected));
            Assert.That(FixMath.MoveTowardsAngle(Fix64.MaxValue, extremeTarget, 2), Is.EqualTo(extremeExpected));
        }

        [Test]
        public void InvalidScalarArgumentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.Sqrt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.Asin(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.Acos(-2));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.Repeat(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.MoveTowards(0, 1, -1));
            Assert.Throws<ArgumentException>(() => FixMath.Clamp(0, 2, 1));
        }
    }
}
