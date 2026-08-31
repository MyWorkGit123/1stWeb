using System;

namespace Brinehold.Net.Transport
{
    /// <summary>
    /// Reliability class for a packet. The production transport implements these over UDP with
    /// retransmission and sequencing; the loopback transport models their observable behaviour so
    /// that tests can reason about loss without a real network.
    /// </summary>
    public enum Channel : byte
    {
        /// <summary>Handshake, commands, lifecycle, intents and private state. Never dropped.</summary>
        ReliableOrdered = 0,
        /// <summary>Corrections and aggregates. May be dropped; the next one supersedes it.</summary>
        UnreliableSequenced = 1
    }

    /// <summary>
    /// The seam between the game and the wire.
    ///
    /// Everything above this interface is transport-agnostic, which is what lets the same server
    /// binary run over an in-process loopback in tests, over Unity Transport in production, and
    /// over LiteNetLib if we ever need to swap (MULTIPLAYER_ARCHITECTURE.md decision D7).
    /// </summary>
    public interface ITransport
    {
        void Send(int connectionId, ArraySegment<byte> payload, Channel channel);

        /// <summary>Returns false when nothing is waiting. The buffer is valid until the next call.</summary>
        bool TryReceive(out int connectionId, out ArraySegment<byte> payload);

        /// <summary>Advances any time-based behaviour. Called once per simulation tick.</summary>
        void Poll();
    }
}
