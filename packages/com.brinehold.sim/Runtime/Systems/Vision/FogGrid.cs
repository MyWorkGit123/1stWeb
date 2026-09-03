using Brinehold.Core.Math;
using Brinehold.Sim.Nav;

namespace Brinehold.Sim.Vision
{
    /// <summary>
    /// Per-player visibility and exploration.
    ///
    /// This is the structure the replication layer consults before sending anything about an entity.
    /// Fog is not a rendering filter in Brinehold: if a cell is not visible to a player, no data
    /// about what stands on it leaves the server. That is what makes map hacks structurally
    /// impossible rather than merely detectable (MULTIPLAYER_ARCHITECTURE.md section 5.3).
    /// </summary>
    public sealed class FogGrid
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _playerCount;

        /// <summary>Currently in someone's vision. Indexed [player * cellCount + cell].</summary>
        private readonly bool[] _visible;
        /// <summary>Has ever been seen. Drives the greyed "last known" terrain and buildings.</summary>
        private readonly bool[] _explored;

        public FogGrid(int width, int height, int playerCount)
        {
            _width = width;
            _height = height;
            _playerCount = playerCount;
            _visible = new bool[width * height * playerCount];
            _explored = new bool[width * height * playerCount];
        }

        public int CellCount => _width * _height;

        public void ClearVisible() => System.Array.Clear(_visible, 0, _visible.Length);

        public bool IsVisible(int player, int cell)
        {
            if (player < 0 || player >= _playerCount || cell < 0 || cell >= CellCount) return false;
            return _visible[player * CellCount + cell];
        }

        public bool IsExplored(int player, int cell)
        {
            if (player < 0 || player >= _playerCount || cell < 0 || cell >= CellCount) return false;
            return _explored[player * CellCount + cell];
        }

        public void Reveal(int player, int cell)
        {
            if (player < 0 || player >= _playerCount || cell < 0 || cell >= CellCount) return;
            int i = player * CellCount + cell;
            _visible[i] = true;
            _explored[i] = true;
        }

        /// <summary>
        /// Marks a filled circle of cells visible. Uses squared integer comparison so the shape is
        /// identical on every machine.
        /// </summary>
        public void RevealCircle(int player, NavGrid grid, Fix2 centre, Fix64 radius)
        {
            int r = radius.ToInt();
            if (r <= 0) return;
            int cx = centre.X.ToInt();
            int cy = centre.Y.ToInt();
            int rSquared = r * r;

            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= _height) continue;
                int dy = y - cy;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= _width) continue;
                    int dx = x - cx;
                    if (dx * dx + dy * dy > rSquared) continue;
                    Reveal(player, y * _width + x);
                }
            }
        }

        public int VisibleCellCount(int player)
        {
            int count = 0;
            int offset = player * CellCount;
            for (int i = 0; i < CellCount; i++) if (_visible[offset + i]) count++;
            return count;
        }

        public int ExploredCellCount(int player)
        {
            int count = 0;
            int offset = player * CellCount;
            for (int i = 0; i < CellCount; i++) if (_explored[offset + i]) count++;
            return count;
        }
    }
}
