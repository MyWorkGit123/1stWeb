using Brinehold.Core.Math;
using Brinehold.Sim.Content;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Client.Placement
{
    /// <summary>
    /// The build ghost: where it sits and whether it is legal.
    ///
    /// The client runs the same placement rules the server does, so the ghost turns red before the
    /// player clicks rather than after the server refuses. The answer is advisory — the world can
    /// change while the command is in flight, and the server's verdict is the only one that counts —
    /// but being right almost always is what makes placement feel responsive.
    /// </summary>
    public sealed class PlacementPreview
    {
        private readonly NavGrid _nav;

        public bool Active { get; private set; }
        public BuildingType Type { get; private set; }
        public int CellX { get; private set; }
        public int CellY { get; private set; }
        public bool Legal { get; private set; }
        public string? Reason { get; private set; }

        public PlacementPreview(NavGrid nav) => _nav = nav;

        public void Begin(BuildingType type)
        {
            Active = true;
            Type = type;
            Legal = false;
            Reason = null;
        }

        public void Cancel()
        {
            Active = false;
            Reason = null;
        }

        /// <summary>Snaps the ghost to the cell under the cursor and re-evaluates legality.</summary>
        public void MoveTo(Fix2 worldPoint)
        {
            if (!Active) return;

            int cell = _nav.CellAt(worldPoint);
            CellX = _nav.CellX(cell);
            CellY = _nav.CellY(cell);
            Legal = Evaluate(out string? reason);
            Reason = reason;
        }

        /// <summary>
        /// Mirrors BuildPlacement on the server, minus the checks that need information the client
        /// does not have. Where the client cannot see, it errs toward allowing the click and letting
        /// the server refuse, rather than blocking a placement that is actually fine.
        /// </summary>
        private bool Evaluate(out string? reason)
        {
            PrototypeContent.BuildingStats stats = PrototypeContent.ForBuilding(Type);
            int half = stats.FootprintHalf;

            if (!_nav.InBounds(CellX - half, CellY - half) || !_nav.InBounds(CellX + half, CellY + half))
            {
                reason = "Outside the map";
                return false;
            }

            for (int y = CellY - half; y <= CellY + half; y++)
            for (int x = CellX - half; x <= CellX + half; x++)
            {
                int cell = _nav.Index(x, y);
                TerrainType terrain = _nav.TerrainAt(cell);
                if (terrain == TerrainType.Water) { reason = "Cannot build on water"; return false; }
                if (terrain == TerrainType.Blocked) { reason = "Cannot build on rock"; return false; }
                if (_nav.IsOccupied(cell)) { reason = "Something is already there"; return false; }
            }

            if (stats.RequiresWaterAdjacency && !_nav.IsAdjacentToWater(CellX, CellY, half))
            {
                reason = "Must be built on the shore";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
