using System.Linq;
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
    /// Losing your connection should cost you the time you were away, not the match.
    ///
    /// These tests drop a client mid-match over real sockets and bring it back. The match keeps
    /// running while it is gone — a disconnect must never work as a free pause — and on return the
    /// client is given its whole visible world again, because the server forgets what it believed
    /// the client knew rather than trying to work out what it missed.
    /// </summary>
    public class ReconnectionTests
    {
        private sealed class RejoinableClient
        {
            public UdpClientTransport Transport = null!;
            public ClientConnection Connection = null!;
            public ReplicaWorld Replica = null!;
        }

        private static RejoinableClient Join(UdpServerTransport server, int port, MatchConfig config,
                                             string name, ulong token, byte expectedSlot)
        {
            var transport = new UdpClientTransport(new IPEndPoint(IPAddress.Loopback, port));
            Assert.True(UdpMatchHarness.Handshake(server, transport, 3000), $"{name} could not reach the server");

            var nav = new NavGrid(config.MapWidth, config.MapHeight);
            var terrainSource = new SimWorld(MatchConfig.TwoPlayer());
            PrototypeMap.Build(terrainSource);
            for (int y = 0; y < nav.Height; y++)
            for (int x = 0; x < nav.Width; x++)
                nav.SetTerrain(x, y, terrainSource.Nav.TerrainAt(terrainSource.Nav.Index(x, y)));

            var replica = new ReplicaWorld(nav, expectedSlot);
            var connection = new ClientConnection(transport, replica);
            connection.SendHello(config.ContentHash(), name, token);

            return new RejoinableClient { Transport = transport, Connection = connection, Replica = replica };
        }

        private static void Pump(MatchHost host, int ticks, params RejoinableClient?[] clients)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Tick();
                foreach (RejoinableClient? client in clients) client?.Connection.Pump();
                System.Threading.Thread.Sleep(1);
            }
        }

        [Fact]
        public void ADroppedClientKeepsItsSlotAndCanRejoinWithItsToken()
        {
            MatchConfig config = MatchConfig.TwoPlayer();
            using var server = new UdpServerTransport(0, IPAddress.Loopback);
            var host = new MatchHost(config, server);
            int port = server.LocalEndPoint.Port;

            RejoinableClient? a = Join(server, port, config, "PlayerA", 0, 0);
            RejoinableClient? b = Join(server, port, config, "PlayerB", 0, 1);
            Pump(host, 80, a, b);

            Assert.True(a.Replica.Welcomed);
            Assert.True(b.Replica.Welcomed);
            ulong token = a.Replica.ReconnectToken;
            Assert.NotEqual(0UL, token);

            // Set the economy going, so there is something to come back to.
            var workers = host.World.Entities.Count > 0
                ? UnitsOf(host.World, 0, EntityKind.Worker)
                : new System.Collections.Generic.List<EntityId>();
            EntityId forest = PrototypeMap.FindNearestNode(
                host.World, host.World.Entities.Position[workers[0].Index], ResourceNodeType.Forest);
            a.Connection.Send(Command.Harvest(0, 0, workers.ToArray(), forest));
            Pump(host, 400, a, b);

            int woodBeforeDrop = host.World.Players[0].Wood;

            // Pull the plug.
            a.Transport.Dispose();
            a = null;
            Pump(host, 120, null, b);

            Assert.Single(host.AwaitingReconnect);
            Assert.Equal(0, host.AwaitingReconnect[0]);
            Assert.Equal(1, host.PlayerCount);            // one live session
            Assert.Equal(2, host.ClaimedSlots);           // but both slots still claimed
            Assert.Empty(host.Resigned);

            // The world kept running while the player was away: a disconnect is not a pause.
            Pump(host, 600, null, b);
            Assert.True(host.World.Players[0].Wood >= woodBeforeDrop,
                "the absent player's economy stopped, so the disconnect acted as a pause");

            // Come back with the token.
            RejoinableClient rejoined = Join(server, port, config, "PlayerA", token, 0);
            Pump(host, 120, rejoined, b);

            Assert.True(rejoined.Replica.Welcomed, "the rejoining client was refused");
            Assert.True(rejoined.Replica.Reconnected, "the server treated the rejoin as a fresh join");
            Assert.Equal(0, rejoined.Replica.LocalPlayer);
            Assert.Equal(2, host.PlayerCount);
            Assert.Empty(host.AwaitingReconnect);

            rejoined.Transport.Dispose();
            b.Transport.Dispose();
        }

        [Fact]
        public void ARejoiningClientIsGivenItsWholeVisibleWorldAgain()
        {
            MatchConfig config = MatchConfig.TwoPlayer();
            using var server = new UdpServerTransport(0, IPAddress.Loopback);
            var host = new MatchHost(config, server);
            int port = server.LocalEndPoint.Port;

            RejoinableClient? a = Join(server, port, config, "PlayerA", 0, 0);
            RejoinableClient? b = Join(server, port, config, "PlayerB", 0, 1);
            Pump(host, 100, a, b);

            int knownBefore = a.Replica.EntityCount;
            int woodBefore = a.Replica.Wood;
            Assert.True(knownBefore > 20, $"the client only knew about {knownBefore} entities to start with");

            ulong token = a.Replica.ReconnectToken;
            a.Transport.Dispose();
            a = null;
            Pump(host, 200, null, b);

            RejoinableClient rejoined = Join(server, port, config, "PlayerA", token, 0);
            Pump(host, 150, rejoined, b);

            Assert.Equal(knownBefore, rejoined.Replica.EntityCount);
            Assert.Equal(woodBefore, rejoined.Replica.Wood);
            Assert.Equal(host.World.Players[0].Wood, rejoined.Replica.Wood);

            // And it is a working client again, not a spectator.
            var workers = UnitsOf(host.World, 0, EntityKind.Worker);
            Brinehold.Core.Math.Fix2 before = host.World.Entities.Position[workers[0].Index];
            rejoined.Connection.Send(Command.Move(0, 0, new[] { workers[0] },
                PrototypeMap.StartCellX[0] + 16, PrototypeMap.StartCellY[0] + 16));
            Pump(host, 400, rejoined, b);

            double moved = Brinehold.Core.Math.Fix2
                .Distance(before, host.World.Entities.Position[workers[0].Index]).ToDouble();
            Assert.True(moved > 5, $"the rejoined client's orders are being ignored (moved {moved:0.0} m)");

            rejoined.Transport.Dispose();
            b.Transport.Dispose();
        }

        [Fact]
        public void AStrangerCannotClaimSomeoneElsesSlot()
        {
            MatchConfig config = MatchConfig.TwoPlayer();
            using var server = new UdpServerTransport(0, IPAddress.Loopback);
            var host = new MatchHost(config, server);
            int port = server.LocalEndPoint.Port;

            RejoinableClient? a = Join(server, port, config, "PlayerA", 0, 0);
            RejoinableClient? b = Join(server, port, config, "PlayerB", 0, 1);
            Pump(host, 80, a, b);

            a.Transport.Dispose();
            a = null;
            Pump(host, 100, null, b);

            // Present a token that was never issued.
            RejoinableClient impostor = Join(server, port, config, "Impostor", 0xBADC0FFEE0DDF00D, 0);
            Pump(host, 100, impostor, b);

            Assert.False(impostor.Replica.Welcomed, "an unknown token was accepted");
            Assert.Equal(HandshakeResult.UnknownReconnectToken, impostor.Replica.Handshake);
            Assert.Single(host.AwaitingReconnect);   // the real player's slot is still reserved

            impostor.Transport.Dispose();
            b.Transport.Dispose();
        }

        [Fact]
        public void AFreshJoinIsRefusedWhileAllSlotsAreStillClaimed()
        {
            MatchConfig config = MatchConfig.TwoPlayer();
            using var server = new UdpServerTransport(0, IPAddress.Loopback);
            var host = new MatchHost(config, server);
            int port = server.LocalEndPoint.Port;

            RejoinableClient? a = Join(server, port, config, "PlayerA", 0, 0);
            RejoinableClient? b = Join(server, port, config, "PlayerB", 0, 1);
            Pump(host, 80, a, b);

            a.Transport.Dispose();
            a = null;
            Pump(host, 100, null, b);

            // Someone else tries to take the empty seat without a token.
            RejoinableClient newcomer = Join(server, port, config, "Newcomer", 0, 0);
            Pump(host, 100, newcomer, b);

            Assert.False(newcomer.Replica.Welcomed);
            Assert.Equal(HandshakeResult.MatchFull, newcomer.Replica.Handshake);

            newcomer.Transport.Dispose();
            b.Transport.Dispose();
        }

        [Fact]
        public void AnExpiredGraceWindowResignsThePlayer()
        {
            MatchConfig config = MatchConfig.TwoPlayer();
            using var server = new UdpServerTransport(0, IPAddress.Loopback);
            var host = new MatchHost(config, server) { DisconnectGraceTicks = 40 };   // two seconds
            int port = server.LocalEndPoint.Port;

            RejoinableClient? a = Join(server, port, config, "PlayerA", 0, 0);
            RejoinableClient? b = Join(server, port, config, "PlayerB", 0, 1);
            Pump(host, 80, a, b);

            ulong token = a.Replica.ReconnectToken;
            a.Transport.Dispose();
            a = null;

            Pump(host, 120, null, b);

            Assert.Contains<byte>(0, host.Resigned);
            Assert.Empty(host.AwaitingReconnect);
            Assert.True(host.World.Players[0].Defeated, "the resigned player was not marked defeated");

            // The token is now worthless: the slot is gone.
            RejoinableClient tooLate = Join(server, port, config, "PlayerA", token, 0);
            Pump(host, 80, tooLate, b);
            Assert.False(tooLate.Replica.Welcomed);

            tooLate.Transport.Dispose();
            b.Transport.Dispose();
        }

        private static System.Collections.Generic.List<EntityId> UnitsOf(SimWorld world, byte player, EntityKind kind)
        {
            var result = new System.Collections.Generic.List<EntityId>();
            for (int i = 1; i < world.Entities.Count; i++)
            {
                if (!world.Entities.Alive[i]) continue;
                if (world.Entities.Owner[i] != player) continue;
                if (world.Entities.Kind[i] != kind) continue;
                result.Add(world.Entities.IdOf(i));
            }
            return result;
        }
    }
}
