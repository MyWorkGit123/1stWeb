using System.Collections.Generic;
using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Net.Client;
using Brinehold.Net.Transport;
using Brinehold.Protocol;
using Brinehold.Server;
using Brinehold.Sim.Map;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Integration.Tests
{
    /// <summary>
    /// A complete match: one authoritative server and N real clients, talking over a loopback
    /// network with controllable latency and loss.
    ///
    /// Everything except the socket is the production code path — the same encoders, the same
    /// replication logic, the same replica. That is what lets these tests make claims about the
    /// architecture rather than about a mock.
    /// </summary>
    public sealed class MatchHarness
    {
        public readonly LoopbackNetwork Network;
        public readonly MatchHost Host;
        public readonly List<ClientConnection> Clients = new List<ClientConnection>();

        /// <summary>Every packet the server sent, kept for wire-level assertions.</summary>
        public readonly List<(uint tick, int player, byte[] data)> SentPackets = new List<(uint, int, byte[])>();

        public MatchHarness(NetworkConditions conditions, int players = 2, ulong seed = 1)
        {
            Network = new LoopbackNetwork(conditions);
            MatchConfig config = MatchConfig.TwoPlayer(seed);
            config.PlayerCount = players;
            Host = new MatchHost(config, Network);

            // Snoop the wire without disturbing it: the callback fires as each packet is accepted
            // for delivery, so tests see exactly the bytes a client will receive.
            MatchHost host = Host;
            Network.ClientPacketSnoop = (connection, payload) =>
            {
                var copy = new byte[payload.Count];
                System.Array.Copy(payload.Array!, payload.Offset, copy, 0, payload.Count);
                SentPackets.Add((host.World.Tick, connection, copy));
            };

            for (int i = 0; i < players; i++)
            {
                bool ok = Host.TryConnect(i, $"Player {i + 1}", ProtocolVersion.Current, config.ContentHash(), out WelcomeMessage welcome);
                if (!ok) continue;

                // The client builds its own copy of the terrain. Terrain is public: what fog protects
                // is where the other player's units and buildings are, and none of that is here.
                var clientNav = new NavGrid(config.MapWidth, config.MapHeight);
                BuildClientTerrain(clientNav);

                var replica = new ReplicaWorld(clientNav, welcome.PlayerId);
                Clients.Add(new ClientConnection(Network, i, replica));
            }
        }

        /// <summary>Mirrors PrototypeMap's terrain generation, which is public map data.</summary>
        private static void BuildClientTerrain(NavGrid nav)
        {
            var scratchConfig = MatchConfig.TwoPlayer();
            var scratchWorld = new SimWorld(scratchConfig);
            PrototypeMap.Build(scratchWorld);
            for (int y = 0; y < nav.Height; y++)
            for (int x = 0; x < nav.Width; x++)
                nav.SetTerrain(x, y, scratchWorld.Nav.TerrainAt(scratchWorld.Nav.Index(x, y)));
        }

        /// <summary>Advances the whole system by one tick: server, wire, then every client.</summary>
        public void Tick()
        {
            Host.Tick();
            for (int i = 0; i < Clients.Count; i++) Clients[i].Pump();
        }

        public void Tick(int count)
        {
            for (int i = 0; i < count; i++) Tick();
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

        public EntityId CoreOf(byte player)
        {
            for (int i = 1; i < World.Entities.Count; i++)
            {
                if (!World.Entities.Alive[i]) continue;
                if (World.Entities.Owner[i] != player) continue;
                if (World.Entities.Kind[i] != EntityKind.Building) continue;
                if (World.Entities.Building[i] != BuildingType.Warehouse) continue;
                return World.Entities.IdOf(i);
            }
            return EntityId.None;
        }
    }
}
