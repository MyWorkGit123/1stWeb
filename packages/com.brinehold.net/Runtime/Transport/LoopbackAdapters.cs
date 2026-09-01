using System;

namespace Brinehold.Net.Transport
{
    /// <summary>
    /// Presents a <see cref="LoopbackNetwork"/> as a server transport.
    ///
    /// The loopback path exists so tests can run a full match with controllable latency and loss and
    /// no sockets. Behind the same interface as the UDP transport, it means the server code under
    /// test is character-for-character the code that ships.
    /// </summary>
    public sealed class LoopbackServerTransport : IServerTransport
    {
        private readonly LoopbackNetwork _network;
        private readonly System.Collections.Generic.Queue<int> _pendingConnections =
            new System.Collections.Generic.Queue<int>();

        public LoopbackServerTransport(LoopbackNetwork network) => _network = network;

        /// <summary>Registers a connection. The loopback has no handshake, so tests declare peers directly.</summary>
        public void RegisterConnection(int connectionId) => _pendingConnections.Enqueue(connectionId);

        public bool TryAcceptConnection(out int connectionId)
        {
            if (_pendingConnections.Count == 0) { connectionId = 0; return false; }
            connectionId = _pendingConnections.Dequeue();
            return true;
        }

        public bool TryTakeDisconnection(out int connectionId)
        {
            connectionId = 0;
            return false;   // the loopback link never drops
        }

        public bool TryReceive(out int connectionId, out ArraySegment<byte> payload)
            => _network.TryReceiveServer(out connectionId, out payload);

        public void Send(int connectionId, ArraySegment<byte> payload, Channel channel)
            => _network.SendToClient(connectionId, payload, channel);

        public void Poll() => _network.Tick();

        public void Dispose() { }
    }

    /// <summary>Presents one endpoint of a <see cref="LoopbackNetwork"/> as a client transport.</summary>
    public sealed class LoopbackClientTransport : IClientTransport
    {
        private readonly LoopbackNetwork _network;
        private readonly int _connectionId;

        public LoopbackClientTransport(LoopbackNetwork network, int connectionId)
        {
            _network = network;
            _connectionId = connectionId;
        }

        public bool IsConnected => true;

        public void Send(ArraySegment<byte> payload, Channel channel)
            => _network.SendToServer(_connectionId, payload, channel);

        public bool TryReceive(out ArraySegment<byte> payload)
            => _network.TryReceiveClient(_connectionId, out payload);

        public void Poll() { }

        public void Dispose() { }
    }
}
