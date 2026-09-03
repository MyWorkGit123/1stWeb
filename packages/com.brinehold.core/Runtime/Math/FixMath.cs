using System;

namespace Brinehold.Core.Math
{
    /// <summary>
    /// Deterministic maths functions over <see cref="Fix64"/>.
    ///
    /// Every function here is pure integer arithmetic. Nothing calls System.Math, so results are
    /// bit-identical on every platform the simulation runs on.
    /// </summary>
    public static class FixMath
    {
        // Taylor coefficients for sine, as exact rationals: 1/6, 1/120, 1/5040, 1/362880.
        private static readonly Fix64 Inv6 = Fix64.FromFraction(1, 6);
        private static readonly Fix64 Inv120 = Fix64.FromFraction(1, 120);
        private static readonly Fix64 Inv5040 = Fix64.FromFraction(1, 5040);
        private static readonly Fix64 Inv362880 = Fix64.FromFraction(1, 362880);

        // Minimax coefficients for atan on [-1, 1], scaled to ten-thousandths.
        private static readonly Fix64 A1 = Fix64.FromFraction(999866, 1000000);
        private static readonly Fix64 A3 = Fix64.FromFraction(-330299, 1000000);
        private static readonly Fix64 A5 = Fix64.FromFraction(180141, 1000000);
        private static readonly Fix64 A7 = Fix64.FromFraction(-85133, 1000000);
        private static readonly Fix64 A9 = Fix64.FromFraction(20835, 1000000);

        public static Fix64 Abs(Fix64 v) => v.Raw < 0 ? -v : v;

        public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw < b.Raw ? a : b;

        public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw > b.Raw ? a : b;

        public static Fix64 Clamp(Fix64 v, Fix64 min, Fix64 max)
            => v.Raw < min.Raw ? min : (v.Raw > max.Raw ? max : v);

        public static Fix64 Clamp01(Fix64 v) => Clamp(v, Fix64.Zero, Fix64.One);

        public static int Sign(Fix64 v) => v.Raw < 0 ? -1 : (v.Raw > 0 ? 1 : 0);

        /// <summary>Rounds toward negative infinity.</summary>
        public static Fix64 Floor(Fix64 v) => Fix64.FromRaw(v.Raw & unchecked((long)0xFFFFFFFF00000000));

        public static Fix64 Ceil(Fix64 v)
        {
            bool hasFraction = (v.Raw & 0x00000000FFFFFFFFL) != 0;
            return hasFraction ? Floor(v) + Fix64.One : v;
        }

        /// <summary>Round half away from zero, so the result never depends on a banker's-rounding rule.</summary>
        public static Fix64 Round(Fix64 v)
        {
            Fix64 floor = Floor(v);
            Fix64 fraction = v - floor;
            if (fraction.Raw > Fix64.RawHalf) return floor + Fix64.One;
            if (fraction.Raw < Fix64.RawHalf) return floor;
            return v.Raw >= 0 ? floor + Fix64.One : floor;
        }

        public static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t) => a + (b - a) * Clamp01(t);

        /// <summary>
        /// Digit-by-digit binary square root. Two passes: the first recovers the integer part, the
        /// second the fractional part.
        /// </summary>
        public static Fix64 Sqrt(Fix64 v)
        {
            long raw = v.Raw;
            if (raw < 0) throw new ArgumentOutOfRangeException(nameof(v), "Fix64 square root of a negative number.");
            if (raw == 0) return Fix64.Zero;

            ulong remainder = (ulong)raw;
            ulong result = 0UL;
            ulong bit = 1UL << 62;

            while (bit > remainder) bit >>= 2;

            for (int pass = 0; pass < 2; pass++)
            {
                while (bit != 0)
                {
                    if (remainder >= result + bit)
                    {
                        remainder -= result + bit;
                        result = (result >> 1) + bit;
                    }
                    else
                    {
                        result >>= 1;
                    }
                    bit >>= 2;
                }

                if (pass == 0)
                {
                    // Shift in the 32 fractional bits and continue with the same algorithm.
                    if (remainder > (1UL << 32) - 1)
                    {
                        remainder -= result;
                        remainder = (remainder << 32) - 0x80000000UL;
                        result = (result << 32) + 0x80000000UL;
                    }
                    else
                    {
                        remainder <<= 32;
                        result <<= 32;
                    }
                    bit = 1UL << 30;
                }
            }

            if (remainder > result) ++result;
            return Fix64.FromRaw((long)result);
        }

        /// <summary>Reduces an angle in radians to [-pi, pi].</summary>
        public static Fix64 NormaliseAngle(Fix64 radians)
        {
            Fix64 twoPi = Fix64.TwoPi;
            Fix64 v = radians % twoPi;
            if (v > Fix64.Pi) v -= twoPi;
            else if (v < -Fix64.Pi) v += twoPi;
            return v;
        }

        /// <summary>
        /// Sine of an angle in radians. The argument is folded into [-pi/2, pi/2] and evaluated with
        /// a ninth-order Taylor series, which is accurate to better than 1e-7 over that interval.
        /// </summary>
        public static Fix64 Sin(Fix64 radians)
        {
            Fix64 x = NormaliseAngle(radians);

            // sin(x) == sin(pi - x) folds the outer quadrants inward.
            if (x > Fix64.HalfPi) x = Fix64.Pi - x;
            else if (x < -Fix64.HalfPi) x = -Fix64.Pi - x;

            Fix64 x2 = x * x;
            Fix64 x3 = x2 * x;
            Fix64 x5 = x3 * x2;
            Fix64 x7 = x5 * x2;
            Fix64 x9 = x7 * x2;

            return x - x3 * Inv6 + x5 * Inv120 - x7 * Inv5040 + x9 * Inv362880;
        }

        public static Fix64 Cos(Fix64 radians) => Sin(radians + Fix64.HalfPi);

        public static Fix64 Tan(Fix64 radians)
        {
            Fix64 cos = Cos(radians);
            if (cos == Fix64.Zero) return Fix64.MaxValue;
            return Sin(radians) / cos;
        }

        /// <summary>Arctangent of z on [-1, 1], via a ninth-order minimax polynomial.</summary>
        private static Fix64 AtanUnitRange(Fix64 z)
        {
            Fix64 z2 = z * z;
            Fix64 z3 = z2 * z;
            Fix64 z5 = z3 * z2;
            Fix64 z7 = z5 * z2;
            Fix64 z9 = z7 * z2;
            return A1 * z + A3 * z3 + A5 * z5 + A7 * z7 + A9 * z9;
        }

        /// <summary>
        /// Full four-quadrant arctangent, in radians on (-pi, pi]. Returns zero when both arguments
        /// are zero rather than throwing, because a zero-length direction vector is a normal state
        /// for a stationary unit.
        /// </summary>
        public static Fix64 Atan2(Fix64 y, Fix64 x)
        {
            if (x == Fix64.Zero && y == Fix64.Zero) return Fix64.Zero;

            Fix64 absY = Abs(y);
            Fix64 absX = Abs(x);

            Fix64 angle;
            if (absX >= absY)
            {
                angle = AtanUnitRange(y / x);
                if (x < Fix64.Zero) angle = y >= Fix64.Zero ? angle + Fix64.Pi : angle - Fix64.Pi;
            }
            else
            {
                angle = Fix64.HalfPi - AtanUnitRange(x / y);
                if (y < Fix64.Zero) angle -= Fix64.Pi;
            }

            return angle;
        }
    }
}
