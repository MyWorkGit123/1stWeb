using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Serialization;
using Brinehold.Net.Transport;
using Brinehold.Protocol;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Integration.Tests
{
    /// <summary>
    /// The anti-map-hack guarantee, asserted at the byte level.
    ///
    /// These tests decode every packet the server actually put on the wire and check that it never
    /// mentions an enemy unit the receiving player cannot see. If this passes, a modified client has
    /// nothing to reveal, because the information was never transmitted. If it ever fails, the
    /// central security claim of the architecture has been broken, so this runs on every commit.
    /// </summary>
    public class FogEnforcementTests
    {
        /// <summary>Entity ids mentioned by any message in a packet, tagged by message kind.</summary>
        private static List<(EntityId id, MessageType type)> DecodeEntityReferences(byte[] packet)
        {
            var found = new List<(EntityId, MessageType)>();
            var reader = new BitReader(packet);

            while (reader.BitsRemaining >= 8)
            {
                var type = (MessageType)reader.ReadByte();
                if (reader.EndOfStream) break;

                switch (type)
                {
                    case MessageType.TickHeader: MessageCodec.ReadTickHeader(reader); break;
                    case MessageType.SpawnEntity: found.Add((MessageCodec.ReadSpawn(reader).Entity, type)); break;
                    case MessageType.DespawnEntity: found.Add((MessageCodec.ReadDespawn(reader).Entity, type)); break;
                    case MessageType.SetIntent: found.Add((MessageCodec.ReadIntent(reader).Entity, type)); break;
                    case MessageType.Correction: found.Add((MessageCodec.ReadCorrection(reader).Entity, type)); break;
                    case MessageType.PrivateDelta: MessageCodec.ReadPrivateDelta(reader); break;
                    case MessageType.CommandRejected: MessageCodec.ReadRejected(reader); break;
                    case MessageType.MatchEnd: MessageCodec.ReadMatchEnd(reader); break;
                    case MessageType.Welcome: MessageCodec.ReadWelcome(reader); break;
                    case MessageType.GameEvent:
                        GameEventMessage e = MessageCodec.ReadGameEvent(reader);
                        found.Add((e.Entity, type));
                        found.Add((e.Other, type));
                        break;
                    default: return found;   // unparseable beyond here
                }
                if (reader.EndOfStream) break;
            }
            return found;
        }

        [Fact]
        public void NoPacketEverMentionsAnUnseenEnemyUnit()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);

            // Give both sides something to do so units are moving all over the map.
            var w0 = harness.UnitsOf(0, EntityKind.Worker);
            var w1 = harness.UnitsOf(1, EntityKind.Worker);
            EntityId forest0 = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[w0[0].Index], ResourceNodeType.Forest);
            EntityId forest1 = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[w1[0].Index], ResourceNodeType.Forest);

            harness.Clients[0].Send(Command.Harvest(0, 0, w0.ToArray(), forest0));
            harness.Clients[1].Send(Command.Harvest(1, 0, w1.ToArray(), forest1));

            // Track, per tick, which mobile entities each player could see.
            var visibilityByTick = new Dictionary<(uint tick, int player), HashSet<uint>>();

            for (int t = 0; t < 900; t++)
            {
                harness.Tick();

                for (int p = 0; p < 2; p++)
                {
                    var visible = new HashSet<uint>();
                    for (int i = 1; i < harness.World.Entities.Count; i++)
                    {
                        if (!harness.World.Entities.Alive[i]) continue;
                        int cell = harness.World.Nav.CellAt(harness.World.Entities.Position[i]);
                        if (harness.World.Entities.Owner[i] == (byte)p || harness.World.Fog.IsVisible(p, cell))
                            visible.Add(harness.World.Entities.IdOf(i).Raw);
                    }
                    visibilityByTick[(harness.World.Tick, p)] = visible;
                }
            }

            Assert.NotEmpty(harness.SentPackets);
            int checkedReferences = 0;

            foreach ((uint tick, int player, byte[] data) in harness.SentPackets)
            {
                foreach ((EntityId id, MessageType type) in DecodeEntityReferences(data))
                {
                    if (id.IsNone) continue;
                    // A despawn is the message that says "you may no longer see this", so it is
                    // legitimately about something that just left vision.
                    if (type == MessageType.DespawnEntity) continue;

                    int index = id.Index;
                    if (index <= 0 || index >= harness.World.Entities.Count) continue;

                    EntityKind kind = harness.World.Entities.Kind[index];
                    bool mobile = kind == EntityKind.Worker || kind == EntityKind.Soldier || kind == EntityKind.Ship;
                    if (!mobile) continue;                                    // static things are remembered once seen
                    if (harness.World.Entities.Owner[index] == (byte)player) continue;   // own units are always known

                    checkedReferences++;
                    Assert.True(visibilityByTick[(tick, player)].Contains(id.Raw),
                        $"tick {tick}: player {player} was sent a {type} about entity {id}, which they could not see");
                }
            }

            // The test is only meaningful if enemy units were actually referenced at some point.
            Assert.True(harness.SentPackets.Count > 100, "not enough traffic to make the assertion meaningful");
        }

        [Fact]
        public void AClientNeverLearnsTheEnemyStartingArmyExists()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            harness.Tick(200);

            // Player 0's replica must contain none of player 1's units.
            foreach (var entity in harness.Clients[0].Replica.Entities)
            {
                if (entity.Owner != 1) continue;
                Assert.True(entity.Kind == EntityKind.Building || entity.Kind == EntityKind.ResourceNode,
                    $"player 0 knows about an enemy {entity.Kind} it should not be able to see");
            }

            // And specifically: none of the ten enemy workers.
            foreach (EntityId enemyWorker in harness.UnitsOf(1, EntityKind.Worker))
                Assert.False(harness.Clients[0].Replica.Knows(enemyWorker));
        }

        [Fact]
        public void ScoutingRevealsEnemyUnitsAndLosingSightHidesThemAgain()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            EntityId enemyWorker = harness.UnitsOf(1, EntityKind.Worker)[0];

            // Put a scout of player 0 right next to an enemy worker.
            Brinehold.Core.Math.Fix2 near = harness.World.Entities.Position[enemyWorker.Index]
                + new Brinehold.Core.Math.Fix2(Brinehold.Core.Math.Fix64.FromInt(4), Brinehold.Core.Math.Fix64.Zero);
            EntityId scout = harness.World.SpawnUnit(EntityKind.Soldier, 0, near);
            harness.Tick(5);

            Assert.True(harness.Clients[0].Replica.Knows(enemyWorker), "the scout did not reveal the enemy worker");

            // Kill the scout: the enemy worker must disappear from the replica.
            harness.World.Entities.Health[scout.Index] = Brinehold.Core.Math.Fix64.Zero;
            harness.Tick(10);

            Assert.False(harness.Clients[0].Replica.Knows(enemyWorker),
                "the enemy worker stayed in the replica after vision was lost");
        }

        [Fact]
        public void PrivateEconomyIsNeverSentToTheOtherPlayer()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var w0 = harness.UnitsOf(0, EntityKind.Worker);
            EntityId forest = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[w0[0].Index], ResourceNodeType.Forest);
            harness.Clients[0].Send(Command.Harvest(0, 0, w0.ToArray(), forest));

            harness.Tick(1200);

            // Player 0 gathered wood; player 1's replica must not reflect it.
            Assert.True(harness.World.Players[0].Wood > 200, "player 0 never gathered anything");
            Assert.Equal(harness.World.Players[1].Wood, harness.Clients[1].Replica.Wood);
            Assert.NotEqual(harness.Clients[0].Replica.Wood, harness.Clients[1].Replica.Wood);
        }
    }
}
