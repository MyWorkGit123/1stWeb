using System;
using System.Collections.Generic;
using Brinehold.Core.Serialization;
using Brinehold.Net;
using Brinehold.Net.Replication;
using Brinehold.Net.Transport;
using Brinehold.Protocol;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;

namespace Brinehold.Server
{
    /// <summary>
    /// One match, hosted authoritatively.
    ///
    /// The host owns the only simulation that matters. Clients send commands; this class decides
    /// whether each one is even worth handing to the simulation, steps the world, and then gives
    /// every player a stream describing only what they are allowed to know.
    ///
    /// Nothing here trusts a client for anything. The player id on an incoming command is ignored
    /// entirely — it is filled in from the authenticated session — and the rate limiter and sequence
    /// checks run before validation, so a flooding or replaying client is cheap to refuse.
    /// </summary>
    public sealed class MatchHost
    {
        /// <summary>Hard ceiling on commands accepted per player per second.</summary>
        public const int MaxCommandsPerSecond = 40;

        private sealed class Session
        {
            public int ConnectionId;
            public byte PlayerId;
            public bool Handshaken;
            public string Name = string.Empty;
            public uint LastSequence;
            /// <summary>Token bucket for rate limiting, in hundredths of a command.</summary>
            public int Tokens = MaxCommandsPerSecond * 100;
            public int DroppedForRateLimit;
            public int DroppedForReplay;
        }

        private readonly IServerTransport _transport;
        private readonly List<Session> _sessions = new List<Session>();
        private readonly BitWriter _scratch = new BitWriter(2048);
        private readonly List<int> _awaitingHello = new List<int>();

        /// <summary>Players whose connection has dropped. The disconnect grace flow lands in M6.</summary>
        public readonly List<byte> Disconnected = new List<byte>();

        public readonly SimWorld World;
        public readonly ReplicationServer Replication;
        public readonly MatchConfig Config;

        /// <summary>
        /// Records the match. Always on: a replay costs a few bytes per order and is the difference
        /// between a bug report you can reproduce and one you can only read about.
        /// </summary>
        public readonly Brinehold.Sim.Replay.ReplayWriter Replay;

        /// <summary>Commands refused before they ever reached the simulation.</summary>
        public int RejectedBeforeSimulation { get; private set; }

        public MatchHost(MatchConfig config, IServerTransport transport)
        {
            Config = config;
            World = new SimWorld(config);
            PrototypeMap.Build(World);
            Replication = new ReplicationServer(World);
            Replay = new Brinehold.Sim.Replay.ReplayWriter(config);
            _transport = transport;
        }

        /// <summary>Convenience for the loopback path used by tests and listen mode.</summary>
        public MatchHost(MatchConfig config, LoopbackNetwork network)
            : this(config, new LoopbackServerTransport(network)) { }

        /// <summary>Registers a connection and assigns it a player slot.</summary>
        public bool TryConnect(int connectionId, string name, ushort protocolVersion, ulong contentHash, out WelcomeMessage welcome)
        {
            welcome = new WelcomeMessage
            {
                PlayerCount = (byte)Config.PlayerCount,
                MapWidth = (ushort)Config.MapWidth,
                MapHeight = (ushort)Config.MapHeight,
                Seed = Config.Seed,
                ContentHash = Config.ContentHash()
            };

            // A refusal is transmitted, not merely returned. A client that is told nothing cannot
            // tell its player whether the build is out of date, the content has been edited, or the
            // match is simply full — it just appears to hang.
            if (protocolVersion != ProtocolVersion.Current)
            {
                welcome.Result = HandshakeResult.ProtocolMismatch;
                SendWelcome(connectionId, welcome);
                return false;
            }

            if (contentHash != Config.ContentHash())
            {
                welcome.Result = HandshakeResult.ContentMismatch;
                SendWelcome(connectionId, welcome);
                return false;
            }

            if (_sessions.Count >= Config.PlayerCount)
            {
                welcome.Result = HandshakeResult.MatchFull;
                SendWelcome(connectionId, welcome);
                return false;
            }

            var session = new Session
            {
                ConnectionId = connectionId,
                PlayerId = (byte)_sessions.Count,
                Handshaken = true,
                Name = name
            };
            _sessions.Add(session);

            welcome.Result = HandshakeResult.Accepted;
            welcome.PlayerId = session.PlayerId;
            SendWelcome(connectionId, welcome);
            return true;
        }

        private void SendWelcome(int connectionId, WelcomeMessage welcome)
        {
            _scratch.Reset();
            MessageCodec.Write(_scratch, welcome);
            _transport.Send(connectionId, _scratch.AsSegment(), Channel.ReliableOrdered);
        }

        public int PlayerCount => _sessions.Count;

        public bool AllPlayersConnected => _sessions.Count >= Config.PlayerCount;

        /// <summary>Advances the match by exactly one simulation tick.</summary>
        public void Tick()
        {
            _transport.Poll();
            AcceptPendingConnections();
            RemoveLostConnections();
            RefillRateLimiters();
            DrainClientPackets();

            World.Step();

            Replication.NoteIntentsFromEvents();
            Replication.UpdateShadow();

            for (int i = 0; i < _sessions.Count; i++)
            {
                Session session = _sessions[i];
                ArraySegment<byte> packet = Replication.BuildPacket(session.PlayerId);
                if (packet.Count == 0) continue;   // nothing this player can see changed
                _transport.Send(session.ConnectionId, packet, Channel.ReliableOrdered);
            }

            RecordReplayCheckpoints();
        }

        /// <summary>
        /// A transport-level connection is not yet a player. The session is created when the client
        /// sends a Hello that passes the version and content checks; until then the connection can
        /// send nothing that reaches the simulation.
        /// </summary>
        private void AcceptPendingConnections()
        {
            while (_transport.TryAcceptConnection(out int connectionId))
                _awaitingHello.Add(connectionId);
        }

        private void RemoveLostConnections()
        {
            while (_transport.TryTakeDisconnection(out int connectionId))
            {
                _awaitingHello.Remove(connectionId);
                for (int i = 0; i < _sessions.Count; i++)
                {
                    if (_sessions[i].ConnectionId != connectionId) continue;
                    Disconnected.Add(_sessions[i].PlayerId);
                    _sessions.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Writes a state fingerprint every 200 ticks, and the result when the match ends. Playback
        /// checks these, so a determinism regression is caught by CI rather than by a player.
        /// </summary>
        private void RecordReplayCheckpoints()
        {
            if (World.Tick % SimConstants.StateHashInterval == 0)
                Replay.RecordStateHash(World.Tick, World.ComputeStateHash());

            if (World.MatchOver && !_recordedEnd)
            {
                _recordedEnd = true;
                Replay.RecordStateHash(World.Tick, World.ComputeStateHash());
                Replay.RecordEnd(World.Tick, World.WinningTeam);
            }
        }

        private bool _recordedEnd;

        private void RefillRateLimiters()
        {
            int refill = MaxCommandsPerSecond * 100 / SimConstants.TicksPerSecond;
            for (int i = 0; i < _sessions.Count; i++)
            {
                Session session = _sessions[i];
                session.Tokens = Math.Min(MaxCommandsPerSecond * 100, session.Tokens + refill);
            }
        }

        private void DrainClientPackets()
        {
            while (_transport.TryReceive(out int connection, out ArraySegment<byte> payload))
            {
                Session? session = FindSession(connection);
                var reader = new BitReader(payload.Array!, payload.Offset, payload.Count);

                while (reader.BitsRemaining >= 8)
                {
                    var type = (MessageType)reader.ReadByte();
                    if (reader.EndOfStream) break;

                    // Before the handshake the only message a connection may send is Hello. Anything
                    // else from an unauthenticated peer is dropped without being parsed further.
                    if (session == null)
                    {
                        if (type != MessageType.Hello) break;
                        HelloMessage hello = MessageCodec.ReadHello(reader);
                        if (reader.EndOfStream) break;
                        HandleHello(connection, hello);
                        session = FindSession(connection);
                        continue;
                    }

                    switch (type)
                    {
                        case MessageType.Hello:
                            MessageCodec.ReadHello(reader);   // already handshaken: ignore
                            break;

                        case MessageType.ClientCommand:
                            HandleCommand(session, reader);
                            break;

                        case MessageType.Ping:
                            MessageCodec.ReadPing(reader);
                            break;

                        default:
                            // Unparseable from here on. Drop the rest of the packet rather than
                            // guessing at field widths for a message we do not know.
                            return;
                    }

                    if (reader.EndOfStream) break;
                }
            }
        }

        /// <summary>
        /// Completes the handshake for a connection that has just introduced itself. A refusal is
        /// answered with a Welcome carrying the reason, so the client can tell the player whether
        /// their build is out of date rather than simply failing to connect.
        /// </summary>
        private void HandleHello(int connectionId, HelloMessage hello)
        {
            if (!_awaitingHello.Contains(connectionId)) return;
            _awaitingHello.Remove(connectionId);

            string name = string.IsNullOrEmpty(hello.PlayerName) ? $"Player {_sessions.Count + 1}" : hello.PlayerName;
            TryConnect(connectionId, name, hello.ProtocolVersion, hello.ContentHash, out _);
        }

        private void HandleCommand(Session session, BitReader reader)
        {
            // The player id comes from the session, never from the packet.
            Command command = MessageCodec.ReadCommand(reader, session.PlayerId);
            if (reader.EndOfStream) return;

            // Replay protection: sequence numbers must strictly increase.
            if (command.Sequence <= session.LastSequence)
            {
                session.DroppedForReplay++;
                RejectedBeforeSimulation++;
                return;
            }

            if (session.Tokens < 100)
            {
                session.DroppedForRateLimit++;
                RejectedBeforeSimulation++;
                World.Events.Add(SimEvent.Rejected(session.PlayerId, command.Sequence, RejectReason.RateLimited));
                return;
            }

            session.Tokens -= 100;
            session.LastSequence = command.Sequence;

            // Recorded against the tick it will execute on, not the tick it arrived: replaying by
            // arrival would re-order anything the network delayed.
            Replay.RecordCommand(World.Tick + 1, command);
            World.EnqueueCommand(command);
        }

        private Session? FindSession(int connectionId)
        {
            for (int i = 0; i < _sessions.Count; i++)
                if (_sessions[i].ConnectionId == connectionId) return _sessions[i];
            return null;
        }

        public int DroppedForRateLimit(byte playerId)
        {
            for (int i = 0; i < _sessions.Count; i++)
                if (_sessions[i].PlayerId == playerId) return _sessions[i].DroppedForRateLimit;
            return 0;
        }

        public int DroppedForReplay(byte playerId)
        {
            for (int i = 0; i < _sessions.Count; i++)
                if (_sessions[i].PlayerId == playerId) return _sessions[i].DroppedForReplay;
            return 0;
        }
    }
}
