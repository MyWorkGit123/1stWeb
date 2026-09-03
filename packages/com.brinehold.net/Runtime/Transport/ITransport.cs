using System;

namespace Brinehold.Net.Transport
{
    /// <summary>
    /// Reliability class for a packet.
    ///
    /// Reliable-ordered carries anything whose loss would change what a player sees: the handshake,
    /// commands, entity lifecycle, intents and private state. Unreliable-sequenced carries
    /// corrections, which are superseded by the next one and are not worth retransmitting.
    /// </summary>
    public enum Channel : byte
    {
        ReliableOrdered = 0,
        UnreliableSequenced = 1
    }

    /// <summary>
    /// The server's end of the wire.
    ///
    /// Everything above this interface is transport-agnostic, which is what lets the same MatchHost
    /// run over an in-process loopback in tests and over UDP in production without a second code
    /// path (MULTIPLAYER_ARCHITECTURE.md decision D7).
    /// </summary>
    public interface IServerTransport : IDisposable
    {
        /// <summary>Sends to one connection. Unknown connection ids are ignored, not thrown on.</summary>
        void Send(int connectionId, ArraySegment<byte> payload, Channel channel);

        /// <summary>
        /// Next payload from any client, or false when nothing is waiting. The buffer is valid only
        /// until the next call.
        /// </summary>
        bool TryReceive(out int connectionId, out ArraySegment<byte> payload);

        /// <summary>A client that has just completed the transport-level handshake.</summary>
        bool TryAcceptConnection(out int connectionId);

        /// <summary>A connection that has gone away, so its session can be cleaned up.</summary>
        bool TryTakeDisconnection(out int connectionId);

        /// <summary>Drives retransmission and timeouts. Called once per simulation tick.</summary>
        void Poll();
    }

    /// <summary>The client's end of the wire.</summary>
    public interface IClientTransport : IDisposable
    {
        bool IsConnected { get; }

        void Send(ArraySegment<byte> payload, Channel channel);

        bool TryReceive(out ArraySegment<byte> payload);

        void Poll();
    }
}
