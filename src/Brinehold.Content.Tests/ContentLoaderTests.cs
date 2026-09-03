using System;
using System.IO;
using Brinehold.Content;
using Brinehold.Sim.Content;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Content.Tests
{
    public class ContentLoaderTests
    {
        private static string DataDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "packages", "com.brinehold.content", "Data");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("could not locate the content data directory");
        }

        /// <summary>
        /// The load-bearing test of the whole content pipeline.
        ///
        /// The code defaults are the fallback when no content files are present, so if the JSON and
        /// the defaults ever disagree, a server with content files and a server without them would
        /// be playing different games while both believing they agreed.
        /// </summary>
        [Fact]
        public void TheAuthoredJsonMatchesTheShippedDefaultsExactly()
        {
            ContentLoader.Result result = ContentLoader.LoadFromDirectory(DataDirectory());

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.Equal(ContentDatabase.CreateDefault().ContentHash(), result.Database!.ContentHash());
        }

        [Fact]
        public void TheShippedContentPassesValidation()
        {
            ContentLoader.Result result = ContentLoader.LoadFromDirectory(DataDirectory());
            Assert.Empty(result.Database!.Validate());
        }

        [Fact]
        public void AMissingDirectoryIsReportedNotThrown()
        {
            ContentLoader.Result result = ContentLoader.LoadFromDirectory("/no/such/place");
            Assert.False(result.Success);
            Assert.NotEmpty(result.Problems);
        }

        [Fact]
        public void PartialContentOverridesOnlyWhatItStates()
        {
            string temporary = Path.Combine(Path.GetTempPath(), "brinehold-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                // State one field of one unit. Everything else must keep the shipped value.
                File.WriteAllText(Path.Combine(temporary, "units.json"),
                    @"{ ""worker"": { ""carryCapacity"": 20 } }");

                ContentLoader.Result result = ContentLoader.LoadFromDirectory(temporary);
                Assert.True(result.Success, string.Join("; ", result.Problems));

                ContentDatabase database = result.Database!;
                ContentDatabase shipped = ContentDatabase.CreateDefault();

                Assert.Equal(20, database.Unit(EntityKind.Worker).CarryCapacity);
                Assert.Equal(shipped.Unit(EntityKind.Worker).MoveSpeed, database.Unit(EntityKind.Worker).MoveSpeed);
                Assert.Equal(shipped.Building(BuildingType.House).CostWood, database.Building(BuildingType.House).CostWood);
            }
            finally { Directory.Delete(temporary, true); }
        }

        [Fact]
        public void UnknownNamesAreReportedRatherThanIgnored()
        {
            string temporary = Path.Combine(Path.GetTempPath(), "brinehold-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                File.WriteAllText(Path.Combine(temporary, "units.json"),
                    @"{ ""kraken"": { ""maxHealth"": 9999 } }");
                File.WriteAllText(Path.Combine(temporary, "buildings.json"),
                    @"{ ""warehouse"": { ""trains"": [""kraken""] } }");

                ContentLoader.Result result = ContentLoader.LoadFromDirectory(temporary);

                Assert.False(result.Success);
                Assert.Contains(result.Problems, p => p.Contains("kraken"));
            }
            finally { Directory.Delete(temporary, true); }
        }

        [Fact]
        public void ValidationRejectsAnUnplayableRuleset()
        {
            var database = ContentDatabase.CreateDefault();

            // Take away the only building that can train a worker.
            ContentDatabase.BuildingStats core = database.Building(BuildingType.Warehouse);
            core.TrainableMask = 0;
            database.SetBuilding(BuildingType.Warehouse, core);

            // And every building that accepts deliveries, so nothing harvested can be banked.
            foreach (BuildingType type in new[]
                     { BuildingType.Warehouse, BuildingType.LumberCamp, BuildingType.FishingWharf })
            {
                ContentDatabase.BuildingStats stats = database.Building(type);
                stats.IsDropOff = false;
                database.SetBuilding(type, stats);
            }

            // And make the opening house unaffordable.
            ContentDatabase.BuildingStats house = database.Building(BuildingType.House);
            house.CostWood = 100000;
            database.SetBuilding(BuildingType.House, house);

            string[] problems = database.Validate();

            Assert.Contains(problems, p => p.Contains("train a worker"));
            Assert.Contains(problems, p => p.Contains("accepts deliveries"));
            Assert.Contains(problems, p => p.Contains("more wood than a player starts with"));
        }

        [Fact]
        public void EditedContentChangesTheHashThatGatesTheHandshake()
        {
            var edited = ContentDatabase.CreateDefault();
            ContentDatabase.UnitStats soldier = edited.Unit(EntityKind.Soldier);
            soldier.CostFood = 1;   // the classic cheat: make my units nearly free
            edited.SetUnit(EntityKind.Soldier, soldier);

            Assert.NotEqual(ContentDatabase.CreateDefault().ContentHash(), edited.ContentHash());

            // And the match configuration hash, which is what the server actually compares at
            // handshake, moves with it.
            var honest = MatchConfig.TwoPlayer();
            var cheating = MatchConfig.TwoPlayer();
            cheating.Content = edited;
            Assert.NotEqual(honest.ContentHash(), cheating.ContentHash());
        }

        [Fact]
        public void AMatchRunsOnLoadedContentAndRespectsIt()
        {
            ContentLoader.Result result = ContentLoader.LoadFromDirectory(DataDirectory());
            ContentDatabase database = result.Database!;

            // Double every worker's carrying capacity and confirm the simulation actually uses it.
            ContentDatabase.UnitStats worker = database.Unit(EntityKind.Worker);
            worker.CarryCapacity *= 2;
            database.SetUnit(EntityKind.Worker, worker);

            var config = MatchConfig.TwoPlayer();
            config.Content = database;
            var world = new SimWorld(config);
            PrototypeMap.Build(world);

            var workers = new System.Collections.Generic.List<Brinehold.Core.Collections.EntityId>();
            for (int i = 1; i < world.Entities.Count; i++)
            {
                if (!world.Entities.Alive[i]) continue;
                if (world.Entities.Owner[i] != 0 || world.Entities.Kind[i] != EntityKind.Worker) continue;
                workers.Add(world.Entities.IdOf(i));
            }

            Brinehold.Core.Collections.EntityId forest = PrototypeMap.FindNearestNode(
                world, world.Entities.Position[workers[0].Index], ResourceNodeType.Forest);

            int startingWood = world.Players[0].Wood;
            world.EnqueueCommand(Brinehold.Sim.Commands.Command.Harvest(0, 1, new[] { workers[0] }, forest));

            for (int t = 0; t < 2000 && world.Players[0].Wood == startingWood; t++) world.Step();

            // One delivery, at the edited capacity rather than the shipped one.
            Assert.Equal(startingWood + worker.CarryCapacity, world.Players[0].Wood);
        }
    }
}
