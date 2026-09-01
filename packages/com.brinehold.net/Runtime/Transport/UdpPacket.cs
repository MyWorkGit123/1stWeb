using System;

namespace Brinehold.Net.Transport
{
    /// <summary>
    /// The datagram header shared by both ends of a UDP link.
    ///
    /// Eleven bytes: enough for a sequence number, a rolling acknowledgement field and fragment
    /// bookkeeping. Everything above this is game data. Acknowledgements ride on ordinary traffic
    /// rather than needing packets of their own, which for a server sending twenty packets a second
    /// to each client means the reliability layer is nearly free.
    /// </summary>
    internal static class UdpPacket
    {
        public const int HeaderSize = 11;

        /// <summary>Conservative payload ceiling, chosen to stay inside a typical path MTU.</summary>
        public const int MaxDatagram = 1200;
        public const int MaxPayload = MaxDatagram - HeaderSize;

        public enum PacketType : byte
        {
            ReliableData = 0,
            UnreliableData = 1,
            ConnectRequest = 2,
            ConnectAccept = 3,
            Disconnect = 4,
            Keepalive = 5
        }

        public static int Write(byte[] buffer, PacketType type, ushort sequence, ushort ackSequence,
                                uint ackBits, byte fragmentIndex, byte fragmentCount,
                                ArraySegment<byte> payload)
        {
            buffer[0] = (byte)type;
            buffer[1] = (byte)(sequence & 0xFF);
            buffer[2] = (byte)(sequence >> 8);
            buffer[3] = (byte)(ackSequence & 0xFF);
            buffer[4] = (byte)(ackSequence >> 8);
            buffer[5] = (byte)(ackBits & 0xFF);
            buffer[6] = (byte)((ackBits >> 8) & 0xFF);
            buffer[7] = (byte)((ackBits >> 16) & 0xFF);
            buffer[8] = (byte)((ackBits >> 24) & 0xFF);
            buffer[9] = fragmentIndex;
            buffer[10] = fragmentCount;

            if (payload.Count > 0)
                Buffer.BlockCopy(payload.Array!, payload.Offset, buffer, HeaderSize, payload.Count);

            return HeaderSize + payload.Count;
        }

        public static bool Read(byte[] buffer, int length, out PacketType type, out ushort sequence,
                                out ushort ackSequence, out uint ackBits, out byte fragmentIndex,
                                out byte fragmentCount, out int payloadOffset, out int payloadLength)
        {
            type = default; sequence = 0; ackSequence = 0; ackBits = 0;
            fragmentIndex = 0; fragmentCount = 0; payloadOffset = 0; payloadLength = 0;

            // A datagram shorter than the header, or one claiming a type we do not know, is junk from
            // a scanner or a hostile client. Reject it without allocating or throwing.
            if (length < HeaderSize) return false;
            if (buffer[0] > (byte)PacketType.Keepalive) return false;

            type = (PacketType)buffer[0];
            sequence = (ushort)(buffer[1] | (buffer[2] << 8));
            ackSequence = (ushort)(buffer[3] | (buffer[4] << 8));
            ackBits = (uint)(buffer[5] | (buffer[6] << 8) | (buffer[7] << 16) | (buffer[8] << 24));
            fragmentIndex = buffer[9];
            fragmentCount = buffer[10];
            payloadOffset = HeaderSize;
            payloadLength = length - HeaderSize;
            return true;
        }
    }
}
