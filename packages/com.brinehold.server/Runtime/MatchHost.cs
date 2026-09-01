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

            /// <summary>The secret this client presents to reclaim its slot after a drop.</summary>
            public ulong ReconnectToken;

            /// <summary>Tick the connection was lost on, or zero while connected.</summary>
            public uint DroppedAtTick;
            public bool IsDropped => DroppedAtTick != 0;
        }

        private readonly IServerTransport _transport;
        private readonly List<Session> _sessions = new List<Session>();
        private readonly BitWriter _scratch = new BitWriter(2048);
        private readonly List<int> _awaitingHello = new List<int>();
        private readonly List<Session> _dropped = new List<Session>();
        private readonly Brinehold.Core.Random.DeterministicRandom _tokens =
            new Brinehold.Core.Random.DeterministicRandom((ulong)System.DateTime.UtcNow.Ticks);

        /// <summary>
        /// How long a dropped player keeps their slot. Their settlement keeps running while they are
        /// away: buildings produce, standing orders continue, and an opponent can attack them. This
        /// is deliberate — a disconnect must not be a free pause.
        /// </summary>
        public uint DisconnectGraceTicks = 180 * SimConstants.TicksPerSecond;

        /// <summary>Players currently disconnected but still inside their grace window.</summary>
        public IReadOnlyList<byte> AwaitingReconnect
        {
            get
            {
                var result = new List<byte>();
                for (int i = 0; i < _dropped.Count; i++) result.Add(_dropped[i].PlayerId);
                return result;
            }
        }

        /// <summary>Players whose grace window expired and who were resigned.</summary>
        public readonly List<byte> Resigned = new List<byte>();

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
            => TryConnect(connectionId, name, protocolVersion, contentHash, 0, out welcome);

        public bool TryConnect(int connectionId, string name, ushort protocolVersion, ulong contentHash,
                               ulong reconnectToken, out WelcomeMessage welcome)
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

            // A client presenting a token is trying to reclaim a slot it already held.
            if (reconnectToken != 0)
            {
                Session? restored = TakeDroppedSession(reconnectToken);
                if (restored == null)
                {
                    welcome.Result = HandshakeResult.UnknownReconnectToken;
                    SendWelcome(connectionId, welcome);
                    return false;
                }

                restored.ConnectionId = connectionId;
                restored.DroppedAtTick = 0;
                _sessions.Add(restored);

                // Forget what this client was believed to know, so the next packet rebuilds their
                // whole visible world and their economy from scratch.
                Replication.ResetPlayerView(restored.PlayerId);

                welcome.Result = HandshakeResult.Accepted;
                welcome.PlayerId = restored.PlayerId;
                welcome.ReconnectToken = restored.ReconnectToken;
                welcome.Reconnected = true;
                welcome.Tick = World.Tick;
                SendWelcome(connectionId, welcome);
                return true;
            }

            if (_sessions.Count + _dropped.Count >= Config.PlayerCount)
            {
                welcome.Result = HandshakeResult.MatchFull;
                SendWelcome(connectionId, welcome);
                return false;
            }

            var session = new Session
            {
                ConnectionId = connectionId,
                PlayerId = (byte)(_sessions.Count + _dropped.Count),
                Handshaken = true,
                Name = name,
                ReconnectToken = NextToken()
            };
            _sessions.Add(session);

            welcome.Result = HandshakeResult.Accepted;
            welcome.PlayerId = session.PlayerId;
            welcome.ReconnectToken = session.ReconnectToken;
            welcome.Tick = World.Tick;
            SendWelcome(connectionId, welcome);
            return true;
        }

        private ulong NextToken()
        {
            ulong token = _tokens.NextULong();
            return token == 0 ? 1 : token;   // zero means "no token"
        }

        private Session? TakeDroppedSession(ulong token)
        {
            for (int i = 0; i < _dropped.Count; i++)
            {
                if (_dropped[i].ReconnectToken != token) continue;
                Session session = _dropped[i];
                _dropped.RemoveAt(i);
                return session;
            }
            return null;
        }

        private void SendWelcome(int connectionId, WelcomeMessage welcome)
        {
            _scratch.Reset();
            MessageCodec.Write(_scratch, welcome);
            _transport.Send(connectionId, _scratch.AsSegment(), Channel.ReliableOrdered);
        }

        public int PlayerCount => _sessions.Count;

        public bool AllPlayersConnected => _sessions.Count >= Config.PlayerCount;

        /// <summary>Slots that have been claimed, whether or not the player is currently connected.</summary>
        public int ClaimedSlots => _sessions.Count + _dropped.Count;

        /// <summary>Advances the match by exactly one simulation tick.</summary>
        public void Tick()
        {
            _transport.Poll();
            AcceptPendingConnections();
            RemoveLostConnections();
            ExpireReconnectWindows();
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

                    // The player keeps their slot, and their settlement keeps running, until the
                    // grace window expires. Losing your connection should cost you the time you
                    // were away, not the match outright — and it must not pause anyone else.
                    Session session = _sessions[i];
                    session.DroppedAtTick = World.Tick == 0 ? 1 : World.Tick;
                    _sessions.RemoveAt(i);
                    _dropped.Add(session);
                    break;
                }
            }
        }

        /// <summary>
        /// Resigns anyone whose grace window has run out. Handing the settlement to an AI instead is
        /// a lobby setting in M14, once there is an AI to hand it to.
        /// </summary>
        private void ExpireReconnectWindows()
        {
            for (int i = _dropped.Count - 1; i >= 0; i--)
            {
                Session session = _dropped[i];
                if (World.Tick - session.DroppedAtTick < DisconnectGraceTicks) continue;

                _dropped.RemoveAt(i);
                Resigned.Add(session.PlayerId);
                if (session.PlayerId < World.Players.Length)
                    World.Players[session.PlayerId].Defeated = true;
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
            TryConnect(connectionId, name, hello.ProtocolVersion, hello.ContentHash, hello.ReconnectToken, out _);
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
