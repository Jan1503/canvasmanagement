namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Pac-Man maze. Generates a left/right-symmetric, fully-connected, loopy (few dead-ends) maze that
///     scales from tiny LED panels up to large ones, and exposes BFS helpers used by the entity AI.
/// </summary>
public class Maze
{
    private static readonly Direction[] AllDirs = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };
    private static int _mazeSeed;
    private readonly (int x, int y) _ghostHouseCenter;
    private readonly (int x, int y)[] _ghostSpawns;

    private readonly (int x, int y) _pacmanSpawn;
    private readonly bool[,] _pellets;
    private readonly bool[,] _powerPellets;
    private readonly bool[,] _walls;

    public Maze(int pixelWidth, int pixelHeight)
    {
        // Pick a cell size that yields a grid big enough for a real maze, shrinking cells on small panels.
        CellSize = Math.Clamp(Math.Min(pixelWidth / 26, pixelHeight / 14), 5, 16);
        RecomputeGrid(pixelWidth, pixelHeight);
        while ((GridWidth < 13 || GridHeight < 11) && CellSize > 3)
        {
            CellSize--;
            RecomputeGrid(pixelWidth, pixelHeight);
        }

        GridWidth = Math.Max(GridWidth, 7);
        GridHeight = Math.Max(GridHeight, 7);

        OffsetX = (pixelWidth - GridWidth * CellSize) / 2;
        OffsetY = (pixelHeight - GridHeight * CellSize) / 2;

        _walls = new bool[GridWidth, GridHeight];
        _pellets = new bool[GridWidth, GridHeight];
        _powerPellets = new bool[GridWidth, GridHeight];

        var cx = GridWidth / 2;
        var cy = GridHeight / 2;
        _ghostHouseCenter = (cx, cy);
        _pacmanSpawn = (cx, Math.Min(GridHeight - 2, cy + Math.Max(2, GridHeight / 4)));
        _ghostSpawns = BuildGhostSpawns(cx, cy);

        GenerateMaze();
    }

    public int GridWidth { get; private set; }
    public int GridHeight { get; private set; }
    public int CellSize { get; private set; }
    public int PixelWidth => GridWidth * CellSize;
    public int PixelHeight => GridHeight * CellSize;

    // Offset to center the maze in the canvas
    public int OffsetX { get; }
    public int OffsetY { get; }

    private void RecomputeGrid(int pixelWidth, int pixelHeight)
    {
        GridWidth = pixelWidth / CellSize;
        GridHeight = pixelHeight / CellSize;
        if (GridWidth % 2 == 0) GridWidth--;
        if (GridHeight % 2 == 0) GridHeight--;
    }

    private (int x, int y)[] BuildGhostSpawns(int cx, int cy)
    {
        // All inside the ghost-house interior (cleared cols cx-3..cx+3, rows cy-1..cy+1).
        return new[]
        {
            (cx, cy), (cx - 1, cy), (cx + 1, cy), (cx, cy - 1),
            (cx - 2, cy), (cx + 2, cy), (cx - 1, cy - 1), (cx + 1, cy - 1)
        };
    }

    // ───────────────────────────────────────────────────────────────────────
    // Generation
    // ───────────────────────────────────────────────────────────────────────

    private void GenerateMaze()
    {
        _mazeSeed++;
        var random = new Random(_mazeSeed * 9176 + 1013);

        for (var x = 0; x < GridWidth; x++)
        for (var y = 0; y < GridHeight; y++)
            _walls[x, y] = true;

        CarveSymmetricMaze(random);
        CreateGhostHouse();
        EnsureSpawnsClear();
        EnsureConnectivity();
        PlacePellets(random);
    }

    /// <summary>
    ///     Carves the left half as a loopy maze (spanning tree + braiding to kill dead-ends), mirrors it to
    ///     the right for a classic symmetric look, then stitches the halves together down the centre column.
    /// </summary>
    private void CarveSymmetricMaze(Random random)
    {
        var center = GridWidth / 2;
        var leftMax = center - 1;
        if (leftMax % 2 == 0) leftMax--; // largest odd lattice column strictly left of centre
        if (leftMax < 1) leftMax = 1;

        var visited = new bool[GridWidth, GridHeight];
        var stack = new Stack<(int x, int y)>();

        var start = (x: 1, y: 1);
        _walls[start.x, start.y] = false;
        visited[start.x, start.y] = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();

            // Lattice neighbours two cells away within the left region.
            var candidates = new List<(int nx, int ny, int wx, int wy)>();
            foreach (var d in AllDirs)
            {
                var v = d.ToVector();
                int nx = x + (int)v.X * 2, ny = y + (int)v.Y * 2;
                if (nx < 1 || nx > leftMax || ny < 1 || ny > GridHeight - 2) continue;
                if (visited[nx, ny]) continue;
                candidates.Add((nx, ny, x + (int)v.X, y + (int)v.Y));
            }

            if (candidates.Count == 0)
            {
                stack.Pop();
                continue;
            }

            var (cx, cy, wx, wy) = candidates[random.Next(candidates.Count)];
            _walls[wx, wy] = false;
            _walls[cx, cy] = false;
            visited[cx, cy] = true;
            stack.Push((cx, cy));
        }

        BraidLeft(random, leftMax);
        MirrorLeftToRight(center);
        JoinHalves(random, center);
        SealBorders();
    }

    /// <summary>Removes most dead-ends in the left half by carving an extra opening, creating loops.</summary>
    private void BraidLeft(Random random, int leftMax)
    {
        for (var x = 1; x <= leftMax; x++)
        for (var y = 1; y <= GridHeight - 2; y++)
        {
            if (_walls[x, y]) continue;
            if (CountOpenNeighbours(x, y) != 1) continue; // only dead-ends
            if (random.NextDouble() > 0.85) continue; // leave a few for character

            // Open a wall neighbour that leads somewhere new (prefer creating a loop).
            var options = new List<(int nx, int ny)>();
            foreach (var d in AllDirs)
            {
                var v = d.ToVector();
                int nx = x + (int)v.X, ny = y + (int)v.Y;
                if (nx < 1 || nx > leftMax || ny < 1 || ny > GridHeight - 2) continue;
                if (_walls[nx, ny]) options.Add((nx, ny));
            }

            if (options.Count > 0)
            {
                var (ox, oy) = options[random.Next(options.Count)];
                _walls[ox, oy] = false;
            }
        }
    }

    private void MirrorLeftToRight(int center)
    {
        for (var x = 0; x < center; x++)
        for (var y = 0; y < GridHeight; y++)
            _walls[GridWidth - 1 - x, y] = _walls[x, y];
    }

    /// <summary>Opens the centre column at lattice rows so the two mirrored halves connect.</summary>
    private void JoinHalves(Random random, int center)
    {
        var openRows = new List<int>();
        for (var y = 1; y <= GridHeight - 2; y++)
            if (!_walls[center - 1, y] && !_walls[center + 1, y])
                openRows.Add(y);

        if (openRows.Count == 0) return;

        // Always join at the top-most and bottom-most candidate rows, plus a random subset between.
        var forced = new HashSet<int> { openRows[0], openRows[^1] };
        foreach (var y in openRows)
            if (forced.Contains(y) || random.NextDouble() < 0.55)
                _walls[center, y] = false;
    }

    private void SealBorders()
    {
        for (var x = 0; x < GridWidth; x++)
        {
            _walls[x, 0] = true;
            _walls[x, GridHeight - 1] = true;
        }

        for (var y = 0; y < GridHeight; y++)
        {
            _walls[0, y] = true;
            _walls[GridWidth - 1, y] = true;
        }
    }

    private void CreateGhostHouse()
    {
        var (cx, cy) = _ghostHouseCenter;

        // Clear interior.
        for (var x = cx - 3; x <= cx + 3; x++)
        for (var y = cy - 1; y <= cy + 1; y++)
            SetOpen(x, y);

        // Surrounding walls.
        for (var y = cy - 1; y <= cy + 1; y++)
        {
            SetWall(cx - 4, y);
            SetWall(cx + 4, y);
        }

        for (var x = cx - 3; x <= cx + 3; x++)
            SetWall(x, cy + 2);

        // Top wall with a 3-wide gate above centre.
        for (var x = cx - 3; x <= cx + 3; x++)
            if (x < cx - 1 || x > cx + 1)
                SetWall(x, cy - 2);

        // Clear an exit corridor straight up out of the gate.
        for (var y = cy - 3; y >= 1 && y >= cy - 5; y--)
        {
            SetOpen(cx, y);
            SetOpen(cx - 1, y);
            SetOpen(cx + 1, y);
        }
    }

    private void EnsureSpawnsClear()
    {
        var (px, py) = _pacmanSpawn;
        for (var x = px - 2; x <= px + 2; x++)
        for (var y = py - 1; y <= py + 1; y++)
            SetOpen(x, y);

        // Connect Pac-Man's spawn upward toward (but not into) the ghost house so it isn't an island.
        for (var y = py; y >= _ghostHouseCenter.y + 3; y--)
            SetOpen(px, y);

        foreach (var (gx, gy) in _ghostSpawns)
            SetOpen(gx, gy);
    }

    /// <summary>
    ///     Safety net: guarantees every open cell is reachable from Pac-Man's spawn by carving a short
    ///     L-path from any stranded region to the nearest reachable cell. Gentle (1-cell wide), unlike a
    ///     bulldozer, so the maze keeps its shape.
    /// </summary>
    private void EnsureConnectivity()
    {
        for (var guard = 0; guard < 64; guard++)
        {
            var reachable = FloodFrom(_pacmanSpawn);

            (int x, int y)? stranded = null;
            for (var x = 1; x < GridWidth - 1 && stranded == null; x++)
            for (var y = 1; y < GridHeight - 1; y++)
                if (!_walls[x, y] && !reachable[x, y])
                {
                    stranded = (x, y);
                    break;
                }

            if (stranded == null) return; // fully connected

            // Nearest reachable open cell (Manhattan), then carve an L toward it.
            var (sx, sy) = stranded.Value;
            (int x, int y) best = (_pacmanSpawn.x, _pacmanSpawn.y);
            var bestDist = int.MaxValue;
            for (var x = 1; x < GridWidth - 1; x++)
            for (var y = 1; y < GridHeight - 1; y++)
                if (reachable[x, y])
                {
                    var dist = Math.Abs(x - sx) + Math.Abs(y - sy);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = (x, y);
                    }
                }

            CarvePath(sx, sy, best.x, best.y);
        }
    }

    private void CarvePath(int x0, int y0, int x1, int y1)
    {
        var x = x0;
        var y = y0;
        while (x != x1)
        {
            x += Math.Sign(x1 - x);
            SetOpen(x, y);
        }

        while (y != y1)
        {
            y += Math.Sign(y1 - y);
            SetOpen(x, y);
        }
    }

    private bool[,] FloodFrom((int x, int y) source)
    {
        var visited = new bool[GridWidth, GridHeight];
        if (_walls[source.x, source.y]) return visited;

        var queue = new Queue<(int x, int y)>();
        queue.Enqueue(source);
        visited[source.x, source.y] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var d in AllDirs)
            {
                var v = d.ToVector();
                int nx = x + (int)v.X, ny = y + (int)v.Y;
                if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                if (_walls[nx, ny] || visited[nx, ny]) continue;
                visited[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return visited;
    }

    private void PlacePellets(Random random)
    {
        var (cx, cy) = _ghostHouseCenter;

        for (var x = 1; x < GridWidth - 1; x++)
        for (var y = 1; y < GridHeight - 1; y++)
        {
            if (_walls[x, y]) continue;
            if ((x, y) == _pacmanSpawn) continue;
            if (_ghostSpawns.Any(g => g == (x, y))) continue;

            // Skip the ghost-house interior + gate exit column.
            if (Math.Abs(x - cx) <= 4 && Math.Abs(y - cy) <= 2) continue;

            _pellets[x, y] = true;
        }

        // Power pellets near the four corners (snapped to the nearest open cell).
        PlacePowerPellet(2, 2);
        PlacePowerPellet(GridWidth - 3, 2);
        PlacePowerPellet(2, GridHeight - 3);
        PlacePowerPellet(GridWidth - 3, GridHeight - 3);
    }

    private void PlacePowerPellet(int x, int y)
    {
        for (var radius = 0; radius < Math.Max(GridWidth, GridHeight); radius++)
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            int px = x + dx, py = y + dy;
            if (px < 1 || px >= GridWidth - 1 || py < 1 || py >= GridHeight - 1) continue;
            if (_walls[px, py]) continue;
            _powerPellets[px, py] = true;
            _pellets[px, py] = false;
            return;
        }
    }

    private void SetOpen(int x, int y)
    {
        if (x >= 1 && x < GridWidth - 1 && y >= 1 && y < GridHeight - 1) _walls[x, y] = false;
    }

    private void SetWall(int x, int y)
    {
        if (x >= 0 && x < GridWidth && y >= 0 && y < GridHeight) _walls[x, y] = true;
    }

    private int CountOpenNeighbours(int x, int y)
    {
        var count = 0;
        foreach (var d in AllDirs)
        {
            var v = d.ToVector();
            if (IsOpenCell(x + (int)v.X, y + (int)v.Y)) count++;
        }

        return count;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Queries
    // ───────────────────────────────────────────────────────────────────────

    public bool IsOpenCell(int x, int y)
    {
        if (x < 0 || x >= GridWidth || y < 0 || y >= GridHeight) return false;
        return !_walls[x, y];
    }

    public bool IsWall(int x, int y)
    {
        return !IsOpenCell(x, y);
    }

    public bool HasPellet(int x, int y)
    {
        if (x < 0 || x >= GridWidth || y < 0 || y >= GridHeight) return false;
        return _pellets[x, y];
    }

    public bool HasPowerPellet(int x, int y)
    {
        if (x < 0 || x >= GridWidth || y < 0 || y >= GridHeight) return false;
        return _powerPellets[x, y];
    }

    public bool CollectPellet(int x, int y)
    {
        if (!HasPellet(x, y)) return false;
        _pellets[x, y] = false;
        return true;
    }

    public bool CollectPowerPellet(int x, int y)
    {
        if (!HasPowerPellet(x, y)) return false;
        _powerPellets[x, y] = false;
        return true;
    }

    public Vector2 GetPacmanSpawn()
    {
        return new Vector2(_pacmanSpawn.x, _pacmanSpawn.y);
    }

    public Vector2[] GetGhostSpawns()
    {
        return _ghostSpawns.Select(g => new Vector2(g.x, g.y)).ToArray();
    }

    /// <summary>True if the cell is within the ghost-house interior (so a ghost should head for the exit).</summary>
    public bool IsInsideGhostHouse(int x, int y)
    {
        var (cx, cy) = _ghostHouseCenter;
        return Math.Abs(x - cx) <= 3 && y >= cy - 1 && y <= cy + 1;
    }

    /// <summary>The cell just above the gate that ghosts steer toward to leave the house.</summary>
    public Vector2 GhostHouseExit
    {
        get
        {
            var (cx, cy) = _ghostHouseCenter;
            return new Vector2(cx, cy - 3);
        }
    }

    public int CountPellets()
    {
        return CountTrue(_pellets) + CountTrue(_powerPellets);
    }

    private static int CountTrue(bool[,] arr)
    {
        var count = 0;
        foreach (var b in arr)
            if (b)
                count++;
        return count;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Pathfinding (used by entity AI)
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Multi-source BFS distance field over open cells. Unreachable / blocked cells are -1. Cells in
    ///     <paramref name="blocked" /> are treated as walls.
    /// </summary>
    public int[,] DistanceFrom(IEnumerable<(int x, int y)> sources, bool[,]? blocked = null)
    {
        var dist = new int[GridWidth, GridHeight];
        for (var x = 0; x < GridWidth; x++)
        for (var y = 0; y < GridHeight; y++)
            dist[x, y] = -1;

        var queue = new Queue<(int x, int y)>();
        foreach (var (sx, sy) in sources)
        {
            if (sx < 0 || sx >= GridWidth || sy < 0 || sy >= GridHeight) continue;
            if (_walls[sx, sy] || dist[sx, sy] == 0) continue;
            if (blocked != null && blocked[sx, sy]) continue;
            dist[sx, sy] = 0;
            queue.Enqueue((sx, sy));
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var d in AllDirs)
            {
                var v = d.ToVector();
                int nx = x + (int)v.X, ny = y + (int)v.Y;
                if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                if (_walls[nx, ny] || dist[nx, ny] != -1) continue;
                if (blocked != null && blocked[nx, ny]) continue;
                dist[nx, ny] = dist[x, y] + 1;
                queue.Enqueue((nx, ny));
            }
        }

        return dist;
    }

    /// <summary>
    ///     BFS for the first move toward the nearest cell matching <paramref name="isTarget" />. Cells in
    ///     <paramref name="blocked" /> are avoided. Neighbour expansion favours <paramref name="preferred" />
    ///     so ties don't cause oscillation/needless reversals. Returns <see cref="Direction.None" /> if no
    ///     target is reachable.
    /// </summary>
    public Direction NextStepToNearestTarget((int x, int y) from, Func<int, int, bool> isTarget,
        bool[,]? blocked = null, Direction preferred = Direction.None)
    {
        var visited = new bool[GridWidth, GridHeight];
        var queue = new Queue<(int x, int y, Direction first)>();
        visited[from.x, from.y] = true;

        foreach (var d in OrderedDirs(preferred))
        {
            var v = d.ToVector();
            int nx = from.x + (int)v.X, ny = from.y + (int)v.Y;
            if (!IsOpenCell(nx, ny) || visited[nx, ny]) continue;
            if (blocked != null && blocked[nx, ny]) continue;
            visited[nx, ny] = true;
            if (isTarget(nx, ny)) return d;
            queue.Enqueue((nx, ny, d));
        }

        while (queue.Count > 0)
        {
            var (x, y, first) = queue.Dequeue();
            foreach (var d in AllDirs)
            {
                var v = d.ToVector();
                int nx = x + (int)v.X, ny = y + (int)v.Y;
                if (!IsOpenCell(nx, ny) || visited[nx, ny]) continue;
                if (blocked != null && blocked[nx, ny]) continue;
                visited[nx, ny] = true;
                if (isTarget(nx, ny)) return first;
                queue.Enqueue((nx, ny, first));
            }
        }

        return Direction.None;
    }

    /// <summary>
    ///     Dijkstra for the first move along the lowest-cost path to the nearest cell matching
    ///     <paramref name="isTarget" />. <paramref name="cellCost" /> is the cost to enter each cell (use a
    ///     high cost near ghosts so Pac-Man routes around danger rather than treating it as a hard wall).
    ///     Ties favour <paramref name="preferred" /> to avoid oscillation.
    /// </summary>
    public Direction LowestCostStepToTarget((int x, int y) from, Func<int, int, bool> isTarget, float[,] cellCost,
        Direction preferred = Direction.None)
    {
        var best = new float[GridWidth, GridHeight];
        var firstDir = new Direction[GridWidth, GridHeight];
        for (var x = 0; x < GridWidth; x++)
        for (var y = 0; y < GridHeight; y++)
            best[x, y] = float.MaxValue;

        var pq = new PriorityQueue<(int x, int y), float>();

        foreach (var d in OrderedDirs(preferred))
        {
            var v = d.ToVector();
            int nx = from.x + (int)v.X, ny = from.y + (int)v.Y;
            if (!IsOpenCell(nx, ny)) continue;
            var c = cellCost[nx, ny];
            if (c >= best[nx, ny]) continue;
            best[nx, ny] = c;
            firstDir[nx, ny] = d;
            pq.Enqueue((nx, ny), c);
        }

        while (pq.TryDequeue(out var cell, out var pri))
        {
            if (pri > best[cell.x, cell.y] + 1e-4f) continue; // stale heap entry
            if (isTarget(cell.x, cell.y)) return firstDir[cell.x, cell.y];

            foreach (var d in AllDirs)
            {
                var v = d.ToVector();
                int nx = cell.x + (int)v.X, ny = cell.y + (int)v.Y;
                if (!IsOpenCell(nx, ny)) continue;
                var nc = best[cell.x, cell.y] + cellCost[nx, ny];
                if (nc >= best[nx, ny]) continue;
                best[nx, ny] = nc;
                firstDir[nx, ny] = firstDir[cell.x, cell.y];
                pq.Enqueue((nx, ny), nc);
            }
        }

        return Direction.None;
    }

    /// <summary>Direction order that explores <paramref name="preferred" /> first, opposite last.</summary>
    private static IEnumerable<Direction> OrderedDirs(Direction preferred)
    {
        if (preferred == Direction.None) return AllDirs;

        var opposite = Opposite(preferred);
        var ordered = new List<Direction> { preferred };
        foreach (var d in AllDirs)
            if (d != preferred && d != opposite)
                ordered.Add(d);
        ordered.Add(opposite);
        return ordered;
    }

    public static Direction Opposite(Direction dir)
    {
        return dir switch
        {
            Direction.Up => Direction.Down,
            Direction.Down => Direction.Up,
            Direction.Left => Direction.Right,
            Direction.Right => Direction.Left,
            _ => Direction.None
        };
    }
}
