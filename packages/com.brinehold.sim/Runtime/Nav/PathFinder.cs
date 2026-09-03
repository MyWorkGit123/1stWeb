using Brinehold.Sim.World;

namespace Brinehold.Sim.Nav
{
    /// <summary>
    /// Deterministic A* over the navigation grid.
    ///
    /// Costs are integers (10 orthogonal, 14 diagonal) so there is no floating-point tie-breaking,
    /// and the open set breaks ties on cell index, so two machines expand nodes in the same order
    /// and produce the same path. The full game layers hierarchical planning and flow fields on top
    /// of this (TECHNICAL_ARCHITECTURE.md section 5.2); this is the exact single-unit fallback that
    /// those layers defer to, so it stays in the codebase rather than being replaced.
    /// </summary>
    public sealed class PathFinder
    {
        private const int CostOrthogonal = 10;
        private const int CostDiagonal = 14;

        private readonly NavGrid _grid;
        private readonly int[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _visitStamp;
        private readonly BinaryHeap _open;
        private int _stamp;

        /// <summary>Nodes expanded by the last search. Exposed so tests can assert the budget is respected.</summary>
        public int LastExpandedNodes { get; private set; }

        /// <summary>Hard ceiling on nodes expanded per request, so one bad order cannot stall a tick.</summary>
        public int NodeBudget = 8000;

        public PathFinder(NavGrid grid)
        {
            _grid = grid;
            int n = grid.CellCount;
            _gScore = new int[n];
            _cameFrom = new int[n];
            _visitStamp = new int[n];
            _open = new BinaryHeap(n);
        }

        /// <summary>
        /// Finds a path from <paramref name="start"/> to <paramref name="goal"/>, writing cell
        /// indices into <paramref name="output"/> excluding the start cell.
        /// Returns the number of waypoints written, or 0 if no path exists.
        /// </summary>
        public int FindPath(int start, int goal, MovementDomain domain, int[] output)
        {
            LastExpandedNodes = 0;
            if (start == goal) return 0;
            if (!_grid.IsPassable(goal, domain))
            {
                int adjusted = _grid.NearestPassable(goal, domain);
                if (adjusted < 0) return 0;
                goal = adjusted;
                if (start == goal) return 0;
            }

            _stamp++;
            _open.Clear();
            _gScore[start] = 0;
            _cameFrom[start] = -1;
            _visitStamp[start] = _stamp;
            _open.Push(Heuristic(start, goal), start);

            bool found = false;
            while (_open.Count > 0)
            {
                int current = _open.Pop();
                if (current == goal) { found = true; break; }

                if (++LastExpandedNodes > NodeBudget) break;

                int cx = _grid.CellX(current);
                int cy = _grid.CellY(current);
                int currentG = _gScore[current];

                for (int dir = 0; dir < 8; dir++)
                {
                    int dx = DirX[dir];
                    int dy = DirY[dir];
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!_grid.InBounds(nx, ny)) continue;

                    int neighbour = _grid.Index(nx, ny);
                    if (!_grid.IsPassable(neighbour, domain)) continue;

                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal)
                    {
                        // No corner cutting: both orthogonal neighbours must be open.
                        if (!_grid.IsPassable(_grid.Index(cx + dx, cy), domain)) continue;
                        if (!_grid.IsPassable(_grid.Index(cx, cy + dy), domain)) continue;
                    }

                    int tentative = currentG + (diagonal ? CostDiagonal : CostOrthogonal);
                    if (_visitStamp[neighbour] == _stamp && tentative >= _gScore[neighbour]) continue;

                    _visitStamp[neighbour] = _stamp;
                    _gScore[neighbour] = tentative;
                    _cameFrom[neighbour] = current;
                    _open.Push(tentative + Heuristic(neighbour, goal), neighbour);
                }
            }

            if (!found) return 0;

            // Walk the parent chain back, then reverse in place.
            int length = 0;
            int node = goal;
            while (node != start && node >= 0)
            {
                if (length >= output.Length) return 0;   // path longer than the buffer: treat as unreachable
                output[length++] = node;
                node = _cameFrom[node];
            }
            if (node != start) return 0;

            for (int i = 0; i < length / 2; i++)
            {
                int tmp = output[i];
                output[i] = output[length - 1 - i];
                output[length - 1 - i] = tmp;
            }
            return length;
        }

        /// <summary>Octile distance, scaled to match the integer step costs.</summary>
        private int Heuristic(int a, int b)
        {
            int ax = _grid.CellX(a), ay = _grid.CellY(a);
            int bx = _grid.CellX(b), by = _grid.CellY(b);
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            int min = dx < dy ? dx : dy;
            int max = dx < dy ? dy : dx;
            return CostOrthogonal * (max - min) + CostDiagonal * min;
        }

        private static readonly int[] DirX = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] DirY = { -1, 0, 1, 0, -1, 1, 1, -1 };

        /// <summary>
        /// Min-heap keyed on f-score, breaking ties on cell index so that the expansion order is
        /// fully determined by the grid rather than by insertion history.
        /// </summary>
        private sealed class BinaryHeap
        {
            private readonly int[] _priority;
            private readonly int[] _value;
            private int _count;

            public BinaryHeap(int capacity)
            {
                _priority = new int[capacity + 1];
                _value = new int[capacity + 1];
            }

            public int Count => _count;

            public void Clear() => _count = 0;

            public void Push(int priority, int value)
            {
                if (_count >= _priority.Length) return;
                int i = _count++;
                _priority[i] = priority;
                _value[i] = value;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (Compare(i, parent) >= 0) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop()
            {
                int result = _value[0];
                _count--;
                if (_count > 0)
                {
                    _priority[0] = _priority[_count];
                    _value[0] = _value[_count];
                    int i = 0;
                    while (true)
                    {
                        int left = 2 * i + 1;
                        int right = left + 1;
                        int smallest = i;
                        if (left < _count && Compare(left, smallest) < 0) smallest = left;
                        if (right < _count && Compare(right, smallest) < 0) smallest = right;
                        if (smallest == i) break;
                        Swap(i, smallest);
                        i = smallest;
                    }
                }
                return result;
            }

            private int Compare(int a, int b)
            {
                if (_priority[a] != _priority[b]) return _priority[a] < _priority[b] ? -1 : 1;
                if (_value[a] != _value[b]) return _value[a] < _value[b] ? -1 : 1;
                return 0;
            }

            private void Swap(int a, int b)
            {
                int p = _priority[a]; _priority[a] = _priority[b]; _priority[b] = p;
                int v = _value[a]; _value[a] = _value[b]; _value[b] = v;
            }
        }
    }
}
