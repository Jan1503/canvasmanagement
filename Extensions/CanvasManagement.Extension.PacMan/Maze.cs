namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Generated maze: left/right symmetric, no dead ends, a pen in the middle with a one-way door,
///     a wrap-around tunnel on the pen's row and power pellets near the corners. Columns wrap only
///     where the tunnel is open, so every other border stays a wall.
/// </summary>
internal sealed class Maze
{
    private readonly Cell[,] _cells;
    private readonly bool[,] _dots;
    private readonly bool[,] _power;
    private readonly int[] _queue;

    public Maze(int cols, int rows, int seed)
    {
        W = Math.Max(15, cols | 1);
        H = Math.Max(11, rows | 1);
        _cells = new Cell[W, H];
        _dots = new bool[W, H];
        _power = new bool[W, H];
        _queue = new int[W * H];
        HouseX = W / 2;
        HouseY = H / 2;
        Door = (HouseX, HouseY - 1);
        Exit = (HouseX, HouseY - 2);
        PacSpawn = (HouseX, Math.Min(H - 2, HouseY + 4));
        var random = new Random(seed);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            Generate(random);
            if (Validate()) break;
        }
        PlaceDots();
    }

    public int W { get; }
    public int H { get; }
    public int HouseX { get; }
    public int HouseY { get; }
    public (int X, int Y) Door { get; }
    public (int X, int Y) Exit { get; }
    public (int X, int Y) PacSpawn { get; }
    public int TunnelRow { get; private set; } = -1;
    public int DotsTotal { get; private set; }
    public int DotsLeft { get; private set; }

    public Cell this[int x, int y] => x < 0 || y < 0 || x >= W || y >= H ? Cell.Wall : _cells[x, y];

    public bool IsWall(int x, int y) => this[x, y] == Cell.Wall;

    /// <summary>Where Pac-Man and ghosts on the grid may stand.</summary>
    public bool IsOpen(int x, int y) => this[x, y] == Cell.Open;

    public bool InTunnel(int x, int y) => y == TunnelRow && (x <= 1 || x >= W - 2);

    public bool HasDot(int x, int y) => (uint)x < W && (uint)y < H && _dots[x, y];
    public bool HasPower(int x, int y) => (uint)x < W && (uint)y < H && _power[x, y];

    public bool EatDot(int x, int y)
    {
        if (!HasDot(x, y)) return false;
        _dots[x, y] = false;
        DotsLeft--;
        return true;
    }

    public bool EatPower(int x, int y)
    {
        if (!HasPower(x, y)) return false;
        _power[x, y] = false;
        DotsLeft--;
        return true;
    }

    public bool AnyPower()
    {
        foreach (var p in _power)
            if (p) return true;
        return false;
    }

    /// <summary>One step in <paramref name="d"/>, wrapping through the tunnel. False if that is a wall.</summary>
    public bool Step(int x, int y, Direction d, out int nx, out int ny)
    {
        nx = x + d.Dx();
        ny = y + d.Dy();
        if (nx < 0) nx += W;
        else if (nx >= W) nx -= W;
        return IsOpen(nx, ny);
    }

    public int Exits(int x, int y)
    {
        var n = 0;
        foreach (var d in Dirs.All)
            if (Step(x, y, d, out _, out _)) n++;
        return n;
    }

    /// <summary>
    ///     Breadth-first step counts from <paramref name="sx"/>,<paramref name="sy"/> over open cells (tunnel
    ///     included). <paramref name="noFirst"/> forbids the first move in that direction (a ghost cannot reverse).
    ///     Unreachable cells are -1.
    /// </summary>
    public void Distances(int sx, int sy, int[,] dist, Direction noFirst = Direction.None)
    {
        for (var x = 0; x < W; x++)
        for (var y = 0; y < H; y++)
            dist[x, y] = -1;
        if ((uint)sx >= W || (uint)sy >= H) return;
        int head = 0, tail = 0;
        dist[sx, sy] = 0;
        _queue[tail++] = sy * W + sx;
        while (head < tail)
        {
            var c = _queue[head++];
            int x = c % W, y = c / W;
            foreach (var d in Dirs.All)
            {
                if (head == 1 && d == noFirst) continue;
                if (!Step(x, y, d, out var nx, out var ny) || dist[nx, ny] >= 0) continue;
                dist[nx, ny] = dist[x, y] + 1;
                _queue[tail++] = ny * W + nx;
            }
        }
    }

    /// <summary>Nearest open cell to a point that may lie in a wall or off the maze.</summary>
    public (int X, int Y) Snap(int x, int y)
    {
        x = Math.Clamp(x, 1, W - 2);
        y = Math.Clamp(y, 1, H - 2);
        if (IsOpen(x, y)) return (x, y);
        for (var r = 1; r < Math.Max(W, H); r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
            if (IsOpen(x + dx, y + dy)) return (x + dx, y + dy);
        }
        return PacSpawn;
    }

    // ------------------------------------------------------------------ generation

    private void Generate(Random random)
    {
        for (var x = 0; x < W; x++)
        for (var y = 0; y < H; y++)
            _cells[x, y] = Cell.Wall;

        var cx = W / 2;
        // Lattice cells sit on odd coordinates. When the centre column is odd it is shared by both halves.
        var maxX = cx % 2 == 1 ? cx : cx - 1;
        var visited = new bool[W, H];
        var stack = new Stack<(int X, int Y)>();
        Set(1, 1, Cell.Open);
        visited[1, 1] = true;
        stack.Push((1, 1));
        var options = new List<(int, int, int, int)>(4);
        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();
            options.Clear();
            foreach (var d in Dirs.All)
            {
                int nx = x + d.Dx() * 2, ny = y + d.Dy() * 2;
                if (nx < 1 || nx > maxX || ny < 1 || ny > H - 2 || visited[nx, ny]) continue;
                options.Add((nx, ny, x + d.Dx(), y + d.Dy()));
            }
            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }
            // Prefer long straight runs a little: arcade corridors, not a twisty labyrinth.
            var pick = options[random.Next(options.Count)];
            var (px, py, wx, wy) = pick;
            Set(wx, wy, Cell.Open);
            Set(px, py, Cell.Open);
            visited[px, py] = true;
            stack.Push((px, py));
        }

        // Extra loops so the ghosts can be dodged: knock out some walls between lattice cells.
        for (var x = 1; x <= maxX; x++)
        for (var y = 1; y <= H - 2; y++)
        {
            if (_cells[x, y] != Cell.Wall || (x % 2) + (y % 2) != 1) continue;
            if (random.NextDouble() < 0.16) _cells[x, y] = Cell.Open;
        }

        // Mirror the left half onto the right.
        for (var x = 0; x < cx; x++)
        for (var y = 0; y < H; y++)
            _cells[W - 1 - x, y] = _cells[x, y];

        // With an even centre column the halves meet through openings in it.
        if (cx % 2 == 0)
        {
            var rows = Enumerable.Range(1, H - 2).Where(y => y % 2 == 1 && IsOpen(cx - 1, y) && IsOpen(cx + 1, y)).ToList();
            for (var i = 0; i < rows.Count; i++)
                if (i == 0 || i == rows.Count - 1 || random.NextDouble() < 0.5)
                    _cells[cx, rows[i]] = Cell.Open;
        }

        BuildHouse();
        BuildTunnel();
        BuildSpawn();
        Connect();
        RemoveDeadEnds(random);
        Connect();
    }

    private void BuildHouse()
    {
        int cx = HouseX, cy = HouseY;
        // Ring corridor around the pen.
        for (var x = cx - 4; x <= cx + 4; x++)
        {
            Set(x, cy - 2, Cell.Open);
            Set(x, cy + 2, Cell.Open);
        }
        for (var y = cy - 2; y <= cy + 2; y++)
        {
            Set(cx - 4, y, Cell.Open);
            Set(cx + 4, y, Cell.Open);
        }
        // Pen walls with the door on top.
        for (var x = cx - 3; x <= cx + 3; x++)
        {
            Set(x, cy - 1, Cell.Wall);
            Set(x, cy + 1, Cell.Wall);
        }
        Set(cx - 3, cy, Cell.Wall);
        Set(cx + 3, cy, Cell.Wall);
        for (var x = cx - 2; x <= cx + 2; x++) Set(x, cy, Cell.House);
        Set(cx, cy - 1, Cell.Door);
    }

    private void BuildTunnel()
    {
        TunnelRow = HouseY;
        var y = TunnelRow;
        // Carve in from the border until the corridor meets the maze, then mirror.
        for (var x = 0; x < HouseX - 4; x++)
        {
            var hit = x > 0 && IsOpen(x, y);
            _cells[x, y] = Cell.Open;
            _cells[W - 1 - x, y] = Cell.Open;
            if (hit) break;
        }
        // Walls above and below the tunnel mouth so it reads as a tunnel.
        for (var x = 0; x <= 1; x++)
        {
            if (y - 1 >= 0) { _cells[x, y - 1] = Cell.Wall; _cells[W - 1 - x, y - 1] = Cell.Wall; }
            if (y + 1 < H) { _cells[x, y + 1] = Cell.Wall; _cells[W - 1 - x, y + 1] = Cell.Wall; }
        }
    }

    private void BuildSpawn()
    {
        var (px, py) = PacSpawn;
        for (var x = px - 2; x <= px + 2; x++) Set(x, py, Cell.Open);
        for (var y = HouseY + 2; y <= py; y++) Set(px, y, Cell.Open);
    }

    /// <summary>Every open cell must be reachable from Pac-Man's spawn; carve the shortest L to any island.</summary>
    private void Connect()
    {
        var dist = new int[W, H];
        for (var guard = 0; guard < 64; guard++)
        {
            Distances(PacSpawn.X, PacSpawn.Y, dist);
            (int X, int Y)? lost = null;
            for (var x = 1; x < W - 1 && lost == null; x++)
            for (var y = 1; y < H - 1; y++)
                if (_cells[x, y] == Cell.Open && dist[x, y] < 0) { lost = (x, y); break; }
            if (lost == null) return;
            var (sx, sy) = lost.Value;
            (int X, int Y) best = PacSpawn;
            var bestD = int.MaxValue;
            for (var x = 1; x < W - 1; x++)
            for (var y = 1; y < H - 1; y++)
            {
                if (dist[x, y] < 0) continue;
                var d = Math.Abs(x - sx) + Math.Abs(y - sy);
                if (d < bestD) { bestD = d; best = (x, y); }
            }
            int cx = sx, cy = sy;
            while (cx != best.X) { cx += Math.Sign(best.X - cx); Carve(cx, cy); Carve(W - 1 - cx, cy); }
            while (cy != best.Y) { cy += Math.Sign(best.Y - cy); Carve(cx, cy); Carve(W - 1 - cx, cy); }
        }
    }

    /// <summary>A dead end is a death trap with four ghosts about. Open a wall beside each one (mirrored).</summary>
    private void RemoveDeadEnds(Random random)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            for (var x = 1; x < W - 1; x++)
            for (var y = 1; y < H - 1; y++)
            {
                if (_cells[x, y] != Cell.Open || Exits(x, y) != 1) continue;
                var walls = Dirs.All
                    .Select(d => (X: x + d.Dx(), Y: y + d.Dy(), Beyond: (X: x + 2 * d.Dx(), Y: y + 2 * d.Dy())))
                    .Where(c => c.X >= 1 && c.X <= W - 2 && c.Y >= 1 && c.Y <= H - 2 && _cells[c.X, c.Y] == Cell.Wall &&
                                !NearHouse(c.X, c.Y))
                    .OrderByDescending(c => IsOpen(c.Beyond.X, c.Beyond.Y) ? 1 : 0)
                    .ThenBy(_ => random.Next())
                    .ToList();
                if (walls.Count == 0) continue;
                Carve(walls[0].X, walls[0].Y);
                Carve(W - 1 - walls[0].X, walls[0].Y);
                changed = true;
            }
            if (!changed) break;
        }
    }

    private bool NearHouse(int x, int y) => Math.Abs(x - HouseX) <= 3 && Math.Abs(y - HouseY) <= 1;

    private bool Validate()
    {
        for (var x = 1; x < W - 1; x++)
        for (var y = 1; y < H - 1; y++)
            if (_cells[x, y] == Cell.Open && Exits(x, y) < 2)
                return false;
        return IsOpen(Exit.X, Exit.Y) && IsOpen(PacSpawn.X, PacSpawn.Y);
    }

    private void Carve(int x, int y)
    {
        if (x < 1 || y < 1 || x > W - 2 || y > H - 2 || NearHouse(x, y)) return;
        _cells[x, y] = Cell.Open;
    }

    private void Set(int x, int y, Cell c)
    {
        if (x >= 1 && y >= 1 && x <= W - 2 && y <= H - 2) _cells[x, y] = c;
    }

    private void PlaceDots()
    {
        DotsTotal = 0;
        for (var x = 1; x < W - 1; x++)
        for (var y = 1; y < H - 1; y++)
        {
            _dots[x, y] = _power[x, y] = false;
            if (_cells[x, y] != Cell.Open || InTunnel(x, y) || (x, y) == PacSpawn) continue;
            // The ring around the pen stays empty, like the arcade.
            if (Math.Abs(x - HouseX) <= 4 && Math.Abs(y - HouseY) <= 2) continue;
            _dots[x, y] = true;
            DotsTotal++;
        }

        foreach (var (x, y) in new[] { (1, 2), (W - 2, 2), (1, H - 3), (W - 2, H - 3) })
        {
            var (px, py) = Snap(x, y);
            if (_dots[px, py]) _dots[px, py] = false;
            else DotsTotal++;
            _power[px, py] = true;
        }
        DotsLeft = DotsTotal;
    }
}
