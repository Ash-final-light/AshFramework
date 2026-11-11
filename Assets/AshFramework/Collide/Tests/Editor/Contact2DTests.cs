using System;
using System.Numerics;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class Contact2DTests
    {
        [Test]
        public void CircleContactRoundsTheThreeFourFiveNormalAndReversesIt()
        {
            Circle2D a = new Circle2D(FixVector2.Zero, 3);
            Circle2D b = new Circle2D(new FixVector2(3, 4), 3);
            FixVector2 expected = new FixVector2(Fix64.FromRaw(2576980378), Fix64.FromRaw(3435973837));

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertContact(contact, expected, Fix64.One);
            Assert.That(Collision2D.TryGetContact(b, a, out Contact2D reversed), Is.True);
            AssertContact(reversed, -expected, Fix64.One);
        }

        [Test]
        public void ConcentricCirclesChoosePositiveXAndUseTheSumOfRadii()
        {
            Circle2D a = new Circle2D(FixVector2.Zero, 4);
            Circle2D b = new Circle2D(FixVector2.Zero, 1);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertContact(contact, FixVector2.Right, 5);
            Assert.That(Collision2D.TryGetContact(b, a, out Contact2D reversed), Is.True);
            AssertContact(reversed, FixVector2.Right, 5);
        }

        [Test]
        public void SubRawCirclePenetrationRoundsUpToOneRawUnit()
        {
            Circle2D a = new Circle2D(FixVector2.Zero, Fix64.Epsilon);
            Circle2D b = new Circle2D(new FixVector2(Fix64.Epsilon, Fix64.Epsilon), Fix64.Epsilon);
            Fix64 diagonal = Fix64.FromRaw(3037000500);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertContact(contact, new FixVector2(diagonal, diagonal), Fix64.Epsilon);
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        public void CirclesDistinguishTangencyFromOneRawUnitOfSeparation(long gap, bool expected)
        {
            Circle2D a = new Circle2D(FixVector2.Zero, 2);
            Circle2D b = new Circle2D(new FixVector2(3 + Fix64.FromRaw(gap), 0), 1);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.EqualTo(expected));
            Assert.That(Collision2D.Overlaps(a, b), Is.EqualTo(expected));
            AssertContact(contact, expected ? FixVector2.Right : FixVector2.Zero, Fix64.Zero);
        }

        [Test]
        public void ContainedBoxUsesTheDistanceToTheOuterBoundary()
        {
            Aabb2D outer = new Aabb2D(FixVector2.Zero, new FixVector2(5, 10));
            Aabb2D inner = new Aabb2D(new FixVector2(2, 0), FixVector2.One);
            Aabb2D atBoundary = new Aabb2D(new FixVector2(6, 0), FixVector2.One);

            Assert.That(Collision2D.TryGetContact(outer, inner, out Contact2D contact), Is.True);
            AssertContact(contact, FixVector2.Right, 4);
            Assert.That(Collision2D.TryGetContact(inner, outer, out Contact2D reversed), Is.True);
            AssertContact(reversed, -FixVector2.Right, 4);
            Assert.That(Collision2D.TryGetContact(outer, atBoundary, out Contact2D touching), Is.True);
            AssertContact(touching, FixVector2.Right, Fix64.Zero);
        }

        [Test]
        public void EqualDepthBoxAxesPreferXAndCenteredBoxesChoosePositiveX()
        {
            Aabb2D a = new Aabb2D(FixVector2.Zero, FixVector2.One);
            Aabb2D b = new Aabb2D(new FixVector2(-1, 1), FixVector2.One);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertContact(contact, -FixVector2.Right, Fix64.One);
            Assert.That(Collision2D.TryGetContact(a, a, out Contact2D centered), Is.True);
            AssertContact(centered, FixVector2.Right, 2);
        }

        [Test]
        public void BoxContactChoosesYAndRejectsAOneRawUnitGap()
        {
            Aabb2D a = new Aabb2D(FixVector2.Zero, new FixVector2(3, 1));
            Aabb2D b = new Aabb2D(new FixVector2(0, -1), FixVector2.One);
            Aabb2D separated = new Aabb2D(new FixVector2(0, -2 - Fix64.Epsilon), FixVector2.One);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertContact(contact, -FixVector2.Up, Fix64.One);
            Assert.That(Collision2D.TryGetContact(a, separated, out Contact2D none), Is.False);
            Assert.That(Collision2D.Overlaps(a, separated), Is.False);
            AssertContact(none, FixVector2.Zero, Fix64.Zero);
        }

        [Test]
        public void LargestRepresentableDepthSucceedsAndLargerDepthThrows()
        {
            Circle2D a = new Circle2D(FixVector2.Zero, Fix64.FromRaw(long.MaxValue / 2));
            Circle2D b = new Circle2D(FixVector2.Zero, Fix64.FromRaw(long.MaxValue / 2 + 1));
            FixVector2 largeExtents = new FixVector2(b.Radius, b.Radius);
            Aabb2D box = new Aabb2D(FixVector2.Zero, largeExtents);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertContact(contact, FixVector2.Right, Fix64.MaxValue);
            Assert.That(Collision2D.Overlaps(b, b), Is.True);
            Assert.That(Collision2D.Overlaps(box, box), Is.True);
            Assert.Throws<OverflowException>(() => Collision2D.TryGetContact(b, b, out _));
            Assert.Throws<OverflowException>(() => Collision2D.TryGetContact(box, box, out _));
        }

        [Test]
        public void UninitializedCircleAndBoxInputsThrow()
        {
            Circle2D circle = new Circle2D(FixVector2.Zero, 1);
            Aabb2D box = new Aabb2D(FixVector2.Zero, FixVector2.One);

            Assert.Throws<ArgumentException>(() => Collision2D.TryGetContact(default(Circle2D), circle, out _));
            Assert.Throws<ArgumentException>(() => Collision2D.TryGetContact(circle, default(Circle2D), out _));
            Assert.Throws<ArgumentException>(() => Collision2D.TryGetContact(default(Aabb2D), box, out _));
            Assert.Throws<ArgumentException>(() => Collision2D.TryGetContact(box, default(Aabb2D), out _));
        }

        [Test]
        public void CircleAgainstWallHasHalfUnitDepthAndZeroDepthAtTangency()
        {
            Aabb2D wall = new Aabb2D(new FixVector2(3, 0), new FixVector2(1, 2));
            Circle2D circle = new Circle2D(new FixVector2(1 + Fix64.Half, 0), 1);
            Circle2D atBoundary = new Circle2D(FixVector2.Right, 1);

            Assert.That(Collision2D.TryGetContact(circle, wall, out Contact2D contact), Is.True);
            AssertContact(contact, FixVector2.Right, Fix64.Half);
            Assert.That(Collision2D.TryGetContact(wall, circle, out Contact2D reversed), Is.True);
            AssertContact(reversed, -FixVector2.Right, Fix64.Half);
            Assert.That(Collision2D.TryGetContact(atBoundary, wall, out Contact2D touching), Is.True);
            AssertContact(touching, FixVector2.Right, Fix64.Zero);
        }

        [Test]
        public void CircleInsideBoxUsesTheNearestFacePlusTheRadius()
        {
            Aabb2D box = new Aabb2D(FixVector2.Zero, new FixVector2(4, 5));
            Circle2D circle = new Circle2D(new FixVector2(2, 0), 1);

            Assert.That(Collision2D.TryGetContact(box, circle, out Contact2D contact), Is.True);
            AssertContact(contact, FixVector2.Right, 3);
            Assert.That(Collision2D.TryGetContact(circle, box, out Contact2D reversed), Is.True);
            AssertContact(reversed, -FixVector2.Right, 3);
        }

        [Test]
        public void CircleAtBoxCornerUsesTheDirectionToTheClosestPoint()
        {
            Aabb2D box = new Aabb2D(FixVector2.Zero, new FixVector2(2, 3));
            Circle2D circle = new Circle2D(new FixVector2(5, 7), 6);
            FixVector2 expected = new FixVector2(Fix64.FromRaw(2576980378), Fix64.FromRaw(3435973837));

            Assert.That(Collision2D.TryGetContact(box, circle, out Contact2D contact), Is.True);
            AssertContact(contact, expected, Fix64.One);
        }

        [Test]
        public void CircleAtRotatedFaceUsesTheLengthOfTheQuantizedAxis()
        {
            Obb2D box = new Obb2D(FixVector2.Zero, new FixVector2(2, 3), FixMath.Pi / 4);
            Circle2D circle = new Circle2D(3 * box.AxisX, 2);

            Assert.That(Collision2D.TryGetContact(circle, box, out Contact2D contact), Is.True);
            AssertRoundedAxis(contact.Normal, -box.AxisX);
            // The face lies at 2 * AxisX and the circle center at 3 * AxisX.
            BigInteger floorDistance = 2 * Fix64.Scale - contact.Depth.RawValue;
            BigInteger lengthSquared = RawLengthSquared(box.AxisX);
            Assert.That(floorDistance * floorDistance, Is.LessThanOrEqualTo(lengthSquared));
            Assert.That((floorDistance + 1) * (floorDistance + 1), Is.GreaterThan(lengthSquared));
            Assert.That(Collision2D.TryGetContact(box, circle, out Contact2D reversed), Is.True);
            AssertContact(reversed, -contact.Normal, contact.Depth);
        }

        [Test]
        public void CircleInsideRotatedBoxUsesTheWorldDistanceToTheNearestFace()
        {
            Obb2D box = new Obb2D(FixVector2.Zero, new FixVector2(2, 3), FixMath.Pi / 6);
            Circle2D circle = new Circle2D(box.AxisX, 1);

            Assert.That(Collision2D.TryGetContact(box, circle, out Contact2D contact), Is.True);
            AssertRoundedAxis(contact.Normal, box.AxisX);
            AssertCeilingLength(contact.Depth.RawValue - Fix64.Scale, RawLengthSquared(box.AxisX));
        }

        [Test]
        public void RotatedCornerCanHavePositivePenetrationSmallerThanOneRawUnit()
        {
            FixVector2 extents = new FixVector2(Fix64.Epsilon, Fix64.Epsilon);
            Obb2D box = new Obb2D(FixVector2.Zero, extents, FixMath.Pi / 4);
            Circle2D overlaps = new Circle2D(new FixVector2(Fix64.FromRaw(2), 0), Fix64.Epsilon);
            Circle2D separated = new Circle2D(new FixVector2(Fix64.FromRaw(3), 0), Fix64.Epsilon);

            Assert.That(Collision2D.TryGetContact(overlaps, box, out Contact2D contact), Is.True);
            Assert.That(contact.Depth, Is.EqualTo(Fix64.Epsilon));
            Assert.That(Collision2D.TryGetContact(box, separated, out Contact2D none), Is.False);
            AssertContact(none, FixVector2.Zero, Fix64.Zero);
        }

        [Test]
        public void RotatedBoxAgainstAlignedWallUsesItsExtremeCorner()
        {
            Aabb2D wall = new Aabb2D(FixVector2.Zero, new FixVector2(1, 20));
            Obb2D box = new Obb2D(new FixVector2(2, 0), FixVector2.One, FixMath.Pi / 4);
            Fix64 cornerOffset = box.AxisX.X + box.AxisX.Y;
            Fix64 depth = cornerOffset - Fix64.One;
            Obb2D touching = new Obb2D(new FixVector2(1 + cornerOffset, 0), box.HalfExtents, box.Rotation);
            Obb2D separated = new Obb2D(touching.Center + new FixVector2(Fix64.Epsilon, 0), box.HalfExtents, box.Rotation);

            Assert.That(Collision2D.TryGetContact(wall, box, out Contact2D contact), Is.True);
            AssertContact(contact, FixVector2.Right, depth);
            Assert.That(Collision2D.TryGetContact(box, wall, out Contact2D reversed), Is.True);
            AssertContact(reversed, -FixVector2.Right, depth);
            Assert.That(Collision2D.TryGetContact(wall, touching, out Contact2D atBoundary), Is.True);
            AssertContact(atBoundary, FixVector2.Right, Fix64.Zero);
            Assert.That(Collision2D.TryGetContact(wall, separated, out Contact2D none), Is.False);
            AssertContact(none, FixVector2.Zero, Fix64.Zero);
        }

        [Test]
        public void ParallelRotatedBoxesMeasureDepthInWorldUnits()
        {
            Obb2D a = new Obb2D(FixVector2.Zero, new FixVector2(1, 3), FixMath.Pi / 6);
            Obb2D b = new Obb2D(a.AxisX, a.HalfExtents, a.Rotation);
            Obb2D touching = new Obb2D(2 * a.AxisX, a.HalfExtents, a.Rotation);
            Obb2D separated = new Obb2D(touching.Center + new FixVector2(Fix64.Epsilon, 0), a.HalfExtents, a.Rotation);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertRoundedAxis(contact.Normal, a.AxisX);
            AssertCeilingLength(contact.Depth.RawValue, RawLengthSquared(a.AxisX));
            Assert.That(Collision2D.TryGetContact(b, a, out Contact2D reversed), Is.True);
            AssertContact(reversed, -contact.Normal, contact.Depth);
            Assert.That(Collision2D.TryGetContact(a, touching, out Contact2D atBoundary), Is.True);
            AssertContact(atBoundary, contact.Normal, Fix64.Zero);
            Assert.That(Collision2D.TryGetContact(a, separated, out Contact2D none), Is.False);
            AssertContact(none, FixVector2.Zero, Fix64.Zero);
        }

        [Test]
        public void EqualDepthBoxAxesAreChosenIndependentlyOfArgumentOrder()
        {
            Obb2D a = new Obb2D(FixVector2.Zero, FixVector2.One, FixMath.HalfPi);
            Obb2D b = new Obb2D(FixVector2.Zero, FixVector2.One, Fix64.Zero);
            Obb2D rotated = new Obb2D(FixVector2.Zero, FixVector2.One, FixMath.Pi / 6);

            Assert.That(Collision2D.TryGetContact(a, b, out Contact2D contact), Is.True);
            AssertContact(contact, FixVector2.Right, 2);
            Assert.That(Collision2D.TryGetContact(b, a, out Contact2D reversed), Is.True);
            AssertContact(reversed, FixVector2.Right, 2);
            Assert.That(Collision2D.TryGetContact(rotated, rotated, out Contact2D tied), Is.True);
            AssertRoundedAxis(tied.Normal, rotated.AxisX);
            AssertCeilingLength(tied.Depth.RawValue, 4 * RawLengthSquared(rotated.AxisX));
        }

        [Test]
        public void WideCenterDifferencesCanBeSeparatedAndRotatedDepthCanOverflow()
        {
            FixVector2 left = new FixVector2(Fix64.FromRaw(long.MinValue + 1), 0);
            FixVector2 right = new FixVector2(Fix64.FromRaw(long.MaxValue - 1), 0);
            FixVector2 tiny = new FixVector2(Fix64.Epsilon, Fix64.Epsilon);
            Circle2D circle = new Circle2D(left, Fix64.Epsilon);
            Obb2D box = new Obb2D(right, tiny, Fix64.Zero);
            Fix64 large = Fix64.FromRaw(long.MaxValue / 2 + 1);
            Obb2D huge = new Obb2D(FixVector2.Zero, new FixVector2(large, large), FixMath.HalfPi);

            Assert.That(Collision2D.TryGetContact(circle, box, out Contact2D none), Is.False);
            AssertContact(none, FixVector2.Zero, Fix64.Zero);
            Assert.That(Collision2D.Overlaps(huge, huge), Is.True);
            Assert.Throws<OverflowException>(() => Collision2D.TryGetContact(huge, huge, out _));
        }

        [Test]
        public void ContactMatchesOverlapForAllNineOverloadsOnSeededShapes()
        {
            FixRandom random = new FixRandom(3801);
            for (int i = 0; i < 200; i++)
            {
                Obb2D obbA = RandomBox(ref random);
                Obb2D obbB = RandomBox(ref random);
                Circle2D circleA = new Circle2D(obbA.Center, obbA.HalfExtents.X);
                Circle2D circleB = new Circle2D(obbB.Center, obbB.HalfExtents.X);
                Aabb2D aabbA = new Aabb2D(obbA.Center, obbA.HalfExtents);
                Aabb2D aabbB = new Aabb2D(obbB.Center, obbB.HalfExtents);

                AssertParity(Collision2D.Overlaps(circleA, circleB),
                    Collision2D.TryGetContact(circleA, circleB, out Contact2D circleCircle), circleCircle);
                AssertParity(Collision2D.Overlaps(circleA, aabbB),
                    Collision2D.TryGetContact(circleA, aabbB, out Contact2D circleAabb), circleAabb);
                AssertParity(Collision2D.Overlaps(aabbB, circleA),
                    Collision2D.TryGetContact(aabbB, circleA, out Contact2D aabbCircle), aabbCircle);
                AssertParity(Collision2D.Overlaps(circleA, obbB),
                    Collision2D.TryGetContact(circleA, obbB, out Contact2D circleObb), circleObb);
                AssertParity(Collision2D.Overlaps(obbB, circleA),
                    Collision2D.TryGetContact(obbB, circleA, out Contact2D obbCircle), obbCircle);
                AssertParity(Collision2D.Overlaps(aabbA, aabbB),
                    Collision2D.TryGetContact(aabbA, aabbB, out Contact2D aabbAabb), aabbAabb);
                AssertParity(Collision2D.Overlaps(aabbA, obbB),
                    Collision2D.TryGetContact(aabbA, obbB, out Contact2D aabbObb), aabbObb);
                AssertParity(Collision2D.Overlaps(obbB, aabbA),
                    Collision2D.TryGetContact(obbB, aabbA, out Contact2D obbAabb), obbAabb);
                AssertParity(Collision2D.Overlaps(obbA, obbB),
                    Collision2D.TryGetContact(obbA, obbB, out Contact2D obbObb), obbObb);
            }
        }

        private static Obb2D RandomBox(ref FixRandom random)
        {
            FixVector2 center = new FixVector2(Fix64.FromRatio(random.Range(-50, 51), 10),
                Fix64.FromRatio(random.Range(-50, 51), 10));
            FixVector2 size = new FixVector2(Fix64.FromRatio(random.Range(1, 26), 10),
                Fix64.FromRatio(random.Range(1, 26), 10));
            return new Obb2D(center, size, Fix64.FromRatio(random.Range(-314, 315), 100));
        }

        private static void AssertParity(bool overlaps, bool hasContact, Contact2D contact)
        {
            Assert.That(hasContact, Is.EqualTo(overlaps));
            if (hasContact)
            {
                Assert.That(contact.Normal, Is.Not.EqualTo(FixVector2.Zero));
                Assert.That(contact.Depth, Is.GreaterThanOrEqualTo(Fix64.Zero));
            }
            else
            {
                AssertContact(contact, FixVector2.Zero, Fix64.Zero);
            }
        }

        private static BigInteger RawLengthSquared(FixVector2 axis)
        {
            BigInteger x = axis.X.RawValue;
            BigInteger y = axis.Y.RawValue;
            return x * x + y * y;
        }

        private static void AssertCeilingLength(long rounded, BigInteger squaredLength)
        {
            BigInteger upper = rounded;
            BigInteger lower = upper - 1;
            Assert.That(lower * lower, Is.LessThan(squaredLength));
            Assert.That(upper * upper, Is.GreaterThanOrEqualTo(squaredLength));
        }

        private static void AssertRoundedAxis(FixVector2 actual, FixVector2 axis)
        {
            BigInteger lengthSquared = RawLengthSquared(axis);
            AssertRoundedComponent(actual.X.RawValue, axis.X.RawValue, lengthSquared);
            AssertRoundedComponent(actual.Y.RawValue, axis.Y.RawValue, lengthSquared);
        }

        private static void AssertRoundedComponent(long rounded, long axis, BigInteger lengthSquared)
        {
            Assert.That(System.Math.Sign(rounded), Is.EqualTo(System.Math.Sign(axis)));
            BigInteger twiceComponent = 2 * BigInteger.Abs(rounded);
            BigInteger lower = BigInteger.Max(0, twiceComponent - 1);
            BigInteger upper = twiceComponent + 1;
            BigInteger scaled = 2 * (BigInteger)axis * Fix64.Scale;
            Assert.That(lower * lower * lengthSquared, Is.LessThanOrEqualTo(scaled * scaled));
            Assert.That(upper * upper * lengthSquared, Is.GreaterThan(scaled * scaled));
        }

        private static void AssertContact(Contact2D actual, FixVector2 normal, Fix64 depth)
        {
            Assert.That(actual.Normal, Is.EqualTo(normal));
            Assert.That(actual.Depth, Is.EqualTo(depth));
        }
    }
}
