using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Content;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Sim.Tests
{
    public class ConstructionTests
    {
        private static (SimWorld world, EntityId[] builders, int cellX, int cellY) Setup()
        {
            SimWorld world = SimFixture.NewMatch();
            var builders = SimFixture.UnitsOf(world, 0, EntityKind.Worker).Take(4).ToArray();
            // Clear ground a little away from the core, on land.
            return (world, builders, PrototypeMap.StartCellX[0] + 10, PrototypeMap.StartCellY[0] - 8);
        }

        [Fact]
        public void PlacingAHouseDeductsResourcesAndCreatesASite()
        {
            var (world, builders, x, y) = Setup();
            int woodBefore = world.Players[0].Wood;

            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.House, x, y));
            world.Step();

            Assert.Equal(woodBefore - PrototypeContent.ForBuilding(BuildingType.House).CostWood, world.Players[0].Wood);
            EntityId house = SimFixture.FirstBuilding(world, 0, BuildingType.House);
            Assert.False(house.IsNone);
            Assert.True(world.Entities.UnderConstruction[house.Index]);
        }

        [Fact]
        public void WorkersCompleteTheHouseAndRaiseThePopulationCap()
        {
            var (world, builders, x, y) = Setup();
            int capBefore = world.Players[0].PopulationCap;

            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.House, x, y));
            EntityId house = EntityId.None;
            SimFixture.StepUntil(world, w =>
            {
                if (house.IsNone) house = SimFixture.FirstBuilding(w, 0, BuildingType.House);
                return !house.IsNone && !w.Entities.UnderConstruction[house.Index];
            }, 2000);

            Assert.False(house.IsNone);
            Assert.False(world.Entities.UnderConstruction[house.Index]);
            Assert.Equal(capBefore + 5, world.Players[0].PopulationCap);
            Assert.Equal(world.Entities.MaxHealth[house.Index], world.Entities.Health[house.Index]);
        }

        [Fact]
        public void MoreBuildersFinishSooner()
        {
            var oneBuilder = SimFixture.NewMatch();
            var manyBuilders = SimFixture.NewMatch();
            int x = PrototypeMap.StartCellX[0] + 10, y = PrototypeMap.StartCellY[0] - 8;

            oneBuilder.EnqueueCommand(Command.Build(0, 1,
                SimFixture.UnitsOf(oneBuilder, 0, EntityKind.Worker).Take(1).ToArray(), BuildingType.House, x, y));
            manyBuilders.EnqueueCommand(Command.Build(0, 1,
                SimFixture.UnitsOf(manyBuilders, 0, EntityKind.Worker).Take(6).ToArray(), BuildingType.House, x, y));

            int TicksToFinish(SimWorld w)
            {
                for (int t = 0; t < 4000; t++)
                {
                    w.Step();
                    EntityId h = SimFixture.FirstBuilding(w, 0, BuildingType.House);
                    if (!h.IsNone && !w.Entities.UnderConstruction[h.Index]) return t;
                }
                return int.MaxValue;
            }

            int slow = TicksToFinish(oneBuilder);
            int fast = TicksToFinish(manyBuilders);
            Assert.True(fast < slow, $"six builders ({fast} ticks) were not faster than one ({slow} ticks)");
        }

        [Fact]
        public void PlacingOnWaterIsRejectedAndCostsNothing()
        {
            var (world, builders, _, _) = Setup();
            int woodBefore = world.Players[0].Wood;

            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.House, 40, 5));  // in the sea
            world.Step();

            Assert.Equal(woodBefore, world.Players[0].Wood);
            Assert.True(SimFixture.FirstBuilding(world, 0, BuildingType.House).IsNone);
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
        }

        [Fact]
        public void PlacingOnTopOfAnExistingBuildingIsRejected()
        {
            var (world, builders, _, _) = Setup();
            int woodBefore = world.Players[0].Wood;

            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.House,
                PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0]));   // on the core
            world.Step();

            Assert.Equal(woodBefore, world.Players[0].Wood);
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
        }

        [Fact]
        public void ADockRequiresWaterAdjacency()
        {
            var (world, builders, x, y) = Setup();

            // Inland: illegal.
            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.Dock, x, y));
            world.Step();
            Assert.True(SimFixture.FirstBuilding(world, 0, BuildingType.Dock).IsNone);

            // On the shoreline: legal.
            world.EnqueueCommand(Command.Build(0, 2, builders, BuildingType.Dock, PrototypeMap.StartCellX[0], PrototypeMap.SeaLine + 2));
            world.Step();
            Assert.False(SimFixture.FirstBuilding(world, 0, BuildingType.Dock).IsNone);
        }

        [Fact]
        public void CannotAffordMeansNoSiteAndNoDeduction()
        {
            var (world, builders, x, y) = Setup();
            world.Players[0].Wood = 10;   // a house costs 50

            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.House, x, y));
            world.Step();

            Assert.Equal(10, world.Players[0].Wood);
            Assert.True(SimFixture.FirstBuilding(world, 0, BuildingType.House).IsNone);
        }
    }

    public class ProductionTests
    {
        [Fact]
        public void TrainingAWorkerDeductsFoodAndSpawnsAfterTheTimer()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);
            world.Players[0].PopulationCap = 30;

            int foodBefore = world.Players[0].Food;
            int workersBefore = SimFixture.UnitsOf(world, 0, EntityKind.Worker).Count;

            world.EnqueueCommand(Command.Train(0, 1, core, EntityKind.Worker));
            world.Step();
            Assert.Equal(foodBefore - PrototypeContent.Worker.CostFood, world.Players[0].Food);
            Assert.Equal(workersBefore, SimFixture.UnitsOf(world, 0, EntityKind.Worker).Count);

            world.Step(PrototypeContent.Worker.TrainTicks + 2);
            Assert.Equal(workersBefore + 1, SimFixture.UnitsOf(world, 0, EntityKind.Worker).Count);
        }

        [Fact]
        public void TrainingIsBlockedByThePopulationCap()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);
            // Population is 10 against a cap of 5 at the start, so any order should be refused.
            int foodBefore = world.Players[0].Food;

            world.EnqueueCommand(Command.Train(0, 1, core, EntityKind.Worker));
            world.Step();

            Assert.Equal(foodBefore, world.Players[0].Food);
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
        }

        [Fact]
        public void AWarehouseCannotBuildAShip()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);
            world.Players[0].PopulationCap = 30;

            world.EnqueueCommand(Command.Train(0, 1, core, EntityKind.Ship));
            world.Step();

            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
            Assert.Empty(SimFixture.UnitsOf(world, 0, EntityKind.Ship));
        }

        [Fact]
        public void CancellingTrainingRefundsTheCost()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);
            world.Players[0].PopulationCap = 30;
            int foodBefore = world.Players[0].Food;

            world.EnqueueCommand(Command.Train(0, 1, core, EntityKind.Worker));
            world.Step();
            world.EnqueueCommand(new Command
            {
                PlayerId = 0, Sequence = 2, Type = CommandType.CancelTraining,
                Entities = new[] { core }, EntityCount = 1
            });
            world.Step();

            Assert.Equal(foodBefore, world.Players[0].Food);
            Assert.Equal(0, world.Entities.TrainingQueued[core.Index]);
        }

        [Fact]
        public void ADockBuildsAShipThatFloats()
        {
            SimWorld world = SimFixture.NewMatch();
            world.Players[0].PopulationCap = 30;
            world.Players[0].Wood = 500;
            world.Players[0].Stone = 500;

            var builders = SimFixture.UnitsOf(world, 0, EntityKind.Worker).Take(6).ToArray();
            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.Dock,
                PrototypeMap.StartCellX[0], PrototypeMap.SeaLine + 2));

            EntityId dock = EntityId.None;
            bool built = SimFixture.StepUntil(world, w =>
            {
                if (dock.IsNone) dock = SimFixture.FirstBuilding(w, 0, BuildingType.Dock);
                return !dock.IsNone && !w.Entities.UnderConstruction[dock.Index];
            }, 3000);
            Assert.True(built, "the dock never finished");

            world.EnqueueCommand(Command.Train(0, 2, dock, EntityKind.Ship));
            world.Step(PrototypeContent.Ship.TrainTicks + 5);

            var ships = SimFixture.UnitsOf(world, 0, EntityKind.Ship);
            Assert.Single(ships);

            int cell = world.Nav.CellAt(world.Entities.Position[ships[0].Index]);
            Assert.Equal(TerrainType.Water, world.Nav.TerrainAt(cell));
        }
    }
}
