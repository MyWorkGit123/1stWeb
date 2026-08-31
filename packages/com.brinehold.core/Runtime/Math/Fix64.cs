using System;
using System.Runtime.CompilerServices;

namespace Brinehold.Core.Math
{
    /// <summary>
    /// Q31.32 fixed-point number backed by a <see cref="long"/>.
    ///
    /// The simulation uses this type for every quantity that affects game state. Floating point is
    /// banned in simulation assemblies because IEEE-754 results are not guaranteed to be identical
    /// across CPU architectures, JIT versions and SIMD paths. Every operation here is pure integer
    /// arithmetic, so it produces bit-identical results on every platform.
    ///
    /// Range: approximately +/- 2.14e9 with a resolution of 2.33e-10.
    /// </summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public const int FractionalBits = 32;

        internal const long RawOne = 1L << FractionalBits;
        internal const long RawHalf = RawOne >> 1;
        internal const long RawMax = long.MaxValue;
        internal const long RawMin = long.MinValue;

        /// <summary>The underlying scaled integer. Exposed for serialisation and hashing only.</summary>
        public readonly long Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Fix64(long raw) => Raw = raw;

        // ---------------------------------------------------------------- constants

        public static Fix64 Zero => new Fix64(0);
        public static Fix64 One => new Fix64(RawOne);
        public static Fix64 Two => new Fix64(RawOne * 2);
        public static Fix64 Half => new Fix64(RawHalf);
        public static Fix64 MinusOne => new Fix64(-RawOne);
        public static Fix64 MaxValue => new Fix64(RawMax);
        public static Fix64 MinValue => new Fix64(RawMin);

        /// <summary>3.14159265358979... to the precision of Q31.32.</summary>
        public static Fix64 Pi => new Fix64(13493037705L);
        public static Fix64 TwoPi => new Fix64(26986075409L);
        public static Fix64 HalfPi => new Fix64(6746518852L);
        public static Fix64 Epsilon => new Fix64(1L);

        // ---------------------------------------------------------------- construction

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromRaw(long raw) => new Fix64(raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromInt(int value) => new Fix64((long)value << FractionalBits);

        /// <summary>Exact rational construction — the preferred way to author constants.</summary>
        public static Fix64 FromFraction(int numerator, int denominator)
        {
            if (denominator == 0) throw new DivideByZeroException("Fix64.FromFraction denominator is zero.");
            return FromInt(numerator) / FromInt(denominator);
        }

        /// <summary>Thousandths. <c>FromMilli(1500)</c> is 1.5. Used by the content loader.</summary>
        public static Fix64 FromMilli(int thousandths) => FromFraction(thousandths, 1000);

        /// <summary>
        /// Authoring and test convenience only. Never call this from simulation code — the analyser
        /// bans <c>double</c> inside simulation assemblies precisely so this cannot leak into a tick.
        /// </summary>
        public static explicit operator Fix64(double value) => new Fix64((long)(value * RawOne));

        public static explicit operator Fix64(int value) => FromInt(value);

        // ---------------------------------------------------------------- conversion out

        /// <summary>Truncates toward negative infinity, matching <see cref="Floor"/>.</summary>
        public int ToInt() => (int)(Raw >> FractionalBits);

        /// <summary>Diagnostics, UI and tests only — never feed the result back into the simulation.</summary>
        public double ToDouble() => (double)Raw / RawOne;

        public float ToFloat() => (float)((double)Raw / RawOne);

        public override string ToString() => ToDouble().ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- arithmetic

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64(a.Raw + b.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64(a.Raw - b.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator -(Fix64 a) => new Fix64(-a.Raw);

        /// <summary>
        /// 64x64 -> 128 bit multiply, keeping the middle 64 bits. Decomposed into 32-bit halves so
        /// it needs no 128-bit type and stays available on netstandard2.1.
        /// Fractional bits below the representable range are truncated toward negative infinity.
        /// </summary>
        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            long xa = a.Raw;
            long xb = b.Raw;

            long ah = xa >> 32;
            ulong al = (ulong)(xa & 0xFFFFFFFFL);
            long bh = xb >> 32;
            ulong bl = (ulong)(xb & 0xFFFFFFFFL);

            ulong lowProduct = al * bl;
            long midA = ah * (long)bl;
            long midB = (long)al * bh;
            long highProduct = ah * bh;

            return new Fix64((highProduct << 32) + midA + midB + (long)(lowProduct >> 32));
        }

        public static Fix64 operator *(Fix64 a, int b) => new Fix64(a.Raw * b);
        public static Fix64 operator *(int a, Fix64 b) => new Fix64(b.Raw * a);

        /// <summary>
        /// Restoring long division on the raw values. Saturates rather than wrapping on overflow so
        /// that a pathological input degrades predictably instead of corrupting simulation state.
        /// </summary>
        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            long xa = a.Raw;
            long xb = b.Raw;

            if (xb == 0) throw new DivideByZeroException("Fix64 division by zero.");

            ulong remainder = (ulong)(xa >= 0 ? xa : -xa);
            ulong divider = (ulong)(xb >= 0 ? xb : -xb);
            ulong quotient = 0UL;
            int bitPos = FractionalBits + 1;

            // Fast path: strip trailing zero nibbles from the divisor.
            while ((divider & 0xF) == 0 && bitPos >= 4)
            {
                divider >>= 4;
                bitPos -= 4;
            }

            while (remainder != 0 && bitPos >= 0)
            {
                int shift = LeadingZeroCount(remainder);
                if (shift > bitPos) shift = bitPos;
                remainder <<= shift;
                bitPos -= shift;

                ulong step = remainder / divider;
                remainder %= divider;
                quotient += step << bitPos;

                if (bitPos < 64 && (step & ~(ulong.MaxValue >> bitPos)) != 0)
                {
                    return ((xa ^ xb) < 0) ? MinValue : MaxValue;
                }

                remainder <<= 1;
                --bitPos;
            }

            ++quotient; // round to nearest
            long result = (long)(quotient >> 1);
            if ((xa ^ xb) < 0) result = -result;
            return new Fix64(result);
        }

        public static Fix64 operator /(Fix64 a, int b) => new Fix64(a.Raw / b);

        public static Fix64 operator %(Fix64 a, Fix64 b) => new Fix64(a.Raw % b.Raw);

        // ---------------------------------------------------------------- comparison

        public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
        public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
        public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
        public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
        public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

        public bool Equals(Fix64 other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is Fix64 other && Raw == other.Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);

        // ---------------------------------------------------------------- helpers

        internal static int LeadingZeroCount(ulong value)
        {
            if (value == 0) return 64;
            int count = 0;
            while ((value & 0xF000000000000000UL) == 0) { count += 4; value <<= 4; }
            while ((value & 0x8000000000000000UL) == 0) { count += 1; value <<= 1; }
            return count;
        }
    }
}
