using Brinehold.Core.Math;
using Xunit;

namespace Brinehold.Core.Tests
{
    public class FixMathTests
    {
        [Fact]
        public void SqrtMatchesReference()
        {
            var rng = new System.Random(31337);
            for (int i = 0; i < 20000; i++)
            {
                double d = rng.NextDouble() * 10000.0;
                Fix64 v = (Fix64)d;
                double expected = System.Math.Sqrt(v.ToDouble());
                double actual = FixMath.Sqrt(v).ToDouble();
                Assert.InRange(actual, expected - 1e-4, expected + 1e-4);
            }
        }

        [Fact]
        public void SqrtOfPerfectSquaresIsExact()
        {
            for (int i = 0; i <= 1000; i++)
            {
                Fix64 root = FixMath.Sqrt(Fix64.FromInt(i * i));
                Assert.InRange(root.ToDouble(), i - 1e-6, i + 1e-6);
            }
        }

        [Fact]
        public void SqrtOfZeroIsZero() => Assert.Equal(Fix64.Zero, FixMath.Sqrt(Fix64.Zero));

        [Fact]
        public void SqrtOfNegativeThrows()
            => Assert.Throws<System.ArgumentOutOfRangeException>(() => FixMath.Sqrt(Fix64.MinusOne));

        [Fact]
        public void SinMatchesReferenceAcrossFullRange()
        {
            for (int degrees = -720; degrees <= 720; degrees++)
            {
                Fix64 radians = Fix64.FromFraction(degrees, 180) * Fix64.Pi;
                double expected = System.Math.Sin(radians.ToDouble());
                double actual = FixMath.Sin(radians).ToDouble();
                Assert.InRange(actual, expected - 1e-5, expected + 1e-5);
            }
        }

        [Fact]
        public void CosMatchesReferenceAcrossFullRange()
        {
            for (int degrees = -720; degrees <= 720; degrees++)
            {
                Fix64 radians = Fix64.FromFraction(degrees, 180) * Fix64.Pi;
                double expected = System.Math.Cos(radians.ToDouble());
                double actual = FixMath.Cos(radians).ToDouble();
                Assert.InRange(actual, expected - 1e-5, expected + 1e-5);
            }
        }

        [Fact]
        public void PythagoreanIdentityHolds()
        {
            for (int degrees = 0; degrees < 360; degrees += 3)
            {
                Fix64 radians = Fix64.FromFraction(degrees, 180) * Fix64.Pi;
                Fix64 s = FixMath.Sin(radians);
                Fix64 c = FixMath.Cos(radians);
                double sum = (s * s + c * c).ToDouble();
                Assert.InRange(sum, 1.0 - 1e-4, 1.0 + 1e-4);
            }
        }

        [Fact]
        public void Atan2MatchesReferenceInAllQuadrants()
        {
            for (int degrees = -180; degrees < 180; degrees += 1)
            {
                double rad = degrees * System.Math.PI / 180.0;
                Fix64 y = (Fix64)System.Math.Sin(rad);
                Fix64 x = (Fix64)System.Math.Cos(rad);
                double expected = System.Math.Atan2(y.ToDouble(), x.ToDouble());
                double actual = FixMath.Atan2(y, x).ToDouble();
                // Wrap the comparison so that +pi and -pi are treated as equal.
                double delta = System.Math.Abs(actual - expected);
                if (delta > System.Math.PI) delta = System.Math.Abs(delta - 2 * System.Math.PI);
                Assert.InRange(delta, 0.0, 1e-3);
            }
        }

        [Fact]
        public void Atan2OfZeroVectorIsZero()
            => Assert.Equal(Fix64.Zero, FixMath.Atan2(Fix64.Zero, Fix64.Zero));

        [Fact]
        public void FloorCeilRound()
        {
            Assert.Equal(2, FixMath.Floor((Fix64)2.7).ToInt());
            Assert.Equal(-3, FixMath.Floor((Fix64)(-2.3)).ToInt());
            Assert.Equal(3, FixMath.Ceil((Fix64)2.3).ToInt());
            Assert.Equal(-2, FixMath.Ceil((Fix64)(-2.3)).ToInt());
            Assert.Equal(3, FixMath.Round((Fix64)2.5).ToInt());
            Assert.Equal(2, FixMath.Round((Fix64)2.4).ToInt());
            Assert.Equal(-3, FixMath.Round((Fix64)(-2.5)).ToInt());
        }

        [Fact]
        public void ClampAndMinMax()
        {
            Assert.Equal(Fix64.One, FixMath.Clamp(Fix64.FromInt(5), Fix64.Zero, Fix64.One));
            Assert.Equal(Fix64.Zero, FixMath.Clamp(Fix64.FromInt(-5), Fix64.Zero, Fix64.One));
            Assert.Equal(Fix64.Half, FixMath.Clamp(Fix64.Half, Fix64.Zero, Fix64.One));
            Assert.Equal(Fix64.Zero, FixMath.Min(Fix64.Zero, Fix64.One));
            Assert.Equal(Fix64.One, FixMath.Max(Fix64.Zero, Fix64.One));
        }

        [Fact]
        public void NormaliseAngleFoldsIntoRange()
        {
            for (int turns = -4; turns <= 4; turns++)
            {
                Fix64 angle = Fix64.Pi * Fix64.FromFraction(1, 4) + Fix64.TwoPi * turns;
                Fix64 normalised = FixMath.NormaliseAngle(angle);
                Assert.InRange(normalised.ToDouble(), System.Math.PI / 4 - 1e-6, System.Math.PI / 4 + 1e-6);
            }
        }
    }
}
