using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.BomberMaze;

/// <summary>
///     Original bomber-maze: yellow spark hero, breakable crates, neon blasts.
///     Plant a bomb, step off it, let the cross-blast clear crates and pink foes.
/// </summary>
[ExtensionInfo("Bomber Maze",
    "Maze bomber — arrows / WASD to walk, Space to bomb, or autopilot",
    "Games",
    IconResourceName = "bomber-maze.svg")]
public class BomberMazeExtension : ICanvasExtension, IDisposable
{
    private const int Empty = 0, Solid = 1, Soft = 2, Extra = 5, RangeUp = 6;

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<BombEnt> _bombs = new();
    private readonly List<Enemy> _enemies = new();
    private readonly List<(int x, int y, int life)> _blasts = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _gw, _gh, _cell, _art;
    private int[,] _map = new int[1, 1];
    private float _px, _py;
    private int _holdX, _holdY;
    private bool _human;
    private int _range = 2, _maxBombs = 1, _score, _lives;
    private int _crashTimer, _frame, _spawnSafe;
    private int _ox, _oy;
    private string _hint = "SPACE = bomb · blast pink foes";
    private string _death = "";

    internal BomberMazeExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 28,
        MinValue = 18, MaxValue = 80, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 28;

    [ExtensionParameter("Difficulty", "Enemy count and speed", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show the HUD", DefaultValue = true, Order = 3)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = false, Order = 4)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI plays until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    public string Name => "Bomber Maze";
    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _human = false;
            _lives = 3;
            _score = 0;
            NewLevel();
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            _timer = new Timer(GameSpeed) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); } catch { }
        }
    }

    private void NewLevel()
    {
        // 8px art, integer scale so tiles are 1:1 on a 128-tall wall (cell = 8).
        _art = Math.Max(1, Math.Min(_canvas.Width / (17 * 8), _canvas.Height / (13 * 8)));
        _cell = 8 * _art;
        _gw = Math.Max(9, _canvas.Width / _cell);
        if (_gw % 2 == 0) _gw--;
        _gh = Math.Max(9, _canvas.Height / _cell);
        if (_gh % 2 == 0) _gh--;
        _ox = (_canvas.Width - _gw * _cell) / 2;
        _oy = (_canvas.Height - _gh * _cell) / 2;
        _map = new int[_gw, _gh];
        _bombs.Clear();
        _blasts.Clear();
        _enemies.Clear();
        _range = 2;
        _maxBombs = 1;
        _holdX = _holdY = 0;
        _crashTimer = 0;
        _spawnSafe = 50;
        _death = "";
        _hint = "SPACE bomb · walk off it · blast pink foes";

        for (var y = 0; y < _gh; y++)
        for (var x = 0; x < _gw; x++)
        {
            if (x == 0 || y == 0 || x == _gw - 1 || y == _gh - 1 || (x % 2 == 0 && y % 2 == 0))
                _map[x, y] = Solid;
            else if (_random.Next(100) < 48)
                _map[x, y] = Soft;
            else
                _map[x, y] = Empty;
        }

        void Clear(int x, int y) => _map[x, y] = Empty;
        Clear(1, 1); Clear(2, 1); Clear(1, 2);
        _px = 1;
        _py = 1;

        var n = 2 + Difficulty / 3;
        for (var i = 0; i < n; i++)
        {
            int ex, ey;
            do
            {
                ex = 1 + _random.Next(_gw - 2);
                ey = 1 + _random.Next(_gh - 2);
            } while (_map[ex, ey] != Empty || (ex < 5 && ey < 5));

            _enemies.Add(new Enemy { X = ex, Y = ey, Dx = 0, Dy = 1 });
        }
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try
            {
                if (_timer != null && Math.Abs(_timer.Interval - GameSpeed) > 0.5) _timer.Interval = GameSpeed;
                if (_crashTimer > 0)
                {
                    _crashTimer--;
                    if (_crashTimer == 0)
                    {
                        if (_lives <= 0) { _lives = 3; _score = 0; }
                        NewLevel();
                    }
                }
                else
                    Update();

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BomberMaze] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        if (_spawnSafe > 0) _spawnSafe--;
        if (AutoPilot && !_human) RunAi();
        TryWalk(_holdX, _holdY);

        for (var i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            b.T--;
            if (b.T <= 0)
            {
                Boom(b.X, b.Y, b.Range);
                _bombs.RemoveAt(i);
            }
            else _bombs[i] = b;
        }

        for (var i = _blasts.Count - 1; i >= 0; i--)
        {
            var bl = _blasts[i];
            bl.life--;
            if (bl.life <= 0) _blasts.RemoveAt(i);
            else _blasts[i] = bl;
        }

        var enemySpd = 0.07f + Difficulty * 0.008f;
        for (var i = _enemies.Count - 1; i >= 0; i--)
        {
            var e = _enemies[i];
            if (!Walkable(e.X + e.Dx * enemySpd, e.Y + e.Dy * enemySpd) || _random.Next(40) == 0)
            {
                var dirs = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
                var d = dirs[_random.Next(4)];
                e.Dx = d.dx;
                e.Dy = d.dy;
            }

            if (Walkable(e.X + e.Dx * enemySpd, e.Y + e.Dy * enemySpd))
            {
                e.X += e.Dx * enemySpd;
                e.Y += e.Dy * enemySpd;
            }

            if (HitBlast((int)Math.Round(e.X), (int)Math.Round(e.Y)))
            {
                _enemies.RemoveAt(i);
                _score += 50;
                continue;
            }

            _enemies[i] = e;
            if (_spawnSafe == 0 && Math.Abs(e.X - _px) < 0.45f && Math.Abs(e.Y - _py) < 0.45f)
            {
                Die("caught by a foe");
                return;
            }
        }

        var tx = (int)Math.Round(_px);
        var ty = (int)Math.Round(_py);
        if (_spawnSafe == 0 && HitBlast(tx, ty))
        {
            Die("caught in the blast");
            return;
        }

        var t = Tile(tx, ty);
        if (t == Extra) { _maxBombs = Math.Min(6, _maxBombs + 1); _map[tx, ty] = Empty; _hint = "+BOMB"; }
        if (t == RangeUp) { _range = Math.Min(8, _range + 1); _map[tx, ty] = Empty; _hint = "+RANGE"; }

        if (_enemies.Count == 0)
        {
            _score += 200;
            _hint = "STAGE CLEAR";
            NewLevel();
        }
    }

    private void RunAi()
    {
        var x = (int)Math.Round(_px);
        var y = (int)Math.Round(_py);
        var danger = DangerMap(horizon: 18);

        if (danger[x, y] >= 0)
        {
            if (StepAwayFromDanger(x, y, danger)) return;
        }

        Enemy? nearest = null;
        var best = 99;
        foreach (var e in _enemies)
        {
            var d = Math.Abs((int)Math.Round(e.X) - x) + Math.Abs((int)Math.Round(e.Y) - y);
            if (d < best) { best = d; nearest = e; }
        }

        if (nearest != null && best > 0 && best <= _range
            && _bombs.Count < _maxBombs
            && Aligned(x, y, (int)Math.Round(nearest.Value.X), (int)Math.Round(nearest.Value.Y))
            && ClearLine(x, y, (int)Math.Round(nearest.Value.X), (int)Math.Round(nearest.Value.Y))
            && HasEscape(x, y))
        {
            PlaceBomb();
            StepAwayFromDanger(x, y, DangerMap(horizon: 22));
            return;
        }

        var (tx, ty) = nearest != null
            ? ((int)Math.Round(nearest.Value.X), (int)Math.Round(nearest.Value.Y))
            : FindSoft(x, y);
        StepTowardSafe(x, y, tx, ty, danger);

        if (_bombs.Count == 0 && Tile(x + 1, y) == Soft && HasEscape(x, y) && _random.Next(12) == 0)
            PlaceBomb();
        else if (_bombs.Count == 0 && Tile(x, y + 1) == Soft && HasEscape(x, y) && _random.Next(12) == 0)
            PlaceBomb();
    }

    private bool StepAwayFromDanger(int x, int y, int[,] danger)
    {
        var step = Bfs(x, y, (nx, ny) => danger[nx, ny] < 0 && Can(nx, ny),
            (nx, ny) => Can(nx, ny) && (danger[nx, ny] < 0 || danger[nx, ny] > 6));
        if (step == (0, 0))
            step = Bfs(x, y, (nx, ny) => danger[nx, ny] < 0, Can);
        _holdX = step.dx;
        _holdY = step.dy;
        return step != (0, 0);
    }

    private void StepTowardSafe(int x, int y, int tx, int ty, int[,] danger)
    {
        var step = Bfs(x, y, (nx, ny) => nx == tx && ny == ty,
            (nx, ny) => Can(nx, ny) && danger[nx, ny] < 0);
        if (step == (0, 0))
            step = Bfs(x, y, (nx, ny) => nx == tx && ny == ty, Can);
        _holdX = step.dx;
        _holdY = step.dy;
    }

    private (int dx, int dy) Bfs(int sx, int sy, Func<int, int, bool> isGoal, Func<int, int, bool> walkable)
    {
        if (isGoal(sx, sy)) return (0, 0);
        var seen = new bool[_gw, _gh];
        var q = new Queue<(int x, int y, int fdx, int fdy)>();
        seen[sx, sy] = true;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var nx = sx + dx;
            var ny = sy + dy;
            if (!In(nx, ny) || seen[nx, ny] || !walkable(nx, ny)) continue;
            if (isGoal(nx, ny)) return (dx, dy);
            seen[nx, ny] = true;
            q.Enqueue((nx, ny, dx, dy));
        }

        while (q.Count > 0)
        {
            var (x, y, fdx, fdy) = q.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var nx = x + dx;
                var ny = y + dy;
                if (!In(nx, ny) || seen[nx, ny] || !walkable(nx, ny)) continue;
                if (isGoal(nx, ny)) return (fdx, fdy);
                seen[nx, ny] = true;
                q.Enqueue((nx, ny, fdx, fdy));
            }
        }

        return (0, 0);
    }

    private int[,] DangerMap(int horizon)
    {
        var d = new int[_gw, _gh];
        for (var y = 0; y < _gh; y++)
        for (var x = 0; x < _gw; x++)
            d[x, y] = -1;

        void Paint(int x, int y, int range, int t)
        {
            void Mark(int cx, int cy)
            {
                if (!In(cx, cy)) return;
                if (d[cx, cy] < 0 || t < d[cx, cy]) d[cx, cy] = t;
            }

            Mark(x, y);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                for (var i = 1; i <= range; i++)
                {
                    var cx = x + dx * i;
                    var cy = y + dy * i;
                    var tile = Tile(cx, cy);
                    if (tile == Solid) break;
                    Mark(cx, cy);
                    if (tile == Soft) break;
                }
        }

        foreach (var b in _bombs)
            if (b.T <= horizon)
                Paint(b.X, b.Y, b.Range, b.T);
        foreach (var bl in _blasts)
            Paint(bl.x, bl.y, 0, 0);
        return d;
    }

    private bool HasEscape(int x, int y)
    {
        // Pretend a bomb is here and see if any cell is outside its cross.
        var fake = DangerMap(horizon: 40);
        void PaintHere()
        {
            fake[x, y] = 0;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                for (var i = 1; i <= _range; i++)
                {
                    var cx = x + dx * i;
                    var cy = y + dy * i;
                    var t = Tile(cx, cy);
                    if (t == Solid) break;
                    fake[cx, cy] = 0;
                    if (t == Soft) break;
                }
        }

        PaintHere();
        var step = Bfs(x, y, (nx, ny) => fake[nx, ny] < 0 && Can(nx, ny),
            (nx, ny) => Can(nx, ny));
        return step != (0, 0);
    }

    private (int, int) FindSoft(int x, int y)
    {
        for (var r = 1; r < 10; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
            if (Tile(x + dx, y + dy) == Soft) return (x + dx, y + dy);
        return (x, y);
    }

    private bool Aligned(int x, int y, int ox, int oy) => x == ox || y == oy;

    private bool ClearLine(int x, int y, int ox, int oy)
    {
        if (x == ox)
        {
            var step = Math.Sign(oy - y);
            for (var cy = y + step; cy != oy; cy += step)
                if (Tile(x, cy) is Solid or Soft) return false;
            return true;
        }

        if (y == oy)
        {
            var step = Math.Sign(ox - x);
            for (var cx = x + step; cx != ox; cx += step)
                if (Tile(cx, y) is Solid or Soft) return false;
            return true;
        }

        return false;
    }

    private bool HitBlast(int x, int y)
    {
        foreach (var b in _blasts)
            if (b.x == x && b.y == y) return true;
        return false;
    }

    private void TryWalk(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        const float spd = 0.28f;
        // Slide onto the cell centre on the unused axis so corners aren't sticky.
        if (dx != 0 && dy == 0)
        {
            var cy = MathF.Round(_py);
            if (Math.Abs(_py - cy) > 0.06f)
            {
                _py += Math.Sign(cy - _py) * Math.Min(spd, Math.Abs(_py - cy));
                return;
            }

            var nx = _px + dx * spd;
            if (Walkable(nx, cy)) { _px = nx; _py = cy; }
        }
        else if (dy != 0 && dx == 0)
        {
            var cx = MathF.Round(_px);
            if (Math.Abs(_px - cx) > 0.06f)
            {
                _px += Math.Sign(cx - _px) * Math.Min(spd, Math.Abs(_px - cx));
                return;
            }

            var ny = _py + dy * spd;
            if (Walkable(cx, ny)) { _px = cx; _py = ny; }
        }
    }

    private bool Walkable(float fx, float fy)
    {
        // Slightly smaller than a cell so grazing a wall doesn't trap you.
        const float r = 0.38f;
        return Can((int)Math.Floor(fx + r), (int)Math.Floor(fy + r))
               && Can((int)Math.Floor(fx + 1f - r), (int)Math.Floor(fy + r))
               && Can((int)Math.Floor(fx + r), (int)Math.Floor(fy + 1f - r))
               && Can((int)Math.Floor(fx + 1f - r), (int)Math.Floor(fy + 1f - r));
    }

    private bool Can(int x, int y)
    {
        var t = Tile(x, y);
        return t is Empty or Extra or RangeUp;
    }

    private bool In(int x, int y) => x >= 0 && y >= 0 && x < _gw && y < _gh;

    private int Tile(int x, int y)
    {
        if (!In(x, y)) return Solid;
        return _map[x, y];
    }

    private void PlaceBomb()
    {
        if (_bombs.Count >= _maxBombs) return;
        var x = (int)Math.Round(_px);
        var y = (int)Math.Round(_py);
        foreach (var b in _bombs)
            if (b.X == x && b.Y == y) return;
        _bombs.Add(new BombEnt { X = x, Y = y, T = 42, Range = _range });
    }

    private void Boom(int x, int y, int range)
    {
        void Arm(int cx, int cy)
        {
            _blasts.Add((cx, cy, 10));
            var t = Tile(cx, cy);
            if (t == Soft)
            {
                _map[cx, cy] = _random.Next(100) < 22 ? (_random.Next(2) == 0 ? Extra : RangeUp) : Empty;
                _score += 5;
            }
        }

        Arm(x, y);
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            for (var i = 1; i <= range; i++)
            {
                var cx = x + dx * i;
                var cy = y + dy * i;
                var t = Tile(cx, cy);
                if (t == Solid) break;
                Arm(cx, cy);
                if (t == Soft) break;
            }
    }

    private void Die(string why)
    {
        if (_crashTimer > 0) return;
        _lives--;
        _crashTimer = 40;
        _death = why;
        _hint = why;
    }

    [ExtensionMethod("Go Left", "Walk left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 1)]
    public void GoLeft()
    {
        lock (_lock) { _human = true; _holdX = -1; _holdY = 0; }
    }

    [ExtensionMethod("Go Right", "Walk right",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 2)]
    public void GoRight()
    {
        lock (_lock) { _human = true; _holdX = 1; _holdY = 0; }
    }

    [ExtensionMethod("Go Up", "Walk up",
        Category = "Controls", KeyboardShortcut = "Up|W", Order = 3)]
    public void GoUp()
    {
        lock (_lock) { _human = true; _holdY = -1; _holdX = 0; }
    }

    [ExtensionMethod("Go Down", "Walk down",
        Category = "Controls", KeyboardShortcut = "Down|S", Order = 4)]
    public void GoDown()
    {
        lock (_lock) { _human = true; _holdY = 1; _holdX = 0; }
    }

    [ExtensionMethod("Stop H", "Release left/right",
        Category = "Controls", KeyboardShortcut = "Left:up|Right:up|A:up|D:up", Order = 5)]
    public void StopH()
    {
        lock (_lock) _holdX = 0;
    }

    [ExtensionMethod("Stop V", "Release up/down",
        Category = "Controls", KeyboardShortcut = "Up:up|Down:up|W:up|S:up", Order = 6)]
    public void StopV()
    {
        lock (_lock) _holdY = 0;
    }

    [ExtensionMethod("Bomb", "Place a bomb",
        Category = "Controls", KeyboardShortcut = "Space", Order = 7)]
    public void DropBomb()
    {
        lock (_lock)
        {
            _human = true;
            PlaceBomb();
        }
    }

    private static readonly string[] Wall =
    {
        "oooooooo",
        "oBBBoBBo",
        "oBBBoBBo",
        "oooooooo",
        "BoBBoBBo",
        "BoBBoBBo",
        "oooooooo",
        "bbbbbbbb"
    };

    private static readonly string[] Crate =
    {
        "oooooooo",
        "oNnnnnNo",
        "onNnnNno",
        "onnXXnno",
        "onnXXnno",
        "onNnnNno",
        "oNnnnnNo",
        "oooooooo"
    };

    private static readonly string[] Floor =
    {
        "........",
        ".x...x..",
        "........",
        "...x....",
        "........",
        ".x...x..",
        "........",
        "........"
    };

    private static readonly string[] Hero =
    {
        "..dddd..",
        ".dyyyyd.",
        "dywwkyyd",
        ".dyyyyd.",
        "dyyyyyyd",
        "d.dd.dd.",
        ".b....b.",
        "........"
    };

    private static readonly string[] BombSpr =
    {
        "....ff..",
        "...dkkd.",
        "..dkskkd",
        ".dkkkkkd",
        "..dkkkd.",
        "...ddd..",
        "........",
        "........"
    };

    private static readonly string[] FoeA =
    {
        "..dmmmd.",
        ".dmmmmmd",
        "dmwwkmmd",
        "dmmmmmmd",
        ".dmmmmmd",
        "..db.bd.",
        "........",
        "........"
    };

    private static readonly string[] FoeB =
    {
        "...dmd..",
        ".dmmmmmd",
        "dmwwkmmd",
        "dmmmmmmd",
        ".dmmmmmd",
        ".db...bd",
        "........",
        "........"
    };

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var canvas = new SKCanvas(bb);
        canvas.Clear(new SKColor(12, 10, 22));
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        var s = _art;

        SKColor PalWall(char ch) => ch switch
        {
            'o' => new SKColor(48, 32, 90),
            'B' => new SKColor(90, 70, 150),
            'b' => new SKColor(36, 24, 70),
            _ => SKColors.Transparent
        };
        SKColor PalCrate(char ch) => ch switch
        {
            'o' => new SKColor(70, 36, 16),
            'N' => new SKColor(230, 150, 60),
            'n' => new SKColor(196, 110, 40),
            'X' => new SKColor(120, 60, 24),
            _ => SKColors.Transparent
        };
        SKColor PalHero(char ch) => ch switch
        {
            'd' => new SKColor(24, 20, 16),
            'y' => new SKColor(255, 220, 50),
            'w' => SKColors.White,
            'k' => new SKColor(20, 20, 20),
            'b' => new SKColor(90, 50, 20),
            _ => SKColors.Transparent
        };
        SKColor PalBomb(char ch) => ch switch
        {
            'd' => new SKColor(20, 20, 24),
            'k' => new SKColor(48, 48, 56),
            's' => new SKColor(90, 90, 100),
            'f' => _frame / 3 % 2 == 0 ? new SKColor(255, 90, 30) : new SKColor(255, 220, 80),
            _ => SKColors.Transparent
        };
        SKColor PalFoe(char ch) => ch switch
        {
            'd' => new SKColor(40, 8, 28),
            'm' => new SKColor(255, 70, 160),
            'w' => SKColors.White,
            'k' => new SKColor(16, 8, 16),
            'b' => new SKColor(180, 40, 110),
            _ => SKColors.Transparent
        };

        for (var y = 0; y < _gh; y++)
        for (var x = 0; x < _gw; x++)
        {
            var rx = _ox + x * _cell;
            var ry = _oy + y * _cell;
            var t = _map[x, y];
            if (t == Solid)
                PixelArt.Blit(canvas, Wall, rx, ry, PalWall, s);
            else if (t == Soft)
                PixelArt.Blit(canvas, Crate, rx, ry, PalCrate, s);
            else
            {
                PixelArt.Blit(canvas, Floor, rx, ry, ch => ch == 'x' ? new SKColor(36, 28, 58) : new SKColor(22, 18, 40), s);
                if (t == Extra)
                {
                    paint.Color = new SKColor(80, 255, 140);
                    PixelArt.Disc(canvas, paint, rx + _cell / 2, ry + _cell / 2, Math.Max(2, _cell / 4));
                }
                else if (t == RangeUp)
                {
                    paint.Color = new SKColor(80, 180, 255);
                    PixelArt.Disc(canvas, paint, rx + _cell / 2, ry + _cell / 2, Math.Max(2, _cell / 4));
                }
            }
        }

        foreach (var bl in _blasts)
        {
            var cx = _ox + bl.x * _cell + _cell / 2;
            var cy = _oy + bl.y * _cell + _cell / 2;
            paint.Color = new SKColor(255, (byte)(70 + bl.life * 18), 20);
            PixelArt.Disc(canvas, paint, cx, cy, Math.Max(3, _cell / 2 - 1));
            paint.Color = new SKColor(255, 230, 80);
            PixelArt.Disc(canvas, paint, cx, cy, Math.Max(2, _cell / 4));
        }

        foreach (var b in _bombs)
            PixelArt.Blit(canvas, BombSpr, _ox + b.X * _cell, _oy + b.Y * _cell, PalBomb, s);

        var foeSpr = _frame / 6 % 2 == 0 ? FoeA : FoeB;
        foreach (var e in _enemies)
            PixelArt.Blit(canvas, foeSpr, _ox + e.X * _cell, _oy + e.Y * _cell, PalFoe, s);

        if (_crashTimer == 0 || _frame / 2 % 2 == 0)
            PixelArt.Blit(canvas, Hero, _ox + _px * _cell, _oy + _py * _cell, PalHero, s);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(7f, _canvas.Height * 0.07f));
            CanvasText.Draw(canvas, _canvas, $"{_score}  x{_lives}  B{_maxBombs} R{_range}", SKColors.White,
                3, size + 1, size, SKTextAlign.Left, UseBdfFont);
            var msg = _crashTimer > 0 && _death.Length > 0 ? _death.ToUpperInvariant() : _hint;
            CanvasText.Draw(canvas, _canvas, msg, new SKColor(255, 230, 120),
                _canvas.Width / 2f, _canvas.Height - 2, Math.Max(6f, size * 0.85f), SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private struct BombEnt
    {
        public int X, Y, T, Range;
    }

    private struct Enemy
    {
        public float X, Y;
        public int Dx, Dy;
    }
}
