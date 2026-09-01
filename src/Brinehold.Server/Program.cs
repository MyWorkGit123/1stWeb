using System;
using System.Diagnostics;
using System.Threading;
using Brinehold.Net.Transport;
using Brinehold.Sim.World;

namespace Brinehold.Server
{
    /// <summary>
    /// Headless dedicated server entry point.
    ///
    /// It is a plain .NET console application with no Unity dependency, which is the whole point of
    /// keeping the simulation engine-agnostic: the match host containerises in tens of megabytes and
    /// needs no graphics stack, no editor and no engine licence on the fleet.
    ///
    /// The socket transport lands in M4. Today this runs a match against the in-process loopback so
    /// that the tick loop, timing and shutdown path are real and measurable.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            int players = ArgInt(args, "--players", 2);
            ulong seed = (ulong)ArgInt(args, "--seed", 1);
            int ticks = ArgInt(args, "--ticks", 0);
            bool benchmark = HasFlag(args, "--benchmark");

            Console.WriteLine($"Brinehold server — {players} players, seed {seed}, {SimConstants.TicksPerSecond} Hz");

            MatchConfig config = MatchConfig.TwoPlayer(seed);
            config.PlayerCount = players;

            int port = ArgInt(args, "--port", 0);
            MatchHost host;

            if (port > 0)
            {
                // Dedicated mode: a real UDP socket. Clients connect, introduce themselves, and are
                // given a player slot only once their protocol version and content hash match.
                var transport = new UdpServerTransport(port);
                host = new MatchHost(config, transport);
                Console.WriteLine($"Listening on UDP {transport.LocalEndPoint}");
                Console.WriteLine($"Map {config.MapWidth}x{config.MapHeight}, content hash {config.ContentHash():X16}");
                Console.WriteLine($"Waiting for {players} players…");
                RunDedicated(host, transport, ticks);
                return 0;
            }

            var network = new LoopbackNetwork(NetworkConditions.Perfect);
            host = new MatchHost(config, network);

            for (int i = 0; i < players; i++)
                host.TryConnect(i, $"Player {i + 1}", Protocol.ProtocolVersion.Current, config.ContentHash(), out _);

            Console.WriteLine($"Map {config.MapWidth}x{config.MapHeight}, content hash {config.ContentHash():X16}");

            if (benchmark)
            {
                if (HasFlag(args, "--busy")) StartEveryoneWorking(host);
                RunBenchmark(host, ticks > 0 ? ticks : 6000);
                return 0;
            }

            RunRealTime(host, ticks);
            return 0;
        }

        /// <summary>
        /// The dedicated-server loop: hold the tick rate, wait for players, then run the match.
        ///
        /// The tick is paced against a monotonic clock rather than by sleeping a fixed interval, so
        /// a slow tick is absorbed by the next one instead of drifting the whole match.
        /// </summary>
        private static void RunDedicated(MatchHost host, UdpServerTransport transport, int maxTicks)
        {
            var stopwatch = Stopwatch.StartNew();
            long nextTickMs = 0;
            bool running = true;
            bool announced = false;

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };

            while (running)
            {
                long now = stopwatch.ElapsedMilliseconds;
                if (now < nextTickMs) { Thread.Sleep(1); continue; }

                host.Tick();
                nextTickMs += SimConstants.MillisecondsPerTick;

                if (!announced && host.AllPlayersConnected)
                {
                    announced = true;
                    Console.WriteLine($"All {host.PlayerCount} players connected. Match running.");
                }

                if (host.World.Tick % 200 == 0 && announced)
                {
                    Console.WriteLine($"tick {host.World.Tick}  hash {host.World.ComputeStateHash():X16}  " +
                                      $"connections {transport.ConnectionCount}  retransmits {transport.Retransmissions}");
                }

                if (host.World.MatchOver)
                {
                    Console.WriteLine($"Match over at tick {host.World.Tick}. Winning team: {host.World.WinningTeam}");
                    break;
                }

                if (maxTicks > 0 && host.World.Tick >= (uint)maxTicks) break;
            }

            for (int p = 0; p < host.PlayerCount; p++)
                Console.WriteLine(host.Replication.Stats.Summary(p, host.World.Tick));

            transport.Dispose();
            Console.WriteLine("Server stopped.");
        }

        private static void RunRealTime(MatchHost host, int maxTicks)
        {
            var stopwatch = Stopwatch.StartNew();
            long nextTickMs = 0;
            bool running = true;

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };

            while (running)
            {
                long now = stopwatch.ElapsedMilliseconds;
                if (now < nextTickMs)
                {
                    Thread.Sleep(1);
                    continue;
                }

                host.Tick();
                nextTickMs += SimConstants.MillisecondsPerTick;

                if (host.World.Tick % 200 == 0)
                    Console.WriteLine($"tick {host.World.Tick}  hash {host.World.ComputeStateHash():X16}");

                if (host.World.MatchOver)
                {
                    Console.WriteLine($"Match over at tick {host.World.Tick}. Winning team: {host.World.WinningTeam}");
                    break;
                }

                if (maxTicks > 0 && host.World.Tick >= (uint)maxTicks) break;
            }

            Console.WriteLine("Server stopped.");
        }

        /// <summary>
        /// Puts every worker on the nearest forest, so the benchmark measures a match that is
        /// actually doing something rather than an empty world.
        /// </summary>
        private static void StartEveryoneWorking(MatchHost host)
        {
            for (byte player = 0; player < host.PlayerCount; player++)
            {
                var workers = new System.Collections.Generic.List<Brinehold.Core.Collections.EntityId>();
                for (int i = 1; i < host.World.Entities.Count; i++)
                {
                    if (!host.World.Entities.Alive[i]) continue;
                    if (host.World.Entities.Owner[i] != player) continue;
                    if (host.World.Entities.Kind[i] != EntityKind.Worker) continue;
                    workers.Add(host.World.Entities.IdOf(i));
                }
                if (workers.Count == 0) continue;

                Brinehold.Core.Collections.EntityId forest = Brinehold.Sim.Map.PrototypeMap.FindNearestNode(
                    host.World, host.World.Entities.Position[workers[0].Index], ResourceNodeType.Forest);

                host.World.EnqueueCommand(
                    Brinehold.Sim.Commands.Command.Harvest(player, 1, workers.ToArray(), forest));
            }
        }

        private static void RunBenchmark(MatchHost host, int ticks)
        {
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++) host.Tick();
            stopwatch.Stop();

            double totalMs = stopwatch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"{ticks} ticks in {totalMs:0.0} ms  ({totalMs / ticks:0.000} ms per tick)");
            Console.WriteLine($"simulated {ticks / (double)SimConstants.TicksPerSecond:0.0} s of match time " +
                              $"in {totalMs / 1000.0:0.00} s wall clock " +
                              $"({ticks / (double)SimConstants.TicksPerSecond / (totalMs / 1000.0):0.0}x real time)");
            for (int p = 0; p < host.PlayerCount; p++)
                Console.WriteLine(host.Replication.Stats.Summary(p, host.World.Tick));
            Console.WriteLine($"final state hash {host.World.ComputeStateHash():X16}");
            for (int p = 0; p < host.PlayerCount; p++)
            {
                PlayerState state = host.World.Players[p];
                Console.WriteLine($"player {p}: wood {state.Wood}, food {state.Food}, " +
                                  $"stone {state.Stone}, coin {state.Coin}, " +
                                  $"population {state.PopulationUsed}/{state.PopulationCap}");
            }
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
