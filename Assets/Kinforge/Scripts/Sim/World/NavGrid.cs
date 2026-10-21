using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// 1 m occupancy grid shared by pathfinding, line of sight and projectile blocking.
    /// Static props and Warwright ramparts stamp cells in and out at runtime, which is the
    /// "fast local navmesh rebuild" the design doc asks for.
    /// </summary>
    public sealed class NavGrid
    {
        public readonly int Size;
        private readonly byte[] _move;
        private readonly byte[] _sight;
        private readonly int[] _owner;
        public int Version { get; private set; }

        // A* scratch
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private int _searchId;
        private readonly MinHeap _open;
        private readonly List<int> _cellScratch = new List<int>(256);

        public NavGrid(int size)
        {
            Size = size;
            int n = size * size;
            _move = new byte[n];
            _sight = new byte[n];
            _owner = new int[n];
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
            _open = new MinHeap(4096);
        }

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Size && z < Size;
        public int Index(int x, int z) => z * Size + x;
        public static int CellX(float x) => (int)MathF.Floor(x);

        public bool BlocksMove(int x, int z) => !InBounds(x, z) || _move[Index(x, z)] > 0;
        public bool BlocksSight(int x, int z) => InBounds(x, z) && _sight[Index(x, z)] > 0;
        public bool BlocksMoveAt(Vec2 p) => BlocksMove(CellX(p.X), CellX(p.Z));
        public bool BlocksSightAt(Vec2 p) => BlocksSight(CellX(p.X), CellX(p.Z));
        public int OwnerAt(int x, int z) => InBounds(x, z) ? _owner[Index(x, z)] : 0;
        public int OwnerOfCell(int index) => _owner[index];
        public bool CellBlocksMove(int index) => _move[index] > 0;

        public Vec2 CellCenter(int index) => new Vec2(index % Size + 0.5f, index / Size + 0.5f);

        /// <summary>Adds (delta = +1) or removes (delta = -1) an occupancy stamp.</summary>
        public void Stamp(List<int> cells, bool move, bool sight, int owner, int delta)
        {
            foreach (int c in cells)
            {
                if (move) _move[c] = (byte)Math.Max(0, Math.Min(255, _move[c] + delta));
                if (sight) _sight[c] = (byte)Math.Max(0, Math.Min(255, _sight[c] + delta));
                if (owner != 0)
                {
                    if (delta > 0) _owner[c] = owner;
                    else if (_owner[c] == owner) _owner[c] = 0;
                }
            }
            Version++;
        }

        public List<int> RasterCircle(Vec2 c, float r)
        {
            var cells = new List<int>();
            int x0 = CellX(c.X - r), x1 = CellX(c.X + r);
            int z0 = CellX(c.Z - r), z1 = CellX(c.Z + r);
            float rsq = r * r;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!InBounds(x, z)) continue;
                float dx = x + 0.5f - c.X;
                float dz = z + 0.5f - c.Z;
                if (dx * dx + dz * dz <= rsq) cells.Add(Index(x, z));
            }
            return cells;
        }

        /// <summary>Oriented box: dir is the long axis, halfLen along it, halfWidth across it.</summary>
        public List<int> RasterBox(Vec2 c, Vec2 dir, float halfLen, float halfWidth)
        {
            var cells = new List<int>();
            dir = dir.Normalized;
            Vec2 right = dir.Right;
            float ext = halfLen + halfWidth + 1f;
            int x0 = CellX(c.X - ext), x1 = CellX(c.X + ext);
            int z0 = CellX(c.Z - ext), z1 = CellX(c.Z + ext);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!InBounds(x, z)) continue;
                Vec2 d = new Vec2(x + 0.5f, z + 0.5f) - c;
                if (MathF.Abs(Vec2.Dot(d, dir)) <= halfLen && MathF.Abs(Vec2.Dot(d, right)) <= halfWidth)
                    cells.Add(Index(x, z));
            }
            return cells;
        }

        /// <summary>Walks the cells a segment passes through; returns the first blocked cell index or -1.</summary>
        public int FirstBlockedCell(Vec2 a, Vec2 b, bool sight)
        {
            int x = CellX(a.X), z = CellX(a.Z);
            int ex = CellX(b.X), ez = CellX(b.Z);
            float dx = b.X - a.X, dz = b.Z - a.Z;
            int stepX = dx > 0 ? 1 : -1;
            int stepZ = dz > 0 ? 1 : -1;
            float tDeltaX = dx != 0 ? MathF.Abs(1f / dx) : float.MaxValue;
            float tDeltaZ = dz != 0 ? MathF.Abs(1f / dz) : float.MaxValue;
            float tMaxX = dx != 0 ? ((stepX > 0 ? (x + 1 - a.X) : (a.X - x)) * tDeltaX) : float.MaxValue;
            float tMaxZ = dz != 0 ? ((stepZ > 0 ? (z + 1 - a.Z) : (a.Z - z)) * tDeltaZ) : float.MaxValue;
            int guard = 0;
            while (true)
            {
                if (sight ? BlocksSight(x, z) : BlocksMove(x, z)) return InBounds(x, z) ? Index(x, z) : -2;
                if (x == ex && z == ez) return -1;
                if (++guard > 2048) return -1;
                if (tMaxX < tMaxZ)
                {
                    if (tMaxX > 1f) return -1;
                    tMaxX += tDeltaX;
                    x += stepX;
                }
                else
                {
                    if (tMaxZ > 1f) return -1;
                    tMaxZ += tDeltaZ;
                    z += stepZ;
                }
            }
        }

        public bool SightClear(Vec2 a, Vec2 b) => FirstBlockedCell(a, b, true) == -1;

        /// <summary>
        /// True if a body of the given radius can travel straight from a to b. Wide checks the full
        /// radius plus a middle pair of lines, for bodies bigger than a grid cell (a grown Titan).
        /// </summary>
        public bool LineWalkable(Vec2 a, Vec2 b, float radius, bool wide = false)
        {
            if (FirstBlockedCell(a, b, false) != -1) return false;
            Vec2 d = (b - a).Normalized;
            if (d == Vec2.Zero) return true;
            Vec2 off = d.Right * (radius * (wide ? 1f : 0.8f));
            if (FirstBlockedCell(a + off, b + off, false) != -1 || FirstBlockedCell(a - off, b - off, false) != -1) return false;
            if (!wide) return true;
            Vec2 mid = d.Right * (radius * 0.5f);
            return FirstBlockedCell(a + mid, b + mid, false) == -1 && FirstBlockedCell(a - mid, b - mid, false) == -1;
        }

        private bool _clearance;

        private bool NearBlocked(int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (BlocksMove(x + dx, z + dz)) return true;
            }
            return false;
        }

        /// <summary>Path for a big body: keeps a cell clear of walls where it can, else a normal path.</summary>
        public bool FindPathWide(Vec2 start, Vec2 goal, List<Vec2> path, float radius)
        {
            _clearance = true;
            bool ok;
            try
            {
                ok = FindPath(start, goal, path, radius);
            }
            finally
            {
                _clearance = false;
            }
            return ok || FindPath(start, goal, path, radius);
        }

        public Vec2 NearestWalkable(Vec2 p, int maxRadius = 8)
        {
            int cx = CellX(p.X), cz = CellX(p.Z);
            if (!BlocksMove(cx, cz)) return p;
            float best = float.MaxValue;
            Vec2 bestP = p;
            for (int r = 1; r <= maxRadius; r++)
            {
                for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (Math.Abs(x - cx) != r && Math.Abs(z - cz) != r) continue;
                    if (BlocksMove(x, z)) continue;
                    Vec2 c = new Vec2(x + 0.5f, z + 0.5f);
                    float d = Vec2.DistanceSq(c, p);
                    if (d < best)
                    {
                        best = d;
                        bestP = c;
                    }
                }
                if (best < float.MaxValue) return bestP;
            }
            return p;
        }

        /// <summary>
        /// 8-way A* with no corner cutting, then string-pulled. Returns false when no route exists
        /// (the path then holds the closest reachable approach, if any).
        /// </summary>
        public bool FindPath(Vec2 start, Vec2 goal, List<Vec2> path, float radius, int maxExpand = 24000)
        {
            path.Clear();
            Vec2 s = NearestWalkable(start, 4);
            Vec2 g = NearestWalkable(goal, 10);
            int sx = CellX(s.X), sz = CellX(s.Z);
            int gx = CellX(g.X), gz = CellX(g.Z);
            if (BlocksMove(sx, sz) || BlocksMove(gx, gz)) return false;

            int startIdx = Index(sx, sz);
            int goalIdx = Index(gx, gz);
            if (startIdx == goalIdx)
            {
                path.Add(g);
                return true;
            }

            _searchId++;
            _open.Clear();
            _stamp[startIdx] = _searchId;
            _closed[startIdx] = false;
            _g[startIdx] = 0f;
            _parent[startIdx] = -1;
            _open.Push(startIdx, Octile(sx, sz, gx, gz));

            int bestIdx = startIdx;
            float bestH = Octile(sx, sz, gx, gz);
            int expanded = 0;
            bool found = false;

            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (_closed[cur]) continue;
                _closed[cur] = true;
                if (cur == goalIdx)
                {
                    found = true;
                    break;
                }
                if (++expanded > maxExpand) break;

                int cx = cur % Size, cz = cur / Size;
                float h = Octile(cx, cz, gx, gz);
                if (h < bestH)
                {
                    bestH = h;
                    bestIdx = cur;
                }

                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (BlocksMove(nx, nz)) continue;
                    if (dx != 0 && dz != 0 && (BlocksMove(cx + dx, cz) || BlocksMove(cx, cz + dz))) continue;
                    int ni = Index(nx, nz);
                    if (_clearance && ni != goalIdx && NearBlocked(nx, nz)) continue;
                    float step = (dx != 0 && dz != 0) ? 1.41421f : 1f;
                    float ng = _g[cur] + step;
                    if (_stamp[ni] != _searchId)
                    {
                        _stamp[ni] = _searchId;
                        _closed[ni] = false;
                        _g[ni] = float.MaxValue;
                    }
                    if (_closed[ni] || ng >= _g[ni]) continue;
                    _g[ni] = ng;
                    _parent[ni] = cur;
                    _open.Push(ni, ng + Octile(nx, nz, gx, gz));
                }
            }

            int end = found ? goalIdx : bestIdx;
            if (end == startIdx) return false;

            _cellScratch.Clear();
            for (int c = end; c != -1; c = _parent[c]) _cellScratch.Add(c);
            _cellScratch.Reverse();

            // String-pull: from the current anchor, keep the farthest cell still reachable in a straight line.
            Vec2 anchor = s;
            int i = 0;
            while (i < _cellScratch.Count)
            {
                int far = i;
                int last = _cellScratch.Count - 1;
                if (last > i && LineWalkable(anchor, CellCenter(_cellScratch[last]), radius, _clearance))
                {
                    far = last;
                }
                else
                {
                    // Bounded look-ahead keeps smoothing linear on long winding routes.
                    for (int j = Math.Min(last - 1, i + 40); j > i; j--)
                    {
                        if (LineWalkable(anchor, CellCenter(_cellScratch[j]), radius, _clearance))
                        {
                            far = j;
                            break;
                        }
                    }
                }
                Vec2 p = CellCenter(_cellScratch[far]);
                path.Add(p);
                anchor = p;
                i = far + 1;
            }
            if (found && path.Count > 0) path[path.Count - 1] = g;
            return found;
        }

        private static float Octile(int x0, int z0, int x1, int z1)
        {
            int dx = Math.Abs(x1 - x0);
            int dz = Math.Abs(z1 - z0);
            return dx > dz ? dx + 0.41421f * dz : dz + 0.41421f * dx;
        }

        private sealed class MinHeap
        {
            private int[] _items;
            private float[] _keys;
            public int Count;

            public MinHeap(int capacity)
            {
                _items = new int[capacity];
                _keys = new float[capacity];
            }

            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == _items.Length)
                {
                    Array.Resize(ref _items, Count * 2);
                    Array.Resize(ref _keys, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_keys[p] <= key) break;
                    _items[i] = _items[p];
                    _keys[i] = _keys[p];
                    i = p;
                }
                _items[i] = item;
                _keys[i] = key;
            }

            public int Pop()
            {
                int top = _items[0];
                Count--;
                if (Count > 0)
                {
                    int item = _items[Count];
                    float key = _keys[Count];
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1;
                        if (l >= Count) break;
                        int r = l + 1;
                        int m = (r < Count && _keys[r] < _keys[l]) ? r : l;
                        if (_keys[m] >= key) break;
                        _items[i] = _items[m];
                        _keys[i] = _keys[m];
                        i = m;
                    }
                    _items[i] = item;
                    _keys[i] = key;
                }
                return top;
            }
        }
    }
}
