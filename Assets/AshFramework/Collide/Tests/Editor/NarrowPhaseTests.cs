using System;
using System.Numerics;
using AshFramework.Math;
using NUnit.Framework;

namespace AshFramework.Collide.Tests
{
    public sealed class NarrowPhaseTests
    {
        private static FixVector2 V(int x, int y)
        {
            return new FixVector2(x, y);
        }

        private static Obb2D Box(int x, int y, int halfX, int halfY, Fix64 angle)
        {
            return new Obb2D(V(x, y), V(halfX, halfY), angle);
        }

        [Test]
        public void CircleCircleIncludesTangencyAndContainment()
        {
            Circle2D a = new Circle2D(V(0, 0), 2);
            Assert.That(Collision2D.Overlaps(a, new Circle2D(V(4, 0), 2)), Is.True);
            Assert.That(Collision2D.Overlaps(a, new Circle2D(V(5, 0), 2)), Is.False);
            Assert.That(Collision2D.Overlaps(a, new Circle2D(V(0, 0), 1)), Is.True);
            Assert.That(Collision2D.Overlaps(a, a), Is.True);
        }

        [Test]
        public void CircleAabbIncludesEdgeCornerAndInterior()
        {
            Aabb2D box = new Aabb2D(V(0, 0), V(2, 2));
            Assert.That(Collision2D.Overlaps(new Circle2D(V(3, 0), 1), box), Is.True);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(5, 6), 5), box), Is.True);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(0, 0), 1), box), Is.True);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(3, 3), 1), box), Is.False);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(6, 6), 5), box), Is.False);
        }

        [Test]
        public void CircleObbUsesWorldCenterAndRotation()
        {
            Obb2D box = Box(10, 20, 3, 1, FixMath.HalfPi);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(12, 20), 1), box), Is.True);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(10, 24), 1), box), Is.True);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(13, 20), 1), box), Is.False);
            Assert.That(Collision2D.Overlaps(new Circle2D(V(10, 20), 1), box), Is.True);
        }

        [Test]
        public void AabbAabbIncludesEdgeCornerContainmentAndRawGap()
        {
            Aabb2D a = new Aabb2D(V(0, 0), V(2, 2));
            Assert.That(Collision2D.Overlaps(a, new Aabb2D(V(4, 0), V(2, 1))), Is.True);
            Assert.That(Collision2D.Overlaps(a, new Aabb2D(V(3, 3), V(1, 1))), Is.True);
            Assert.That(Collision2D.Overlaps(a, new Aabb2D(V(0, 0), V(1, 1))), Is.True);
            FixVector2 center = new FixVector2(Fix64.FromRaw(4 * Fix64.Scale + 1), 0);
            Assert.That(Collision2D.Overlaps(a, new Aabb2D(center, V(2, 1))), Is.False);
        }

        [Test]
        public void AabbObbUsesObbSeparatingAxes()
        {
            Aabb2D a = new Aabb2D(V(0, 0), V(1, 1));
            Obb2D separated = new Obb2D(V(2, 2), new FixVector2(2, Fix64.FromRatio(1, 10)),
                -FixMath.Pi / 4);
            Assert.That(Collision2D.Overlaps(a, separated.GetBounds()), Is.True);
            Assert.That(Collision2D.Overlaps(a, separated), Is.False);
            Assert.That(Collision2D.Overlaps(a, Box(0, 0, 3, 1, FixMath.Pi / 4)), Is.True);
            Assert.That(Collision2D.Overlaps(a, Box(2, 0, 1, 1, FixMath.HalfPi)), Is.True);
        }

        [Test]
        public void ObbObbSeparatesParallelThinBoxesDespiteOverlappingBounds()
        {
            Fix64 angle = FixMath.Pi / 4;
            FixVector2 extents = new FixVector2(3, Fix64.FromRatio(1, 10));
            Obb2D a = new Obb2D(V(0, 0), extents, angle);
            Obb2D b = new Obb2D(V(-1, 1), extents, angle);
            Assert.That(Collision2D.Overlaps(a.GetBounds(), b.GetBounds()), Is.True);
            Assert.That(Collision2D.Overlaps(a, b), Is.False);
            Assert.That(Collision2D.Overlaps(a, new Obb2D(V(0, 0), extents, -angle)), Is.True);
            Assert.That(Collision2D.Overlaps(a, Box(0, 0, 10, 10, -angle)), Is.True);
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        [TestCase(5, true)]
        public void EveryUnorderedPairDistinguishesContactFromOneRawGap(int pair, bool gap)
        {
            FixVector2 centerB = new FixVector2(Fix64.FromRaw(2 * Fix64.Scale + (gap ? 1 : 0)), 0);
            Circle2D circleA = new Circle2D(V(0, 0), 1);
            Circle2D circleB = new Circle2D(centerB, 1);
            Aabb2D aabbA = new Aabb2D(V(0, 0), V(1, 1));
            Aabb2D aabbB = new Aabb2D(centerB, V(1, 1));
            Obb2D obbA = Box(0, 0, 1, 1, FixMath.HalfPi);
            Obb2D obbB = new Obb2D(centerB, V(1, 1), -FixMath.HalfPi);
            bool result;
            switch (pair)
            {
                case 0:
                    result = Collision2D.Overlaps(circleA, circleB);
                    break;
                case 1:
                    result = Collision2D.Overlaps(circleA, aabbB);
                    break;
                case 2:
                    result = Collision2D.Overlaps(circleA, obbB);
                    break;
                case 3:
                    result = Collision2D.Overlaps(aabbA, aabbB);
                    break;
                case 4:
                    result = Collision2D.Overlaps(aabbA, obbB);
                    break;
                default:
                    result = Collision2D.Overlaps(obbA, obbB);
                    break;
            }

            Assert.That(result, Is.EqualTo(!gap));
        }

        [Test]
        public void TinyShapesDistinguishTangencyFromOneRawUnitGap()
        {
            Fix64 tiny = Fix64.Epsilon;
            FixVector2 extents = new FixVector2(tiny, tiny);
            Circle2D circle = new Circle2D(FixVector2.Zero, tiny);
            Aabb2D box = new Aabb2D(FixVector2.Zero, extents);
            Obb2D obb = new Obb2D(FixVector2.Zero, extents, Fix64.Zero);
            Circle2D touching = new Circle2D(new FixVector2(Fix64.FromRaw(2), 0), tiny);
            Circle2D separated = new Circle2D(new FixVector2(Fix64.FromRaw(3), 0), tiny);
            Assert.That(Collision2D.Overlaps(circle, touching), Is.True);
            Assert.That(Collision2D.Overlaps(circle, separated), Is.False);
            Assert.That(Collision2D.Overlaps(touching, box), Is.True);
            Assert.That(Collision2D.Overlaps(separated, box), Is.False);
            Assert.That(Collision2D.Overlaps(touching, obb), Is.True);
            Assert.That(Collision2D.Overlaps(separated, obb), Is.False);
            Assert.That(Collision2D.Overlaps(box, obb), Is.True);
        }

        [Test]
        public void CircleObbCompensatesForQuantizedAxisLengthAtRawScale()
        {
            // 极小旋转方框的右顶点约为 1.414 个 raw 单位，两个圆分别覆盖和远离该顶点。
            Obb2D box = new Obb2D(FixVector2.Zero, new FixVector2(Fix64.Epsilon, Fix64.Epsilon), FixMath.Pi / 4);
            BigInteger normSquared = (BigInteger)box.AxisX.X.RawValue * box.AxisX.X.RawValue
                + (BigInteger)box.AxisX.Y.RawValue * box.AxisX.Y.RawValue;
            Assert.That(normSquared == (BigInteger)Fix64.Scale * Fix64.Scale, Is.False);
            Circle2D touching = new Circle2D(new FixVector2(Fix64.FromRaw(2), 0), Fix64.Epsilon);
            Circle2D separated = new Circle2D(new FixVector2(Fix64.FromRaw(3), 0), Fix64.Epsilon);
            Assert.That(Collision2D.Overlaps(touching, box), Is.True);
            Assert.That(Collision2D.Overlaps(separated, box), Is.False);
        }

        [Test]
        public void LargeCoordinatesAndRadiusSumsDoNotOverflowPredicates()
        {
            Circle2D huge = new Circle2D(V(0, 0), 1500000000);
            Assert.That(Collision2D.Overlaps(huge, huge), Is.True);
            Circle2D left = new Circle2D(V(-1500000000, 0), 100);
            Circle2D right = new Circle2D(V(1500000000, 0), 100);
            Assert.That(Collision2D.Overlaps(left, right), Is.False);
            Aabb2D a = new Aabb2D(V(-1500000000, 0), V(100, 100));
            Obb2D b = Box(1500000000, 0, 100, 100, FixMath.Pi / 4);
            Assert.That(Collision2D.Overlaps(left, b), Is.False);
            Assert.That(Collision2D.Overlaps(right, a), Is.False);
            Assert.That(Collision2D.Overlaps(a, b), Is.False);
            Assert.That(Collision2D.Overlaps(a, new Aabb2D(V(1500000000, 0), V(100, 100))), Is.False);
            Assert.That(Collision2D.Overlaps(Box(-1500000000, 0, 100, 100, -FixMath.Pi / 4), b), Is.False);
        }

        [Test]
        public void BoundsExposeFullCircleAndAxisAlignedBox()
        {
            Aabb2D bounds = new Circle2D(V(3, 4), 2).GetBounds();
            Assert.That(bounds.Min, Is.EqualTo(V(1, 2)));
            Assert.That(bounds.Max, Is.EqualTo(V(5, 6)));
            Assert.That(bounds.GetBounds().HalfExtents, Is.EqualTo(V(2, 2)));
            Aabb2D rotated = Box(10, 20, 3, 1, FixMath.HalfPi).GetBounds();
            Assert.That(rotated.Min, Is.EqualTo(V(9, 17)));
            Assert.That(rotated.Max, Is.EqualTo(V(11, 23)));
        }

        [Test]
        public void ObbBoundsRoundOutwardForEveryExactRationalCorner()
        {
            FixRandom random = new FixRandom(1307);
            for (int i = 0; i < 300; i++)
            {
                Obb2D box = RandomBox(ref random);
                AssertBoundsContainCorners(box);
                Obb2D tiny = new Obb2D(box.Center,
                    new FixVector2(Fix64.Epsilon, Fix64.FromRaw(3)), box.Rotation);
                AssertBoundsContainCorners(tiny);
            }
        }

        private static void AssertBoundsContainCorners(Obb2D box)
        {
            Aabb2D bounds = box.GetBounds();
            BigInteger extentX = (BigInteger)box.HalfExtents.X.RawValue * BigInteger.Abs(box.AxisX.X.RawValue)
                + (BigInteger)box.HalfExtents.Y.RawValue * BigInteger.Abs(box.AxisY.X.RawValue);
            BigInteger extentY = (BigInteger)box.HalfExtents.X.RawValue * BigInteger.Abs(box.AxisX.Y.RawValue)
                + (BigInteger)box.HalfExtents.Y.RawValue * BigInteger.Abs(box.AxisY.Y.RawValue);
            Assert.That(extentX <= (BigInteger)bounds.HalfExtents.X.RawValue * Fix64.Scale, Is.True);
            Assert.That(extentY <= (BigInteger)bounds.HalfExtents.Y.RawValue * Fix64.Scale, Is.True);
            Assert.That(extentX > (BigInteger)(bounds.HalfExtents.X.RawValue - 1) * Fix64.Scale, Is.True);
            Assert.That(extentY > (BigInteger)(bounds.HalfExtents.Y.RawValue - 1) * Fix64.Scale, Is.True);
            Assert.That(bounds.Min.X <= box.Center.X && bounds.Max.X >= box.Center.X, Is.True);
        }

        [Test]
        public void ConstructorsRejectNonPositiveSizesAndUnrepresentableBounds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Circle2D(V(0, 0), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Circle2D(V(0, 0), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Aabb2D(V(0, 0), V(0, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Box(0, 0, 1, -1, 0));
            FixVector2 high = new FixVector2(Fix64.MaxValue, 0);
            FixVector2 low = new FixVector2(Fix64.MinValue, 0);
            Assert.Throws<OverflowException>(() => new Circle2D(high, 1));
            Assert.Throws<OverflowException>(() => new Aabb2D(low, V(1, 1)));
            Assert.Throws<OverflowException>(() => new Obb2D(high, V(1, 1), FixMath.Pi / 4));
            Assert.Throws<OverflowException>(() => new Obb2D(V(0, 0),
                new FixVector2(Fix64.MaxValue, Fix64.MaxValue), FixMath.Pi / 4));
        }

        [Test]
        public void CoreAssemblyHasNoUnityReferencesAndOnlyUsesExistingMathCore()
        {
            System.Reflection.AssemblyName[] references = typeof(Collision2D).Assembly.GetReferencedAssemblies();
            bool hasMath = false;
            foreach (System.Reflection.AssemblyName reference in references)
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False);
                if (reference.Name == "AshFramework.Math")
                {
                    hasMath = true;
                }
            }

            Assert.That(hasMath, Is.True);
        }

        [Test]
        public void AllNineOverloadsRejectDefaultShapes()
        {
            Circle2D circle = new Circle2D(V(0, 0), 1);
            Aabb2D aabb = new Aabb2D(V(0, 0), V(1, 1));
            Obb2D obb = Box(0, 0, 1, 1, 0);
            Assert.That(default(Circle2D).IsValid, Is.False);
            Assert.That(default(Aabb2D).IsValid, Is.False);
            Assert.That(default(Obb2D).IsValid, Is.False);
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(default(Circle2D), circle));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(circle, default(Aabb2D)));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(circle, default(Obb2D)));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(aabb, default(Circle2D)));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(aabb, default(Aabb2D)));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(aabb, default(Obb2D)));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(obb, default(Circle2D)));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(obb, default(Aabb2D)));
            Assert.Throws<ArgumentException>(() => Collision2D.Overlaps(obb, default(Obb2D)));
            Assert.Throws<ArgumentException>(() => default(Circle2D).GetBounds());
            Assert.Throws<ArgumentException>(() => default(Aabb2D).GetBounds());
            Assert.Throws<ArgumentException>(() => default(Obb2D).GetBounds());
        }

        [Test]
        public void PairOrderAndIntegerTranslationPreserveAllNineOverloads()
        {
            FixRandom random = new FixRandom(238);
            FixVector2 shift = V(17, -23);
            for (int i = 0; i < 300; i++)
            {
                Obb2D a = RandomBox(ref random);
                Obb2D b = RandomBox(ref random);
                Circle2D circleA = new Circle2D(a.Center, a.HalfExtents.X);
                Circle2D circleB = new Circle2D(b.Center, b.HalfExtents.X);
                Aabb2D aabbA = new Aabb2D(a.Center, a.HalfExtents);
                Aabb2D aabbB = new Aabb2D(b.Center, b.HalfExtents);
                Assert.That(Collision2D.Overlaps(circleA, circleB), Is.EqualTo(Collision2D.Overlaps(circleB, circleA)));
                Assert.That(Collision2D.Overlaps(circleA, aabbB), Is.EqualTo(Collision2D.Overlaps(aabbB, circleA)));
                Assert.That(Collision2D.Overlaps(circleA, b), Is.EqualTo(Collision2D.Overlaps(b, circleA)));
                Assert.That(Collision2D.Overlaps(aabbA, aabbB), Is.EqualTo(Collision2D.Overlaps(aabbB, aabbA)));
                Assert.That(Collision2D.Overlaps(aabbA, b), Is.EqualTo(Collision2D.Overlaps(b, aabbA)));
                Assert.That(Collision2D.Overlaps(a, b), Is.EqualTo(Collision2D.Overlaps(b, a)));
                Assert.That(Collision2D.Overlaps(a, b), Is.EqualTo(Collision2D.Overlaps(
                    new Obb2D(a.Center + shift, a.HalfExtents, a.Rotation),
                    new Obb2D(b.Center + shift, b.HalfExtents, b.Rotation))));
                Assert.That(Collision2D.Overlaps(circleA, b), Is.EqualTo(Collision2D.Overlaps(
                    new Circle2D(circleA.Center + shift, circleA.Radius),
                    new Obb2D(b.Center + shift, b.HalfExtents, b.Rotation))));
            }
        }

        [Test]
        public void RandomPairsMatchIndependentEdgeAndContainmentReference()
        {
            FixRandom random = new FixRandom(9317);
            int hits = 0;
            int misses = 0;
            for (int i = 0; i < 1000; i++)
            {
                Obb2D a = RandomBox(ref random);
                Obb2D b = RandomBox(ref random);
                Circle2D circleA = new Circle2D(a.Center, a.HalfExtents.X);
                Circle2D circleB = new Circle2D(b.Center, b.HalfExtents.X);
                Aabb2D aabbA = new Aabb2D(a.Center, a.HalfExtents);
                Aabb2D aabbB = new Aabb2D(b.Center, b.HalfExtents);
                Point[] cornersA = Corners(a);
                Point[] cornersB = Corners(b);
                Point[] alignedA = Corners(new Obb2D(a.Center, a.HalfExtents, 0));
                Point[] alignedB = Corners(new Obb2D(b.Center, b.HalfExtents, 0));
                Assert.That(Collision2D.Overlaps(a, b), Is.EqualTo(PolygonsOverlap(cornersA, cornersB)), $"OBB sample {i}");
                Assert.That(Collision2D.Overlaps(aabbA, b), Is.EqualTo(PolygonsOverlap(alignedA, cornersB)), $"mixed sample {i}");
                Assert.That(Collision2D.Overlaps(aabbA, aabbB), Is.EqualTo(PolygonsOverlap(alignedA, alignedB)), $"AABB sample {i}");
                Assert.That(Collision2D.Overlaps(circleA, b), Is.EqualTo(CirclePolygon(circleA, cornersB)), $"circle OBB sample {i}");
                Assert.That(Collision2D.Overlaps(circleA, aabbB), Is.EqualTo(CirclePolygon(circleA, alignedB)), $"circle AABB sample {i}");
                double dx = D(circleA.Center.X) - D(circleB.Center.X);
                double dy = D(circleA.Center.Y) - D(circleB.Center.Y);
                double r = D(circleA.Radius) + D(circleB.Radius);
                Assert.That(Collision2D.Overlaps(circleA, circleB), Is.EqualTo(dx * dx + dy * dy <= r * r));
                if (Collision2D.Overlaps(a, b))
                {
                    hits++;
                    Assert.That(Collision2D.Overlaps(a.GetBounds(), b.GetBounds()), Is.True);
                }
                else
                {
                    misses++;
                }
            }

            Assert.That(hits, Is.GreaterThan(50));
            Assert.That(misses, Is.GreaterThan(50));
        }

        private static Obb2D RandomBox(ref FixRandom random)
        {
            FixVector2 center = new FixVector2(Fix64.FromRatio(random.Range(-500, 501), 100),
                Fix64.FromRatio(random.Range(-500, 501), 100));
            FixVector2 size = new FixVector2(Fix64.FromRatio(random.Range(10, 301), 100),
                Fix64.FromRatio(random.Range(10, 301), 100));
            Fix64 angle = Fix64.FromRatio(random.Range(-314, 315), 100);
            return new Obb2D(center, size, angle);
        }

        private static double D(Fix64 value)
        {
            return (double)value.RawValue / Fix64.Scale;
        }

        private readonly struct Point
        {
            internal double X { get; }
            internal double Y { get; }

            internal Point(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        private static Point[] Corners(Obb2D box)
        {
            Point[] result = new Point[4];
            int[] xs = { -1, 1, 1, -1 };
            int[] ys = { -1, -1, 1, 1 };
            for (int i = 0; i < 4; i++)
            {
                result[i] = new Point(D(box.Center.X) + xs[i] * D(box.HalfExtents.X) * D(box.AxisX.X)
                    + ys[i] * D(box.HalfExtents.Y) * D(box.AxisY.X),
                    D(box.Center.Y) + xs[i] * D(box.HalfExtents.X) * D(box.AxisX.Y)
                    + ys[i] * D(box.HalfExtents.Y) * D(box.AxisY.Y));
            }

            return result;
        }

        private static double Side(Point a, Point b, Point point)
        {
            return (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
        }

        private static bool Contains(Point[] polygon, Point point)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                if (Side(polygon[i], polygon[(i + 1) % polygon.Length], point) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PolygonsOverlap(Point[] a, Point[] b)
        {
            if (Contains(a, b[0]) || Contains(b, a[0]))
            {
                return true;
            }

            for (int i = 0; i < a.Length; i++)
            {
                Point a0 = a[i];
                Point a1 = a[(i + 1) % a.Length];
                for (int j = 0; j < b.Length; j++)
                {
                    Point b0 = b[j];
                    Point b1 = b[(j + 1) % b.Length];
                    if (Side(a0, a1, b0) * Side(a0, a1, b1) < 0
                        && Side(b0, b1, a0) * Side(b0, b1, a1) < 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool CirclePolygon(Circle2D circle, Point[] polygon)
        {
            Point center = new Point(D(circle.Center.X), D(circle.Center.Y));
            if (Contains(polygon, center))
            {
                return true;
            }

            double radius = D(circle.Radius);
            for (int i = 0; i < polygon.Length; i++)
            {
                Point a = polygon[i];
                Point b = polygon[(i + 1) % polygon.Length];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double t = ((center.X - a.X) * dx + (center.Y - a.Y) * dy) / (dx * dx + dy * dy);
                t = System.Math.Max(0, System.Math.Min(1, t));
                double x = center.X - (a.X + t * dx);
                double y = center.Y - (a.Y + t * dy);
                if (x * x + y * y <= radius * radius)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
