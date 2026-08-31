using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Net;
using Brinehold.Net.Transport;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Integration.Tests
{
    /// <summary>
    /// The bandwidth and accuracy claims of intent replication, measured rather than asserted.
    /// </summary>
    public class ReplicationTests
    {
        [Fact]
        public void MovementCostsIntentsNotAStreamOfTransforms()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var workers = harness.UnitsOf(0, EntityKind.Worker);

            // Send ten workers on a long walk across the map.
            harness.Clients[0].Send(Command.Move(0, 0, workers.ToArray(),
                PrototypeMap.StartCellX[0] + 40, PrototypeMap.StartCellY[0] + 30));

            harness.Tick(600);

            int intents = harness.Host.Replication.Stats.MessageCount(0, NetStats.Category.Intent);
            int corrections = harness.Host.Replication.Stats.MessageCount(0, NetStats.Category.Correction);

            // A naive implementation would send ten transforms per tick: 6,000 updates.
            const int naiveTransformUpdates = 10 * 600;
            int actual = intents + corrections;

            Assert.True(actual < naiveTransformUpdates / 10,
                $"{actual} position-bearing messages for a walk that would cost {naiveTransformUpdates} " +
                "transform updates — intent replication is not doing its job");
        }

        /// <summary>
        /// Regression guard for a real bug: several systems changed an entity's job without
        /// emitting an intent, so clients kept walking workers that the server had already stopped
        /// at a tree. It cost 13,271 corrections and 330 B/s in a ten-minute two-player match.
        /// Routing every transition through SetJobIfChanged took that to zero corrections and
        /// 35 B/s. If this number climbs again, an intent is being dropped somewhere.
        /// </summary>
        [Fact]
        public void AFullHarvestCycleNeedsAlmostNoCorrections()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var workers = harness.UnitsOf(0, EntityKind.Worker);
            EntityId forest = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[workers[0].Index], ResourceNodeType.Forest);

            harness.Clients[0].Send(Command.Harvest(0, 0, workers.ToArray(), forest));
            harness.Tick(3000);   // two and a half minutes of walking, harvesting and delivering

            int corrections = harness.Host.Replication.Stats.MessageCount(0, NetStats.Category.Correction);
            int intents = harness.Host.Replication.Stats.MessageCount(0, NetStats.Category.Intent);

            Assert.True(intents > 20, "the workers never changed behaviour, so the test proves nothing");
            Assert.True(corrections < intents,
                $"{corrections} corrections against {intents} intents — the client's extrapolation " +
                "is diverging from the server, which means a job transition is not emitting an intent");
        }

        [Fact]
        public void TheClientReplicaTracksServerTruthClosely()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var workers = harness.UnitsOf(0, EntityKind.Worker);
            harness.Clients[0].Send(Command.Move(0, 0, workers.ToArray(),
                PrototypeMap.StartCellX[0] + 25, PrototypeMap.StartCellY[0] + 20));

            harness.Tick(500);

            double worst = 0;
            foreach (EntityId worker in workers)
            {
                if (!harness.World.Entities.IsAlive(worker)) continue;
                Fix2 truth = harness.World.Entities.Position[worker.Index];
                double error = harness.Clients[0].Replica.PositionErrorAgainst(worker, truth).ToDouble();
                if (error > worst) worst = error;
            }

            Assert.True(worst < 2.0, $"worst replica position error was {worst:0.00} m");
        }

        [Fact]
        public void ABusyMatchStaysInsideTheBandwidthBudget()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);

            var w0 = harness.UnitsOf(0, EntityKind.Worker);
            var w1 = harness.UnitsOf(1, EntityKind.Worker);
            EntityId forest0 = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[w0[0].Index], ResourceNodeType.Forest);
            EntityId forest1 = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[w1[0].Index], ResourceNodeType.Forest);

            harness.Clients[0].Send(Command.Harvest(0, 0, w0.Take(6).ToArray(), forest0));
            harness.Clients[0].Send(Command.Build(0, 0, w0.Skip(6).ToArray(), BuildingType.House,
                PrototypeMap.StartCellX[0] + 10, PrototypeMap.StartCellY[0] - 8));
            harness.Clients[1].Send(Command.Harvest(1, 0, w1.ToArray(), forest1));

            harness.Tick(2400);   // two minutes of match time

            for (int p = 0; p < 2; p++)
            {
                double bytesPerSecond = harness.Host.Replication.Stats.BytesPerSecond(p, harness.World.Tick);
                Assert.True(bytesPerSecond < 8000,
                    $"player {p} used {bytesPerSecond:0} B/s, above the 8 KB/s prototype budget. " +
                    harness.Host.Replication.Stats.Summary(p, harness.World.Tick));
            }
        }

        [Fact]
        public void IdleEntitiesCostNothing()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            harness.Tick(60);   // let the initial spawns flush
            long baseline = harness.Host.Replication.Stats.TotalBytes(0);

            harness.Tick(600);  // thirty seconds in which nobody does anything
            long spent = harness.Host.Replication.Stats.TotalBytes(0) - baseline;

            // Only two things should be crossing the wire: the once-a-second keepalive, and the
            // once-a-second economy refresh that keeps a client's HUD self-healing after a lost or
            // tampered message. Together that is about 25 B/s against a 25 KB/s budget.
            Assert.True(spent < 1200, $"an idle match spent {spent} bytes over 30 seconds");
            Assert.True(spent / 30.0 < 40, $"idle traffic was {spent / 30.0:0} B/s");
        }

        [Fact]
        public void BothClientsAgreeAboutAnEntityTheyCanBothSee()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);

            // A neutral spot both players can see: give each a scout standing beside it.
            var meetingPoint = Fix2.FromInt(80, 78);
            EntityId scout0 = harness.World.SpawnUnit(EntityKind.Soldier, 0, meetingPoint + new Fix2(Fix64.FromInt(2), Fix64.Zero));
            EntityId scout1 = harness.World.SpawnUnit(EntityKind.Soldier, 1, meetingPoint - new Fix2(Fix64.FromInt(2), Fix64.Zero));
            harness.Tick(20);

            Assert.True(harness.Clients[0].Replica.Knows(scout1), "player 0 cannot see the enemy scout beside it");
            Assert.True(harness.Clients[1].Replica.Knows(scout0), "player 1 cannot see the enemy scout beside it");

            harness.Clients[0].Replica.TryGet(scout1, out var view0);
            harness.Clients[1].Replica.TryGet(scout1, out var view1);
            double disagreement = Fix2.Distance(view0.State.Value.Position, view1.State.Value.Position).ToDouble();
            Assert.True(disagreement < 1.0, $"the two clients disagree about the same unit by {disagreement:0.00} m");
        }

        [Fact]
        public void AClientSeesItsOwnEconomyExactly()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var workers = harness.UnitsOf(0, EntityKind.Worker);
            EntityId forest = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[workers[0].Index], ResourceNodeType.Forest);
            harness.Clients[0].Send(Command.Harvest(0, 0, workers.ToArray(), forest));

            harness.Tick(1500);

            Assert.Equal(harness.World.Players[0].Wood, harness.Clients[0].Replica.Wood);
            Assert.Equal(harness.World.Players[0].Food, harness.Clients[0].Replica.Food);
            Assert.Equal(harness.World.Players[0].PopulationUsed, harness.Clients[0].Replica.PopulationUsed);
            Assert.Equal(harness.World.Players[0].PopulationCap, harness.Clients[0].Replica.PopulationCap);
        }
    }
}
