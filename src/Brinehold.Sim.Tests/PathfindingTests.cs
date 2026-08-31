using Brinehold.Sim.Map;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Sim.Tests
{
    public class PathfindingTests
    {
        private static (SimWorld world, PathFinder finder, int[] buffer) Setup()
        {
            SimWorld world = SimFixture.NewMatch();
            return (world, new PathFinder(world.Nav), new int[SimConstants.MaxPathLength]);
        }

        [Fact]
        public void ALandPathExistsBetweenTheTwoStartingAreas()
        {
            var (world, finder, buffer) = Setup();
            int start = world.Nav.Index(PrototypeMap.StartCellX[0] + 8, PrototypeMap.StartCellY[0]);
            int goal = world.Nav.Index(PrototypeMap.StartCellX[1] - 8, PrototypeMap.StartCellY[1]);

            int length = finder.FindPath(start, goal, MovementDomain.Land, buffer);
            Assert.True(length > 0, "no land route between the two bases");
        }

        [Fact]
        public void PathsNeverCrossBlockedTerrain()
        {
            var (world, finder, buffer) = Setup();
            int start = world.Nav.Index(PrototypeMap.StartCellX[0] + 8, PrototypeMap.StartCellY[0]);
            int goal = world.Nav.Index(PrototypeMap.StartCellX[1] - 8, PrototypeMap.StartCellY[1]);

            int length = finder.FindPath(start, goal, MovementDomain.Land, buffer);
            for (int i = 0; i < length; i++)
                Assert.NotEqual(TerrainType.Blocked, world.Nav.TerrainAt(buffer[i]));
        }

        [Fact]
        public void ALandUnitCannotPathIntoOpenWater()
        {
            var (world, finder, buffer) = Setup();
            int start = world.Nav.Index(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] + 8);
            int goal = world.Nav.Index(80, 4);   // well out to sea

            int length = finder.FindPath(start, goal, MovementDomain.Land, buffer);
            for (int i = 0; i < length; i++)
                Assert.NotEqual(TerrainType.Water, world.Nav.TerrainAt(buffer[i]));
        }

        [Fact]
        public void AShipPathsAlongTheCoastBetweenBothBases()
        {
            var (world, finder, buffer) = Setup();
            int start = world.Nav.Index(PrototypeMap.StartCellX[0], 8);
            int goal = world.Nav.Index(PrototypeMap.StartCellX[1], 8);

            int length = finder.FindPath(start, goal, MovementDomain.Water, buffer);
            Assert.True(length > 0, "no sea route between the two coasts");
            for (int i = 0; i < length; i++)
                Assert.Equal(TerrainType.Water, world.Nav.TerrainAt(buffer[i]));
        }

        [Fact]
        public void PathsAreIdenticalOnRepeatedSearches()
        {
            var (world, finder, buffer) = Setup();
            var second = new int[SimConstants.MaxPathLength];
            int start = world.Nav.Index(PrototypeMap.StartCellX[0] + 8, PrototypeMap.StartCellY[0]);
            int goal = world.Nav.Index(PrototypeMap.StartCellX[0] + 30, PrototypeMap.StartCellY[0] + 30);

            int a = finder.FindPath(start, goal, MovementDomain.Land, buffer);
            int b = finder.FindPath(start, goal, MovementDomain.Land, second);

            Assert.Equal(a, b);
            for (int i = 0; i < a; i++) Assert.Equal(buffer[i], second[i]);
        }

        [Fact]
        public void TwoIndependentPathfindersAgree()
        {
            var (world, finder, buffer) = Setup();
            var other = new PathFinder(world.Nav);
            var second = new int[SimConstants.MaxPathLength];

            int start = world.Nav.Index(PrototypeMap.StartCellX[0] + 8, PrototypeMap.StartCellY[0]);
            int goal = world.Nav.Index(PrototypeMap.StartCellX[1] - 8, PrototypeMap.StartCellY[1]);

            int a = finder.FindPath(start, goal, MovementDomain.Land, buffer);
            int b = other.FindPath(start, goal, MovementDomain.Land, second);

            Assert.Equal(a, b);
            for (int i = 0; i < a; i++) Assert.Equal(buffer[i], second[i]);
        }

        [Fact]
        public void AnUnreachableGoalReturnsNoPathRatherThanHanging()
        {
            SimWorld world = SimFixture.NewMatch();
            // Wall a single cell in completely.
            int x = 60, y = 60;
            world.Nav.SetTerrain(x + 1, y, TerrainType.Blocked);
            world.Nav.SetTerrain(x - 1, y, TerrainType.Blocked);
            world.Nav.SetTerrain(x, y + 1, TerrainType.Blocked);
            world.Nav.SetTerrain(x, y - 1, TerrainType.Blocked);
            world.Nav.SetTerrain(x + 1, y + 1, TerrainType.Blocked);
            world.Nav.SetTerrain(x - 1, y - 1, TerrainType.Blocked);
            world.Nav.SetTerrain(x + 1, y - 1, TerrainType.Blocked);
            world.Nav.SetTerrain(x - 1, y + 1, TerrainType.Blocked);

            var finder = new PathFinder(world.Nav);
            var buffer = new int[SimConstants.MaxPathLength];
            int start = world.Nav.Index(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] + 6);

            int length = finder.FindPath(start, world.Nav.Index(x, y), MovementDomain.Land, buffer);
            // Either no path, or a path that stops at the nearest reachable cell — never inside the pocket.
            if (length > 0) Assert.NotEqual(world.Nav.Index(x, y), buffer[length - 1]);
        }

        [Fact]
        public void TheNodeBudgetIsRespected()
        {
            var (world, finder, buffer) = Setup();
            finder.NodeBudget = 500;
            int start = world.Nav.Index(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] + 8);
            int goal = world.Nav.Index(PrototypeMap.StartCellX[1], PrototypeMap.StartCellY[1] + 40);

            finder.FindPath(start, goal, MovementDomain.Land, buffer);
            Assert.True(finder.LastExpandedNodes <= 501, $"expanded {finder.LastExpandedNodes} nodes against a budget of 500");
        }
    }
}
