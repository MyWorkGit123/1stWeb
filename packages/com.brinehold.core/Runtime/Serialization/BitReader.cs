using System;

namespace Brinehold.Core.Serialization
{
    /// <summary>
    /// Counterpart to <see cref="BitWriter"/>.
    ///
    /// Every read is bounds-checked. A malformed or truncated packet from a hostile client must
    /// fail cleanly with <see cref="EndOfStream"/> set, never throw its way up into the tick loop.
    /// </summary>
    public sealed class BitReader
    {
        private byte[] _buffer;
        private int _bitPosition;
        private int _bitLength;

        public BitReader(byte[] buffer) : this(buffer, 0, buffer.Length) { }

        public BitReader(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _bitPosition = offset * 8;
            _bitLength = (offset + count) * 8;
        }

        /// <summary>Set once a read has run past the end of the buffer. All later reads return zero.</summary>
        public bool EndOfStream { get; private set; }

        public int BitPosition => _bitPosition;
        public int BitsRemaining => _bitLength - _bitPosition;

        public void Reset(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _bitPosition = offset * 8;
            _bitLength = (offset + count) * 8;
            EndOfStream = false;
        }

        public uint ReadBits(int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits));
            if (_bitPosition + bits > _bitLength) { EndOfStream = true; return 0u; }

            uint value = 0u;
            for (int i = 0; i < bits; i++)
            {
                int byteIndex = (_bitPosition + i) >> 3;
                int bitIndex = (_bitPosition + i) & 7;
                if ((_buffer[byteIndex] & (1 << bitIndex)) != 0) value |= 1u << i;
            }

            _bitPosition += bits;
            return value;
        }

        public bool ReadBool() => ReadBits(1) != 0u;

        public byte ReadByte() => (byte)ReadBits(8);

        public ushort ReadUInt16() => (ushort)ReadBits(16);

        public uint ReadUInt32() => ReadBits(32);

        public int ReadInt32() => unchecked((int)ReadBits(32));

        public ulong ReadUInt64()
        {
            ulong low = ReadBits(32);
            ulong high = ReadBits(32);
            return low | (high << 32);
        }

        public long ReadInt64() => unchecked((long)ReadUInt64());

        public int ReadRanged(int min, int max)
        {
            int bits = BitWriter.BitsRequired(min, max);
            if (bits == 0) return min;
            return min + unchecked((int)ReadBits(bits));
        }

        public string ReadString()
        {
            int length = ReadByte();
            if (_bitPosition + length * 8 > _bitLength) { EndOfStream = true; return string.Empty; }
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++) bytes[i] = ReadByte();
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
