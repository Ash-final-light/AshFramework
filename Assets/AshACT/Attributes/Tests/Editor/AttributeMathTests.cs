using Framework.FixedPoint;
using NUnit.Framework;

namespace AshACT.Attributes.Tests
{
    public sealed class AttributeMathTests
    {
        [TestCase(long.MaxValue, 1L)]
        [TestCase(long.MinValue, -1L)]
        public void AddRejectsRawOverflow(long left, long right)
        {
            Assert.That(AttributeMath.Add(FP64.FromRaw(left), FP64.FromRaw(right), out FP64 value),
                Is.EqualTo(AttributeError.Overflow));
            Assert.That(value.Raw, Is.Zero);
        }

        [TestCase(long.MinValue, 1L)]
        [TestCase(long.MaxValue, -1L)]
        public void SubtractRejectsRawOverflow(long left, long right)
        {
            Assert.That(AttributeMath.Subtract(FP64.FromRaw(left), FP64.FromRaw(right), out _),
                Is.EqualTo(AttributeError.Overflow));
        }

        [Test]
        public void AddAndSubtractAcceptExactRepresentableBoundaries()
        {
            Assert.That(AttributeMath.Add(FP64.MinValue, FP64.MaxValue, out FP64 sum), Is.EqualTo(AttributeError.None));
            Assert.That(sum.Raw, Is.EqualTo(-1L));
            Assert.That(AttributeMath.Subtract(FP64.FromRaw(-1), FP64.MinValue, out FP64 difference),
                Is.EqualTo(AttributeError.None));
            Assert.That(difference.Raw, Is.EqualTo(long.MaxValue));
        }

        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        [TestCase(-1L)]
        [TestCase(1L)]
        public void WideMultiplicationAndDivisionPreserveExactIdentity(long raw)
        {
            FP64 value = FP64.FromRaw(raw);
            Assert.That(AttributeMath.Multiply(value, FP64.One, out FP64 product), Is.EqualTo(AttributeError.None));
            Assert.That(product.Raw, Is.EqualTo(raw));
            Assert.That(AttributeMath.Divide(value, FP64.One, out FP64 quotient), Is.EqualTo(AttributeError.None));
            Assert.That(quotient.Raw, Is.EqualTo(raw));
        }

        [Test]
        public void MultiplicationDetectsLostHighBitsAndNegativeBoundaryOverflow()
        {
            Assert.That(AttributeMath.Multiply(FP64.MaxValue, FP64.FromInt(2), out _), Is.EqualTo(AttributeError.Overflow));
            Assert.That(AttributeMath.Multiply(FP64.MinValue, FP64.NegOne, out _), Is.EqualTo(AttributeError.Overflow));
            Assert.That(AttributeMath.Multiply(FP64.MaxValue, FP64.NegOne, out FP64 value), Is.EqualTo(AttributeError.None));
            Assert.That(value.Raw, Is.EqualTo(-long.MaxValue));
        }

        [Test]
        public void DivisionDetectsLostQuotientBitsAndZeroDenominator()
        {
            Assert.That(AttributeMath.Divide(FP64.One, FP64.Epsilon, out _), Is.EqualTo(AttributeError.Overflow));
            Assert.That(AttributeMath.Divide(FP64.MinValue, FP64.NegOne, out _), Is.EqualTo(AttributeError.Overflow));
            Assert.That(AttributeMath.Divide(FP64.Zero, FP64.Zero, out _), Is.EqualTo(AttributeError.DivideByZero));
        }

        [Test]
        public void NegativeSubRawResultsRoundTowardNegativeInfinity()
        {
            Assert.That(AttributeMath.Multiply(FP64.FromRaw(-1), FP64.Half, out FP64 product), Is.EqualTo(AttributeError.None));
            Assert.That(product.Raw, Is.EqualTo(-1L));
            Assert.That(AttributeMath.Divide(FP64.FromRaw(-1), FP64.FromInt(2), out FP64 quotient), Is.EqualTo(AttributeError.None));
            Assert.That(quotient.Raw, Is.EqualTo(-1L));
            Assert.That(AttributeMath.Divide(FP64.One, FP64.FromInt(-3), out quotient), Is.EqualTo(AttributeError.None));
            Assert.That(quotient.Raw, Is.EqualTo(-1431655766L));
        }

        [Test]
        public void RepresentableResultsMatchExistingFP64RoundingAcrossSignedSamples()
        {
            long[] samples = { -999 * FP64.OneRaw - 17, -FP64.OneRaw, -3, 0, 7, FP64.HalfRaw, 231 * FP64.OneRaw + 31 };
            foreach (long leftRaw in samples)
            {
                foreach (long rightRaw in samples)
                {
                    FP64 left = FP64.FromRaw(leftRaw);
                    FP64 right = FP64.FromRaw(rightRaw);
                    Assert.That(AttributeMath.Multiply(left, right, out FP64 product), Is.EqualTo(AttributeError.None));
                    Assert.That(product.Raw, Is.EqualTo((left * right).Raw));
                    if (rightRaw != 0)
                    {
                        AttributeError error = AttributeMath.Divide(left, right, out FP64 quotient);
                        if (error == AttributeError.None)
                        {
                            Assert.That(quotient.Raw, Is.EqualTo((left / right).Raw));
                        }
                        else
                        {
                            Assert.That(error, Is.EqualTo(AttributeError.Overflow));
                        }
                    }
                }
            }
        }

        [TestCase("0", 0L)]
        [TestCase("+1.5", 6442450944L)]
        [TestCase("0.1", 429496730L)]
        [TestCase("-0.1", -429496730L)]
        [TestCase("1000000000", 4294967296000000000L)]
        [TestCase("-1000000000", -4294967296000000000L)]
        [TestCase("0.000000000116415321826934814453125", 0L)]
        [TestCase("-0.000000000116415321826934814453125", 0L)]
        [TestCase("0.000000000349245965480804443359375", 2L)]
        [TestCase("-0.000000000349245965480804443359375", -2L)]
        [TestCase("0.000000000582076609134674072265625", 2L)]
        public void DecimalConfigurationConversionUsesExactFractionsAndTiesToEven(string text, long expectedRaw)
        {
            Assert.That(AttributeMath.FromDecimalText(text, out FP64 value), Is.EqualTo(AttributeError.None));
            Assert.That(value.Raw, Is.EqualTo(expectedRaw));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("+")]
        [TestCase("1.")]
        [TestCase(".1")]
        [TestCase("1.2.3")]
        [TestCase("1e2")]
        [TestCase(" 1")]
        [TestCase("1,5")]
        [TestCase("NaN")]
        [TestCase("1000000001")]
        [TestCase("-1000000000.000000000000001")]
        public void InvalidDecimalConfigurationIsRejectedBeforeRounding(string text)
        {
            Assert.That(AttributeMath.FromDecimalText(text, out FP64 value), Is.EqualTo(AttributeError.InvalidConfiguration));
            Assert.That(value.Raw, Is.Zero);
        }
    }
}
