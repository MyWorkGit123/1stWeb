using Brinehold.Core.Math;
using Xunit;

namespace Brinehold.Core.Tests
{
    public class Fix2Tests
    {
        [Fact]
        public void MagnitudeOfThreeFourFive()
        {
            Fix2 v = Fix2.FromInt(3, 4);
            Assert.InRange(v.Magnitude.ToDouble(), 5.0 - 1e-6, 5.0 + 1e-6);
            Assert.Equal(25, v.SqrMagnitude.ToInt());
        }

        [Fact]
        public void NormalisedIsUnitLength()
        {
            var rng = new System.Random(8080);
            for (int i = 0; i < 2000; i++)
            {
                Fix2 v = new Fix2((Fix64)(rng.NextDouble() * 200 - 100), (Fix64)(rng.NextDouble() * 200 - 100));
                if (v.SqrMagnitude == Fix64.Zero) continue;
                Assert.InRange(v.Normalised.Magnitude.ToDouble(), 1.0 - 1e-4, 1.0 + 1e-4);
            }
        }

        [Fact]
        public void NormalisedZeroVectorIsZero()
            => Assert.Equal(Fix2.Zero, Fix2.Zero.Normalised);

        [Fact]
        public void MoveTowardsLandsExactlyOnTarget()
        {
            Fix2 from = Fix2.FromInt(0, 0);
            Fix2 to = Fix2.FromInt(3, 4);
            // Step further than the distance: must snap to the target, not overshoot.
            Assert.Equal(to, Fix2.MoveTowards(from, to, Fix64.FromInt(10)));
        }

        [Fact]
        public void MoveTowardsCoversExpectedDistance()
        {
            Fix2 from = Fix2.FromInt(0, 0);
            Fix2 to = Fix2.FromInt(30, 40);   // distance 50
            Fix2 stepped = Fix2.MoveTowards(from, to, Fix64.FromInt(10));
            Assert.InRange(stepped.Magnitude.ToDouble(), 10.0 - 1e-3, 10.0 + 1e-3);
        }

        [Fact]
        public void RepeatedMoveTowardsTerminates()
        {
            Fix2 pos = Fix2.FromInt(0, 0);
            Fix2 target = new Fix2(Fix64.FromFraction(1234, 100), Fix64.FromFraction(-567, 100));
            for (int i = 0; i < 1000; i++) pos = Fix2.MoveTowards(pos, target, Fix64.FromFraction(7, 100));
            Assert.Equal(target, pos);
        }

        [Fact]
        public void DotAndCross()
        {
            Assert.Equal(Fix64.Zero, Fix2.Dot(Fix2.UnitX, Fix2.UnitY));
            Assert.Equal(Fix64.One, Fix2.Cross(Fix2.UnitX, Fix2.UnitY));
            Assert.Equal(Fix64.One, Fix2.Dot(Fix2.UnitX, Fix2.UnitX));
        }

        [Fact]
        public void FromAngleRoundTrips()
        {
            for (int degrees = -170; degrees <= 170; degrees += 10)
            {
                Fix64 radians = Fix64.FromFraction(degrees, 180) * Fix64.Pi;
                Fix2 v = Fix2.FromAngle(radians);
                Assert.InRange(v.Angle.ToDouble(), radians.ToDouble() - 1e-3, radians.ToDouble() + 1e-3);
            }
        }
    }
}
