using System;
using Brinehold.Core.Math;
using Xunit;

namespace Brinehold.Core.Tests
{
    /// <summary>
    /// Fix64 is the highest-risk primitive in the codebase: every simulated quantity flows through
    /// it, and an error here is a silent desync rather than a crash. These tests compare against a
    /// double-precision reference, which is legitimate in a test assembly — the ban on floating
    /// point applies to simulation code, not to the oracle we check it against.
    /// </summary>
    public class Fix64Tests
    {
        private const double Tolerance = 1e-6;

        [Fact]
        public void IntegerRoundTrip()
        {
            for (int i = -100000; i <= 100000; i += 997)
                Assert.Equal(i, Fix64.FromInt(i).ToInt());
        }

        [Theory]
        [InlineData(1, 2, 0.5)]
        [InlineData(-1, 2, -0.5)]
        [InlineData(1, 3, 0.333333)]
        [InlineData(22, 7, 3.142857)]
        public void FractionConstruction(int numerator, int denominator, double expected)
        {
            Fix64 v = Fix64.FromFraction(numerator, denominator);
            Assert.InRange(v.ToDouble(), expected - 1e-5, expected + 1e-5);
        }

        [Fact]
        public void AdditionAndSubtractionAreExact()
        {
            Fix64 a = Fix64.FromFraction(3, 4);
            Fix64 b = Fix64.FromFraction(1, 4);
            Assert.Equal(Fix64.One, a + b);
            Assert.Equal(Fix64.Half, a - b);
        }

        [Fact]
        public void MultiplicationMatchesReference()
        {
            var rng = new System.Random(12345);
            for (int i = 0; i < 20000; i++)
            {
                double da = rng.NextDouble() * 2000.0 - 1000.0;
                double db = rng.NextDouble() * 2000.0 - 1000.0;
                Fix64 fa = (Fix64)da;
                Fix64 fb = (Fix64)db;
                double expected = fa.ToDouble() * fb.ToDouble();
                double actual = (fa * fb).ToDouble();
                Assert.InRange(actual, expected - 1e-5, expected + 1e-5);
            }
        }

        [Fact]
        public void DivisionMatchesReference()
        {
            var rng = new System.Random(999);
            for (int i = 0; i < 20000; i++)
            {
                double da = rng.NextDouble() * 2000.0 - 1000.0;
                double db = rng.NextDouble() * 200.0 - 100.0;
                if (System.Math.Abs(db) < 0.01) continue;
                Fix64 fa = (Fix64)da;
                Fix64 fb = (Fix64)db;
                double expected = fa.ToDouble() / fb.ToDouble();
                double actual = (fa / fb).ToDouble();
                Assert.InRange(actual, expected - 1e-4, expected + 1e-4);
            }
        }

        [Fact]
        public void MultiplicationIsCommutative()
        {
            var rng = new System.Random(4242);
            for (int i = 0; i < 20000; i++)
            {
                Fix64 a = Fix64.FromRaw(NextRaw(rng));
                Fix64 b = Fix64.FromRaw(NextRaw(rng));
                Assert.Equal((a * b).Raw, (b * a).Raw);
            }
        }

        [Fact]
        public void DivisionByZeroThrows()
        {
            Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
        }

        [Fact]
        public void DivideThenMultiplyRoundTrips()
        {
            var rng = new System.Random(777);
            for (int i = 0; i < 5000; i++)
            {
                Fix64 a = (Fix64)(rng.NextDouble() * 1000.0 + 1.0);
                Fix64 b = (Fix64)(rng.NextDouble() * 100.0 + 1.0);
                Fix64 result = (a / b) * b;
                Assert.InRange(result.ToDouble(), a.ToDouble() - 1e-3, a.ToDouble() + 1e-3);
            }
        }

        [Fact]
        public void PiConstantIsAccurate()
        {
            Assert.InRange(Fix64.Pi.ToDouble(), System.Math.PI - 1e-9, System.Math.PI + 1e-9);
            Assert.InRange(Fix64.TwoPi.ToDouble(), 2 * System.Math.PI - 1e-9, 2 * System.Math.PI + 1e-9);
            Assert.InRange(Fix64.HalfPi.ToDouble(), System.Math.PI / 2 - 1e-9, System.Math.PI / 2 + 1e-9);
        }

        [Fact]
        public void ComparisonOperators()
        {
            Assert.True(Fix64.Zero < Fix64.One);
            Assert.True(Fix64.One > Fix64.Zero);
            Assert.True(Fix64.MinusOne < Fix64.Zero);
            Assert.True(Fix64.One >= Fix64.One);
            Assert.True(Fix64.One <= Fix64.One);
            Assert.True(Fix64.FromInt(5) != Fix64.FromInt(6));
        }

        private static long NextRaw(System.Random rng)
        {
            // Keep magnitudes modest so that products stay inside the representable range.
            return (long)((rng.NextDouble() * 2.0 - 1.0) * 1000.0 * (1L << 32));
        }
    }
}
