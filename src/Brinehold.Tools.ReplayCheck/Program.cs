using System;
using System.Diagnostics;
using System.IO;
using Brinehold.Sim.Replay;
using Brinehold.Sim.World;

namespace Brinehold.Tools.ReplayCheck
{
    /// <summary>
    /// Re-simulates a replay and verifies its state hashes.
    ///
    /// This is the determinism gate. CI runs it over the golden corpus on every platform we ship to;
    /// if any checkpoint disagrees, the simulation is no longer deterministic and the merge is
    /// blocked. It doubles as a developer's debugger entry point: <c>--break-at-tick</c> stops on a
    /// chosen tick and prints the world state there.
    ///
    ///   ReplayCheck --replay match.brhr
    ///   ReplayCheck --dir tests/replays
    ///   ReplayCheck --replay match.brhr --break-at-tick 4120
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string? file = ArgString(args, "--replay", null);
            string? directory = ArgString(args, "--dir", null);
            int breakAt = ArgInt(args, "--break-at-tick", -1);

            if (file == null && directory == null)
            {
                Console.Error.WriteLine("usage: ReplayCheck --replay <file.brhr> | --dir <folder> [--break-at-tick N]");
                return 2;
            }

            if (directory != null)
            {
                if (!Directory.Exists(directory))
                {
                    Console.Error.WriteLine($"no such directory: {directory}");
                    return 2;
                }

                string[] files = Directory.GetFiles(directory, "*.brhr");
                Array.Sort(files, StringComparer.Ordinal);

                if (files.Length == 0)
                {
                    Console.WriteLine($"No replays in {directory}. Nothing to verify.");
                    return 0;
                }

                int failures = 0;
                foreach (string path in files) if (!Check(path, -1)) failures++;

                Console.WriteLine(failures == 0
                    ? $"\nAll {files.Length} replays reproduced exactly."
                    : $"\n{failures} of {files.Length} replays DIVERGED.");
                return failures == 0 ? 0 : 1;
            }

            return Check(file!, breakAt) ? 0 : 1;
        }

        private static bool Check(string path, int breakAt)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"no such file: {path}");
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (!ReplayData.TryParse(bytes, out ReplayData data, out string error))
            {
                Console.Error.WriteLine($"{Path.GetFileName(path)}: {error}");
                return false;
            }

            Console.WriteLine($"── {Path.GetFileName(path)} ({bytes.Length:N0} bytes)");
            Console.WriteLine($"   seed {data.Header.Seed}, {data.Header.PlayerCount} players, " +
                              $"map {data.Header.MapWidth}x{data.Header.MapHeight}, " +
                              $"content hash {data.Header.ContentHash:X16}");

            // A replay recorded against a different ruleset may still reproduce, but it is not
            // evidence about the current build. Say so rather than letting it quietly pass.
            ulong currentContent = data.Header.ToConfig().ContentHash();
            if (currentContent != data.Header.ContentHash)
                Console.WriteLine($"   NOTE     recorded against different content " +
                                  $"(this build would be {currentContent:X16}) — regenerate the corpus");

            var player = new ReplayPlayer(data);
            var stopwatch = Stopwatch.StartNew();

            if (breakAt > 0)
            {
                player.StepTo((uint)breakAt);
                stopwatch.Stop();
                Console.WriteLine($"   stopped at tick {player.World.Tick}");
                DumpState(player.World);
                return player.Divergences.Count == 0;
            }

            bool ok = player.Verify();
            stopwatch.Stop();

            Console.WriteLine($"   {player.Summary()}  [{stopwatch.ElapsedMilliseconds} ms]");
            if (data.HasEnd)
                Console.WriteLine($"   recorded result: winning team {data.WinningTeam} at tick {data.EndTick}");

            foreach (ReplayPlayer.Divergence divergence in player.Divergences)
                Console.Error.WriteLine($"   DIVERGENCE {divergence}");

            return ok;
        }

        private static void DumpState(SimWorld world)
        {
            Console.WriteLine($"   state hash {world.ComputeStateHash():X16}");
            for (int p = 0; p < world.Players.Length; p++)
            {
                PlayerState state = world.Players[p];
                Console.WriteLine($"   player {p}: wood {state.Wood}, food {state.Food}, stone {state.Stone}, " +
                                  $"coin {state.Coin}, pop {state.PopulationUsed}/{state.PopulationCap}" +
                                  (state.Defeated ? ", DEFEATED" : string.Empty));
            }

            int workers = 0, soldiers = 0, ships = 0, buildings = 0, nodes = 0;
            for (int i = 1; i < world.Entities.Count; i++)
            {
                if (!world.Entities.Alive[i]) continue;
                switch (world.Entities.Kind[i])
                {
                    case EntityKind.Worker: workers++; break;
                    case EntityKind.Soldier: soldiers++; break;
                    case EntityKind.Ship: ships++; break;
                    case EntityKind.Building: buildings++; break;
                    case EntityKind.ResourceNode: nodes++; break;
                }
            }
            Console.WriteLine($"   entities: {workers} workers, {soldiers} soldiers, {ships} ships, " +
                              $"{buildings} buildings, {nodes} resource nodes");
        }

        private static string? ArgString(string[] args, string name, string? fallback)
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
    }
}
