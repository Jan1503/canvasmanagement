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

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = true, Order = 4)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI plays until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    private GameSound? _snd;
    private GameSound _sound => _snd ??= new GameSound(_canvas, GameSong.Action);

    [ExtensionParameter("Sound", "Sound effects on the audio board", DefaultValue = true, Order = 20)]
    public bool Sound { get => _sound.Effects; set => _sound.Effects = value; }

    [ExtensionParameter("Music", "Background music", DefaultValue = true, Order = 21)]
    public bool Music { get => _sound.Music; set => _sound.Music = value; }

    [ExtensionParameter("Volume", "How loud this game is on the audio board", DefaultValue = 70, MinValue = 0,
        MaxValue = 100, Order = 22)]
    public int Volume { get => (int)MathF.Round(_sound.Volume * 100); set => _sound.Volume = Math.Clamp(value, 0, 100) / 100f; }

    [ExtensionParameter("Music Volume", "Music level relative to the effects", DefaultValue = 50, MinValue = 0,
        MaxValue = 100, Order = 23)]
    public int MusicVolume
    {
        get => (int)MathF.Round(_sound.MusicVolume * 100);
        set => _sound.MusicVolume = Math.Clamp(value, 0, 100) / 100f;
    }

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
            _sound.Stop();
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
                _sound.Duck(_crashTimer > 0 ? 0.35f : 1f);
                _sound.Update();
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
                _fx.Burst(_ox + e.X * _cell + _cell / 2f, _oy + e.Y * _cell + _cell / 2f, new SKColor(255, 70, 160), 18, 2.4f, 0.7f, 0.04f);
                _fx.Pop("+50", _ox + e.X * _cell + _cell / 2f, _oy + e.Y * _cell, new SKColor(255, 160, 220));
                _sound.Play(GameSfx.Pop);
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
        if (t is Extra or RangeUp) _sound.Play(GameSfx.PowerUp, 0.6f);
        if (t == Extra) { _maxBombs = Math.Min(6, _maxBombs + 1); _map[tx, ty] = Empty; _hint = "+BOMB"; }
        if (t == RangeUp) { _range = Math.Min(8, _range + 1); _map[tx, ty] = Empty; _hint = "+RANGE"; }

        if (_enemies.Count == 0)
        {
            _score += 200;
            _sound.Play(GameSfx.LevelUp, 0.7f);
            _hint = "STAGE CLEAR";
            NewLevel();
        }
    }

    /// <summary>
    ///     Demo player: flee any blast that will reach it, otherwise bomb a foe lined up in range, walk to a spot
    ///     from which it can hit one, pick up power-ups, or dig: walk next to a soft block and bomb it. A bomb is
    ///     only laid when there is a safe cell to run to.
    /// </summary>
    private void RunAi()
    {
        var x = (int)Math.Round(_px);
        var y = (int)Math.Round(_py);
        var danger = DangerMap(horizon: 60);

        if (danger[x, y] >= 0)
        {
            if (StepAwayFromDanger(x, y, danger)) return;
        }

        bool Safe(int nx, int ny) => Can(nx, ny) && danger[nx, ny] < 0 && !NearFoe(nx, ny);

        // A foe in line and in reach: bomb and run.
        if (_bombs.Count < _maxBombs && FoeInReach(x, y) && HasEscape(x, y))
        {
            PlaceBomb();
            StepAwayFromDanger(x, y, DangerMap(horizon: 60));
            return;
        }

        // While a bomb ticks, keep out of its way and let it do the work.
        if (_bombs.Count >= _maxBombs)
        {
            _holdX = _holdY = 0;
            return;
        }

        // Power-ups, then a spot to shoot a foe from, then a soft block to dig.
        Func<int, int, bool>[] goals =
        {
            (nx, ny) => Tile(nx, ny) is Extra or RangeUp,
            (nx, ny) => FoeInReach(nx, ny),
            (nx, ny) => Tile(nx + 1, ny) == Soft || Tile(nx - 1, ny) == Soft || Tile(nx, ny + 1) == Soft || Tile(nx, ny - 1) == Soft
        };
        for (var g = 0; g < goals.Length; g++)
        {
            var goal = goals[g];
            if (goal(x, y))
            {
                if (g == 0) continue;
                if (HasEscape(x, y))
                {
                    PlaceBomb();
                    StepAwayFromDanger(x, y, DangerMap(horizon: 60));
                    return;
                }
                continue;
            }
            var step = Bfs(x, y, goal, Safe);
            if (step != (0, 0))
            {
                _holdX = step.dx;
                _holdY = step.dy;
                return;
            }
        }

        // Nothing to do (or boxed in): wander somewhere safe.
        var wander = Bfs(x, y, (nx, ny) => Math.Abs(nx - x) + Math.Abs(ny - y) >= 3, Safe);
        _holdX = wander.dx;
        _holdY = wander.dy;
    }

    /// <summary>
    ///     When each cell burns: from the tick its bomb goes off until the blast fades (-1 = never). A bomb we
    ///     are about to lay can be included.
    /// </summary>
    private (int[,] From, int[,] To) BurnWindows((int X, int Y)? extra = null)
    {
        var from = new int[_gw, _gh];
        var to = new int[_gw, _gh];
        for (var y = 0; y < _gh; y++)
        for (var x = 0; x < _gw; x++)
            from[x, y] = to[x, y] = -1;

        void Mark(int cx, int cy, int t0, int t1)
        {
            if (!In(cx, cy)) return;
            if (from[cx, cy] < 0 || t0 < from[cx, cy]) from[cx, cy] = t0;
            to[cx, cy] = Math.Max(to[cx, cy], t1);
        }

        void Cross(int bx, int by, int range, int t)
        {
            Mark(bx, by, t, t + 11);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                for (var i = 1; i <= range; i++)
                {
                    var tile = Tile(bx + dx * i, by + dy * i);
                    if (tile == Solid) break;
                    Mark(bx + dx * i, by + dy * i, t, t + 11);
                    if (tile == Soft) break;
                }
        }

        foreach (var b in _bombs) Cross(b.X, b.Y, b.Range, b.T);
        if (extra is { } e) Cross(e.X, e.Y, _range, 42);
        foreach (var bl in _blasts) Mark(bl.x, bl.y, 0, bl.life + 1);
        return (from, to);
    }

    /// <summary>
    ///     First step to the nearest cell that never burns, walking only through cells that are not burning
    ///     when the player passes (about four ticks per cell). (0,0) if already safe or no way out.
    /// </summary>
    private (int dx, int dy) FleeStep(int sx, int sy, (int[,] From, int[,] To) w)
    {
        const int perCell = 4;
        bool Burning(int x, int y, int at) => w.From[x, y] >= 0 && w.From[x, y] <= at + 3 && at - 2 <= w.To[x, y];
        if (w.From[sx, sy] < 0) return (0, 0);
        var seen = new bool[_gw, _gh];
        var q = new Queue<(int x, int y, int d, int fdx, int fdy)>();
        seen[sx, sy] = true;
        q.Enqueue((sx, sy, 0, 0, 0));
        while (q.Count > 0)
        {
            var (x, y, d, fdx, fdy) = q.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (!In(nx, ny) || seen[nx, ny] || !Can(nx, ny)) continue;
                var at = (d + 1) * perCell;
                if (Burning(nx, ny, at)) continue;
                seen[nx, ny] = true;
                var first = d == 0 ? (dx, dy) : (fdx, fdy);
                if (w.From[nx, ny] < 0 && !NearFoe(nx, ny)) return first;
                q.Enqueue((nx, ny, d + 1, first.Item1, first.Item2));
            }
        }
        return (0, 0);
    }

    private bool NearFoe(int x, int y)
    {
        foreach (var e in _enemies)
            if (Math.Abs(e.X - x) + Math.Abs(e.Y - y) < 1.6f) return true;
        return false;
    }

    private bool FoeInReach(int x, int y)
    {
        if (!Can(x, y)) return false;
        foreach (var e in _enemies)
        {
            int ex = (int)Math.Round(e.X), ey = (int)Math.Round(e.Y);
            var d = Math.Abs(ex - x) + Math.Abs(ey - y);
            if (d > 0 && d <= _range && Aligned(x, y, ex, ey) && ClearLine(x, y, ex, ey)) return true;
        }
        return false;
    }

    private bool StepAwayFromDanger(int x, int y, int[,] danger)
    {
        var flee = FleeStep(x, y, BurnWindows());
        if (flee != (0, 0))
        {
            _holdX = flee.dx;
            _holdY = flee.dy;
            return true;
        }

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

    /// <summary>Would a bomb laid here leave a timed way out?</summary>
    private bool HasEscape(int x, int y) => FleeStep(x, y, BurnWindows((x, y))) != (0, 0);

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
        _sound.Play(GameSfx.Bomb, 0.6f);
    }

    private void Boom(int x, int y, int range)
    {
        void Arm(int cx, int cy)
        {
            _blasts.Add((cx, cy, 10));
            var t = Tile(cx, cy);
            if (t == Soft)
            {
                // Splinters.
                _fx.Burst(_ox + cx * _cell + _cell / 2f, _oy + cy * _cell + _cell / 2f, new SKColor(200, 120, 50), 10, 2.2f, 0.8f, 0.12f, false);
                _map[cx, cy] = _random.Next(100) < 22 ? (_random.Next(2) == 0 ? Extra : RangeUp) : Empty;
                _score += 5;
            }
        }

        _sound.Play(GameSfx.Explode, 0.7f, (x / (float)Math.Max(1, _gw)) * 2 - 1);
        _fx.Shake(3 + range * 0.4f);
        _fx.Burst(_ox + x * _cell + _cell / 2f, _oy + y * _cell + _cell / 2f, new SKColor(255, 200, 80), 20, 3f, 0.6f, 0.02f);
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
        _sound.Play(_lives <= 0 ? GameSfx.GameOver : GameSfx.Die, 0.8f);
        _fx.Flash(new SKColor(255, 60, 40), 0.6f);
        _fx.Shake(5);
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

    private readonly GameFx _fx = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastRender;

    /// <summary>
    ///     Explosions are glowing flame crosses that flicker and fade, crates splinter into debris, bombs swell
    ///     and spit sparks from the fuse, power-ups glow and bob, foes burst, and every blast shakes the maze.
    /// </summary>
    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastRender, 0.001, 0.1);
        _lastRender = now;
        _fx.Update(dt);
        using var canvas = new SKCanvas(bb);
        canvas.Clear(new SKColor(12, 10, 22));
        _fx.BeginShake(canvas);
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
                if (t is Extra or RangeUp)
                {
                    var col = t == Extra ? new SKColor(80, 255, 140) : new SKColor(80, 180, 255);
                    var bob = (int)MathF.Round(MathF.Sin(_fx.Time * 4 + x + y) * Math.Max(1, _cell / 8f));
                    GameFx.GlowDisc(canvas, rx + _cell / 2f, ry + _cell / 2f + bob, _cell * 0.8f, col, 0.4f);
                    paint.Color = col;
                    PixelArt.Disc(canvas, paint, rx + _cell / 2, ry + _cell / 2 + bob, Math.Max(2, _cell / 4));
                    paint.Color = SKColors.White;
                    canvas.DrawRect(rx + _cell / 2 - 1, ry + _cell / 2 + bob - 1, 1, 1, paint);
                }
            }
        }

        // Flames: each blast cell is a flickering block of fire that joins its neighbours, with a glow.
        var fire = new HashSet<(int, int)>(_blasts.Select(b => (b.x, b.y)));
        foreach (var bl in _blasts)
        {
            var rx = _ox + bl.x * _cell;
            var ry = _oy + bl.y * _cell;
            var heat = bl.life / 10f;
            GameFx.Glow(canvas, rx, ry, _cell, _cell, new SKColor(255, 120, 20), 3, 0.25f * heat);
            var horiz = fire.Contains((bl.x - 1, bl.y)) || fire.Contains((bl.x + 1, bl.y));
            var vert = fire.Contains((bl.x, bl.y - 1)) || fire.Contains((bl.x, bl.y + 1));
            var inset = Math.Max(1, (int)(_cell * (0.35f - 0.25f * heat)));
            for (var layer = 0; layer < 3; layer++)
            {
                var k = inset + layer * Math.Max(1, _cell / 8);
                paint.Color = layer switch
                {
                    0 => new SKColor(220, 40, 10),
                    1 => new SKColor(255, (byte)(120 + 60 * heat), 20),
                    _ => new SKColor(255, 250, 200)
                };
                var jitter = (_frame + bl.x * 3 + bl.y) % 3 == 0 ? 1 : 0;
                if (horiz || !vert) canvas.DrawRect(rx, ry + k + jitter, _cell, _cell - 2 * k, paint);
                if (vert || !horiz) canvas.DrawRect(rx + k + jitter, ry, _cell - 2 * k, _cell, paint);
            }
        }

        // Bombs swell as the fuse burns down and spit sparks.
        foreach (var b in _bombs)
        {
            var bx = _ox + b.X * _cell;
            var by = _oy + b.Y * _cell;
            var urgency = 1 - b.T / 42f;
            var pulse = MathF.Sin(_fx.Time * (8 + urgency * 20)) > 0;
            if (pulse && urgency > 0.5f) GameFx.GlowDisc(canvas, bx + _cell / 2f, by + _cell / 2f, _cell * 0.9f, new SKColor(255, 60, 30), 0.35f * urgency);
            PixelArt.Blit(canvas, BombSpr, bx, by, PalBomb, s);
            if (_frame % 2 == 0)
                _fx.Spark(bx + _cell * 0.7f, by + _cell * 0.15f, _fx.Rand(-0.5f, 0.5f), _fx.Rand(-1f, -0.3f), new SKColor(255, 220, 80), 0.25f, 1, true, 0.05f);
        }

        var foeSpr = _frame / 6 % 2 == 0 ? FoeA : FoeB;
        foreach (var e in _enemies)
            PixelArt.Blit(canvas, foeSpr, _ox + e.X * _cell, _oy + e.Y * _cell, PalFoe, s);

        if (_crashTimer == 0 || _frame / 2 % 2 == 0)
            PixelArt.Blit(canvas, Hero, _ox + _px * _cell, _oy + _py * _cell, PalHero, s);

        _fx.DrawParticles(canvas);
        _fx.EndShake(canvas);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(7f, _canvas.Height * 0.07f));
            CanvasText.Draw(canvas, _canvas, $"{_score}  x{_lives}  B{_maxBombs} R{_range}", SKColors.White,
                3, size + 1, size, SKTextAlign.Left, UseBdfFont);
            var msg = _crashTimer > 0 && _death.Length > 0 ? _death.ToUpperInvariant() : _hint;
            paint.Color = new SKColor(0, 0, 0, 150);
            canvas.DrawRect(0, _canvas.Height - size - 2, _canvas.Width, size + 2, paint);
            CanvasText.Draw(canvas, _canvas, msg, new SKColor(255, 230, 120),
                _canvas.Width / 2f, _canvas.Height - 2, Math.Max(6f, size * 0.85f), SKTextAlign.Center, UseBdfFont);
        }

        _fx.DrawOverlay(canvas, _canvas, _canvas.Width, _canvas.Height, Math.Max(7f, _canvas.Height * 0.07f));
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
