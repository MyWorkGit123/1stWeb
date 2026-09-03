using System;
using Brinehold.Content;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Tools.ContentCheck
{
    /// <summary>
    /// Validates the authored content files.
    ///
    /// Runs in CI on every change, because a typo in a balance file is a broken build that no unit
    /// test would otherwise catch — and because a content set that loads but is unplayable (nothing
    /// can train a worker, no building accepts deliveries, the first house costs more than a player
    /// starts with) is worse than one that fails outright.
    ///
    ///   ContentCheck [--dir packages/com.brinehold.content/Data] [--compare-default]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string directory = ArgString(args, "--dir", "packages/com.brinehold.content/Data");
            bool compareDefault = HasFlag(args, "--compare-default");

            Console.WriteLine($"Validating content in {directory}");

            ContentLoader.Result result = ContentLoader.LoadFromDirectory(directory);

            foreach (string problem in result.Problems) Console.Error.WriteLine($"  PROBLEM  {problem}");

            if (result.Database == null)
            {
                Console.Error.WriteLine("Content could not be loaded.");
                return 1;
            }

            ContentDatabase database = result.Database;
            Console.WriteLine($"  ruleset  '{database.Name}'");
            Console.WriteLine($"  hash     {database.ContentHash():X16}");
            Console.WriteLine($"  opening  {database.StartingWorkers} workers, " +
                              $"{database.StartingWood} wood, {database.StartingFood} food, " +
                              $"{database.StartingStone} stone, {database.StartingCoin} coin");

            foreach (EntityKind kind in new[] { EntityKind.Worker, EntityKind.Soldier, EntityKind.Ship })
            {
                ContentDatabase.UnitStats unit = database.Unit(kind);
                Console.WriteLine($"  unit     {kind,-8} hp {unit.MaxHealth,5}  speed {unit.MoveSpeed,5}  " +
                                  $"vision {unit.VisionRange,4}  train {unit.TrainTicks / 20.0,5:0.0}s  pop {unit.PopulationCost}");
            }

            foreach (BuildingType type in new[]
                     {
                         BuildingType.Warehouse, BuildingType.House, BuildingType.LumberCamp,
                         BuildingType.FishingWharf, BuildingType.Dock
                     })
            {
                ContentDatabase.BuildingStats building = database.Building(type);
                Console.WriteLine($"  building {type,-13} hp {building.MaxHealth,5}  " +
                                  $"build {building.BuildTicks / 20.0,5:0.0}s  " +
                                  $"cost {building.CostWood}w/{building.CostStone}s  " +
                                  $"{(building.IsDropOff ? "drop-off " : "")}" +
                                  $"{(building.RequiresWaterAdjacency ? "shoreline" : "")}");
            }

            if (compareDefault)
            {
                ulong loaded = database.ContentHash();
                ulong shipped = ContentDatabase.CreateDefault().ContentHash();
                if (loaded != shipped)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"  MISMATCH the JSON ({loaded:X16}) differs from the code defaults ({shipped:X16}).");
                    Console.Error.WriteLine("           One of them has been edited without the other. They must agree, because");
                    Console.Error.WriteLine("           the code defaults are the fallback when no content files are present.");
                    return 1;
                }
                Console.WriteLine("  match    the JSON and the code defaults agree");
            }

            if (result.Problems.Count > 0)
            {
                Console.Error.WriteLine($"\n{result.Problems.Count} problem(s) found.");
                return 1;
            }

            Console.WriteLine("\nContent is valid.");
            return 0;
        }

        private static string ArgString(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++) if (args[i] == name) return true;
            return false;
        }
    }
}
