using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Brinehold.Client.Hud;
using Brinehold.Client.Selection;
using Brinehold.Core.Collections;
using Brinehold.Net.Client;
using Brinehold.Net.Transport;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Tools.TestClient
{
    /// <summary>
    /// A headless client that connects over the network and plays a scripted opening.
    ///
    /// It exists to prove the whole stack end to end without Unity: real UDP sockets, the real
    /// handshake, the real replication stream, the real client-side replica and the real order
    /// path. Point two of these at a running server and the match plays out between separate
    /// operating-system processes.
    ///
    /// It is also the seed of the load-test harness in M4, which is the same loop with more clients
    /// and no console output.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string host = ArgString(args, "--host", "127.0.0.1");
            int port = ArgInt(args, "--port", 7777);
            string name = ArgString(args, "--name", "Bot");
            int seconds = ArgInt(args, "--seconds", 60);
            bool quiet = HasFlag(args, "--quiet");

            Console.WriteLine($"{name}: connecting to {host}:{port}…");

            IPAddress address = IPAddress.TryParse(host, out IPAddress? parsed)
                ? parsed
                : Dns.GetHostAddresses(host)[0];

            using var transport = new UdpClientTransport(new IPEndPoint(address, port));
            if (!transport.WaitForConnection(10000))
            {
                Console.Error.WriteLine($"{name}: could not reach {host}:{port}");
                return 1;
            }

            // The client builds its own copy of the terrain, which is public map data. Everything
            // that fog protects arrives through the replication stream or not at all.
            MatchConfig config = MatchConfig.TwoPlayer();
            var nav = new NavGrid(config.MapWidth, config.MapHeight);
            var terrainSource = new SimWorld(MatchConfig.TwoPlayer());
            PrototypeMap.Build(terrainSource);
            for (int y = 0; y < nav.Height; y++)
            for (int x = 0; x < nav.Width; x++)
                nav.SetTerrain(x, y, terrainSource.Nav.TerrainAt(terrainSource.Nav.Index(x, y)));

            var replica = new ReplicaWorld(nav, 0);
            var connection = new ClientConnection(transport, replica);
            var selection = new SelectionModel(replica);
            var hud = new HudModel(replica);

            connection.SendHello(config.ContentHash(), name);

            var stopwatch = Stopwatch.StartNew();
            long nextTickMs = 0;
            long deadlineMs = seconds * 1000L;
            bool welcomed = false;
            bool ordered = false;
            long lastReportMs = -5000;

            while (stopwatch.ElapsedMilliseconds < deadlineMs)
            {
                long now = stopwatch.ElapsedMilliseconds;
                if (now < nextTickMs) { Thread.Sleep(1); continue; }
                nextTickMs += SimConstants.MillisecondsPerTick;

                connection.Pump();
                selection.Prune();

                if (!welcomed && replica.Welcomed)
                {
                    welcomed = true;
                    Console.WriteLine($"{name}: welcomed as player {replica.LocalPlayer + 1}");
                }

                if (welcomed && !ordered && replica.EntityCount > 0)
                    ordered = OrderEveryoneToWork(replica, connection, name);

                if (!quiet && now - lastReportMs >= 5000)
                {
                    lastReportMs = now;
                    hud.CountOwnUnits(out int workers, out int soldiers, out int ships, out int idle);
                    Console.WriteLine($"{name}: t={hud.MatchClock()} wood={hud.Wood} food={hud.Food} " +
                                      $"stone={hud.Stone} pop={hud.PopulationUsed}/{hud.PopulationCap} " +
                                      $"workers={workers} idle={idle} known={replica.EntityCount} " +
                                      $"retransmits={transport.Retransmissions}");
                }

                if (replica.MatchOver)
                {
                    Console.WriteLine($"{name}: match over — {(replica.LocalPlayerWon ? "VICTORY" : "DEFEAT")}");
                    break;
                }
            }

            Console.WriteLine($"{name}: finished. wood={hud.Wood} food={hud.Food} known entities={replica.EntityCount}");
            return 0;
        }

        /// <summary>
        /// Sends every worker to the nearest forest the client can see. Deliberately naive — this is
        /// a transport test, not an AI; the real opponent AI arrives in M14.
        /// </summary>
        private static bool OrderEveryoneToWork(ReplicaWorld replica, ClientConnection connection, string name)
        {
            var workers = new List<EntityId>();
            EntityId nearestForest = EntityId.None;
            Brinehold.Core.Math.Fix64 bestSqr = Brinehold.Core.Math.Fix64.MaxValue;
            Brinehold.Core.Math.Fix2 anchor = Brinehold.Core.Math.Fix2.Zero;

            foreach (ReplicaWorld.Entity entity in replica.Entities)
            {
                if (entity.Owner != replica.LocalPlayer) continue;
                if (entity.Kind != EntityKind.Worker) continue;
                workers.Add(entity.Id);
                anchor = entity.State.Value.Position;
            }

            if (workers.Count == 0) return false;

            foreach (ReplicaWorld.Entity entity in replica.Entities)
            {
                if (entity.Kind != EntityKind.ResourceNode) continue;
                if (entity.Node != ResourceNodeType.Forest) continue;

                Brinehold.Core.Math.Fix64 sqr =
                    Brinehold.Core.Math.Fix2.SqrDistance(anchor, entity.State.Value.Position);
                if (sqr < bestSqr) { bestSqr = sqr; nearestForest = entity.Id; }
            }

            if (nearestForest.IsNone) return false;

            connection.Send(Command.Harvest(replica.LocalPlayer, 0, workers.ToArray(), nearestForest));
            Console.WriteLine($"{name}: sent {workers.Count} workers to the nearest forest");
            return true;
        }

        private static string ArgString(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        private static int ArgInt(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && int.TryParse(args[i + 1], out int value)) return value;
            return fallback;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++) if (args[i] == name) return true;
            return false;
        }
    }
}
