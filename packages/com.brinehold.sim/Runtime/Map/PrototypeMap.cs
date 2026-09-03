using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Map
{
    /// <summary>
    /// "Twin Coves" — the single handcrafted map the M3 prototype ships with.
    ///
    /// It is built by deterministic code rather than loaded from a file so that the prototype has no
    /// binary asset dependency; M4 replaces this with a compiled map binary produced by
    /// Brinehold.Tools.MapCompiler. The layout is deliberately shaped to exercise the systems the
    /// prototype exists to prove:
    ///
    ///   - a southern sea, so docks, ships and fishing are reachable from both bases;
    ///   - a central rock ridge with one gap, so pathfinding has a real chokepoint to solve and an
    ///     alternative northern route to compare against;
    ///   - mirrored resources, so neither player has an economic advantage to confound a test.
    /// </summary>
    public static class PrototypeMap
    {
        public const int Width = 160;
        public const int Height = 160;

        /// <summary>Everything with y below this is open water.</summary>
        public const int SeaLine = 22;

        private const int RidgeMinX = 78;
        private const int RidgeMaxX = 82;
        private const int RidgeMinY = SeaLine;
        private const int RidgeMaxY = 120;
        private const int RidgeGapMinY = 66;
        private const int RidgeGapMaxY = 74;

        public static readonly int[] StartCellX = { 36, 124 };
        public static readonly int[] StartCellY = { 44, 44 };

        public static void Build(SimWorld world)
        {
            BuildTerrain(world);
            BuildResources(world);
            BuildStartingSettlements(world);

            // Prime the fog so that the first replication pass already reflects starting vision.
            new Systems.VisionSystem().Execute(world);
        }

        private static void BuildTerrain(SimWorld world)
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                TerrainType terrain = y < SeaLine ? TerrainType.Water : TerrainType.Land;

                bool inRidgeColumn = x >= RidgeMinX && x <= RidgeMaxX;
                bool inRidgeRow = y >= RidgeMinY && y <= RidgeMaxY;
                bool inGap = y >= RidgeGapMinY && y <= RidgeGapMaxY;
                if (inRidgeColumn && inRidgeRow && !inGap) terrain = TerrainType.Blocked;

                world.Nav.SetTerrain(x, y, terrain);
            }
        }

        private static void BuildResources(SimWorld world)
        {
            for (int player = 0; player < 2; player++)
            {
                int bx = StartCellX[player];
                int by = StartCellY[player];
                int mirror = player == 0 ? 1 : -1;

                // Two forest stands: one close for the opening, one further out that has to be
                // walked to, so haul distance is visible from the first minute.
                PlaceForestStand(world, bx + 16 * mirror, by + 10, 5, 4);
                PlaceForestStand(world, bx + 6 * mirror, by + 26, 4, 4);

                // Stone within reach of the base.
                world.SpawnResourceNode(ResourceNodeType.StoneOutcrop, bx - 14 * mirror, by + 6);
                world.SpawnResourceNode(ResourceNodeType.StoneOutcrop, bx - 12 * mirror, by + 9);

                // Fish just off the beach, so a fishing wharf and a dock are both viable early.
                for (int f = 0; f < 4; f++)
                    world.SpawnResourceNode(ResourceNodeType.FishShoal, bx - 6 + f * 4, SeaLine - 4 - (f % 2) * 3);
            }

            // Contested middle: a stand in the ridge gap that both players can reach.
            PlaceForestStand(world, 80, 78, 4, 3);
        }

        private static void PlaceForestStand(SimWorld world, int centreX, int centreY, int columns, int rows)
        {
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                int x = centreX + (c - columns / 2) * 2;
                int y = centreY + (r - rows / 2) * 2;
                if (!world.Nav.InBounds(x, y)) continue;
                if (world.Nav.TerrainAt(world.Nav.Index(x, y)) != TerrainType.Land) continue;
                world.SpawnResourceNode(ResourceNodeType.Forest, x, y);
            }
        }

        private static void BuildStartingSettlements(SimWorld world)
        {
            int playerCount = System.Math.Min(world.Players.Length, StartCellX.Length);

            for (int p = 0; p < playerCount; p++)
            {
                byte player = (byte)p;
                int bx = StartCellX[p];
                int by = StartCellY[p];

                PlayerState state = world.Players[p];
                state.Wood = world.Content.StartingWood;
                state.Food = world.Content.StartingFood;
                state.Stone = world.Content.StartingStone;
                state.Coin = world.Content.StartingCoin;
                state.PopulationCap = world.Content.BasePopulationCap;

                world.SpawnBuilding(BuildingType.Warehouse, player, bx, by, completed: true);

                // Ten workers in a fixed ring around the core. Fixed order, so both machines agree
                // on which entity id belongs to which worker.
                int placed = 0;
                for (int radius = 4; radius <= 10 && placed < world.Content.StartingWorkers; radius++)
                {
                    for (int dy = -radius; dy <= radius && placed < world.Content.StartingWorkers; dy++)
                    for (int dx = -radius; dx <= radius && placed < world.Content.StartingWorkers; dx++)
                    {
                        if (System.Math.Abs(dx) != radius && System.Math.Abs(dy) != radius) continue;
                        int x = bx + dx, y = by + dy;
                        if (!world.Nav.InBounds(x, y)) continue;
                        int cell = world.Nav.Index(x, y);
                        if (!world.Nav.IsPassable(cell, MovementDomain.Land)) continue;

                        world.SpawnUnit(EntityKind.Worker, player, world.Nav.CellCentre(cell));
                        placed++;
                    }
                }
            }
        }

        /// <summary>Nearest resource node of a given type to a cell. Used by tests and by the AI stub.</summary>
        public static EntityId FindNearestNode(SimWorld world, Fix2 from, ResourceNodeType type)
        {
            EntityId best = EntityId.None;
            Fix64 bestSqr = Fix64.MaxValue;
            int count = world.Entities.Count;

            for (int i = 1; i < count; i++)
            {
                if (!world.Entities.Alive[i]) continue;
                if (world.Entities.Kind[i] != EntityKind.ResourceNode) continue;
                if (world.Entities.NodeType[i] != type) continue;

                Fix64 sqr = Fix2.SqrDistance(from, world.Entities.Position[i]);
                if (sqr < bestSqr) { bestSqr = sqr; best = world.Entities.IdOf(i); }
            }
            return best;
        }
    }
}
