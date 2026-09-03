using System;
using System.Collections.Generic;

namespace Brinehold.Net.Reliability
{
    /// <summary>
    /// Reliable, ordered delivery over an unreliable datagram link.
    ///
    /// Sequence numbers with a rolling acknowledgement bitfield, retransmission on timeout, and an
    /// in-order delivery buffer on the receiving side. This is the mechanism the loopback transport
    /// has always modelled the *behaviour* of; here it is actually implemented, because over real
    /// UDP nothing else guarantees that a command or an entity spawn arrives at all.
    ///
    /// Deliberately small: an RTS sends few, small reliable messages per tick, so the window can be
    /// modest and the bookkeeping cheap. It is not a general-purpose stream protocol.
    /// </summary>
    public sealed class ReliableChannel
    {
        /// <summary>How long to wait for an acknowledgement before resending, in milliseconds.</summary>
        public int RetransmitTimeoutMs = 120;

        /// <summary>Give up on a peer that has acknowledged nothing for this long.</summary>
        public int ConnectionTimeoutMs = 10000;

        /// <summary>Unacknowledged packets in flight before the sender stalls rather than growing.</summary>
        public const int MaxInFlight = 512;

        private struct Pending
        {
            public byte[] Payload;
            public int Length;
            public long FirstSentMs;
            public long LastSentMs;
            public int Attempts;
        }

        private readonly Dictionary<ushort, Pending> _unacked = new Dictionary<ushort, Pending>();
        private readonly Dictionary<ushort, byte[]> _outOfOrder = new Dictionary<ushort, byte[]>();
        private readonly List<ushort> _scratch = new List<ushort>(64);

        private ushort _nextOutgoing = 1;
        private ushort _nextExpectedIncoming = 1;

        /// <summary>Highest sequence received, and the 32 before it, for the acknowledgement field.</summary>
        private ushort _remoteHighest;
        private uint _remoteHistory;

        public long LastReceiveMs { get; private set; }
        public int Retransmissions { get; private set; }
        public int DuplicatesDropped { get; private set; }
        public int InFlight => _unacked.Count;

        public ReliableChannel(long nowMs) => LastReceiveMs = nowMs;

        /// <summary>Assigns the next sequence number and records the payload for retransmission.</summary>
        public ushort Track(byte[] payload, int length, long nowMs)
        {
            ushort sequence = _nextOutgoing++;
            if (_nextOutgoing == 0) _nextOutgoing = 1;   // zero means "no sequence"

            if (_unacked.Count < MaxInFlight)
            {
                _unacked[sequence] = new Pending
                {
                    Payload = payload,
                    Length = length,
                    FirstSentMs = nowMs,
                    LastSentMs = nowMs,
                    Attempts = 1
                };
            }

            return sequence;
        }

        /// <summary>Applies an incoming acknowledgement field, clearing everything it covers.</summary>
        public void Acknowledge(ushort ackSequence, uint ackBits)
        {
            if (ackSequence != 0) _unacked.Remove(ackSequence);

            for (int bit = 0; bit < 32; bit++)
            {
                if ((ackBits & (1u << bit)) == 0) continue;
                var covered = (ushort)(ackSequence - (bit + 1));
                if (covered != 0) _unacked.Remove(covered);
            }
        }

        /// <summary>
        /// Records that a sequence arrived and reports whether it is new. Duplicates are dropped
        /// here rather than being handed up twice.
        /// </summary>
        public bool NoteReceived(ushort sequence, long nowMs)
        {
            LastReceiveMs = nowMs;
            if (sequence == 0) return true;   // unreliable traffic carries no sequence

            int distance = SequenceDistance(sequence, _remoteHighest);

            if (distance > 0)
            {
                // Newer than anything seen: shift the history window along.
                _remoteHistory = distance >= 32 ? 0u : (_remoteHistory << distance) | (1u << (distance - 1));
                _remoteHighest = sequence;
                return true;
            }

            int back = -distance;
            if (back == 0 || back > 32) { DuplicatesDropped++; return false; }

            uint mask = 1u << (back - 1);
            if ((_remoteHistory & mask) != 0) { DuplicatesDropped++; return false; }

            _remoteHistory |= mask;
            return true;
        }

        public ushort AckSequence => _remoteHighest;
        public uint AckBits => _remoteHistory;

        /// <summary>
        /// Hands a received reliable payload to the in-order queue, returning everything that is now
        /// deliverable. A packet that arrives early waits until its predecessors turn up.
        /// </summary>
        public void Deliver(ushort sequence, byte[] payload, List<byte[]> output)
        {
            if (sequence == 0) { output.Add(payload); return; }

            if (SequenceDistance(sequence, _nextExpectedIncoming) < 0) return;   // already delivered

            _outOfOrder[sequence] = payload;

            while (_outOfOrder.TryGetValue(_nextExpectedIncoming, out byte[] next))
            {
                _outOfOrder.Remove(_nextExpectedIncoming);
                output.Add(next);
                _nextExpectedIncoming++;
                if (_nextExpectedIncoming == 0) _nextExpectedIncoming = 1;
            }
        }

        /// <summary>Packets due for retransmission. The caller re-sends them with their original sequence.</summary>
        public void CollectRetransmissions(long nowMs, List<(ushort sequence, byte[] payload, int length)> output)
        {
            _scratch.Clear();
            foreach (KeyValuePair<ushort, Pending> pair in _unacked)
                if (nowMs - pair.Value.LastSentMs >= RetransmitTimeoutMs) _scratch.Add(pair.Key);

            // Resend oldest first, so a stalled stream catches up in order.
            _scratch.Sort();

            for (int i = 0; i < _scratch.Count; i++)
            {
                ushort sequence = _scratch[i];
                Pending pending = _unacked[sequence];
                pending.LastSentMs = nowMs;
                pending.Attempts++;
                _unacked[sequence] = pending;
                Retransmissions++;
                output.Add((sequence, pending.Payload, pending.Length));
            }
        }

        public bool HasTimedOut(long nowMs) => nowMs - LastReceiveMs > ConnectionTimeoutMs;

        /// <summary>
        /// Distance from <paramref name="other"/> to <paramref name="sequence"/>, wrapping correctly
        /// through the 16-bit space. Positive means newer.
        /// </summary>
        private static int SequenceDistance(ushort sequence, ushort other)
        {
            return (short)(sequence - other);
        }
    }
}
