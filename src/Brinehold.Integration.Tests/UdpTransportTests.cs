using System;
using System.Collections.Generic;
using System.Net;
using Brinehold.Core.Collections;
using Brinehold.Net.Client;
using Brinehold.Net.Transport;
using Brinehold.Protocol;
using Brinehold.Server;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Integration.Tests
{
    /// <summary>
    /// A full match over real UDP sockets.
    ///
    /// Everything else in this suite runs the replication stack in-process. These tests put actual
    /// datagrams on the loopback interface, through the real reliability layer, so the claim that
    /// the game is playable across a network is backed by packets rather than by an interface.
    /// </summary>
    public sealed class UdpMatchHarness : IDisposable
    {
        public readonly UdpServerTransport ServerTransport;
        public readonly MatchHost Host;
        public readonly List<UdpClientTransport> ClientTransports = new List<UdpClientTransport>();
        public readonly List<ClientConnection> Clients = new List<ClientConnection>();
        public readonly MatchConfig Config;

        public UdpMatchHarness(int players = 2, int simulatedLossPercent = 0)
        {
            Config = MatchConfig.TwoPlayer();
            Config.PlayerCount = players;

            // Port 0 asks the operating system for a free port, so tests never collide.
            ServerTransport = new UdpServerTransport(0, IPAddress.Loopback)
            {
                SimulatedLossPercent = simulatedLossPercent
            };
            Host = new MatchHost(Config, ServerTransport);

            int port = ServerTransport.LocalEndPoint.Port;

            for (int i = 0; i < players; i++)
            {
                var transport = new UdpClientTransport(new IPEndPoint(IPAddress.Loopback, port));
                // Both ends have to be pumped: the server only answers a connection request while
                // its own socket is being drained.
                Assert.True(Handshake(ServerTransport, transport, 3000), $"client {i} could not reach the server");
                ClientTransports.Add(transport);

                var nav = new NavGrid(Config.MapWidth, Config.MapHeight);
                var terrainSource = new SimWorld(MatchConfig.TwoPlayer());
                PrototypeMap.Build(terrainSource);
                for (int y = 0; y < nav.Height; y++)
                for (int x = 0; x < nav.Width; x++)
                    nav.SetTerrain(x, y, terrainSource.Nav.TerrainAt(terrainSource.Nav.Index(x, y)));

                var replica = new ReplicaWorld(nav, (byte)i);
                var connection = new ClientConnection(transport, replica);
                connection.SendHello(Config.ContentHash(), $"Player {i + 1}");
                Clients.Add(connection);
            }
        }

        /// <summary>
        /// Drives both ends until the transport-level handshake completes. A real client talks to a
        /// server process that is polling itself, so it can just call WaitForConnection; in-process
        /// both sides need turning.
        /// </summary>
        public static bool Handshake(IServerTransport server, UdpClientTransport client, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                server.Poll();
                client.Poll();
                if (client.IsConnected) return true;
                System.Threading.Thread.Sleep(2);
            }
            return client.IsConnected;
        }

        /// <summary>
        /// Advances the match. Real sockets need a moment to move bytes, so each tick yields the
        /// thread briefly rather than spinning; this is a test harness, not the game loop.
        /// </summary>
        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Host.Tick();
                for (int c = 0; c < Clients.Count; c++) Clients[c].Pump();
                System.Threading.Thread.Sleep(1);
            }
        }

        public SimWorld World => Host.World;

        public List<EntityId> UnitsOf(byte player, EntityKind kind)
        {
            var result = new List<EntityId>();
            for (int i = 1; i < World.Entities.Count; i++)
            {
                if (!World.Entities.Alive[i]) continue;
                if (World.Entities.Owner[i] != player) continue;
                if (World.Entities.Kind[i] != kind) continue;
                result.Add(World.Entities.IdOf(i));
            }
            return result;
        }

        public void Dispose()
        {
            foreach (UdpClientTransport transport in ClientTransports) transport.Dispose();
            ServerTransport.Dispose();
        }
    }

    public class UdpTransportTests
    {
        [Fact]
        public void TwoClientsConnectOverRealSocketsAndAreGivenDistinctSlots()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(60);

            Assert.Equal(2, harness.Host.PlayerCount);
            Assert.True(harness.Clients[0].Replica.Welcomed, "client 0 was never welcomed");
            Assert.True(harness.Clients[1].Replica.Welcomed, "client 1 was never welcomed");
            Assert.NotEqual(harness.Clients[0].Replica.LocalPlayer, harness.Clients[1].Replica.LocalPlayer);
        }

        [Fact]
        public void EachClientReceivesItsOwnStartingSettlementOverTheWire()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(80);

            for (int c = 0; c < 2; c++)
            {
                int ownWorkers = 0;
                foreach (ReplicaWorld.Entity entity in harness.Clients[c].Replica.Entities)
                    if (entity.Owner == harness.Clients[c].Replica.LocalPlayer && entity.Kind == EntityKind.Worker)
                        ownWorkers++;

                Assert.Equal(10, ownWorkers);
            }
        }

        [Fact]
        public void FogIsStillEnforcedOverRealSockets()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(80);

            foreach (EntityId enemyWorker in harness.UnitsOf(1, EntityKind.Worker))
                Assert.False(harness.Clients[0].Replica.Knows(enemyWorker),
                    "an enemy worker reached client 0 across the network");
        }

        [Fact]
        public void AnOrderSentOverUdpIsExecutedByTheServer()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(60);

            EntityId worker = harness.UnitsOf(0, EntityKind.Worker)[0];
            Brinehold.Core.Math.Fix2 before = harness.World.Entities.Position[worker.Index];

            harness.Clients[0].Send(Command.Move(0, 0, new[] { worker },
                PrototypeMap.StartCellX[0] + 18, PrototypeMap.StartCellY[0] + 18));

            harness.Tick(400);

            double moved = Brinehold.Core.Math.Fix2
                .Distance(before, harness.World.Entities.Position[worker.Index]).ToDouble();
            Assert.True(moved > 8, $"the worker moved only {moved:0.0} m; the order did not cross the wire");
        }

        [Fact]
        public void TheEconomyRunsAndTheClientsHudMatchesTheServer()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(60);

            var workers = harness.UnitsOf(0, EntityKind.Worker);
            EntityId forest = PrototypeMap.FindNearestNode(
                harness.World, harness.World.Entities.Position[workers[0].Index], ResourceNodeType.Forest);

            harness.Clients[0].Send(Command.Harvest(0, 0, workers.ToArray(), forest));
            harness.Tick(1400);

            Assert.True(harness.World.Players[0].Wood > 200, "no wood was gathered over the network");
            Assert.Equal(harness.World.Players[0].Wood, harness.Clients[0].Replica.Wood);
        }

        [Fact]
        public void TheReplicaTracksServerPositionsAcrossTheNetwork()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(60);

            var workers = harness.UnitsOf(0, EntityKind.Worker);
            harness.Clients[0].Send(Command.Move(0, 0, workers.ToArray(),
                PrototypeMap.StartCellX[0] + 20, PrototypeMap.StartCellY[0] + 16));
            harness.Tick(500);

            double worst = 0;
            foreach (EntityId worker in workers)
            {
                if (!harness.World.Entities.IsAlive(worker)) continue;
                double error = harness.Clients[0].Replica
                    .PositionErrorAgainst(worker, harness.World.Entities.Position[worker.Index]).ToDouble();
                if (error > worst) worst = error;
            }

            Assert.True(worst < 3.0, $"worst replica error over UDP was {worst:0.00} m");
        }

        [Fact]
        public void ReliableTrafficSurvivesTwentyPercentPacketLoss()
        {
            using var harness = new UdpMatchHarness(simulatedLossPercent: 20);
            harness.Tick(120);

            // The handshake and the starting settlement are reliable traffic: they must arrive
            // despite a fifth of datagrams being thrown away.
            Assert.True(harness.Clients[0].Replica.Welcomed);

            int ownWorkers = 0;
            foreach (ReplicaWorld.Entity entity in harness.Clients[0].Replica.Entities)
                if (entity.Owner == 0 && entity.Kind == EntityKind.Worker) ownWorkers++;

            Assert.Equal(10, ownWorkers);
            Assert.True(harness.ServerTransport.Retransmissions > 0,
                "no retransmissions occurred, so the loss simulation was not exercised");
        }

        [Fact]
        public void AMatchPlaysToVictoryOverUdp()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(60);

            EntityId enemyCore = EntityId.None;
            for (int i = 1; i < harness.World.Entities.Count; i++)
            {
                if (!harness.World.Entities.Alive[i]) continue;
                if (harness.World.Entities.Owner[i] != 1) continue;
                if (harness.World.Entities.Building[i] != BuildingType.Warehouse) continue;
                enemyCore = harness.World.Entities.IdOf(i);
            }
            Assert.False(enemyCore.IsNone);

            Brinehold.Core.Math.Fix2 near = harness.World.Entities.Position[enemyCore.Index]
                + new Brinehold.Core.Math.Fix2(Brinehold.Core.Math.Fix64.FromInt(4), Brinehold.Core.Math.Fix64.Zero);

            var raiders = new EntityId[8];
            for (int i = 0; i < raiders.Length; i++)
                raiders[i] = harness.World.SpawnUnit(EntityKind.Soldier, 0,
                    near + new Brinehold.Core.Math.Fix2(Brinehold.Core.Math.Fix64.FromInt(i), Brinehold.Core.Math.Fix64.Zero));

            harness.Tick(5);
            harness.Clients[0].Send(Command.Attack(0, 0, raiders, enemyCore));

            for (int t = 0; t < 3000 && !harness.World.MatchOver; t++) harness.Tick();

            Assert.True(harness.World.MatchOver, "the match never ended");
            harness.Tick(20);

            Assert.True(harness.Clients[0].Replica.MatchOver, "the winner was never told");
            Assert.True(harness.Clients[1].Replica.MatchOver, "the loser was never told");
            Assert.True(harness.Clients[0].Replica.LocalPlayerWon);
            Assert.False(harness.Clients[1].Replica.LocalPlayerWon);
        }

        [Fact]
        public void AClientWithTheWrongProtocolVersionIsRefused()
        {
            using var serverTransport = new UdpServerTransport(0, IPAddress.Loopback);
            MatchConfig config = MatchConfig.TwoPlayer();
            var host = new MatchHost(config, serverTransport);

            using var transport = new UdpClientTransport(
                new IPEndPoint(IPAddress.Loopback, serverTransport.LocalEndPoint.Port));
            Assert.True(UdpMatchHarness.Handshake(serverTransport, transport, 3000));

            var replica = new ReplicaWorld(new NavGrid(config.MapWidth, config.MapHeight), 0);
            var connection = new ClientConnection(transport, replica);

            // Introduce ourselves with a version the server does not speak.
            var writer = new Brinehold.Core.Serialization.BitWriter(128);
            MessageCodec.Write(writer, new HelloMessage
            {
                ProtocolVersion = 9999,
                ContentHash = config.ContentHash(),
                PlayerName = "Old Build"
            });
            transport.Send(writer.AsSegment(), Channel.ReliableOrdered);

            for (int i = 0; i < 60; i++)
            {
                host.Tick();
                connection.Pump();
                System.Threading.Thread.Sleep(1);
            }

            Assert.Equal(HandshakeResult.ProtocolMismatch, replica.Handshake);
            Assert.False(replica.Welcomed);
            Assert.Equal(0, host.PlayerCount);
        }

        [Fact]
        public void JunkDatagramsDoNotDisturbAnActiveMatch()
        {
            using var harness = new UdpMatchHarness();
            harness.Tick(60);

            using var attacker = new System.Net.Sockets.UdpClient();
            var target = new IPEndPoint(IPAddress.Loopback, harness.ServerTransport.LocalEndPoint.Port);
            var rng = new Random(9001);

            for (int i = 0; i < 300; i++)
            {
                var junk = new byte[rng.Next(1, 400)];
                rng.NextBytes(junk);
                attacker.Send(junk, junk.Length, target);
            }

            harness.Tick(60);

            Assert.Equal(2, harness.Host.PlayerCount);
            Assert.False(harness.World.MatchOver);
            Assert.Equal(10, harness.UnitsOf(0, EntityKind.Worker).Count);
            Assert.Equal(10, harness.UnitsOf(1, EntityKind.Worker).Count);
        }
    }
}
