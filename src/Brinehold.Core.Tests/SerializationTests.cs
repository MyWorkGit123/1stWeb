using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Core.Random;
using Brinehold.Core.Serialization;
using Xunit;

namespace Brinehold.Core.Tests
{
    public class SerializationTests
    {
        [Fact]
        public void BitRoundTripAcrossAllWidths()
        {
            var rng = new System.Random(5150);
            for (int bits = 1; bits <= 32; bits++)
            {
                var writer = new BitWriter();
                uint mask = bits == 32 ? uint.MaxValue : (1u << bits) - 1u;
                uint[] values = new uint[200];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (uint)rng.Next() & mask;
                    writer.WriteBits(values[i], bits);
                }

                var reader = new BitReader(writer.ToArray());
                for (int i = 0; i < values.Length; i++)
                    Assert.Equal(values[i], reader.ReadBits(bits));
                Assert.False(reader.EndOfStream);
            }
        }

        [Fact]
        public void MixedTypeRoundTrip()
        {
            var writer = new BitWriter();
            writer.WriteBool(true);
            writer.WriteBool(false);
            writer.WriteByte(203);
            writer.WriteUInt16(50000);
            writer.WriteInt32(-123456789);
            writer.WriteInt64(-9007199254740993L);
            writer.WriteString("brinehold");
            writer.WriteRanged(7, 0, 15);

            var reader = new BitReader(writer.ToArray());
            Assert.True(reader.ReadBool());
            Assert.False(reader.ReadBool());
            Assert.Equal(203, reader.ReadByte());
            Assert.Equal(50000, reader.ReadUInt16());
            Assert.Equal(-123456789, reader.ReadInt32());
            Assert.Equal(-9007199254740993L, reader.ReadInt64());
            Assert.Equal("brinehold", reader.ReadString());
            Assert.Equal(7, reader.ReadRanged(0, 15));
            Assert.False(reader.EndOfStream);
        }

        [Fact]
        public void RangedWritesOnlyTheBitsTheRangeNeeds()
        {
            var writer = new BitWriter();
            for (int i = 0; i < 8; i++) writer.WriteRanged(i, 0, 7);
            // Eight values in [0,7] need three bits each: 24 bits, so three bytes.
            Assert.Equal(24, writer.BitLength);
            Assert.Equal(3, writer.ByteLength);
        }

        [Fact]
        public void ReadingPastTheEndSetsEndOfStreamRatherThanThrowing()
        {
            var writer = new BitWriter();
            writer.WriteByte(1);
            var reader = new BitReader(writer.ToArray());
            reader.ReadByte();
            uint overrun = reader.ReadBits(32);
            Assert.True(reader.EndOfStream);
            Assert.Equal(0u, overrun);
        }

        [Fact]
        public void TruncatedStringDoesNotThrow()
        {
            var writer = new BitWriter();
            writer.WriteByte(200);   // claims a 200-byte string that is not there
            var reader = new BitReader(writer.ToArray());
            Assert.Equal(string.Empty, reader.ReadString());
            Assert.True(reader.EndOfStream);
        }

        [Fact]
        public void PositionQuantisationStaysWithinHalfAUnit()
        {
            for (int centimetres = 0; centimetres < 300000; centimetres += 137)
            {
                Fix64 metres = Fix64.FromFraction(centimetres, 100);
                if (metres.ToInt() > Quantise.MaxPositionMetres) break;
                Fix64 decoded = Quantise.DecodePosition(Quantise.EncodePosition(metres));
                Assert.InRange(decoded.ToDouble(), metres.ToDouble() - 0.026, metres.ToDouble() + 0.026);
            }
        }

        [Fact]
        public void AngleQuantisationStaysWithinOneStep()
        {
            for (int degrees = -179; degrees <= 179; degrees += 1)
            {
                Fix64 radians = Fix64.FromFraction(degrees, 180) * Fix64.Pi;
                Fix64 decoded = Quantise.DecodeAngle(Quantise.EncodeAngle(radians));
                double delta = System.Math.Abs(decoded.ToDouble() - radians.ToDouble());
                if (delta > System.Math.PI) delta = System.Math.Abs(delta - 2 * System.Math.PI);
                Assert.InRange(delta, 0.0, 2 * System.Math.PI / 256 + 1e-6);
            }
        }
    }

    public class RandomTests
    {
        [Fact]
        public void SameSeedProducesSameSequence()
        {
            var a = new DeterministicRandom(0xDEADBEEF);
            var b = new DeterministicRandom(0xDEADBEEF);
            for (int i = 0; i < 10000; i++) Assert.Equal(a.NextULong(), b.NextULong());
        }

        [Fact]
        public void DifferentSeedsDiverge()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            int same = 0;
            for (int i = 0; i < 1000; i++) if (a.NextULong() == b.NextULong()) same++;
            Assert.True(same < 5, $"sequences overlapped {same} times");
        }

        [Fact]
        public void StateCaptureAndRestoreReproducesTheSequence()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++) rng.NextULong();
            rng.GetState(out ulong s0, out ulong s1);
            ulong[] expected = new ulong[50];
            for (int i = 0; i < expected.Length; i++) expected[i] = rng.NextULong();

            rng.SetState(s0, s1);
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], rng.NextULong());
        }

        [Fact]
        public void NextIntStaysInRangeAndCoversIt()
        {
            var rng = new DeterministicRandom(7);
            var seen = new bool[6];
            for (int i = 0; i < 20000; i++)
            {
                int v = rng.NextInt(6);
                Assert.InRange(v, 0, 5);
                seen[v] = true;
            }
            Assert.All(seen, Assert.True);
        }

        [Fact]
        public void NextFixIsInUnitInterval()
        {
            var rng = new DeterministicRandom(11);
            for (int i = 0; i < 20000; i++)
            {
                Fix64 v = rng.NextFix();
                Assert.True(v >= Fix64.Zero && v < Fix64.One, $"out of range: {v}");
            }
        }
    }

    public class StateHashTests
    {
        [Fact]
        public void OrderChangesTheHash()
        {
            var a = StateHash.Create();
            a.Add(1); a.Add(2);
            var b = StateHash.Create();
            b.Add(2); b.Add(1);
            Assert.NotEqual(a.Value, b.Value);
        }

        [Fact]
        public void SameInputProducesSameHash()
        {
            var a = StateHash.Create();
            var b = StateHash.Create();
            for (int i = 0; i < 500; i++)
            {
                a.Add(Fix64.FromInt(i));
                b.Add(Fix64.FromInt(i));
            }
            Assert.Equal(a.Value, b.Value);
        }
    }

    public class EntityIdTests
    {
        [Fact]
        public void PacksAndUnpacks()
        {
            var id = new EntityId(0xABCDEF, 0x12);
            Assert.Equal(0xABCDEF, id.Index);
            Assert.Equal(0x12, id.Generation);
            Assert.False(id.IsNone);
        }

        [Fact]
        public void NoneIsZero()
        {
            Assert.True(EntityId.None.IsNone);
            Assert.Equal(0u, EntityId.None.Raw);
        }

        [Fact]
        public void SameIndexDifferentGenerationAreNotEqual()
        {
            Assert.NotEqual(new EntityId(5, 1), new EntityId(5, 2));
        }
    }
}
