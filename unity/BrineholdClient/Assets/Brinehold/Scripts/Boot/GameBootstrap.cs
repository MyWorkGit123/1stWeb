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
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// Wires a match together and drives the fixed tick.
    ///
    /// In listen mode the client starts the authoritative server in-process and connects to it as an
    /// ordinary client over the loopback transport. There is deliberately no second code path: the
    /// host is a client like any other and gets no privileged access to the simulation, which is
    /// what stops "host advantage" bugs existing in the first place.
    ///
    /// The simulation advances on a fixed 50 ms accumulator, never on Update. Rendering interpolates
    /// between the last two simulation states, so frame rate and simulation rate stay independent.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Match")]
        [Tooltip("Seed for the match. The same seed always produces the same map and the same result for the same orders.")]
        public ulong Seed = 1;

        [Tooltip("Which player slot this client controls.")]
        public byte LocalPlayer;

        [Header("Scene references")]
        public Camera SceneCamera;
        public TerrainBuilder Terrain;
        public EntityViewPool Views;
        public FogRenderer Fog;

        // --- the match -------------------------------------------------------
        public MatchHost Host { get; private set; }
        public LoopbackNetwork Network { get; private set; }
        public ClientConnection Connection { get; private set; }
        public ReplicaWorld Replica { get; private set; }
        public NavGrid ClientNav { get; private set; }

        // --- client logic (all engine-independent and unit tested) ------------
        public SelectionModel Selection { get; private set; }
        public ControlGroups Groups { get; private set; }
        public OrderIssuer Orders { get; private set; }
        public HudModel Hud { get; private set; }
        public CameraRig Rig { get; private set; }
        public PlacementPreview Placement { get; private set; }

        private float _tickAccumulator;
        /// <summary>0..1 through the current simulation tick, for view interpolation.</summary>
        public float TickAlpha { get; private set; }

        private void Awake()
        {
            MatchConfig config = MatchConfig.TwoPlayer(Seed);

            Network = new LoopbackNetwork(NetworkConditions.Perfect);
            Host = new MatchHost(config, Network);

            for (int i = 0; i < config.PlayerCount; i++)
                Host.TryConnect(i, $"Player {i + 1}", ProtocolVersion.Current, config.ContentHash(), out _);

            // The client builds its own copy of the terrain. Terrain is public information; what fog
            // protects is where the other player's units and buildings are, and none of that is here.
            ClientNav = new NavGrid(config.MapWidth, config.MapHeight);
            var terrainSource = new SimWorld(MatchConfig.TwoPlayer(Seed));
            PrototypeMap.Build(terrainSource);
            for (int y = 0; y < ClientNav.Height; y++)
            for (int x = 0; x < ClientNav.Width; x++)
                ClientNav.SetTerrain(x, y, terrainSource.Nav.TerrainAt(terrainSource.Nav.Index(x, y)));

            Replica = new ReplicaWorld(ClientNav, LocalPlayer);
            Connection = new ClientConnection(Network, LocalPlayer, Replica);

            Selection = new SelectionModel(Replica);
            Groups = new ControlGroups(Replica);
            Orders = new OrderIssuer(Replica, Selection);
            Hud = new HudModel(Replica);
            Rig = new CameraRig(config.MapWidth, config.MapHeight);
            Placement = new PlacementPreview(ClientNav);

            Rig.JumpTo(new Brinehold.Core.Math.Fix2(
                Brinehold.Core.Math.Fix64.FromInt(PrototypeMap.StartCellX[LocalPlayer]),
                Brinehold.Core.Math.Fix64.FromInt(PrototypeMap.StartCellY[LocalPlayer])));

            if (Terrain != null) Terrain.Build(ClientNav);
            if (Fog != null) Fog.Initialise(config.MapWidth, config.MapHeight);
        }

        private void Update()
        {
            // Fixed simulation rate, decoupled from frame rate. A slow frame runs several ticks; a
            // fast one runs none and simply interpolates further through the current tick.
            _tickAccumulator += Time.deltaTime;
            float tickSeconds = SimConstants.MillisecondsPerTick / 1000f;

            int guard = 0;
            while (_tickAccumulator >= tickSeconds && guard++ < 5)
            {
                _tickAccumulator -= tickSeconds;
                StepOnce();
            }

            TickAlpha = Mathf.Clamp01(_tickAccumulator / tickSeconds);
        }

        /// <summary>
        /// Interpolates the views after everything else has had its Update.
        ///
        /// This deliberately does not live in the input controller: doing it there worked only
        /// because of the order the components happened to be added in, which is exactly the kind of
        /// dependency that breaks silently the first time somebody reorders a prefab.
        /// </summary>
        private void LateUpdate()
        {
            if (Views != null) Views.Interpolate(TickAlpha);
        }

        private void StepOnce()
        {
            Host.Tick();
            Connection.Pump();
            Selection.Prune();
            Groups.Prune();

            if (Views != null) Views.Synchronise(Replica);
            if (Fog != null) Fog.Refresh(Host.World, LocalPlayer);
        }

        /// <summary>Sends an order, if there is one. Called by the input controllers.</summary>
        public void Issue(Brinehold.Sim.Commands.Command order)
        {
            if (order == null) return;
            Connection.Send(order);
        }
    }
}
