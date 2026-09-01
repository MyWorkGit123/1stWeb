using System;
using System.Collections.Generic;
using System.IO;
using Brinehold.Core.Math;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Content
{
    /// <summary>
    /// Turns authored JSON into a <see cref="ContentDatabase"/>.
    ///
    /// Content is authored in decimal — a worker walks at 1.4 metres per second, not at
    /// 6012954214 raw fixed-point units — and converted here, at one point, into the integer types
    /// the simulation uses. That conversion is the only place a decimal number touches the pipeline,
    /// which is what lets the content files stay readable without weakening determinism.
    ///
    /// Loading never throws on bad data. It collects every problem and reports them together, so a
    /// designer sees the whole list rather than fixing one typo at a time.
    /// </summary>
    public static class ContentLoader
    {
        public sealed class Result
        {
            public ContentDatabase? Database;
            public readonly List<string> Problems = new List<string>();
            public bool Success => Database != null && Problems.Count == 0;
        }

        private static readonly Dictionary<string, EntityKind> UnitNames = new Dictionary<string, EntityKind>(StringComparer.OrdinalIgnoreCase)
        {
            { "worker", EntityKind.Worker },
            { "soldier", EntityKind.Soldier },
            { "ship", EntityKind.Ship }
        };

        private static readonly Dictionary<string, BuildingType> BuildingNames = new Dictionary<string, BuildingType>(StringComparer.OrdinalIgnoreCase)
        {
            { "warehouse", BuildingType.Warehouse },
            { "house", BuildingType.House },
            { "lumberCamp", BuildingType.LumberCamp },
            { "fishingWharf", BuildingType.FishingWharf },
            { "dock", BuildingType.Dock }
        };

        private static readonly Dictionary<string, ResourceNodeType> NodeNames = new Dictionary<string, ResourceNodeType>(StringComparer.OrdinalIgnoreCase)
        {
            { "forest", ResourceNodeType.Forest },
            { "fishShoal", ResourceNodeType.FishShoal },
            { "stoneOutcrop", ResourceNodeType.StoneOutcrop }
        };

        private static readonly Dictionary<string, ResourceType> ResourceNames = new Dictionary<string, ResourceType>(StringComparer.OrdinalIgnoreCase)
        {
            { "wood", ResourceType.Wood },
            { "food", ResourceType.Food },
            { "stone", ResourceType.Stone },
            { "coin", ResourceType.Coin }
        };

        private static readonly Dictionary<string, MovementDomain> DomainNames = new Dictionary<string, MovementDomain>(StringComparer.OrdinalIgnoreCase)
        {
            { "land", MovementDomain.Land },
            { "water", MovementDomain.Water },
            { "static", MovementDomain.Static }
        };

        /// <summary>Loads every content file in a directory. Missing files fall back to the shipped defaults.</summary>
        public static Result LoadFromDirectory(string directory)
        {
            var result = new Result();

            if (!Directory.Exists(directory))
            {
                result.Problems.Add($"content directory not found: {directory}");
                return result;
            }

            // Start from the shipped ruleset so a partial content set is still playable, and a
            // designer can override one building without restating the whole game.
            ContentDatabase database = ContentDatabase.CreateDefault();

            LoadFile(Path.Combine(directory, "tunables.json"), result, json => ApplyTunables(database, json, result));
            LoadFile(Path.Combine(directory, "units.json"), result, json => ApplyUnits(database, json, result));
            LoadFile(Path.Combine(directory, "buildings.json"), result, json => ApplyBuildings(database, json, result));
            LoadFile(Path.Combine(directory, "resourceNodes.json"), result, json => ApplyNodes(database, json, result));

            foreach (string problem in database.Validate()) result.Problems.Add($"validation: {problem}");

            result.Database = database;
            return result;
        }

        private static void LoadFile(string path, Result result, Action<JsonValue> apply)
        {
            if (!File.Exists(path)) return;   // optional: defaults stand

            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException exception) { result.Problems.Add($"{Path.GetFileName(path)}: {exception.Message}"); return; }

            if (!JsonValue.TryParse(text, out JsonValue json, out string error))
            {
                result.Problems.Add($"{Path.GetFileName(path)}: {error}");
                return;
            }

            apply(json);
        }

        private static void ApplyTunables(ContentDatabase database, JsonValue json, Result result)
        {
            database.Name = json.GetString("name", database.Name);
            database.HarvestTicksPerUnit = json.GetInt("harvestTicksPerUnit", database.HarvestTicksPerUnit);
            database.BuildProgressPerWorkerTick = json.GetInt("buildProgressPerWorkerTick", database.BuildProgressPerWorkerTick);
            database.StartingWood = json.GetInt("startingWood", database.StartingWood);
            database.StartingFood = json.GetInt("startingFood", database.StartingFood);
            database.StartingStone = json.GetInt("startingStone", database.StartingStone);
            database.StartingCoin = json.GetInt("startingCoin", database.StartingCoin);
            database.StartingWorkers = json.GetInt("startingWorkers", database.StartingWorkers);
            database.BasePopulationCap = json.GetInt("basePopulationCap", database.BasePopulationCap);
            database.MaxPopulationCap = json.GetInt("maxPopulationCap", database.MaxPopulationCap);

            if (database.HarvestTicksPerUnit <= 0)
                result.Problems.Add("tunables: harvestTicksPerUnit must be positive, or harvesting never completes");
            if (database.BuildProgressPerWorkerTick <= 0)
                result.Problems.Add("tunables: buildProgressPerWorkerTick must be positive, or construction never finishes");
        }

        private static void ApplyUnits(ContentDatabase database, JsonValue json, Result result)
        {
            foreach (string key in json.Keys)
            {
                if (!UnitNames.TryGetValue(key, out EntityKind kind))
                {
                    result.Problems.Add($"units: unknown unit '{key}'");
                    continue;
                }

                JsonValue entry = json[key];
                ContentDatabase.UnitStats stats = database.Unit(kind);

                stats.MaxHealth = Milli(entry, "maxHealth", stats.MaxHealth);
                stats.MoveSpeed = Milli(entry, "moveSpeed", stats.MoveSpeed);
                stats.VisionRange = Milli(entry, "visionRange", stats.VisionRange);
                stats.AttackDamage = Milli(entry, "attackDamage", stats.AttackDamage);
                stats.AttackRange = Milli(entry, "attackRange", stats.AttackRange);
                stats.AttackCooldownTicks = entry.GetInt("attackCooldownTicks", stats.AttackCooldownTicks);
                stats.CarryCapacity = entry.GetInt("carryCapacity", stats.CarryCapacity);
                stats.TrainTicks = entry.GetInt("trainTicks", stats.TrainTicks);
                stats.PopulationCost = entry.GetInt("populationCost", stats.PopulationCost);

                JsonValue cost = entry["cost"];
                stats.CostWood = cost.GetInt("wood", stats.CostWood);
                stats.CostFood = cost.GetInt("food", stats.CostFood);
                stats.CostStone = cost.GetInt("stone", stats.CostStone);
                stats.CostCoin = cost.GetInt("coin", stats.CostCoin);

                if (entry.Has("domain"))
                {
                    string domain = entry.GetString("domain");
                    if (DomainNames.TryGetValue(domain, out MovementDomain parsed)) stats.Domain = parsed;
                    else result.Problems.Add($"units.{key}: unknown domain '{domain}'");
                }

                database.SetUnit(kind, stats);
            }
        }

        private static void ApplyBuildings(ContentDatabase database, JsonValue json, Result result)
        {
            foreach (string key in json.Keys)
            {
                if (!BuildingNames.TryGetValue(key, out BuildingType type))
                {
                    result.Problems.Add($"buildings: unknown building '{key}'");
                    continue;
                }

                JsonValue entry = json[key];
                ContentDatabase.BuildingStats stats = database.Building(type);

                stats.MaxHealth = Milli(entry, "maxHealth", stats.MaxHealth);
                stats.VisionRange = Milli(entry, "visionRange", stats.VisionRange);
                stats.FootprintHalf = entry.GetInt("footprintHalf", stats.FootprintHalf);
                stats.BuildTicks = entry.GetInt("buildTicks", stats.BuildTicks);
                stats.RequiresWaterAdjacency = entry.GetBool("requiresWaterAdjacency", stats.RequiresWaterAdjacency);
                stats.IsDropOff = entry.GetBool("isDropOff", stats.IsDropOff);
                stats.PopulationCapacity = entry.GetInt("populationCapacity", stats.PopulationCapacity);

                JsonValue cost = entry["cost"];
                stats.CostWood = cost.GetInt("wood", stats.CostWood);
                stats.CostFood = cost.GetInt("food", stats.CostFood);
                stats.CostStone = cost.GetInt("stone", stats.CostStone);
                stats.CostCoin = cost.GetInt("coin", stats.CostCoin);

                if (entry.Has("trains"))
                {
                    int mask = 0;
                    foreach (JsonValue trained in entry["trains"].AsArray)
                    {
                        string name = trained.AsString;
                        if (UnitNames.TryGetValue(name, out EntityKind kind)) mask |= 1 << (int)kind;
                        else result.Problems.Add($"buildings.{key}: cannot train unknown unit '{name}'");
                    }
                    stats.TrainableMask = mask;
                }

                database.SetBuilding(type, stats);
            }
        }

        private static void ApplyNodes(ContentDatabase database, JsonValue json, Result result)
        {
            foreach (string key in json.Keys)
            {
                if (!NodeNames.TryGetValue(key, out ResourceNodeType type))
                {
                    result.Problems.Add($"resourceNodes: unknown node '{key}'");
                    continue;
                }

                JsonValue entry = json[key];
                ContentDatabase.NodeStats stats = database.Node(type);
                stats.Capacity = entry.GetInt("capacity", stats.Capacity);

                if (entry.Has("yields"))
                {
                    string yields = entry.GetString("yields");
                    if (ResourceNames.TryGetValue(yields, out ResourceType resource)) stats.Yields = resource;
                    else result.Problems.Add($"resourceNodes.{key}: unknown resource '{yields}'");
                }

                database.SetNode(type, stats);
            }
        }

        /// <summary>Reads an authored decimal as fixed point, via thousandths.</summary>
        private static Fix64 Milli(JsonValue entry, string key, Fix64 fallback)
            => entry.Has(key) ? Fix64.FromMilli(entry.GetMilli(key)) : fallback;
    }
}
