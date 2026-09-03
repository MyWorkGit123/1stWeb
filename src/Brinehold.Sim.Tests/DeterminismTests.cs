using System.Collections.Generic;
using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Sim.Tests
{
    /// <summary>
    /// Determinism is what makes replays small, bug reports reproducible and the CI matrix
    /// meaningful. These tests run the same command stream through independent worlds and require
    /// the state fingerprints to match at every checkpoint.
    /// </summary>
    public class DeterminismTests
    {
        /// <summary>A scripted opening: harvest, build, train and fight, so most systems are exercised.</summary>
        private static void RunScript(SimWorld world, int ticks, List<ulong> hashes)
        {
            var workers = SimFixture.UnitsOf(world, 0, EntityKind.Worker);
            var enemyWorkers = SimFixture.UnitsOf(world, 1, EntityKind.Worker);
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[workers[0].Index], ResourceNodeType.Forest);
            EntityId core = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);

            world.EnqueueCommand(Command.Harvest(0, 1, workers.Take(4).ToArray(), forest));
            world.EnqueueCommand(Command.Build(0, 2, workers.Skip(4).Take(4).ToArray(),
                BuildingType.House, PrototypeMap.StartCellX[0] + 10, PrototypeMap.StartCellY[0] - 8));

            EntityId enemyForest = PrototypeMap.FindNearestNode(world, world.Entities.Position[enemyWorkers[0].Index], ResourceNodeType.Forest);
            world.EnqueueCommand(Command.Harvest(1, 1, enemyWorkers.Take(6).ToArray(), enemyForest));

            for (int t = 0; t < ticks; t++)
            {
                if (t == 400) world.EnqueueCommand(Command.Train(0, 3, core, EntityKind.Worker));
                if (t == 600)
                    world.EnqueueCommand(Command.Move(1, 2, enemyWorkers.Skip(6).ToArray(),
                        PrototypeMap.StartCellX[1] - 20, PrototypeMap.StartCellY[1] + 10));

                world.Step();
                if (t % 100 == 0) hashes.Add(world.ComputeStateHash());
            }
            hashes.Add(world.ComputeStateHash());
        }

        [Fact]
        public void TwoWorldsWithTheSameScriptProduceIdenticalHashes()
        {
            var a = new List<ulong>();
            var b = new List<ulong>();
            RunScript(SimFixture.NewMatch(seed: 20260831), 1200, a);
            RunScript(SimFixture.NewMatch(seed: 20260831), 1200, b);

            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.True(a[i] == b[i], $"state diverged at checkpoint {i}: {a[i]:X16} vs {b[i]:X16}");
        }

        [Fact]
        public void ADifferentScriptProducesADifferentHash()
        {
            var a = new List<ulong>();
            RunScript(SimFixture.NewMatch(seed: 7), 400, a);

            SimWorld other = SimFixture.NewMatch(seed: 7);
            other.Step(400);
            ulong idle = other.ComputeStateHash();

            Assert.NotEqual(a[a.Count - 1], idle);
        }

        [Fact]
        public void HashIsStableAcrossRepeatedEvaluation()
        {
            SimWorld world = SimFixture.NewMatch();
            world.Step(200);
            ulong first = world.ComputeStateHash();
            ulong second = world.ComputeStateHash();
            Assert.Equal(first, second);
        }

        [Fact]
        public void CommandOrderWithinATickDoesNotDependOnSubmissionOrder()
        {
            SimWorld a = SimFixture.NewMatch();
            SimWorld b = SimFixture.NewMatch();

            var wa = SimFixture.UnitsOf(a, 0, EntityKind.Worker);
            var wb = SimFixture.UnitsOf(b, 0, EntityKind.Worker);

            // Same commands, submitted in opposite order. The ingest system sorts by
            // (player, sequence), so the outcome must not differ.
            a.EnqueueCommand(Command.Move(0, 1, new[] { wa[0] }, 40, 50));
            a.EnqueueCommand(Command.Move(0, 2, new[] { wa[1] }, 42, 52));
            b.EnqueueCommand(Command.Move(0, 2, new[] { wb[1] }, 42, 52));
            b.EnqueueCommand(Command.Move(0, 1, new[] { wb[0] }, 40, 50));

            a.Step(300);
            b.Step(300);

            Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        }

        [Fact]
        public void EntityAllocationOrderIsStableAfterRecycling()
        {
            SimWorld a = SimFixture.NewMatch();
            SimWorld b = SimFixture.NewMatch();

            foreach (SimWorld w in new[] { a, b })
            {
                var workers = SimFixture.UnitsOf(w, 0, EntityKind.Worker);
                w.Entities.Health[workers[3].Index] = Brinehold.Core.Math.Fix64.Zero;
                w.Entities.Health[workers[1].Index] = Brinehold.Core.Math.Fix64.Zero;
                w.Step();
                w.SpawnUnit(EntityKind.Soldier, 0, w.Entities.Position[workers[0].Index]);
                w.SpawnUnit(EntityKind.Soldier, 0, w.Entities.Position[workers[0].Index]);
                w.Step(10);
            }

            Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        }
    }
}
