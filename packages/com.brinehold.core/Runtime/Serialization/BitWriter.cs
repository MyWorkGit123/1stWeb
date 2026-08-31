using System;

namespace Brinehold.Core.Serialization
{
    /// <summary>
    /// Little-endian bit-level writer.
    ///
    /// The replication protocol is bit-packed rather than byte-aligned because the difference is the
    /// whole bandwidth budget: a quantised position is 38 bits, not 96, and an entity reference
    /// inside an interest set is 11 bits, not 32.
    /// </summary>
    public sealed class BitWriter
    {
        private byte[] _buffer;
        private int _bitPosition;

        public BitWriter(int capacityBytes = 1200)
        {
            _buffer = new byte[capacityBytes];
            _bitPosition = 0;
        }

        public int BitLength => _bitPosition;
        public int ByteLength => (_bitPosition + 7) >> 3;

        public void Reset() => _bitPosition = 0;

        private void EnsureCapacity(int extraBits)
        {
            int neededBytes = ((_bitPosition + extraBits) >> 3) + 1;
            if (neededBytes <= _buffer.Length) return;
            int newSize = _buffer.Length * 2;
            while (newSize < neededBytes) newSize *= 2;
            Array.Resize(ref _buffer, newSize);
        }

        /// <summary>Writes the low <paramref name="bits"/> bits of <paramref name="value"/>.</summary>
        public void WriteBits(uint value, int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits));
            EnsureCapacity(bits);

            if (bits < 32) value &= (1u << bits) - 1u;

            for (int i = 0; i < bits; i++)
            {
                int byteIndex = (_bitPosition + i) >> 3;
                int bitIndex = (_bitPosition + i) & 7;
                if (((value >> i) & 1u) != 0) _buffer[byteIndex] |= (byte)(1 << bitIndex);
                else _buffer[byteIndex] &= (byte)~(1 << bitIndex);
            }

            _bitPosition += bits;
        }

        public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

        public void WriteByte(byte value) => WriteBits(value, 8);

        public void WriteUInt16(ushort value) => WriteBits(value, 16);

        public void WriteUInt32(uint value) => WriteBits(value, 32);

        public void WriteInt32(int value) => WriteBits(unchecked((uint)value), 32);

        public void WriteUInt64(ulong value)
        {
            WriteBits((uint)(value & 0xFFFFFFFFUL), 32);
            WriteBits((uint)(value >> 32), 32);
        }

        public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

        /// <summary>Writes an integer known to lie in [min, max] using only the bits that range needs.</summary>
        public void WriteRanged(int value, int min, int max)
        {
            if (max < min) throw new ArgumentException("max is below min", nameof(max));
            if (value < min) value = min;
            if (value > max) value = max;
            int bits = BitsRequired(min, max);
            if (bits == 0) return;
            WriteBits(unchecked((uint)(value - min)), bits);
        }

        public void WriteString(string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length > 255) throw new ArgumentException("String exceeds 255 bytes", nameof(value));
            WriteByte((byte)bytes.Length);
            for (int i = 0; i < bytes.Length; i++) WriteByte(bytes[i]);
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[ByteLength];
            Array.Copy(_buffer, result, ByteLength);
            return result;
        }

        /// <summary>Exposes the internal buffer to avoid a copy on the hot send path.</summary>
        public ArraySegment<byte> AsSegment() => new ArraySegment<byte>(_buffer, 0, ByteLength);

        public static int BitsRequired(int min, int max)
        {
            long range = (long)max - min;
            if (range <= 0) return 0;
            int bits = 0;
            while (range > 0) { bits++; range >>= 1; }
            return bits;
        }
    }
}
