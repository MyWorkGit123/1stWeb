using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Core.Serialization;
using Brinehold.Protocol;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Net.Client
{
    /// <summary>
    /// What one client believes about the match.
    ///
    /// This is a presentation replica, not a second simulation. It never decides anything: it holds
    /// only what the server chose to send, extrapolates movement between intent messages so units
    /// animate smoothly, and snaps when corrected. A player who edits this in memory changes what
    /// their own screen draws and nothing else, and the next correction overwrites it.
    ///
    /// Crucially it also cannot be mined for information the player should not have, because the
    /// entities they are not allowed to see were never transmitted into it.
    /// </summary>
    public sealed class ReplicaWorld
    {
        /// <summary>Snap rather than smooth when a correction is further away than this.</summary>
        private static readonly Fix64 SnapDistance = Fix64.FromInt(2);

        public sealed class Entity
        {
            public EntityId Id;
            public EntityKind Kind;
            public byte Owner;
            public BuildingType Building;
            public ResourceNodeType Node;
            public bool UnderConstruction;
            public Fix64 HealthRatio;
            public IntentExtrapolatorState State;
            /// <summary>False once the entity has left vision but is still remembered (buildings, nodes).</summary>
            public bool Live = true;
        }

        /// <summary>Alias so the view layer does not have to name the replication namespace.</summary>
        public struct IntentExtrapolatorState
        {
            public Replication.IntentExtrapolator.Entity Value;
        }

        private readonly Dictionary<uint, Entity> _entities = new Dictionary<uint, Entity>();
        private readonly List<uint> _order = new List<uint>();
        private readonly Replication.IntentExtrapolator _extrapolator;

        public readonly NavGrid Nav;
        public byte LocalPlayer { get; private set; }

        /// <summary>True once the server has accepted this client into a player slot.</summary>
        public bool Welcomed { get; private set; }

        /// <summary>The server's handshake verdict, so a refused client can explain itself.</summary>
        public HandshakeResult Handshake { get; private set; } = HandshakeResult.Accepted;
        public uint Tick { get; private set; }
        public bool MatchOver { get; private set; }
        public bool LocalPlayerWon { get; private set; }
        public int WinningTeam { get; private set; } = -1;

        public int Wood, Food, Stone, Coin, PopulationUsed, PopulationCap;

        /// <summary>Rejections the server sent back, for the UI to explain to the player.</summary>
        public readonly List<CommandRejectedMessage> Rejections = new List<CommandRejectedMessage>();
        public readonly List<GameEventMessage> RecentEvents = new List<GameEventMessage>();

        public ReplicaWorld(NavGrid nav, byte localPlayer)
        {
            Nav = nav;
            LocalPlayer = localPlayer;
            _extrapolator = new Replication.IntentExtrapolator(nav);
        }

        public int EntityCount => _entities.Count;

        /// <summary>Entities in a stable order, so the renderer and tests see a deterministic list.</summary>
        public IEnumerable<Entity> Entities
        {
            get
            {
                _order.Clear();
                foreach (KeyValuePair<uint, Entity> pair in _entities) _order.Add(pair.Key);
                _order.Sort();
                for (int i = 0; i < _order.Count; i++) yield return _entities[_order[i]];
            }
        }

        public bool TryGet(EntityId id, out Entity entity) => _entities.TryGetValue(id.Raw, out entity!);

        public bool Knows(EntityId id) => _entities.ContainsKey(id.Raw);

        /// <summary>Decodes one server packet and applies every message in it.</summary>
        public void Receive(System.ArraySegment<byte> payload)
        {
            var reader = new BitReader(payload.Array!, payload.Offset, payload.Count);
            RecentEvents.Clear();

            while (reader.BitsRemaining >= 8)
            {
                var type = (MessageType)reader.ReadByte();
                if (reader.EndOfStream) return;

                switch (type)
                {
                    case MessageType.TickHeader:
                        Tick = MessageCodec.ReadTickHeader(reader).Tick;
                        break;

                    case MessageType.SpawnEntity:
                        ApplySpawn(MessageCodec.ReadSpawn(reader));
                        break;

                    case MessageType.DespawnEntity:
                        ApplyDespawn(MessageCodec.ReadDespawn(reader));
                        break;

                    case MessageType.SetIntent:
                        ApplyIntent(MessageCodec.ReadIntent(reader));
                        break;

                    case MessageType.Correction:
                        ApplyCorrection(MessageCodec.ReadCorrection(reader));
                        break;

                    case MessageType.PrivateDelta:
                        ApplyPrivate(MessageCodec.ReadPrivateDelta(reader));
                        break;

                    case MessageType.GameEvent:
                        RecentEvents.Add(MessageCodec.ReadGameEvent(reader));
                        break;

                    case MessageType.CommandRejected:
                        Rejections.Add(MessageCodec.ReadRejected(reader));
                        break;

                    case MessageType.MatchEnd:
                        MatchEndMessage end = MessageCodec.ReadMatchEnd(reader);
                        MatchOver = true;
                        WinningTeam = end.WinningTeam;
                        LocalPlayerWon = end.LocalPlayerWon;
                        break;

                    case MessageType.Welcome:
                        WelcomeMessage welcome = MessageCodec.ReadWelcome(reader);
                        Handshake = welcome.Result;
                        if (welcome.Result == HandshakeResult.Accepted)
                        {
                            LocalPlayer = welcome.PlayerId;
                            Welcomed = true;
                        }
                        break;

                    default:
                        // An unknown message type means the stream is no longer parseable; stop
                        // rather than guessing at field widths.
                        return;
                }

                if (reader.EndOfStream) return;
            }
        }

        /// <summary>Advances local extrapolation by one tick. Called once per simulation tick.</summary>
        public void Step()
        {
            _order.Clear();
            foreach (KeyValuePair<uint, Entity> pair in _entities) _order.Add(pair.Key);
            _order.Sort();

            for (int i = 0; i < _order.Count; i++)
            {
                Entity entity = _entities[_order[i]];
                if (!entity.Live) continue;
                Replication.IntentExtrapolator.Entity state = entity.State.Value;
                _extrapolator.Step(ref state);
                entity.State.Value = state;
            }
        }

        // ------------------------------------------------------------------ message handlers

        private void ApplySpawn(SpawnEntityMessage m)
        {
            var entity = new Entity
            {
                Id = m.Entity,
                Kind = m.Kind,
                Owner = m.Owner,
                Building = m.Building,
                Node = m.Node,
                UnderConstruction = m.UnderConstruction,
                HealthRatio = Quantise.DecodeUnitRatio(m.HealthRatio),
                Live = true
            };

            Brinehold.Sim.Content.PrototypeContent.UnitStats stats =
                Brinehold.Sim.Content.PrototypeContent.ForKind(m.Kind);

            entity.State.Value = new Replication.IntentExtrapolator.Entity
            {
                Id = m.Entity,
                Kind = m.Kind,
                Owner = m.Owner,
                Domain = m.Kind == EntityKind.Building || m.Kind == EntityKind.ResourceNode
                    ? MovementDomain.Static
                    : stats.Domain,
                Speed = m.Kind == EntityKind.Building || m.Kind == EntityKind.ResourceNode
                    ? Fix64.Zero
                    : stats.MoveSpeed,
                Position = new Fix2(Quantise.DecodePosition(m.PositionX), Quantise.DecodePosition(m.PositionY)),
                Job = JobType.Idle
            };

            _entities[m.Entity.Raw] = entity;

            // Buildings block movement. Marking their footprint here keeps the client's navigation
            // grid in step with the server's, so local extrapolation follows the same route the
            // server does. Without this the client walks units straight through structures and
            // every step of the way is drift the server then has to correct.
            if (m.Kind == EntityKind.Building) SetBuildingFootprint(entity, true);
        }

        private void SetBuildingFootprint(Entity entity, bool occupied)
        {
            int half = Brinehold.Sim.Content.PrototypeContent.ForBuilding(entity.Building).FootprintHalf;
            Fix2 position = entity.State.Value.Position;
            int cell = Nav.CellAt(position);
            Nav.SetFootprint(Nav.CellX(cell), Nav.CellY(cell), half, occupied);
        }

        private void ApplyDespawn(DespawnEntityMessage m)
        {
            if (!_entities.TryGetValue(m.Entity.Raw, out Entity? entity)) return;

            // A destroyed building stops blocking movement. One that merely left vision is not
            // despawned at all, so its footprint correctly stays on the client's map.
            if (entity.Kind == EntityKind.Building && m.Destroyed) SetBuildingFootprint(entity, false);

            // Destroyed means gone. Merely losing sight of a mobile unit also removes it, because
            // remembering where an enemy was is exactly what fog is meant to prevent.
            _entities.Remove(m.Entity.Raw);
        }

        private void ApplyIntent(SetIntentMessage m)
        {
            if (!_entities.TryGetValue(m.Entity.Raw, out Entity? entity)) return;

            Replication.IntentExtrapolator.Entity state = entity.State.Value;
            state.Position = new Fix2(Quantise.DecodePosition(m.OriginX), Quantise.DecodePosition(m.OriginY));
            _extrapolator.SetIntent(ref state, m.Job,
                new Fix2(Quantise.DecodePosition(m.DestinationX), Quantise.DecodePosition(m.DestinationY)),
                m.Target);
            entity.State.Value = state;
        }

        private void ApplyCorrection(CorrectionMessage m)
        {
            if (!_entities.TryGetValue(m.Entity.Raw, out Entity? entity)) return;

            Replication.IntentExtrapolator.Entity state = entity.State.Value;
            var corrected = new Fix2(Quantise.DecodePosition(m.PositionX), Quantise.DecodePosition(m.PositionY));

            // A large correction snaps; a small one would be blended over ~150 ms by the view layer.
            // The replica itself always takes the authoritative value.
            state.Position = corrected;
            state.Heading = Quantise.DecodeAngle(m.Heading);
            entity.State.Value = state;
            entity.HealthRatio = Quantise.DecodeUnitRatio(m.HealthRatio);
        }

        private void ApplyPrivate(PrivateDeltaMessage m)
        {
            Wood = m.Wood;
            Food = m.Food;
            Stone = m.Stone;
            Coin = m.Coin;
            PopulationUsed = m.PopulationUsed;
            PopulationCap = m.PopulationCap;
        }

        /// <summary>Test and diagnostic helper: how far this replica thinks an entity is from a point.</summary>
        public Fix64 PositionErrorAgainst(EntityId id, Fix2 truth)
        {
            if (!_entities.TryGetValue(id.Raw, out Entity? entity)) return Fix64.MaxValue;
            return Fix2.Distance(entity.State.Value.Position, truth);
        }
    }
}
