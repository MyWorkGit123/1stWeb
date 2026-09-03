using Brinehold.Core.Math;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Nav
{
    /// <summary>
    /// Uniform one-metre navigation grid.
    ///
    /// The prototype uses a single elevation tier. The full game layers several tiers joined by
    /// connectors (TECHNICAL_ARCHITECTURE.md section 5.1); the tier field is present here so that
    /// the pathfinder and the wire format do not have to change shape when tiers arrive in M9.
    /// </summary>
    public sealed class NavGrid
    {
        public readonly int Width;
        public readonly int Height;

        private readonly TerrainType[] _terrain;
        /// <summary>Cells blocked by a building footprint. Separate from terrain so buildings can be removed.</summary>
        private readonly bool[] _occupied;
        private readonly byte[] _tier;

        public NavGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _terrain = new TerrainType[width * height];
            _occupied = new bool[width * height];
            _tier = new byte[width * height];
        }

        public int CellCount => Width * Height;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public int Index(int x, int y) => y * Width + x;

        public int CellX(int index) => index % Width;

        public int CellY(int index) => index / Width;

        public TerrainType TerrainAt(int index) => _terrain[index];

        public void SetTerrain(int x, int y, TerrainType type)
        {
            if (InBounds(x, y)) _terrain[Index(x, y)] = type;
        }

        public byte TierAt(int index) => _tier[index];

        public void SetTier(int x, int y, byte tier)
        {
            if (InBounds(x, y)) _tier[Index(x, y)] = tier;
        }

        public bool IsOccupied(int index) => _occupied[index];

        public void SetOccupied(int index, bool value) => _occupied[index] = value;

        /// <summary>Marks a square footprint as occupied or free.</summary>
        public void SetFootprint(int centreX, int centreY, int halfExtent, bool occupied)
        {
            for (int y = centreY - halfExtent; y <= centreY + halfExtent; y++)
            for (int x = centreX - halfExtent; x <= centreX + halfExtent; x++)
                if (InBounds(x, y)) _occupied[Index(x, y)] = occupied;
        }

        /// <summary>Can an entity of this movement domain stand in this cell?</summary>
        public bool IsPassable(int index, MovementDomain domain)
        {
            if (index < 0 || index >= _terrain.Length) return false;
            if (_occupied[index]) return false;
            TerrainType t = _terrain[index];
            if (t == TerrainType.Blocked) return false;
            return domain == MovementDomain.Water ? t == TerrainType.Water : t == TerrainType.Land;
        }

        /// <summary>Terrain-only test, ignoring buildings. Used for placement legality.</summary>
        public bool IsTerrain(int x, int y, TerrainType type)
            => InBounds(x, y) && _terrain[Index(x, y)] == type;

        /// <summary>World-space centre of a cell. Cells are one metre, so the centre is offset by a half.</summary>
        public Fix2 CellCentre(int index)
        {
            int x = index % Width;
            int y = index / Width;
            return new Fix2(Fix64.FromInt(x) + Fix64.Half, Fix64.FromInt(y) + Fix64.Half);
        }

        public int CellAt(Fix2 position)
        {
            int x = position.X.ToInt();
            int y = position.Y.ToInt();
            if (x < 0) x = 0; if (y < 0) y = 0;
            if (x >= Width) x = Width - 1;
            if (y >= Height) y = Height - 1;
            return Index(x, y);
        }

        /// <summary>True if any cell orthogonally adjacent to the footprint is water. Docks need this.</summary>
        public bool IsAdjacentToWater(int centreX, int centreY, int halfExtent)
        {
            for (int y = centreY - halfExtent - 1; y <= centreY + halfExtent + 1; y++)
            for (int x = centreX - halfExtent - 1; x <= centreX + halfExtent + 1; x++)
            {
                if (!InBounds(x, y)) continue;
                if (_terrain[Index(x, y)] == TerrainType.Water) return true;
            }
            return false;
        }

        /// <summary>
        /// Nearest cell to <paramref name="origin"/> that the given domain can stand in, searched in
        /// expanding rings. Ring order is fixed, so the result is identical on every machine.
        /// </summary>
        public int NearestPassable(int origin, MovementDomain domain, int maxRadius = 24)
        {
            if (IsPassable(origin, domain)) return origin;
            int ox = CellX(origin);
            int oy = CellY(origin);

            for (int r = 1; r <= maxRadius; r++)
            {
                // Within a ring, take the geometrically nearest cell rather than the first one the
                // scan happens to reach. A corner of the ring is 41% further away than an edge,
                // which is enough to leave a worker standing outside its own warehouse's reach.
                int best = -1;
                int bestDistanceSquared = int.MaxValue;

                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (System.Math.Abs(dx) != r && System.Math.Abs(dy) != r) continue;
                    int x = ox + dx, y = oy + dy;
                    if (!InBounds(x, y)) continue;
                    int index = Index(x, y);
                    if (!IsPassable(index, domain)) continue;

                    int distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared < bestDistanceSquared || (distanceSquared == bestDistanceSquared && index < best))
                    {
                        bestDistanceSquared = distanceSquared;
                        best = index;
                    }
                }

                if (best >= 0) return best;
            }
            return -1;
        }
    }
}
