using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace AshFramework.Math.Tests
{
    public sealed class DeterminismTests
    {
        [Test]
        public void RandomMatchesPublishedSplitMix64SeedZeroSequence()
        {
            FixRandom random = new FixRandom(0);
            Assert.That(random.NextUInt64(), Is.EqualTo(0xE220A8397B1DCDAFUL));
            Assert.That(random.NextUInt64(), Is.EqualTo(0x6E789E6AA1B965F4UL));
            Assert.That(random.NextUInt64(), Is.EqualTo(0x06C45D188009454FUL));
        }

        [Test]
        public void RestoringRandomStateReplaysEverySubsequentOutput()
        {
            FixRandom random = new FixRandom(123);
            random.NextUInt64();
            ulong snapshot = random.State;
            ulong[] values = new ulong[100];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = random.NextUInt64();
            }

            random.RestoreState(snapshot);
            foreach (ulong value in values)
            {
                Assert.That(random.NextUInt64(), Is.EqualTo(value));
            }
        }

        [Test]
        public void RandomRangesRespectBoundsAndDegenerateIntervals()
        {
            FixRandom random = new FixRandom(17);
            ulong state = random.State;
            Assert.That(random.Range(5, 5), Is.EqualTo(5));
            Assert.That(random.Range(Fix64.One, Fix64.One), Is.EqualTo(Fix64.One));
            Assert.That(random.State, Is.EqualTo(state));
            for (int index = 0; index < 1000; index++)
            {
                Assert.That(random.Range(-3, 4), Is.InRange(-3, 3));
                Fix64 value = random.Range(Fix64.MinValue, Fix64.MaxValue);
                Assert.That(value >= Fix64.MinValue && value < Fix64.MaxValue, Is.True);
                Fix64 unit = random.NextUnit();
                Assert.That(unit >= Fix64.Zero && unit < Fix64.One, Is.True);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextUInt64(0));
            Assert.Throws<ArgumentException>(() => random.Range(2, 1));
        }

        [Test]
        public void RandomSamplingAndShuffleAreReproducible()
        {
            FixRandom first = new FixRandom(42);
            FixRandom second = new FixRandom(42);
            List<int> a = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
            List<int> b = new List<int>(a);
            first.Shuffle(a);
            second.Shuffle(b);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4, 5, 6 }));
            for (int index = 0; index < 100; index++)
            {
                FixVector2 circle = first.OnUnitCircle();
                Assert.That(circle, Is.EqualTo(second.OnUnitCircle()));
                FixMathTests.Near(circle.Magnitude, 1);
                FixVector2 inside = first.InsideUnitCircle();
                Assert.That(inside, Is.EqualTo(second.InsideUnitCircle()));
                Assert.That(inside.SqrMagnitude <= Fix64.One + Fix64.FromRaw(8), Is.True);
                FixVector3 sphere = first.OnUnitSphere();
                Assert.That(sphere, Is.EqualTo(second.OnUnitSphere()));
                FixMathTests.Near(sphere.Magnitude, 1);
            }
        }

        [Test]
        public void RationalAccumulatorPreservesTheWholeAcrossSixtySteps()
        {
            RationalAccumulator accumulator = new RationalAccumulator(60);
            long total = 0;
            for (int index = 0; index < 60; index++)
            {
                total += accumulator.Accumulate(Fix64.Scale);
            }

            Assert.That(total, Is.EqualTo(Fix64.Scale));
            Assert.That(accumulator.Remainder, Is.Zero);
            accumulator.Accumulate(-Fix64.Scale);
            long remainder = accumulator.Remainder;
            RationalAccumulator restored = new RationalAccumulator(60, remainder);
            Assert.That(restored.Accumulate(123), Is.EqualTo(accumulator.Accumulate(123)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RationalAccumulator(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => accumulator.RestoreRemainder(60));
        }

        [Test]
        public void BinaryEncodingIsLittleEndianAndPreservesSignedExtremeBits()
        {
            byte[] bytes = new byte[8];
            FixBinary.WriteInt64(bytes, 0, 0x0102030405060708L);
            Assert.That(bytes, Is.EqualTo(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }));
            FixBinary.Write(bytes, 0, Fix64.MinValue);
            Assert.That(FixBinary.ReadFix64(bytes, 0), Is.EqualTo(Fix64.MinValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixBinary.Write(new byte[7], 0, Fix64.One));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixBinary.ReadFix64(bytes, int.MaxValue));
            Assert.Throws<ArgumentNullException>(() => FixBinary.ReadFix64(null, 0));
        }

        [Test]
        public void BinaryEncodingRoundTripsVectorsAndQuaternionWithoutPartialWrites()
        {
            byte[] bytes = new byte[80];
            FixVector2 vector2 = new FixVector2(Fix64.MinValue, Fix64.MaxValue);
            FixVector3 vector3 = new FixVector3(1, -2, Fix64.Epsilon);
            FixQuaternion rotation = FixQuaternion.Euler(1, 2, 3);
            FixBinary.Write(bytes, 3, vector2);
            FixBinary.Write(bytes, 19, vector3);
            FixBinary.Write(bytes, 43, rotation);
            Assert.That(FixBinary.ReadVector2(bytes, 3), Is.EqualTo(vector2));
            Assert.That(FixBinary.ReadVector3(bytes, 19), Is.EqualTo(vector3));
            Assert.That(FixBinary.ReadQuaternion(bytes, 43), Is.EqualTo(rotation));
            byte[] tooSmall = new byte[15];
            Assert.Throws<ArgumentOutOfRangeException>(() => FixBinary.Write(tooSmall, 0, vector2));
            Assert.That(tooSmall, Is.EqualTo(new byte[15]));
        }

        [Test]
        public void StableHashMatchesFnv1aGoldenVectorAndExplicitBinaryOrder()
        {
            StableHash64 hash = new StableHash64();
            foreach (byte value in new byte[] { 104, 101, 108, 108, 111 })
            {
                hash.AddByte(value);
            }

            Assert.That(hash.Value, Is.EqualTo(0xA430D84680AABD0BUL));
            hash.Reset();
            FixVector3 vector = new FixVector3(-1, 2, 3);
            hash.Add(vector);
            byte[] bytes = new byte[24];
            FixBinary.Write(bytes, 0, vector);
            StableHash64 fromBytes = new StableHash64();
            foreach (byte value in bytes)
            {
                fromBytes.AddByte(value);
            }

            Assert.That(hash.Value, Is.EqualTo(fromBytes.Value));
        }

        [Test]
        public void RestoringMathStateReplaysOneThousandStepsBitForBit()
        {
            FixRandom random = new FixRandom(90210);
            FixVector2 position = new FixVector2(1, 2);
            FixVector2 velocity = new FixVector2(3, 4);
            FixVector2 savedPosition = position;
            FixVector2 savedVelocity = velocity;
            ulong savedRandom = random.State;
            ulong[] first = Simulate(random, ref position, ref velocity, 1000);
            random.RestoreState(savedRandom);
            position = savedPosition;
            velocity = savedVelocity;
            ulong[] second = Simulate(random, ref position, ref velocity, 1000);
            Assert.That(second, Is.EqualTo(first));
        }

        private static ulong[] Simulate(FixRandom random, ref FixVector2 position, ref FixVector2 velocity, int steps)
        {
            ulong[] result = new ulong[steps];
            Fix64 delta = Fix64.FromRatio(1, 50);
            StableHash64 hash = new StableHash64();
            for (int tick = 0; tick < steps; tick++)
            {
                FixVector2 acceleration = new FixVector2(random.Range(-2, 3), random.Range(-2, 3));
                velocity = FixVector2.ClampMagnitude(velocity + acceleration * delta, 10);
                position += velocity * delta;
                hash.Reset();
                hash.AddInt64(tick);
                hash.Add(position);
                hash.Add(velocity);
                hash.AddUInt64(random.State);
                result[tick] = hash.Value;
            }

            return result;
        }
    }
}
