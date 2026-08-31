using Brinehold.Core.Math;

namespace Brinehold.Core.Random
{
    /// <summary>
    /// xorshift128+ pseudo-random generator.
    ///
    /// There is exactly one instance per simulation, seeded from the match seed and advanced in
    /// tick order. Never create a second generator, and never use a thread-local one: the sequence
    /// of draws is part of the simulation state, and a replay reproduces it exactly.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _s0;
        private ulong _s1;

        public DeterministicRandom(ulong seed)
        {
            // SplitMix64 expansion, so that even a small or sequential seed produces well-mixed state.
            _s0 = SplitMix(ref seed);
            _s1 = SplitMix(ref seed);
            if (_s0 == 0 && _s1 == 0) _s1 = 0x9E3779B97F4A7C15UL;
        }

        private static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong x = _s0;
            ulong y = _s1;
            _s0 = y;
            x ^= x << 23;
            _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return unchecked(_s1 + y);
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Uniform in [0, exclusiveMax). Uses rejection sampling so the distribution has no modulo bias.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) return 0;
            uint bound = (uint)exclusiveMax;
            uint threshold = (uint)(-(int)bound) % bound;
            while (true)
            {
                uint value = NextUInt();
                if (value >= threshold) return (int)(value % bound);
            }
        }

        /// <summary>Uniform in [inclusiveMin, exclusiveMax).</summary>
        public int NextInt(int inclusiveMin, int exclusiveMax)
            => inclusiveMin + NextInt(exclusiveMax - inclusiveMin);

        /// <summary>Uniform in [0, 1).</summary>
        public Fix64 NextFix() => Fix64.FromRaw((long)(NextULong() >> 32));

        public Fix64 NextFix(Fix64 inclusiveMin, Fix64 exclusiveMax)
            => inclusiveMin + (exclusiveMax - inclusiveMin) * NextFix();

        /// <summary>True with probability <paramref name="percent"/> out of 100.</summary>
        public bool Chance(int percent) => NextInt(100) < percent;

        /// <summary>State capture for snapshots. The generator is part of the world state.</summary>
        public void GetState(out ulong s0, out ulong s1) { s0 = _s0; s1 = _s1; }

        public void SetState(ulong s0, ulong s1) { _s0 = s0; _s1 = s1; }
    }
}
