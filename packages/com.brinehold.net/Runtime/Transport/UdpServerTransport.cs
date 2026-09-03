using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Brinehold.Net.Transport
{
    /// <summary>
    /// The dedicated server's UDP socket.
    ///
    /// One socket serves every client; connections are identified by endpoint. The socket is
    /// non-blocking and is drained once per simulation tick, so the server never blocks on the
    /// network — a client that stops responding costs a timeout, not a stalled tick.
    ///
    /// Unknown or malformed datagrams are dropped without allocation. This is an internet-facing
    /// port, so the parsing path assumes hostility by default.
    /// </summary>
    public sealed class UdpServerTransport : IServerTransport
    {
        private readonly Socket _socket;
        private readonly Dictionary<string, UdpConnection> _byEndPoint = new Dictionary<string, UdpConnection>();
        private readonly Dictionary<int, UdpConnection> _byId = new Dictionary<int, UdpConnection>();
        private readonly Queue<int> _accepted = new Queue<int>();
        private readonly Queue<int> _disconnected = new Queue<int>();
        private readonly Queue<(int connection, byte[] payload)> _inbox = new Queue<(int, byte[])>();

        private readonly byte[] _receiveBuffer = new byte[2048];
        private readonly byte[] _sendBuffer = new byte[UdpPacket.MaxDatagram];
        private readonly List<byte[]> _delivered = new List<byte[]>(8);
        private readonly List<(ushort sequence, byte[] payload, int length)> _retransmits =
            new List<(ushort, byte[], int)>(16);

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _nextConnectionId = 1;

        /// <summary>Artificial loss for testing, as a percentage. Zero in production.</summary>
        public int SimulatedLossPercent;
        private readonly Core.Random.DeterministicRandom _lossRng = new Core.Random.DeterministicRandom(1337);

        public int ConnectionCount => _byId.Count;
        public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint!;

        public UdpServerTransport(int port, IPAddress? address = null)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                Blocking = false,
                SendBufferSize = 1 << 20,
                ReceiveBufferSize = 1 << 20
            };
            _socket.Bind(new IPEndPoint(address ?? IPAddress.Any, port));
        }

        private long NowMs => _clock.ElapsedMilliseconds;

        public bool TryAcceptConnection(out int connectionId)
        {
            if (_accepted.Count == 0) { connectionId = 0; return false; }
            connectionId = _accepted.Dequeue();
            return true;
        }

        public bool TryTakeDisconnection(out int connectionId)
        {
            if (_disconnected.Count == 0) { connectionId = 0; return false; }
            connectionId = _disconnected.Dequeue();
            return true;
        }

        public bool TryReceive(out int connectionId, out ArraySegment<byte> payload)
        {
            if (_inbox.Count == 0) { connectionId = 0; payload = default; return false; }
            (int connection, byte[] data) = _inbox.Dequeue();
            connectionId = connection;
            payload = new ArraySegment<byte>(data);
            return true;
        }

        public void Send(int connectionId, ArraySegment<byte> payload, Channel channel)
        {
            if (payload.Count == 0) return;
            if (!_byId.TryGetValue(connectionId, out UdpConnection connection)) return;
            SendTo(connection, payload, channel);
        }

        private void SendTo(UdpConnection connection, ArraySegment<byte> payload, Channel channel)
        {
            if (channel == Channel.UnreliableSequenced)
            {
                if (payload.Count > UdpPacket.MaxPayload) return;   // corrections are small; never fragment them
                int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.UnreliableData, 0,
                    connection.Reliability.AckSequence, connection.Reliability.AckBits, 0, 1, payload);
                RawSend(connection, length);
                return;
            }

            // Reliable: split into fragments, each with its own sequence number so retransmission
            // works per fragment rather than per whole message.
            int fragmentCount = (payload.Count + UdpPacket.MaxPayload - 1) / UdpPacket.MaxPayload;
            if (fragmentCount > 255) return;

            for (int i = 0; i < fragmentCount; i++)
            {
                int offset = i * UdpPacket.MaxPayload;
                int size = Math.Min(UdpPacket.MaxPayload, payload.Count - offset);
                var slice = new ArraySegment<byte>(payload.Array!, payload.Offset + offset, size);

                var copy = new byte[size];
                Buffer.BlockCopy(payload.Array!, payload.Offset + offset, copy, 0, size);

                ushort sequence = connection.Reliability.Track(copy, size, NowMs);
                int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.ReliableData, sequence,
                    connection.Reliability.AckSequence, connection.Reliability.AckBits,
                    (byte)i, (byte)fragmentCount, slice);
                RawSend(connection, length);
            }
        }

        private void RawSend(UdpConnection connection, int length)
        {
            if (SimulatedLossPercent > 0 && _lossRng.Chance(SimulatedLossPercent)) return;
            try { _socket.SendTo(_sendBuffer, 0, length, SocketFlags.None, connection.EndPoint); }
            catch (SocketException) { /* transient: retransmission will cover reliable traffic */ }
        }

        public void Poll()
        {
            DrainSocket();
            Retransmit();
            ExpireConnections();
        }

        private void DrainSocket()
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                int received;
                try
                {
                    if (_socket.Available <= 0) return;
                    received = _socket.ReceiveFrom(_receiveBuffer, ref from);
                }
                catch (SocketException) { return; }

                if (!UdpPacket.Read(_receiveBuffer, received, out UdpPacket.PacketType type, out ushort sequence,
                        out ushort ackSequence, out uint ackBits, out byte fragmentIndex, out byte fragmentCount,
                        out int payloadOffset, out int payloadLength))
                {
                    continue;   // malformed
                }

                var endPoint = (IPEndPoint)from;
                string key = endPoint.ToString();

                if (type == UdpPacket.PacketType.ConnectRequest)
                {
                    HandleConnectRequest(endPoint, key);
                    continue;
                }

                if (!_byEndPoint.TryGetValue(key, out UdpConnection connection)) continue;   // not a known peer

                connection.Reliability.Acknowledge(ackSequence, ackBits);

                if (type == UdpPacket.PacketType.Disconnect)
                {
                    Drop(connection);
                    continue;
                }

                if (!connection.Reliability.NoteReceived(sequence, NowMs)) continue;   // duplicate
                if (type == UdpPacket.PacketType.Keepalive) continue;
                if (payloadLength <= 0) continue;

                var payload = new byte[payloadLength];
                Buffer.BlockCopy(_receiveBuffer, payloadOffset, payload, 0, payloadLength);

                if (type == UdpPacket.PacketType.UnreliableData)
                {
                    _inbox.Enqueue((connection.Id, payload));
                    continue;
                }

                _delivered.Clear();
                connection.Reliability.Deliver(sequence, payload, _delivered);
                for (int i = 0; i < _delivered.Count; i++)
                {
                    byte[] complete = connection.Reassemble(_delivered[i], fragmentIndex, fragmentCount)!;
                    if (complete != null) _inbox.Enqueue((connection.Id, complete));
                }
            }
        }

        private void HandleConnectRequest(IPEndPoint endPoint, string key)
        {
            if (!_byEndPoint.TryGetValue(key, out UdpConnection connection))
            {
                connection = new UdpConnection(_nextConnectionId++, endPoint, NowMs);
                _byEndPoint[key] = connection;
                _byId[connection.Id] = connection;
                _accepted.Enqueue(connection.Id);
            }

            // Always answer, so a request that was lost on the way back is retried successfully.
            int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.ConnectAccept, 0, 0, 0, 0, 1,
                new ArraySegment<byte>(Array.Empty<byte>()));
            try { _socket.SendTo(_sendBuffer, 0, length, SocketFlags.None, endPoint); }
            catch (SocketException) { }
        }

        private void Retransmit()
        {
            foreach (KeyValuePair<int, UdpConnection> pair in _byId)
            {
                UdpConnection connection = pair.Value;
                if (!connection.Connected) continue;

                _retransmits.Clear();
                connection.Reliability.CollectRetransmissions(NowMs, _retransmits);

                for (int i = 0; i < _retransmits.Count; i++)
                {
                    (ushort sequence, byte[] payload, int payloadLength) = _retransmits[i];
                    int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.ReliableData, sequence,
                        connection.Reliability.AckSequence, connection.Reliability.AckBits, 0, 1,
                        new ArraySegment<byte>(payload, 0, payloadLength));
                    RawSend(connection, length);
                }
            }
        }

        private void ExpireConnections()
        {
            List<UdpConnection>? dead = null;
            foreach (KeyValuePair<int, UdpConnection> pair in _byId)
                if (pair.Value.Reliability.HasTimedOut(NowMs)) (dead ??= new List<UdpConnection>()).Add(pair.Value);

            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) Drop(dead[i]);
        }

        private void Drop(UdpConnection connection)
        {
            if (!connection.Connected) return;
            connection.Connected = false;
            _byEndPoint.Remove(connection.EndPoint.ToString());
            _byId.Remove(connection.Id);
            _disconnected.Enqueue(connection.Id);
        }

        public int Retransmissions
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<int, UdpConnection> pair in _byId) total += pair.Value.Reliability.Retransmissions;
                return total;
            }
        }

        public void Dispose()
        {
            try { _socket.Close(); } catch { }
            _socket.Dispose();
        }
    }
}
