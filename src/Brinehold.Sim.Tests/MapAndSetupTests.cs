using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Sim.Tests
{
    public class MapAndSetupTests
    {
        [Fact]
        public void BothPlayersStartWithTenWorkersAndACore()
        {
            SimWorld world = SimFixture.NewMatch();

            for (byte p = 0; p < 2; p++)
            {
                Assert.Equal(10, SimFixture.UnitsOf(world, p, EntityKind.Worker).Count);
                Assert.False(SimFixture.FirstBuilding(world, p, BuildingType.Warehouse).IsNone);
                Assert.Equal(200, world.Players[p].Wood);
                Assert.Equal(200, world.Players[p].Food);
                Assert.Equal(100, world.Players[p].Stone);
                Assert.Equal(100, world.Players[p].Coin);
                Assert.Equal(10, world.Players[p].PopulationUsed);
                // Base capacity of 5 plus the settlement core's own 5: a player starts exactly at
                // their cap and must put up a house before they can grow.
                Assert.Equal(10, world.Players[p].PopulationCap);
            }
        }

        [Fact]
        public void StartingAreasAreSeparated()
        {
            SimWorld world = SimFixture.NewMatch();
            var core0 = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);
            var core1 = SimFixture.FirstBuilding(world, 1, BuildingType.Warehouse);
            double distance = Brinehold.Core.Math.Fix2
                .Distance(world.Entities.Position[core0.Index], world.Entities.Position[core1.Index]).ToDouble();
            Assert.True(distance > 80, $"starting cores only {distance:0.0} m apart");
        }

        [Fact]
        public void MapHasWaterLandAndBlockedTerrain()
        {
            SimWorld world = SimFixture.NewMatch();
            int water = 0, land = 0, blocked = 0;
            for (int i = 0; i < world.Nav.CellCount; i++)
            {
                switch (world.Nav.TerrainAt(i))
                {
                    case TerrainType.Water: water++; break;
                    case TerrainType.Land: land++; break;
                    case TerrainType.Blocked: blocked++; break;
                }
            }
            Assert.True(water > 1000, $"only {water} water cells");
            Assert.True(land > 10000, $"only {land} land cells");
            Assert.True(blocked > 200, $"only {blocked} blocked cells");
        }

        [Fact]
        public void ResourceNodesExistForBothPlayers()
        {
            SimWorld world = SimFixture.NewMatch();
            int forests = 0, fish = 0, stone = 0;
            for (int i = 1; i < world.Entities.Count; i++)
            {
                if (!world.Entities.Alive[i]) continue;
                if (world.Entities.Kind[i] != EntityKind.ResourceNode) continue;
                switch (world.Entities.NodeType[i])
                {
                    case ResourceNodeType.Forest: forests++; break;
                    case ResourceNodeType.FishShoal: fish++; break;
                    case ResourceNodeType.StoneOutcrop: stone++; break;
                }
            }
            Assert.True(forests >= 40, $"only {forests} forest nodes");
            Assert.Equal(8, fish);
            Assert.Equal(4, stone);
        }

        [Fact]
        public void EmptyWorldTicksWithoutError()
        {
            SimWorld world = SimFixture.NewMatch();
            world.Step(600);
            Assert.Equal(600u, world.Tick);
            Assert.False(world.MatchOver);
        }
    }
}
