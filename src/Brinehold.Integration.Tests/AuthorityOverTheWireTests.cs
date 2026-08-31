using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Net.Transport;
using Brinehold.Protocol;
using Brinehold.Server;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Integration.Tests
{
    /// <summary>
    /// The cheat client, run over the real wire.
    ///
    /// Each of these sends something a modified client could plausibly send and asserts the world is
    /// unchanged. Together with the fog tests they are the evidence for the claim that the cheats
    /// that matter in an RTS are impossible here rather than merely detectable.
    /// </summary>
    public class AuthorityOverTheWireTests
    {
        [Fact]
        public void AClientCannotOrderTheOtherPlayersUnits()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            EntityId enemyWorker = harness.UnitsOf(1, EntityKind.Worker)[0];
            Fix2 before = harness.World.Entities.Position[enemyWorker.Index];

            // Player 0's client sends a move order naming player 1's worker.
            harness.Clients[0].Send(Command.Move(0, 0, new[] { enemyWorker }, 60, 60));
            harness.Tick(200);

            Assert.Equal(before, harness.World.Entities.Position[enemyWorker.Index]);
        }

        [Fact]
        public void ForgingThePlayerIdOnACommandAchievesNothing()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            EntityId enemyWorker = harness.UnitsOf(1, EntityKind.Worker)[0];
            Fix2 before = harness.World.Entities.Position[enemyWorker.Index];

            // Claim to be player 1. The field is not even on the wire; the server uses the session.
            var forged = Command.Move(1, 0, new[] { enemyWorker }, 60, 60);
            harness.Clients[0].Send(forged);
            harness.Tick(200);

            Assert.Equal(before, harness.World.Entities.Position[enemyWorker.Index]);
        }

        [Fact]
        public void AClientCannotGiveItselfResources()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            harness.Tick(20);

            int serverWood = harness.World.Players[0].Wood;

            // Tamper with the local replica, exactly as a memory editor would.
            harness.Clients[0].Replica.Wood = 999999;
            harness.Tick(40);

            Assert.Equal(serverWood, harness.World.Players[0].Wood);
            // The next authoritative delta overwrites the tampered value.
            Assert.NotEqual(999999, harness.Clients[0].Replica.Wood);
        }

        [Fact]
        public void AClientCannotBuildWhatItCannotAfford()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            harness.World.Players[0].Wood = 0;
            var builders = harness.UnitsOf(0, EntityKind.Worker).Take(3).ToArray();

            harness.Clients[0].Send(Command.Build(0, 0, builders, BuildingType.House,
                PrototypeMap.StartCellX[0] + 10, PrototypeMap.StartCellY[0] - 8));
            harness.Tick(60);

            Assert.Equal(0, harness.World.Players[0].Wood);
            bool anyHouse = false;
            for (int i = 1; i < harness.World.Entities.Count; i++)
                if (harness.World.Entities.Alive[i] && harness.World.Entities.Building[i] == BuildingType.House)
                    anyHouse = true;
            Assert.False(anyHouse);
        }

        [Fact]
        public void AClientCannotTamperWithItsOwnUnitPositions()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            EntityId worker = harness.UnitsOf(0, EntityKind.Worker)[0];
            harness.Tick(20);

            Fix2 serverPosition = harness.World.Entities.Position[worker.Index];

            // Teleport the local replica across the map.
            harness.Clients[0].Replica.TryGet(worker, out var view);
            var state = view.State.Value;
            state.Position = Fix2.FromInt(120, 120);
            view.State.Value = state;

            // Make the unit move so the server sends a correction.
            harness.Clients[0].Send(Command.Move(0, 0, new[] { worker },
                PrototypeMap.StartCellX[0] + 6, PrototypeMap.StartCellY[0] + 6));
            harness.Tick(120);

            // The server never moved it to the forged position.
            Assert.True(Fix2.Distance(harness.World.Entities.Position[worker.Index], Fix2.FromInt(120, 120)).ToDouble() > 50);
            // And the replica has been dragged back to the truth.
            double error = harness.Clients[0].Replica.PositionErrorAgainst(worker, harness.World.Entities.Position[worker.Index]).ToDouble();
            Assert.True(error < 2.0, $"the replica was still {error:0.0} m from the truth after correction");
        }

        [Fact]
        public void CommandFloodingIsRateLimited()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var worker = harness.UnitsOf(0, EntityKind.Worker)[0];

            // Five hundred orders in a single tick, far above the 40-per-second ceiling.
            for (int i = 0; i < 500; i++)
                harness.Clients[0].Send(Command.Move(0, 0, new[] { worker }, 40 + (i % 10), 50));

            harness.Tick(5);

            Assert.True(harness.Host.DroppedForRateLimit(0) > 400,
                $"only {harness.Host.DroppedForRateLimit(0)} of 500 flooded commands were dropped");
            Assert.False(harness.World.MatchOver);
        }

        [Fact]
        public void ReplayedSequenceNumbersAreIgnored()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var worker = harness.UnitsOf(0, EntityKind.Worker)[0];

            var command = Command.Move(0, 0, new[] { worker }, 45, 50);
            harness.Clients[0].Send(command);          // sequence 1
            harness.Tick(2);

            // Re-send the identical packet, stale sequence and all.
            harness.Clients[0].SendRaw(command);
            harness.Clients[0].SendRaw(command);
            harness.Tick(5);

            Assert.True(harness.Host.DroppedForReplay(0) >= 2,
                $"replayed commands were not refused (dropped {harness.Host.DroppedForReplay(0)})");
        }

        [Fact]
        public void AProtocolMismatchIsRefusedAtHandshake()
        {
            var network = new LoopbackNetwork(NetworkConditions.Perfect);
            MatchConfig config = MatchConfig.TwoPlayer();
            var host = new MatchHost(config, network);

            bool ok = host.TryConnect(0, "Old Build", 9999, config.ContentHash(), out WelcomeMessage welcome);

            Assert.False(ok);
            Assert.Equal(HandshakeResult.ProtocolMismatch, welcome.Result);
            Assert.Equal(0, host.PlayerCount);
        }

        [Fact]
        public void AContentMismatchIsRefusedAtHandshake()
        {
            var network = new LoopbackNetwork(NetworkConditions.Perfect);
            MatchConfig config = MatchConfig.TwoPlayer();
            var host = new MatchHost(config, network);

            bool ok = host.TryConnect(0, "Edited Content", ProtocolVersion.Current, 0xBADBADBAD, out WelcomeMessage welcome);

            Assert.False(ok);
            Assert.Equal(HandshakeResult.ContentMismatch, welcome.Result);
            Assert.Equal(0, host.PlayerCount);
        }

        [Fact]
        public void AFullMatchIsRefusedFurtherPlayers()
        {
            var network = new LoopbackNetwork(NetworkConditions.Perfect);
            MatchConfig config = MatchConfig.TwoPlayer();
            var host = new MatchHost(config, network);

            host.TryConnect(0, "A", ProtocolVersion.Current, config.ContentHash(), out _);
            host.TryConnect(1, "B", ProtocolVersion.Current, config.ContentHash(), out _);
            bool third = host.TryConnect(2, "C", ProtocolVersion.Current, config.ContentHash(), out WelcomeMessage welcome);

            Assert.False(third);
            Assert.Equal(HandshakeResult.MatchFull, welcome.Result);
        }

        [Fact]
        public void MalformedPacketsDoNotCrashTheServer()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var rng = new System.Random(4242);

            for (int i = 0; i < 200; i++)
            {
                var junk = new byte[rng.Next(1, 200)];
                rng.NextBytes(junk);
                harness.Network.SendToServer(0, new System.ArraySegment<byte>(junk), Channel.ReliableOrdered);
            }

            harness.Tick(30);

            Assert.False(harness.World.MatchOver);
            Assert.Equal(10, harness.UnitsOf(0, EntityKind.Worker).Count);
            Assert.Equal(10, harness.UnitsOf(1, EntityKind.Worker).Count);
        }
    }

    /// <summary>End-to-end behaviour under adverse network conditions and to a real conclusion.</summary>
    public class MatchLifecycleTests
    {
        [Fact]
        public void AMatchRunsCorrectlyUnder200MillisecondsOfLatencyAndFivePercentLoss()
        {
            var harness = new MatchHarness(NetworkConditions.Poor);
            var workers = harness.UnitsOf(0, EntityKind.Worker);
            EntityId forest = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[workers[0].Index], ResourceNodeType.Forest);

            harness.Clients[0].Send(Command.Harvest(0, 0, workers.ToArray(), forest));
            harness.Tick(2400);

            // The economy still ran.
            Assert.True(harness.World.Players[0].Wood > 200, "no wood was gathered under poor network conditions");
            // The client's own economy view is still exact, because it travels on the reliable channel.
            Assert.Equal(harness.World.Players[0].Wood, harness.Clients[0].Replica.Wood);
            // And its unit positions are still close to the truth.
            double worst = 0;
            foreach (EntityId worker in workers)
            {
                if (!harness.World.Entities.IsAlive(worker)) continue;
                double error = harness.Clients[0].Replica
                    .PositionErrorAgainst(worker, harness.World.Entities.Position[worker.Index]).ToDouble();
                if (error > worst) worst = error;
            }
            Assert.True(worst < 4.0, $"worst replica error under poor conditions was {worst:0.0} m");
        }

        [Fact]
        public void DestroyingTheEnemyCoreEndsTheMatchForBothClients()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            EntityId enemyCore = harness.CoreOf(1);

            // A raiding party big enough to finish the job.
            Fix2 near = harness.World.Entities.Position[enemyCore.Index] + new Fix2(Fix64.FromInt(4), Fix64.Zero);
            var raiders = new EntityId[8];
            for (int i = 0; i < raiders.Length; i++)
                raiders[i] = harness.World.SpawnUnit(EntityKind.Soldier, 0, near + new Fix2(Fix64.FromInt(i), Fix64.Zero));

            harness.Tick(2);
            harness.Clients[0].Send(Command.Attack(0, 0, raiders, enemyCore));

            for (int t = 0; t < 4000 && !harness.World.MatchOver; t++) harness.Tick();

            Assert.True(harness.World.MatchOver, "the match never ended");
            Assert.True(harness.World.Players[1].Defeated);

            // Both clients were told, and told the right thing.
            harness.Tick(5);
            Assert.True(harness.Clients[0].Replica.MatchOver);
            Assert.True(harness.Clients[1].Replica.MatchOver);
            Assert.True(harness.Clients[0].Replica.LocalPlayerWon);
            Assert.False(harness.Clients[1].Replica.LocalPlayerWon);
        }

        [Fact]
        public void RejectionsAreReportedBackToTheIssuingClientOnly()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            var builders = harness.UnitsOf(0, EntityKind.Worker).Take(2).ToArray();

            // Build in the sea: illegal.
            harness.Clients[0].Send(Command.Build(0, 0, builders, BuildingType.House, 40, 5));
            harness.Tick(10);

            Assert.NotEmpty(harness.Clients[0].Replica.Rejections);
            Assert.Empty(harness.Clients[1].Replica.Rejections);
            Assert.Equal(RejectReason.IllegalPlacement, harness.Clients[0].Replica.Rejections[0].Reason);
        }
    }
}
