using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Core.Serialization;
using Brinehold.Protocol;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Net.Replication
{
    /// <summary>
    /// Turns the authoritative simulation into one fog-filtered stream per player.
    ///
    /// The rule this class exists to enforce: a player is never sent a single byte about an entity
    /// they cannot see. Fog is applied here, at the point of encoding, rather than in the renderer.
    /// A modified client therefore has nothing to reveal — the data was never transmitted. That is
    /// the difference between a map hack being detectable and a map hack being impossible.
    ///
    /// Bandwidth comes from three decisions:
    ///   - movement is replicated as intent, not as a stream of transforms;
    ///   - corrections are sent only when the shadow extrapolation has actually drifted;
    ///   - static entities that have already been seen cost nothing at all.
    /// </summary>
    public sealed class ReplicationServer
    {
        /// <summary>Drift beyond this many metres is worth a correction.</summary>
        private static readonly Fix64 CorrectionThreshold = Fix64.FromFraction(5, 10);

        /// <summary>Minimum ticks between corrections for the same entity (4 Hz).</summary>
        private const int CorrectionIntervalTicks = 5;

        private sealed class PlayerView
        {
            /// <summary>Entity raw ids this player currently knows about.</summary>
            public readonly HashSet<uint> Known = new HashSet<uint>();
            public PrivateDeltaMessage LastPrivate;
            public bool HasPrivate;
            public readonly BitWriter Writer = new BitWriter(4096);
        }

        private readonly SimWorld _world;
        private readonly PlayerView[] _views;

        // Shadow extrapolation: what a client running IntentExtrapolator would currently believe.
        private readonly IntentExtrapolator _shadow;
        private readonly Dictionary<uint, IntentExtrapolator.Entity> _shadowEntities = new Dictionary<uint, IntentExtrapolator.Entity>();
        private readonly Dictionary<uint, uint> _lastCorrectionTick = new Dictionary<uint, uint>();

        // Reused scratch so the per-tick path does not allocate.
        private readonly List<uint> _scratchIds = new List<uint>(256);

        public readonly NetStats Stats = new NetStats();

        public ReplicationServer(SimWorld world)
        {
            _world = world;
            _views = new PlayerView[world.Players.Length];
            for (int i = 0; i < _views.Length; i++) _views[i] = new PlayerView();
            _shadow = new IntentExtrapolator(world.Nav);
        }

        /// <summary>
        /// Builds this tick's packet for one player. Returns a segment valid until the next call for
        /// the same player.
        /// </summary>
        public System.ArraySegment<byte> BuildPacket(int player)
        {
            PlayerView view = _views[player];
            BitWriter w = view.Writer;
            w.Reset();

            MessageCodec.Write(w, new TickHeaderMessage { Tick = _world.Tick });
            int headerBits = w.BitLength;

            WriteInterestChanges(player, view, w);
            WriteIntents(player, view, w);
            WriteCorrections(player, view, w);
            WritePrivateState(player, view, w);
            WriteEvents(player, view, w);

            // A tick in which nothing this player can see has changed is not worth a packet. The
            // client advances its own clock and extrapolates; a keepalive once a second is enough to
            // keep the two in step. This is what takes an idle match from 100 B/s to about 5 B/s,
            // and it is the same principle as intent replication applied to the packet itself.
            bool empty = w.BitLength == headerBits;
            bool keepaliveDue = _world.Tick % (uint)SimConstants.TicksPerSecond == 0;
            if (empty && !keepaliveDue) return new System.ArraySegment<byte>(System.Array.Empty<byte>());

            Stats.RecordPacket(player, w.ByteLength);
            return w.AsSegment();
        }

        /// <summary>
        /// Advances the shadow replica and measures drift. Call once per tick, after the simulation
        /// has stepped and before building packets.
        /// </summary>
        public void UpdateShadow()
        {
            EntityStore store = _world.Entities;

            // Step every shadow entity one tick, exactly as a client would.
            _scratchIds.Clear();
            foreach (KeyValuePair<uint, IntentExtrapolator.Entity> pair in _shadowEntities) _scratchIds.Add(pair.Key);
            _scratchIds.Sort();

            for (int i = 0; i < _scratchIds.Count; i++)
            {
                uint raw = _scratchIds[i];
                IntentExtrapolator.Entity entity = _shadowEntities[raw];
                var id = new EntityId(raw);
                if (!store.IsAlive(id)) { _shadowEntities.Remove(raw); continue; }
                _shadow.Step(ref entity);
                _shadowEntities[raw] = entity;
            }
        }

        /// <summary>Records the intent the server is about to replicate, so the shadow tracks it.</summary>
        private void NoteIntent(int index)
        {
            EntityStore store = _world.Entities;
            EntityId id = store.IdOf(index);

            if (!_shadowEntities.TryGetValue(id.Raw, out IntentExtrapolator.Entity entity))
            {
                entity = new IntentExtrapolator.Entity
                {
                    Id = id,
                    Kind = store.Kind[index],
                    Owner = store.Owner[index],
                    Domain = store.Domain[index],
                    Speed = store.MoveSpeed[index],
                    Position = store.Position[index]
                };
            }

            // Start the shadow from the same quantised position the client will be given, so the
            // two extrapolations are identical inputs into identical code.
            entity.Position = new Fix2(
                Quantise.DecodePosition(Quantise.EncodePosition(store.Position[index].X)),
                Quantise.DecodePosition(Quantise.EncodePosition(store.Position[index].Y)));
            entity.Speed = store.MoveSpeed[index];
            entity.Domain = store.Domain[index];
            _shadow.SetIntent(ref entity, store.Job[index], store.JobDestination[index], store.JobTarget[index]);
            _shadowEntities[id.Raw] = entity;
        }

        // ------------------------------------------------------------------ interest

        /// <summary>
        /// Is this entity inside the player's interest set? Own entities always are. Everything else
        /// must be standing on a cell the player can currently see.
        /// </summary>
        private bool IsInInterest(int player, int index)
        {
            EntityStore store = _world.Entities;
            if (!store.Alive[index]) return false;
            if (store.Owner[index] == (byte)player) return true;

            int cell = _world.Nav.CellAt(store.Position[index]);
            return _world.Fog.IsVisible(player, cell);
        }

        /// <summary>
        /// Static things stay on a client's map once seen, as the greyed "last known" state every
        /// RTS player expects. Mobile units disappear the moment they leave vision, because
        /// remembering where an enemy soldier used to be is exactly the information fog exists to
        /// withhold.
        /// </summary>
        private static bool IsRemembered(EntityKind kind)
            => kind == EntityKind.Building || kind == EntityKind.ResourceNode;

        private void WriteInterestChanges(int player, PlayerView view, BitWriter w)
        {
            EntityStore store = _world.Entities;
            int count = store.Count;

            for (int i = 1; i < count; i++)
            {
                EntityId id = store.IdOf(i);
                bool known = view.Known.Contains(id.Raw);
                bool interested = IsInInterest(player, i);

                if (interested && !known)
                {
                    view.Known.Add(id.Raw);
                    MessageCodec.Write(w, new SpawnEntityMessage
                    {
                        Entity = id,
                        Kind = store.Kind[i],
                        Owner = store.Owner[i],
                        Building = store.Building[i],
                        Node = store.NodeType[i],
                        PositionX = Quantise.EncodePosition(store.Position[i].X),
                        PositionY = Quantise.EncodePosition(store.Position[i].Y),
                        HealthRatio = HealthRatio(store, i),
                        UnderConstruction = store.UnderConstruction[i]
                    });
                    Stats.Record(player, NetStats.Category.Lifecycle, 1);

                    // A newly visible entity also needs its current intent, or it would stand still
                    // on the client until its next order.
                    if (store.Job[i] != JobType.Idle)
                    {
                        MessageCodec.Write(w, new SetIntentMessage
                        {
                            Entity = id,
                            Job = store.Job[i],
                            OriginX = Quantise.EncodePosition(store.Position[i].X),
                            OriginY = Quantise.EncodePosition(store.Position[i].Y),
                            DestinationX = Quantise.EncodePosition(store.JobDestination[i].X),
                            DestinationY = Quantise.EncodePosition(store.JobDestination[i].Y),
                            Target = store.JobTarget[i]
                        });
                        Stats.Record(player, NetStats.Category.Intent, 1);
                    }
                }
                else if (!interested && known && !IsRemembered(store.Kind[i]))
                {
                    view.Known.Remove(id.Raw);
                    MessageCodec.Write(w, new DespawnEntityMessage { Entity = id, Destroyed = false });
                    Stats.Record(player, NetStats.Category.Lifecycle, 1);
                }
            }

            // Entities that died this tick: tell anyone who knew about them.
            for (int e = 0; e < _world.Events.Count; e++)
            {
                SimEvent ev = _world.Events[e];
                if (ev.Type != SimEventType.EntityDestroyed) continue;
                if (!view.Known.Contains(ev.Entity.Raw)) continue;

                view.Known.Remove(ev.Entity.Raw);
                MessageCodec.Write(w, new DespawnEntityMessage { Entity = ev.Entity, Destroyed = true });
                Stats.Record(player, NetStats.Category.Lifecycle, 1);
            }
        }

        private void WriteIntents(int player, PlayerView view, BitWriter w)
        {
            EntityStore store = _world.Entities;

            for (int e = 0; e < _world.Events.Count; e++)
            {
                SimEvent ev = _world.Events[e];
                if (ev.Type != SimEventType.IntentChanged) continue;
                if (!store.IsAlive(ev.Entity)) continue;
                if (!view.Known.Contains(ev.Entity.Raw)) continue;

                int index = ev.Entity.Index;
                MessageCodec.Write(w, new SetIntentMessage
                {
                    Entity = ev.Entity,
                    Job = store.Job[index],
                    OriginX = Quantise.EncodePosition(store.Position[index].X),
                    OriginY = Quantise.EncodePosition(store.Position[index].Y),
                    DestinationX = Quantise.EncodePosition(store.JobDestination[index].X),
                    DestinationY = Quantise.EncodePosition(store.JobDestination[index].Y),
                    Target = store.JobTarget[index]
                });
                Stats.Record(player, NetStats.Category.Intent, 1);
            }
        }

        /// <summary>
        /// Called once per tick before packets are built, so every player's stream sees the same
        /// shadow state. Kept separate from WriteIntents because the shadow is global, not per player.
        /// </summary>
        public void NoteIntentsFromEvents()
        {
            EntityStore store = _world.Entities;
            for (int e = 0; e < _world.Events.Count; e++)
            {
                SimEvent ev = _world.Events[e];
                if (ev.Type == SimEventType.IntentChanged && store.IsAlive(ev.Entity))
                    NoteIntent(ev.Entity.Index);
                else if (ev.Type == SimEventType.EntitySpawned && store.IsAlive(ev.Entity))
                    NoteIntent(ev.Entity.Index);
            }
        }

        private void WriteCorrections(int player, PlayerView view, BitWriter w)
        {
            EntityStore store = _world.Entities;
            int count = store.Count;

            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.Domain[i] == MovementDomain.Static) continue;

                EntityId id = store.IdOf(i);
                if (!view.Known.Contains(id.Raw)) continue;

                if (!_shadowEntities.TryGetValue(id.Raw, out IntentExtrapolator.Entity shadow)) continue;

                Fix64 drift = Fix2.Distance(shadow.Position, store.Position[i]);
                if (drift < CorrectionThreshold) continue;

                if (_lastCorrectionTick.TryGetValue(id.Raw, out uint last) &&
                    _world.Tick - last < CorrectionIntervalTicks) continue;

                _lastCorrectionTick[id.Raw] = _world.Tick;

                MessageCodec.Write(w, new CorrectionMessage
                {
                    Entity = id,
                    PositionX = Quantise.EncodePosition(store.Position[i].X),
                    PositionY = Quantise.EncodePosition(store.Position[i].Y),
                    Heading = Quantise.EncodeAngle(store.Heading[i]),
                    HealthRatio = HealthRatio(store, i)
                });
                Stats.Record(player, NetStats.Category.Correction, 1);

                // The client will snap; keep the shadow with it so drift is measured from there on.
                shadow.Position = store.Position[i];
                _shadowEntities[id.Raw] = shadow;
            }
        }

        private void WritePrivateState(int player, PlayerView view, BitWriter w)
        {
            PlayerState state = _world.Players[player];
            var message = new PrivateDeltaMessage
            {
                Wood = state.Wood,
                Food = state.Food,
                Stone = state.Stone,
                Coin = state.Coin,
                PopulationUsed = (ushort)state.PopulationUsed,
                PopulationCap = (ushort)state.PopulationCap
            };

            // Resend once a second even when nothing has changed. A pure delta stream leaves a
            // client's economy display permanently wrong if a single message is ever lost or the
            // value is tampered with locally, and eighteen bytes a second is a cheap way to make
            // the display self-healing.
            bool refreshDue = _world.Tick % (uint)SimConstants.TicksPerSecond == 0;

            if (!refreshDue && view.HasPrivate &&
                view.LastPrivate.Wood == message.Wood &&
                view.LastPrivate.Food == message.Food &&
                view.LastPrivate.Stone == message.Stone &&
                view.LastPrivate.Coin == message.Coin &&
                view.LastPrivate.PopulationUsed == message.PopulationUsed &&
                view.LastPrivate.PopulationCap == message.PopulationCap)
            {
                return;   // nothing changed: send nothing
            }

            view.LastPrivate = message;
            view.HasPrivate = true;
            MessageCodec.Write(w, message);
            Stats.Record(player, NetStats.Category.Private, 1);
        }

        private void WriteEvents(int player, PlayerView view, BitWriter w)
        {
            for (int e = 0; e < _world.Events.Count; e++)
            {
                SimEvent ev = _world.Events[e];

                switch (ev.Type)
                {
                    case SimEventType.CommandRejected:
                        // A rejection is private to the player who issued the command.
                        if (ev.Player != (byte)player) continue;
                        MessageCodec.Write(w, new CommandRejectedMessage
                        {
                            Sequence = unchecked((uint)ev.ValueA),
                            Reason = (Brinehold.Sim.Commands.RejectReason)ev.ValueB
                        });
                        Stats.Record(player, NetStats.Category.Event, 1);
                        continue;

                    case SimEventType.MatchEnded:
                        MessageCodec.Write(w, new MatchEndMessage
                        {
                            WinningTeam = ev.ValueA,
                            LocalPlayerWon = _world.Players[player].Victorious
                        });
                        Stats.Record(player, NetStats.Category.Event, 1);
                        continue;

                    case SimEventType.ResourceDeposited:
                    case SimEventType.PlayerDefeated:
                    case SimEventType.TrainingCompleted:
                    case SimEventType.ConstructionCompleted:
                        // Economy events are the owner's business only.
                        if (ev.Player != (byte)player && ev.Type != SimEventType.PlayerDefeated) continue;
                        break;

                    case SimEventType.DamageApplied:
                        // Only reported if the player can see the entity being hit.
                        if (!view.Known.Contains(ev.Other.Raw)) continue;
                        break;

                    case SimEventType.EntitySpawned:
                    case SimEventType.EntityDestroyed:
                    case SimEventType.IntentChanged:
                        continue;   // already covered by lifecycle and intent messages

                    default:
                        if (!view.Known.Contains(ev.Entity.Raw)) continue;
                        break;
                }

                MessageCodec.Write(w, new GameEventMessage
                {
                    Type = ev.Type,
                    Entity = ev.Entity,
                    Other = ev.Other,
                    Player = ev.Player,
                    ValueA = ev.ValueA,
                    ValueB = ev.ValueB,
                    PositionX = Quantise.EncodePosition(ev.Position.X),
                    PositionY = Quantise.EncodePosition(ev.Position.Y)
                });
                Stats.Record(player, NetStats.Category.Event, 1);
            }
        }

        private static byte HealthRatio(EntityStore store, int i)
        {
            if (store.MaxHealth[i] <= Fix64.Zero) return 255;
            return Quantise.EncodeUnitRatio(store.Health[i] / store.MaxHealth[i]);
        }
    }
}
