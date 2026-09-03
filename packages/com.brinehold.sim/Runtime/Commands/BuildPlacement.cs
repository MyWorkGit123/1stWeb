using Brinehold.Sim.Content;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Commands
{
    /// <summary>
    /// Placement legality for buildings.
    ///
    /// The client runs this too, so it can grey out an illegal ghost, but the client's answer is
    /// advisory. The server runs it again at execution time and its answer is the only one that
    /// counts — the two can legitimately disagree, because the world may have changed in the time
    /// the command spent on the wire.
    /// </summary>
    public static class BuildPlacement
    {
        public static bool IsLegal(SimWorld world, BuildingType type, int cellX, int cellY, out RejectReason reason)
        {
            ContentDatabase.BuildingStats stats = world.Content.Building(type);
            int half = stats.FootprintHalf;
            NavGrid nav = world.Nav;

            if (!nav.InBounds(cellX - half, cellY - half) || !nav.InBounds(cellX + half, cellY + half))
            {
                reason = RejectReason.OutOfBounds;
                return false;
            }

            for (int y = cellY - half; y <= cellY + half; y++)
            for (int x = cellX - half; x <= cellX + half; x++)
            {
                int cell = nav.Index(x, y);
                if (nav.TerrainAt(cell) != TerrainType.Land || nav.IsOccupied(cell))
                {
                    reason = RejectReason.IllegalPlacement;
                    return false;
                }
            }

            if (stats.RequiresWaterAdjacency && !nav.IsAdjacentToWater(cellX, cellY, half))
            {
                reason = RejectReason.IllegalPlacement;
                return false;
            }

            // A footprint may not be dropped on top of a unit or a resource node.
            EntityStore store = world.Entities;
            int count = store.Count;
            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.Kind[i] == EntityKind.Building) continue;   // already covered by occupancy
                int ex = store.Position[i].X.ToInt();
                int ey = store.Position[i].Y.ToInt();
                if (ex < cellX - half || ex > cellX + half) continue;
                if (ey < cellY - half || ey > cellY + half) continue;
                if (store.Kind[i] == EntityKind.ResourceNode)
                {
                    reason = RejectReason.IllegalPlacement;
                    return false;
                }
            }

            reason = RejectReason.None;
            return true;
        }
    }
}
