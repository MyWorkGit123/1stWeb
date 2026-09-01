using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Brinehold.Net.Transport
{
    /// <summary>
    /// The client's UDP socket.
    ///
    /// Connection is a request-and-accept handshake retried until it succeeds or the attempt times
    /// out, because the first datagram of a session is exactly as likely to be lost as any other.
    /// Once connected, the same reliability layer the server uses runs here, so ordering and
    /// retransmission behave identically in both directions.
    /// </summary>
    public sealed class UdpClientTransport : IClientTransport
    {
        private readonly Socket _socket;
        private readonly IPEndPoint _serverEndPoint;
        private readonly UdpConnection _connection;
        private readonly Queue<byte[]> _inbox = new Queue<byte[]>();

        private readonly byte[] _receiveBuffer = new byte[2048];
        private readonly byte[] _sendBuffer = new byte[UdpPacket.MaxDatagram];
        private readonly List<byte[]> _delivered = new List<byte[]>(8);
        private readonly List<(ushort sequence, byte[] payload, int length)> _retransmits =
            new List<(ushort, byte[], int)>(16);

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastConnectAttemptMs = -1000;
        private long _lastKeepaliveMs;
        private bool _ackPending;
        private long _ackPendingSinceMs;

        /// <summary>Milliseconds between connection attempts while unconnected.</summary>
        public int ConnectRetryMs = 250;

        /// <summary>Send an empty packet if nothing else has gone out for this long, to carry acks.</summary>
        public int KeepaliveMs = 500;

        /// <summary>
        /// Acknowledge received reliable data within this long, even with nothing to say.
        ///
        /// A client that only speaks every 500 ms leaves the server retransmitting packets that
        /// arrived perfectly well: at twenty packets a second against a 120 ms retransmit timeout,
        /// most of the stream gets sent twice. Prompt acknowledgement costs eleven bytes and removes
        /// the waste entirely.
        /// </summary>
        public int AckDelayMs = 25;

        public bool IsConnected { get; private set; }

        public UdpClientTransport(IPEndPoint server)
        {
            _serverEndPoint = server;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                Blocking = false,
                SendBufferSize = 1 << 20,
                ReceiveBufferSize = 1 << 20
            };
            _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            _connection = new UdpConnection(0, server, _clock.ElapsedMilliseconds);
        }

        public static UdpClientTransport Connect(string host, int port)
        {
            IPAddress address = IPAddress.TryParse(host, out IPAddress? parsed)
                ? parsed
                : Dns.GetHostAddresses(host)[0];
            return new UdpClientTransport(new IPEndPoint(address, port));
        }

        private long NowMs => _clock.ElapsedMilliseconds;

        public void Send(ArraySegment<byte> payload, Channel channel)
        {
            if (payload.Count == 0 || !IsConnected) return;

            if (channel == Channel.UnreliableSequenced)
            {
                if (payload.Count > UdpPacket.MaxPayload) return;
                int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.UnreliableData, 0,
                    _connection.Reliability.AckSequence, _connection.Reliability.AckBits, 0, 1, payload);
                RawSend(length);
                return;
            }

            int fragmentCount = (payload.Count + UdpPacket.MaxPayload - 1) / UdpPacket.MaxPayload;
            if (fragmentCount > 255) return;

            for (int i = 0; i < fragmentCount; i++)
            {
                int offset = i * UdpPacket.MaxPayload;
                int size = Math.Min(UdpPacket.MaxPayload, payload.Count - offset);
                var slice = new ArraySegment<byte>(payload.Array!, payload.Offset + offset, size);

                var copy = new byte[size];
                Buffer.BlockCopy(payload.Array!, payload.Offset + offset, copy, 0, size);

                ushort sequence = _connection.Reliability.Track(copy, size, NowMs);
                int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.ReliableData, sequence,
                    _connection.Reliability.AckSequence, _connection.Reliability.AckBits,
                    (byte)i, (byte)fragmentCount, slice);
                RawSend(length);
            }

            _lastKeepaliveMs = NowMs;
            _ackPending = false;
        }

        public bool TryReceive(out ArraySegment<byte> payload)
        {
            if (_inbox.Count == 0) { payload = default; return false; }
            payload = new ArraySegment<byte>(_inbox.Dequeue());
            return true;
        }

        public void Poll()
        {
            if (!IsConnected) AttemptConnect();
            DrainSocket();
            if (!IsConnected) return;

            Retransmit();
            SendKeepaliveIfIdle();
        }

        /// <summary>Blocks until connected or the timeout expires. Convenience for console clients.</summary>
        public bool WaitForConnection(int timeoutMs)
        {
            long deadline = NowMs + timeoutMs;
            while (NowMs < deadline)
            {
                Poll();
                if (IsConnected) return true;
                System.Threading.Thread.Sleep(5);
            }
            return IsConnected;
        }

        private void AttemptConnect()
        {
            if (NowMs - _lastConnectAttemptMs < ConnectRetryMs) return;
            _lastConnectAttemptMs = NowMs;

            int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.ConnectRequest, 0, 0, 0, 0, 1,
                new ArraySegment<byte>(Array.Empty<byte>()));
            RawSend(length);
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

                // Ignore anything that did not come from the server we are talking to.
                if (!((IPEndPoint)from).Equals(_serverEndPoint)) continue;

                if (!UdpPacket.Read(_receiveBuffer, received, out UdpPacket.PacketType type, out ushort sequence,
                        out ushort ackSequence, out uint ackBits, out byte fragmentIndex, out byte fragmentCount,
                        out int payloadOffset, out int payloadLength))
                {
                    continue;
                }

                if (type == UdpPacket.PacketType.ConnectAccept)
                {
                    IsConnected = true;
                    _connection.Reliability.NoteReceived(0, NowMs);
                    continue;
                }

                if (type == UdpPacket.PacketType.Disconnect) { IsConnected = false; continue; }

                _connection.Reliability.Acknowledge(ackSequence, ackBits);
                if (!_connection.Reliability.NoteReceived(sequence, NowMs)) continue;
                if (type == UdpPacket.PacketType.Keepalive) continue;
                if (payloadLength <= 0) continue;

                var payload = new byte[payloadLength];
                Buffer.BlockCopy(_receiveBuffer, payloadOffset, payload, 0, payloadLength);

                if (type == UdpPacket.PacketType.UnreliableData) { _inbox.Enqueue(payload); continue; }

                // Reliable data has arrived: the server is waiting to hear about it.
                if (!_ackPending) { _ackPending = true; _ackPendingSinceMs = NowMs; }

                _delivered.Clear();
                _connection.Reliability.Deliver(sequence, payload, _delivered);
                for (int i = 0; i < _delivered.Count; i++)
                {
                    byte[] complete = _connection.Reassemble(_delivered[i], fragmentIndex, fragmentCount)!;
                    if (complete != null) _inbox.Enqueue(complete);
                }
            }
        }

        private void Retransmit()
        {
            _retransmits.Clear();
            _connection.Reliability.CollectRetransmissions(NowMs, _retransmits);

            for (int i = 0; i < _retransmits.Count; i++)
            {
                (ushort sequence, byte[] payload, int payloadLength) = _retransmits[i];
                int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.ReliableData, sequence,
                    _connection.Reliability.AckSequence, _connection.Reliability.AckBits, 0, 1,
                    new ArraySegment<byte>(payload, 0, payloadLength));
                RawSend(length);
            }
        }

        /// <summary>
        /// Acknowledgements normally ride on ordinary traffic, but a client that is not issuing
        /// orders sends nothing at all, which would leave the server retransmitting forever.
        /// </summary>
        private void SendKeepaliveIfIdle()
        {
            bool ackDue = _ackPending && NowMs - _ackPendingSinceMs >= AckDelayMs;
            if (!ackDue && NowMs - _lastKeepaliveMs < KeepaliveMs) return;

            _lastKeepaliveMs = NowMs;
            _ackPending = false;

            int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.Keepalive, 0,
                _connection.Reliability.AckSequence, _connection.Reliability.AckBits, 0, 1,
                new ArraySegment<byte>(Array.Empty<byte>()));
            RawSend(length);
        }

        private void RawSend(int length)
        {
            try { _socket.SendTo(_sendBuffer, 0, length, SocketFlags.None, _serverEndPoint); }
            catch (SocketException) { }
        }

        public int Retransmissions => _connection.Reliability.Retransmissions;

        public void Disconnect()
        {
            if (!IsConnected) return;
            int length = UdpPacket.Write(_sendBuffer, UdpPacket.PacketType.Disconnect, 0, 0, 0, 0, 1,
                new ArraySegment<byte>(Array.Empty<byte>()));
            RawSend(length);
            IsConnected = false;
        }

        public void Dispose()
        {
            Disconnect();
            try { _socket.Close(); } catch { }
            _socket.Dispose();
        }
    }
}
