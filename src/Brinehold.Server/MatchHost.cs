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

        private readonly LoopbackNetwork _network;
        private readonly List<Session> _sessions = new List<Session>();
        private readonly BitWriter _scratch = new BitWriter(2048);

        public readonly SimWorld World;
        public readonly ReplicationServer Replication;
        public readonly MatchConfig Config;

        /// <summary>Commands refused before they ever reached the simulation.</summary>
        public int RejectedBeforeSimulation { get; private set; }

        public MatchHost(MatchConfig config, LoopbackNetwork network)
        {
            Config = config;
            World = new SimWorld(config);
            PrototypeMap.Build(World);
            Replication = new ReplicationServer(World);
            _network = network;
        }

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

            if (protocolVersion != ProtocolVersion.Current)
            {
                welcome.Result = HandshakeResult.ProtocolMismatch;
                return false;
            }

            if (contentHash != Config.ContentHash())
            {
                welcome.Result = HandshakeResult.ContentMismatch;
                return false;
            }

            if (_sessions.Count >= Config.PlayerCount)
            {
                welcome.Result = HandshakeResult.MatchFull;
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

            _scratch.Reset();
            MessageCodec.Write(_scratch, welcome);
            _network.SendToClient(connectionId, _scratch.AsSegment(), Channel.ReliableOrdered);
            return true;
        }

        public int PlayerCount => _sessions.Count;

        public bool AllPlayersConnected => _sessions.Count >= Config.PlayerCount;

        /// <summary>Advances the match by exactly one simulation tick.</summary>
        public void Tick()
        {
            _network.Tick();
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
                _network.SendToClient(session.ConnectionId, packet, Channel.ReliableOrdered);
            }
        }

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
            while (_network.TryReceiveServer(out int connection, out ArraySegment<byte> payload))
            {
                Session? session = FindSession(connection);
                if (session == null) continue;   // unknown connection: ignore entirely

                var reader = new BitReader(payload.Array!, payload.Offset, payload.Count);
                while (reader.BitsRemaining >= 8)
                {
                    var type = (MessageType)reader.ReadByte();
                    if (reader.EndOfStream) break;

                    switch (type)
                    {
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
