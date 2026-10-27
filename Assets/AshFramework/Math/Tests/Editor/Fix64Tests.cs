using System;
using System.Globalization;
using NUnit.Framework;

namespace AshFramework.Math.Tests
{
    public sealed class Fix64Tests
    {
        [Test]
        public void RawAndRatioConstructionHaveExplicitMeaning()
        {
            Assert.That(Fix64.One.RawValue, Is.EqualTo(4294967296L));
            Assert.That(Fix64.FromRaw(2147483648L), Is.EqualTo(Fix64.Half));
            Assert.That(Fix64.FromRatio(3, 2).RawValue, Is.EqualTo(6442450944L));
            Assert.That(Fix64.FromRatio(-1, 3).RawValue, Is.EqualTo(-1431655765L));
        }

        [TestCase("0")]
        [TestCase("1.25")]
        [TestCase("-1.25")]
        [TestCase("2147483647.99999999976716935634613037109375")]
        [TestCase("-2147483648")]
        [TestCase("0.00000000023283064365386962890625")]
        public void ExactDecimalTextRoundTripsRawBits(string text)
        {
            Fix64 value = Fix64.Parse(text);
            Assert.That(Fix64.Parse(value.ToString()).RawValue, Is.EqualTo(value.RawValue));
            Assert.That(value.ToString(), Is.EqualTo(text));
        }

        [TestCase(1L)]
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        [TestCase(123456789012345L)]
        public void ArbitraryRawValuesRoundTripThroughText(long raw)
        {
            Fix64 value = Fix64.FromRaw(raw);
            Assert.That(Fix64.Parse(value.ToString()).RawValue, Is.EqualTo(raw));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("+")]
        [TestCase(".")]
        [TestCase("1,5")]
        [TestCase("1e3")]
        [TestCase("NaN")]
        [TestCase("2147483648")]
        [TestCase("-2147483648.1")]
        public void InvalidOrOutOfRangeTextIsRejected(string text)
        {
            Assert.That(Fix64.TryParse(text, out _), Is.False);
        }

        [TestCase("1.5", 1L, 1L, 2L, 2L)]
        [TestCase("-1.5", -1L, -2L, -1L, -2L)]
        [TestCase("1.25", 1L, 1L, 2L, 1L)]
        [TestCase("-1.25", -1L, -2L, -1L, -1L)]
        [TestCase("-2", -2L, -2L, -2L, -2L)]
        public void RoundingRulesAreExplicit(string text, long truncate, long floor, long ceil, long round)
        {
            Fix64 value = Fix64.Parse(text);
            Assert.That(value.TruncateToInt(), Is.EqualTo(truncate));
            Assert.That(value.FloorToInt(), Is.EqualTo(floor));
            Assert.That(value.CeilToInt(), Is.EqualTo(ceil));
            Assert.That(value.RoundToInt(), Is.EqualTo(round));
        }

        [Test]
        public void ArithmeticUsesWideIntermediatesAndTruncatesTowardZero()
        {
            Fix64 large = Fix64.FromInt(1000000000);
            Assert.That(large * Fix64.One, Is.EqualTo(large));
            Assert.That(large / Fix64.One, Is.EqualTo(large));
            Assert.That(Fix64.Parse("1.5") * 2, Is.EqualTo(Fix64.FromInt(3)));
            Assert.That(Fix64.FromRaw(-1) * Fix64.Half, Is.EqualTo(Fix64.Zero));
            Assert.That(Fix64.MinValue * -Fix64.Half, Is.EqualTo(Fix64.FromInt(1073741824)));
            Assert.That(Fix64.MaxValue / Fix64.One, Is.EqualTo(Fix64.MaxValue));
            Assert.That(Fix64.MinValue % Fix64.FromRaw(-1), Is.EqualTo(Fix64.Zero));
        }

        [Test]
        public void OverflowAndUndefinedOperationsFailExplicitly()
        {
            Assert.Throws<OverflowException>(() => Consume(Fix64.MaxValue + Fix64.Epsilon));
            Assert.Throws<OverflowException>(() => Consume(-Fix64.MinValue));
            Assert.Throws<OverflowException>(() => Consume(Fix64.MinValue * -Fix64.One));
            Assert.Throws<OverflowException>(() => Consume(Fix64.FromInt(2147483648L)));
            Assert.Throws<DivideByZeroException>(() => Consume(Fix64.One / Fix64.Zero));
            Assert.Throws<DivideByZeroException>(() => Consume(Fix64.FromRatio(1, 0)));
        }

        [Test]
        public void TextDoesNotDependOnCurrentCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.That(Fix64.FromRatio(5, 4).ToString(), Is.EqualTo("1.25"));
                Assert.That(Fix64.Parse("1.25"), Is.EqualTo(Fix64.FromRatio(5, 4)));
                Assert.That(Fix64.FromDecimal(1.25m), Is.EqualTo(Fix64.FromRatio(5, 4)));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        private static void Consume(Fix64 value)
        {
            GC.KeepAlive(value);
        }
    }
}
