using Brinehold.Client.CameraControl;
using Brinehold.Client.Hud;
using Brinehold.Client.Input;
using Brinehold.Client.Placement;
using Brinehold.Client.Selection;
using Brinehold.Net.Client;
using Brinehold.Net.Transport;
using Brinehold.Protocol;
using Brinehold.Server;
using Brinehold.Sim.Map;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Client.Tests
{
    /// <summary>
    /// A single client wired to a real authoritative server.
    ///
    /// The client logic is tested against the actual replication stream rather than a mock world,
    /// so a test that selects a worker is selecting something the server really told this client
    /// about, through the real encoder.
    /// </summary>
    public sealed class ClientHarness
    {
        public readonly MatchHost Host;
        public readonly LoopbackNetwork Network;
        public readonly ClientConnection Connection;
        public readonly ReplicaWorld Replica;
        public readonly SelectionModel Selection;
        public readonly ControlGroups Groups;
        public readonly OrderIssuer Orders;
        public readonly HudModel Hud;
        public readonly CameraRig Camera;
        public readonly PlacementPreview Placement;
        public readonly NavGrid ClientNav;

        public ClientHarness(byte localPlayer = 0)
        {
            Network = new LoopbackNetwork(NetworkConditions.Perfect);
            MatchConfig config = MatchConfig.TwoPlayer();
            Host = new MatchHost(config, Network);

            for (int i = 0; i < config.PlayerCount; i++)
                Host.TryConnect(i, $"Player {i + 1}", ProtocolVersion.Current, config.ContentHash(), out _);

            ClientNav = new NavGrid(config.MapWidth, config.MapHeight);
            var terrainSource = new SimWorld(MatchConfig.TwoPlayer());
            PrototypeMap.Build(terrainSource);
            for (int y = 0; y < ClientNav.Height; y++)
            for (int x = 0; x < ClientNav.Width; x++)
                ClientNav.SetTerrain(x, y, terrainSource.Nav.TerrainAt(terrainSource.Nav.Index(x, y)));

            Replica = new ReplicaWorld(ClientNav, localPlayer);
            Connection = new ClientConnection(Network, localPlayer, Replica);

            Selection = new SelectionModel(Replica);
            Groups = new ControlGroups(Replica);
            Orders = new OrderIssuer(Replica, Selection);
            Hud = new HudModel(Replica);
            Camera = new CameraRig(config.MapWidth, config.MapHeight);
            Placement = new PlacementPreview(ClientNav);
        }

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Host.Tick();
                Connection.Pump();
                Selection.Prune();
                Groups.Prune();
            }
        }

        public SimWorld World => Host.World;
    }
}
