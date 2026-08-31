using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Content;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Sim.Tests
{
    public class EconomyTests
    {
        [Fact]
        public void WorkerHarvestsWoodAndDeliversItToTheWarehouse()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[worker.Index], ResourceNodeType.Forest);
            Assert.False(forest.IsNone);

            int startingWood = world.Players[0].Wood;
            world.EnqueueCommand(Command.Harvest(0, 1, new[] { worker }, forest));

            // Long enough to walk out, fill a load and walk back.
            bool delivered = SimFixture.StepUntil(world, w => w.Players[0].Wood > startingWood, 1500);

            Assert.True(delivered, "the worker never delivered any wood");
            Assert.Equal(startingWood + PrototypeContent.Worker.CarryCapacity, world.Players[0].Wood);
        }

        [Fact]
        public void ResourcesOnlyRiseOnDepositNotWhileCarrying()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[worker.Index], ResourceNodeType.Forest);

            int startingWood = world.Players[0].Wood;
            world.EnqueueCommand(Command.Harvest(0, 1, new[] { worker }, forest));

            // Run until the worker is carrying something but has not yet delivered.
            bool sawCarryingWithoutIncome = false;
            for (int i = 0; i < 1500; i++)
            {
                world.Step();
                if (world.Entities.CarriedAmount[worker.Index] > 0 && world.Players[0].Wood == startingWood)
                    sawCarryingWithoutIncome = true;
                if (world.Players[0].Wood > startingWood) break;
            }

            Assert.True(sawCarryingWithoutIncome, "the worker never held goods before the resource count changed");
        }

        [Fact]
        public void HarvestingDrainsTheNode()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[worker.Index], ResourceNodeType.Forest);
            int capacity = world.Entities.NodeRemaining[forest.Index];

            world.EnqueueCommand(Command.Harvest(0, 1, new[] { worker }, forest));
            SimFixture.StepUntil(world, w => w.Entities.NodeRemaining[forest.Index] < capacity, 1500);

            Assert.True(world.Entities.NodeRemaining[forest.Index] < capacity);
        }

        [Fact]
        public void WorkerKeepsCyclingAndDeliversRepeatedly()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[worker.Index], ResourceNodeType.Forest);

            int startingWood = world.Players[0].Wood;
            world.EnqueueCommand(Command.Harvest(0, 1, new[] { worker }, forest));
            world.Step(4000);

            int gained = world.Players[0].Wood - startingWood;
            Assert.True(gained >= PrototypeContent.Worker.CarryCapacity * 2,
                $"only {gained} wood after 200 seconds; the haul cycle is not repeating");
        }

        [Fact]
        public void DestroyingTheDropOffStrandsTheCarriedLoad()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[worker.Index], ResourceNodeType.Forest);
            EntityId core = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);

            world.EnqueueCommand(Command.Harvest(0, 1, new[] { worker }, forest));
            SimFixture.StepUntil(world, w => w.Entities.Job[worker.Index] == JobType.Delivering, 1500);
            Assert.Equal(JobType.Delivering, world.Entities.Job[worker.Index]);

            int woodBefore = world.Players[0].Wood;
            world.Entities.Health[core.Index] = Brinehold.Core.Math.Fix64.Zero;
            world.Step(200);

            Assert.False(world.Entities.IsAlive(core));
            Assert.Equal(woodBefore, world.Players[0].Wood);
            Assert.True(world.Entities.CarriedAmount[worker.Index] > 0, "the load vanished instead of being stranded");
        }

        [Fact]
        public void MultipleWorkersOnOneNodeAllContribute()
        {
            SimWorld world = SimFixture.NewMatch();
            var workers = SimFixture.UnitsOf(world, 0, EntityKind.Worker).Take(5).ToArray();
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[workers[0].Index], ResourceNodeType.Forest);

            int startingWood = world.Players[0].Wood;
            world.EnqueueCommand(Command.Harvest(0, 1, workers, forest));
            world.Step(2500);

            int gained = world.Players[0].Wood - startingWood;
            Assert.True(gained >= PrototypeContent.Worker.CarryCapacity * 4,
                $"five workers only produced {gained} wood");
        }
    }
}
