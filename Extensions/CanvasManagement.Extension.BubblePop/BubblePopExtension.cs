using System.Collections.Concurrent;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.BubblePop;

/// <summary>
///     Bubble shooter for a wide LED wall. A glass well in the middle of the wall holds a hex field of bubbles;
///     a launcher at the bottom (with the dino next to it) fires one at a time. Three or more of a colour pop,
///     anything no longer hanging from the top falls. After a few shots without a pop the ceiling pushes a new
///     row in; a bubble past the red line ends the game. Clear the field for the next level (more colours).
///     The demo player tries every angle with both the loaded and the next bubble and takes the best shot.
/// </summary>
[ExtensionInfo("Bubble Pop",
    "Bubble shooter — Left/Right aim, Space or A shoot, Down or B swap, or autopilot",
    "Games",
    IconResourceName = "bubble-pop.svg")]
public class BubblePopExtension : GameExtension
{
    private static readonly SKColor[] Palette =
    {
        new(255, 70, 110),
        new(255, 170, 40),
        new(255, 230, 60),
        new(60, 230, 140),
        new(70, 190, 255),
        new(170, 90, 255),
        new(255, 255, 255)
    };

    private readonly Random _random = new();
    private readonly GameFx _fx = new();

    // Layout (pixels).
    private int _d, _rowH, _cols, _rows, _fieldX, _fieldW, _top, _deadY, _launchX, _launchY;

    // Board: 0 empty, else palette index + 1. Rows shift sideways in turn; _parity flips when a row is pushed in.
    private int[,] _grid = new int[1, 1];
    private int _parity;

    private float _ang = -MathF.PI / 2;
    private int _loaded, _next;
    private bool _flying;
    private float _shotX, _shotY, _shotVx, _shotVy;
    private int _shotColor;
    private int _misses, _dropEvery, _colors, _level;
    private int _score, _best, _combo;
    private float _over; // seconds since game over, 0 while playing
    private float _swing; // launcher recoil
    private float _ceilingFlash;

    private volatile int _hold;
    private volatile bool _human;
    private readonly ConcurrentQueue<string> _input = new();

    // Autopilot target.
    private float _aiAng = float.NaN;
    private bool _aiSwap;
    private float _aiWait;

    internal BubblePopExtension(ICanvas canvas) : base(canvas, GameSong.Arcade) { }

    [ExtensionParameter("Game Speed", "Shot speed (higher is faster)", DefaultValue = 22, MinValue = 14, MaxValue = 50,
        Order = 1)]
    public int GameSpeed { get; set; } = 22;

    [ExtensionParameter("Difficulty", "How soon the ceiling comes down and how many colours", DefaultValue = 3,
        MinValue = 1, MaxValue = 10, Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show the HUD", DefaultValue = true, Order = 3)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = true, Order = 4)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI aims until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    public override string Name => "Bubble Pop";

    protected override double FrameIntervalMs => 1000.0 / 60;

    protected override void OnStart()
    {
        _human = false;
        _score = 0;
        _level = 1;
        NewBoard();
    }

    /// <summary>The well is laid out for the canvas size; a new size starts a new board.</summary>
    protected override void OnResize(int width, int height)
    {
        ClearBubbleImages();
        NewBoard();
    }

    protected override void OnStop()
    {
        base.OnStop();
        ClearBubbleImages();
    }

    // ------------------------------------------------------------------ controls

    [ExtensionMethod("Aim Left", "Rotate aim left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A|PadLeft", Order = 1)]
    public void AimLeft()
    {
        _human = true;
        _hold = -1;
    }

    [ExtensionMethod("Aim Right", "Rotate aim right", Category = "Controls", KeyboardShortcut = "Right|D|PadRight", Order = 2)]
    public void AimRight()
    {
        _human = true;
        _hold = 1;
    }

    [ExtensionMethod("Release Left", "Release left", Category = "Controls",
        KeyboardShortcut = "Left:up|A:up|PadLeft:up", Order = 3)]
    public void ReleaseLeft()
    {
        if (_hold < 0) _hold = 0;
    }

    [ExtensionMethod("Release Right", "Release right", Category = "Controls",
        KeyboardShortcut = "Right:up|D:up|PadRight:up", Order = 4)]
    public void ReleaseRight()
    {
        if (_hold > 0) _hold = 0;
    }

    [ExtensionMethod("Shoot", "Fire the loaded bubble", Category = "Controls", KeyboardShortcut = "Space|Up|W|PadA|PadUp",
        Order = 5)]
    public void Fire()
    {
        _human = true;
        _input.Enqueue("fire");
    }

    [ExtensionMethod("Swap", "Swap the loaded and the next bubble", Category = "Controls",
        KeyboardShortcut = "Down|S|PadB|PadX|PadDown", Order = 6)]
    public void Swap()
    {
        _human = true;
        _input.Enqueue("swap");
    }

    // ------------------------------------------------------------------ board

    private void NewBoard()
    {
        var w = Canvas.Width;
        var h = Canvas.Height;
        _d = Math.Clamp(h / 11, 7, 21) | 1; // odd, so a bubble has a centre pixel
        _rowH = (int)MathF.Round(_d * 0.87f);
        var fieldMax = Math.Min(w - 8, (int)(h * 1.3f));
        _cols = Math.Clamp(fieldMax / _d, 7, 24);
        _fieldW = _cols * _d + _d / 2;
        _fieldX = (w - _fieldW) / 2;
        _top = 2;
        _launchY = h - 6;
        _launchX = _fieldX + _fieldW / 2;
        _deadY = h - Math.Max(20, _d * 2 + 4);
        _rows = Math.Max(6, (_deadY - _top) / _rowH + 2);
        _grid = new int[_cols, _rows];
        _parity = 0;
        _colors = Math.Clamp(3 + (Difficulty + 1) / 3 + (_level - 1) / 2, 3, Palette.Length);
        _dropEvery = Math.Max(3, 8 - Difficulty / 2 - (_level - 1) / 3);
        _misses = 0;
        var fill = Math.Min(_rows / 2 + 1, 5 + _level / 2);
        for (var r = 0; r < fill; r++)
        for (var c = 0; c < RowCols(r); c++)
            _grid[c, r] = 1 + _random.Next(_colors);
        _loaded = PickColor();
        _next = PickColor();
        _flying = false;
        _over = 0;
        _combo = 0;
        _aiAng = float.NaN;
        _fx.Clear();
    }

    private bool Shifted(int r) => ((r + _parity) & 1) == 1;
    private int RowCols(int r) => Shifted(r) ? _cols - 1 : _cols;

    private void Center(int c, int r, out float x, out float y)
    {
        x = _fieldX + _d / 2f + c * _d + (Shifted(r) ? _d / 2f : 0);
        y = _top + _d / 2f + r * _rowH;
    }

    private IEnumerable<(int C, int R)> Neighbours(int c, int r)
    {
        var s = Shifted(r) ? 1 : 0;
        (int, int)[] offs =
        {
            (-1, 0), (1, 0),
            (s - 1, -1), (s, -1),
            (s - 1, 1), (s, 1)
        };
        foreach (var (dc, dr) in offs)
        {
            int nc = c + dc, nr = r + dr;
            if (nr < 0 || nr >= _rows || nc < 0 || nc >= RowCols(nr)) continue;
            yield return (nc, nr);
        }
    }

    /// <summary>Only colours still on the board are dealt (none left: any).</summary>
    private int PickColor()
    {
        var present = new List<int>();
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < RowCols(r); c++)
            if (_grid[c, r] > 0 && !present.Contains(_grid[c, r] - 1))
                present.Add(_grid[c, r] - 1);
        return present.Count > 0 ? present[_random.Next(present.Count)] : _random.Next(_colors);
    }

    // ------------------------------------------------------------------ loop

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        dt = Math.Clamp(dt, 0.001f, 0.05f);
        Update(dt);
        _fx.Update(dt);
        Draw(canvas, width, height);
        Audio.Duck(_over > 0 ? 0.35f : 1f);
        Audio.Update();
    }

    private void Update(float dt)
    {
        _swing = Math.Max(0, _swing - dt * 6);
        _ceilingFlash = Math.Max(0, _ceilingFlash - dt * 2);

        if (_over > 0)
        {
            _over += dt;
            if (_over > 4f || _over > 1f && _input.TryDequeue(out _))
            {
                _score = 0;
                _level = 1;
                NewBoard();
            }
            return;
        }

        var demo = AutoPilot && !_human;
        while (_input.TryDequeue(out var cmd))
        {
            if (cmd == "fire") Shoot();
            else if (cmd == "swap" && !_flying) DoSwap();
        }

        if (demo) RunAi(dt);
        else _ang = Math.Clamp(_ang + _hold * 1.6f * dt, -MathF.PI + 0.17f, -0.17f);

        if (_flying) Fly(dt);
    }

    private void DoSwap()
    {
        (_loaded, _next) = (_next, _loaded);
        Audio.Play(GameSfx.Select, 0.4f);
    }

    private void Shoot()
    {
        if (_flying || _over > 0) return;
        var speed = _d * (GameSpeed * 1.1f); // px per second
        _shotX = _launchX;
        _shotY = _launchY - _d / 2f;
        _shotVx = MathF.Cos(_ang) * speed;
        _shotVy = MathF.Sin(_ang) * speed;
        _shotColor = _loaded;
        _loaded = _next;
        _next = PickColor();
        _flying = true;
        _swing = 1;
        Audio.Play(GameSfx.Shoot, 0.4f, 0, 1.4f);
        _fx.Spray(_launchX + MathF.Cos(_ang) * _d, _launchY + MathF.Sin(_ang) * _d, _ang, 0.5f, Palette[_shotColor], 6, 1.5f, 0.3f, 0, true);
    }

    private void Fly(float dt)
    {
        // Sub-steps of at most a third of a bubble so it cannot tunnel through a gap.
        var dist = MathF.Sqrt(_shotVx * _shotVx + _shotVy * _shotVy) * dt;
        var steps = Math.Max(1, (int)MathF.Ceiling(dist / (_d / 3f)));
        for (var i = 0; i < steps; i++)
        {
            _shotX += _shotVx * dt / steps;
            _shotY += _shotVy * dt / steps;
            var left = _fieldX + _d / 2f;
            var right = _fieldX + _fieldW - _d / 2f;
            if (_shotX < left) { _shotX = left; _shotVx = Math.Abs(_shotVx); Bank(); }
            if (_shotX > right) { _shotX = right; _shotVx = -Math.Abs(_shotVx); Bank(); }
            if (_shotY <= _top + _d / 2f || Touches(_shotX, _shotY))
            {
                Land();
                return;
            }
        }
        if (_frameTrail++ % 2 == 0)
            _fx.Spark(_shotX, _shotY, 0, 0, Palette[_shotColor], 0.25f, 1, true);
    }

    private int _frameTrail;

    private void Bank()
    {
        Audio.Play(GameSfx.Bounce, 0.3f);
        _fx.Spray(_shotX, _shotY, _shotVx > 0 ? 0 : MathF.PI, 0.8f, SKColors.White, 4, 1f, 0.25f, 0, true);
    }

    private bool Touches(float x, float y)
    {
        var reach = _d * 0.86f;
        var r0 = Math.Max(0, (int)((y - _top) / _rowH) - 2);
        var r1 = Math.Min(_rows - 1, r0 + 4);
        for (var r = r0; r <= r1; r++)
        for (var c = 0; c < RowCols(r); c++)
        {
            if (_grid[c, r] == 0) continue;
            Center(c, r, out var cx, out var cy);
            if ((cx - x) * (cx - x) + (cy - y) * (cy - y) < reach * reach) return true;
        }
        return false;
    }

    /// <summary>The empty cell nearest to (x, y) that touches the ceiling or another bubble.</summary>
    private (int C, int R) SnapCell(float x, float y)
    {
        var best = (C: 0, R: 0);
        var bestD = float.MaxValue;
        var r0 = Math.Max(0, (int)((y - _top) / _rowH) - 2);
        var r1 = Math.Min(_rows - 1, r0 + 4);
        for (var r = r0; r <= r1; r++)
        for (var c = 0; c < RowCols(r); c++)
        {
            if (_grid[c, r] != 0) continue;
            var attached = r == 0 || Neighbours(c, r).Any(n => _grid[n.C, n.R] != 0);
            if (!attached) continue;
            Center(c, r, out var cx, out var cy);
            var dd = (cx - x) * (cx - x) + (cy - y) * (cy - y);
            if (dd < bestD) { bestD = dd; best = (c, r); }
        }
        return best;
    }

    private void Land()
    {
        _flying = false;
        var (c, r) = SnapCell(_shotX, _shotY);
        _grid[c, r] = _shotColor + 1;
        Center(c, r, out var x, out var y);
        var group = Flood(c, r);
        if (group.Count >= 3)
        {
            _combo++;
            foreach (var (gc, gr) in group)
            {
                Center(gc, gr, out var px, out var py);
                _fx.Burst(px, py, Palette[_grid[gc, gr] - 1], 8, 2f, 0.5f, 0.04f, true);
                _grid[gc, gr] = 0;
            }
            var dropped = DropFloating();
            var pts = group.Count * 10 + dropped * 20 * Math.Max(1, dropped / 3);
            _score += pts;
            _fx.Pop($"+{pts}", x, y, dropped > 0 ? new SKColor(255, 230, 120) : SKColors.White);
            Audio.Play(GameSfx.Pop, 0.8f, Pan(x), 1f + Math.Min(group.Count + dropped, 12) * 0.04f);
            if (dropped > 0) Audio.Play(GameSfx.Coin, 0.5f, Pan(x), 0.9f);
            if (group.Count + dropped >= 8) _fx.Shake(3);
            _misses = 0;
        }
        else
        {
            _combo = 0;
            Audio.Play(GameSfx.Thud, 0.4f, Pan(x));
            if (++_misses >= _dropEvery)
            {
                _misses = 0;
                PushRow();
            }
        }
        _best = Math.Max(_best, _score);

        if (Empty())
        {
            _level++;
            _score += 500;
            Audio.Play(GameSfx.LevelUp, 0.7f);
            _fx.Confetti(_launchX, Canvas.Height / 2f, 70, 3.5f);
            _fx.Flash(new SKColor(255, 240, 160), 0.5f);
            _fx.Pop($"LEVEL {_level}", _launchX, Canvas.Height / 2f, new SKColor(255, 240, 120));
            var keep = _score;
            NewBoard();
            _score = keep;
            return;
        }

        if (PastLine()) GameOver();
        // The colours left on the board decide what can come next.
        if (!ColorPresent(_loaded)) _loaded = PickColor();
        if (!ColorPresent(_next)) _next = PickColor();
        _aiAng = float.NaN;
    }

    private float Pan(float x) => Math.Clamp((x / Canvas.Width) * 2 - 1, -1, 1);

    private bool ColorPresent(int color)
    {
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < RowCols(r); c++)
            if (_grid[c, r] == color + 1) return true;
        return Empty();
    }

    private bool Empty()
    {
        foreach (var v in _grid)
            if (v != 0) return false;
        return true;
    }

    private List<(int C, int R)> Flood(int c, int r) => Flood(_grid, c, r);

    private List<(int C, int R)> Flood(int[,] grid, int c, int r)
    {
        var color = grid[c, r];
        var list = new List<(int, int)>();
        if (color == 0) return list;
        var seen = new HashSet<(int, int)> { (c, r) };
        var q = new Queue<(int, int)>();
        q.Enqueue((c, r));
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            list.Add(cur);
            foreach (var n in Neighbours(cur.Item1, cur.Item2))
                if (grid[n.C, n.R] == color && seen.Add(n))
                    q.Enqueue(n);
        }
        return list;
    }

    /// <summary>Removes everything that no longer hangs from the ceiling; it falls as particles.</summary>
    private int DropFloating(int[,]? grid = null, bool effects = true)
    {
        grid ??= _grid;
        var hang = new bool[_cols, _rows];
        var q = new Queue<(int, int)>();
        for (var c = 0; c < RowCols(0); c++)
            if (grid[c, 0] != 0)
            {
                hang[c, 0] = true;
                q.Enqueue((c, 0));
            }
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var n in Neighbours(c, r))
            {
                if (hang[n.C, n.R] || grid[n.C, n.R] == 0) continue;
                hang[n.C, n.R] = true;
                q.Enqueue(n);
            }
        }
        var dropped = 0;
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < RowCols(r); c++)
        {
            if (grid[c, r] == 0 || hang[c, r]) continue;
            if (effects)
            {
                Center(c, r, out var x, out var y);
                _fx.Spark(x, y, _fx.Rand(-0.8f, 0.8f), _fx.Rand(-1.2f, -0.2f), Palette[grid[c, r] - 1], 1.2f, _d - 1, false,
                    0.25f, null, 0);
            }
            grid[c, r] = 0;
            dropped++;
        }
        return dropped;
    }

    /// <summary>The ceiling pushes a new row in from the top.</summary>
    private void PushRow()
    {
        var old = _grid;
        _grid = new int[_cols, _rows];
        _parity ^= 1;
        for (var r = 1; r < _rows; r++)
        for (var c = 0; c < RowCols(r); c++)
            _grid[c, r] = c < old.GetLength(0) ? old[c, r - 1] : 0;
        for (var c = 0; c < RowCols(0); c++)
            _grid[c, 0] = 1 + _random.Next(_colors);
        _ceilingFlash = 1;
        _fx.Shake(2);
        Audio.Play(GameSfx.Drop, 0.6f, 0, 0.7f);
        if (PastLine()) GameOver();
    }

    private bool PastLine()
    {
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < RowCols(r); c++)
        {
            if (_grid[c, r] == 0) continue;
            Center(c, r, out _, out var y);
            if (y + _d / 2f > _deadY) return true;
        }
        return false;
    }

    private void GameOver()
    {
        if (_over > 0) return;
        _over = 0.001f;
        _flying = false;
        Audio.Play(GameSfx.GameOver, 0.7f);
        _fx.Flash(new SKColor(255, 60, 80), 0.6f);
        _fx.Shake(5);
    }

    // ------------------------------------------------------------------ autopilot

    private void RunAi(float dt)
    {
        if (_flying) return;
        if (float.IsNaN(_aiAng))
        {
            _aiWait = 0.25f; // a short look before aiming, like a person
            Plan();
        }
        if (_aiWait > 0)
        {
            _aiWait -= dt;
            return;
        }
        if (_aiSwap)
        {
            DoSwap();
            _aiSwap = false;
            return;
        }
        var diff = _aiAng - _ang;
        var turn = 1.8f * dt;
        if (MathF.Abs(diff) <= turn)
        {
            _ang = _aiAng;
            Shoot();
            _aiAng = float.NaN;
        }
        else _ang += MathF.Sign(diff) * turn;
    }

    /// <summary>Every angle, with the loaded and with the next bubble: where it lands and what it achieves.</summary>
    private void Plan()
    {
        var bestScore = float.MinValue;
        var bestAng = -MathF.PI / 2;
        var bestSwap = false;
        for (var swap = 0; swap < 2; swap++)
        {
            var color = swap == 0 ? _loaded : _next;
            if (swap == 1 && _next == _loaded) break;
            for (var a = -MathF.PI + 0.2f; a <= -0.2f; a += 0.02f)
            {
                if (!Trace(a, out var cell)) continue;
                var s = Evaluate(cell.C, cell.R, color) - (swap == 1 ? 1.5f : 0) - MathF.Abs(a - _ang) * 0.5f;
                if (s > bestScore)
                {
                    bestScore = s;
                    bestAng = a;
                    bestSwap = swap == 1;
                }
            }
        }
        _aiAng = bestAng;
        _aiSwap = bestSwap;
    }

    private bool Trace(float ang, out (int C, int R) cell)
    {
        float x = _launchX, y = _launchY - _d / 2f;
        float vx = MathF.Cos(ang), vy = MathF.Sin(ang);
        var step = _d / 3f;
        var left = _fieldX + _d / 2f;
        var right = _fieldX + _fieldW - _d / 2f;
        for (var i = 0; i < 600; i++)
        {
            x += vx * step;
            y += vy * step;
            if (x < left) { x = left; vx = Math.Abs(vx); }
            if (x > right) { x = right; vx = -Math.Abs(vx); }
            if (y <= _top + _d / 2f || Touches(x, y))
            {
                cell = SnapCell(x, y);
                return true;
            }
        }
        cell = default;
        return false;
    }

    private float Evaluate(int c, int r, int color)
    {
        var g = (int[,])_grid.Clone();
        g[c, r] = color + 1;
        var group = Flood(g, c, r);
        Center(c, r, out _, out var y);
        var depth = y / _deadY; // 0 top .. 1 at the line
        if (group.Count >= 3)
        {
            foreach (var (gc, gr) in group) g[gc, gr] = 0;
            var dropped = DropFloating(g, false);
            return 100 + group.Count * 10 + dropped * 18 + depth * 5;
        }
        // No pop: build towards one (same colour neighbours), stay high, never past the line.
        if (y + _d / 2f > _deadY) return -1000;
        var same = Neighbours(c, r).Count(n => g[n.C, n.R] == color + 1);
        var other = Neighbours(c, r).Count(n => g[n.C, n.R] != 0 && g[n.C, n.R] != color + 1);
        return same * 12 - other * 2 - depth * 25;
    }

    // ------------------------------------------------------------------ drawing

    private void Draw(SKCanvas canvas, int w, int h)
    {
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        GameFx.VerticalGradient(canvas, w, h, new SKColor(30, 12, 64), new SKColor(6, 12, 40));
        // Background bubbles drifting up outside the well.
        for (var i = 0; i < 18; i++)
        {
            var speed = 6 + i % 5 * 3;
            var bx = i * 53 % w + MathF.Sin(_fx.Time * 0.8f + i) * 4;
            var by = h - (_fx.Time * speed + i * 37) % (h + 20) + 10;
            paint.Color = new SKColor(120, 160, 255, 40);
            PixelArt.Ring(canvas, paint, (int)bx, (int)by, 2 + i % 3);
        }

        // The well: dark glass with a glowing rim; the dead line pulses when bubbles get close.
        paint.Color = new SKColor(6, 8, 24, 200);
        canvas.DrawRect(_fieldX, 0, _fieldW, h, paint);
        GameFx.Glow(canvas, _fieldX - 1, 0, 1, h, new SKColor(90, 140, 255), 3, 0.35f);
        GameFx.Glow(canvas, _fieldX + _fieldW, 0, 1, h, new SKColor(90, 140, 255), 3, 0.35f);
        paint.Color = new SKColor(120, 170, 255);
        canvas.DrawRect(_fieldX - 1, 0, 1, h, paint);
        canvas.DrawRect(_fieldX + _fieldW, 0, 1, h, paint);
        // Ceiling bar (flashes when it pushes a row in).
        paint.Color = GameFx.Lerp(new SKColor(80, 90, 140), new SKColor(255, 240, 200), _ceilingFlash);
        canvas.DrawRect(_fieldX, 0, _fieldW, _top, paint);

        var danger = Danger();
        var pulse = 0.5f + 0.5f * MathF.Sin(_fx.Time * (4 + danger * 10));
        for (var x = _fieldX; x < _fieldX + _fieldW; x += 4)
        {
            paint.Color = new SKColor(255, 60, 80, (byte)(60 + 160 * danger * pulse + 30));
            canvas.DrawRect(x, _deadY, 2, 1, paint);
        }

        _fx.BeginShake(canvas);
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < RowCols(r); c++)
        {
            if (_grid[c, r] == 0) continue;
            Center(c, r, out var x, out var y);
            var col = Palette[_grid[c, r] - 1];
            if (_over > 0)
            {
                // Game over: they go grey from the bottom up.
                var t = Math.Clamp(_over * 1.5f - (_rows - r) * 0.12f, 0, 1);
                col = GameFx.Lerp(col, new SKColor(70, 70, 80), t);
            }
            var wob = MathF.Sin(_fx.Time * 2.2f + c * 0.7f + r * 1.3f) * 0.5f;
            Bubble(canvas, paint, x, y + wob, col);
        }

        if (_flying)
        {
            GameFx.GlowDisc(canvas, _shotX, _shotY, _d * 1.2f, Palette[_shotColor], 0.35f);
            Bubble(canvas, paint, _shotX, _shotY, Palette[_shotColor]);
        }

        DrawLauncher(canvas, paint);
        _fx.DrawParticles(canvas);
        _fx.EndShake(canvas);

        // Side panels: score on the left, next bubble and the countdown to the next row on the right.
        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(7f, h * 0.075f));
            var lx = _fieldX / 2f;
            Text(canvas, "SCORE", lx, size + 2, size, new SKColor(150, 180, 255));
            Text(canvas, _score.ToString(), lx, size * 2 + 4, size, SKColors.White);
            Text(canvas, "LEVEL", lx, size * 3 + 10, size, new SKColor(150, 180, 255));
            Text(canvas, _level.ToString(), lx, size * 4 + 12, size, SKColors.White);
            Text(canvas, "HI", lx, size * 5 + 18, size, new SKColor(255, 200, 80));
            Text(canvas, _best.ToString(), lx, size * 6 + 20, size, new SKColor(255, 220, 120));

            var rx = _fieldX + _fieldW + (w - _fieldX - _fieldW) / 2f;
            Text(canvas, "NEXT", rx, size + 2, size, new SKColor(150, 180, 255));
            Bubble(canvas, paint, rx, size * 2 + 6, Palette[_next]);
            Text(canvas, "ROW IN", rx, size * 4 + 6, size, new SKColor(150, 180, 255));
            var left = _dropEvery - _misses;
            for (var i = 0; i < _dropEvery; i++)
            {
                paint.Color = i < left ? new SKColor(120, 255, 170) : new SKColor(60, 40, 70);
                canvas.DrawRect(rx - _dropEvery * 3 / 2f + i * 3, size * 4 + 10, 2, 2, paint);
            }
        }

        if (_over > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.12f));
            Text(canvas, "GAME OVER", _launchX, h * 0.45f, size, new SKColor(255, 90, 110));
        }

        _fx.DrawOverlay(canvas, Canvas, w, h, Math.Max(7f, h * 0.07f));
    }

    /// <summary>0 when the lowest bubble is far from the line, 1 when it is a row away.</summary>
    private float Danger()
    {
        var low = 0f;
        for (var r = _rows - 1; r >= 0 && low == 0; r--)
        for (var c = 0; c < RowCols(r); c++)
            if (_grid[c, r] != 0)
            {
                Center(c, r, out _, out var y);
                low = y;
                break;
            }
        return Math.Clamp(1 - (_deadY - low) / (_rowH * 4f), 0, 1);
    }

    private void Text(SKCanvas c, string s, float x, float y, float size, SKColor color)
    {
        CanvasText.Draw(c, Canvas, s, new SKColor(0, 0, 0, 170), x + 1, y + 1, size, SKTextAlign.Center, UseBdfFont);
        CanvasText.Draw(c, Canvas, s, color, x, y, size, SKTextAlign.Center, UseBdfFont);
    }

    private void DrawLauncher(SKCanvas canvas, SKPaint paint)
    {
        var cx = _launchX;
        var cy = _launchY;
        // Aim line with a bank shot preview (dotted, marching).
        if (!_flying && _over <= 0)
        {
            float x = cx, y = cy - _d / 2f, vx = MathF.Cos(_ang), vy = MathF.Sin(_ang);
            var left = _fieldX + _d / 2f;
            var right = _fieldX + _fieldW - _d / 2f;
            var march = (int)(_fx.Time * 20) % 4;
            for (var i = 0; i < 160; i++)
            {
                x += vx * 2;
                y += vy * 2;
                if (x < left) { x = left; vx = Math.Abs(vx); }
                if (x > right) { x = right; vx = -Math.Abs(vx); }
                if (y < _top + _d / 2f || Touches(x, y)) break;
                if ((i + march) % 4 != 0) continue;
                paint.Color = new SKColor(255, 255, 255, (byte)Math.Max(40, 200 - i));
                canvas.DrawRect(MathF.Round(x), MathF.Round(y), 1, 1, paint);
            }
        }

        // Base, barrel (recoils on a shot) and the loaded bubble in it.
        paint.Color = new SKColor(60, 70, 110);
        canvas.DrawRect(cx - _d, cy, _d * 2, 6, paint);
        paint.Color = new SKColor(120, 140, 200);
        canvas.DrawRect(cx - _d, cy, _d * 2, 1, paint);
        var len = _d * 1.4f - _swing * 2;
        for (var t = 0f; t < len; t += 0.5f)
        {
            var bx = cx + MathF.Cos(_ang) * t;
            var by = cy + MathF.Sin(_ang) * t;
            paint.Color = new SKColor(200, 210, 240);
            canvas.DrawRect(MathF.Round(bx) - 1, MathF.Round(by) - 1, 3, 3, paint);
        }
        if (!_flying && _over <= 0) Bubble(canvas, paint, cx, cy - 1, Palette[_loaded]);

        // The dino operator, left of the launcher, cheering after a big pop.
        var dpx = Math.Max(1, Canvas.Height / 128);
        var dw = Dino[0].Length * dpx;
        var dh = Dino.Length * dpx;
        var hop = _combo > 1 && (int)(_fx.Time * 6) % 2 == 0 ? 2 : 0;
        PixelArt.Blit(canvas, (int)(_fx.Time * 4) % 2 == 0 ? Dino : DinoB, cx - _d * 1.5f - dw, Canvas.Height - dh - hop, ChDino, dpx);
    }

    /// <summary>Shaded bubble images by (diameter, colour); a frame draws ~150 bubbles, each one call.</summary>
    private readonly Dictionary<(int D, SKColor Col), SKImage> _bubbleImages = new();

    private void Bubble(SKCanvas canvas, SKPaint paint, float x, float y, SKColor col)
    {
        var img = BubbleImage(col);
        var r = _d / 2;
        canvas.DrawImage(img, (int)MathF.Round(x) - r, (int)MathF.Round(y) - r);
    }

    private SKImage BubbleImage(SKColor col)
    {
        if (_bubbleImages.TryGetValue((_d, col), out var img)) return img;
        // Game over fades every bubble through grey, so the set can grow; start over when it gets big.
        if (_bubbleImages.Count >= 96) ClearBubbleImages();

        // Round at small sizes: a row-by-row disc (r² + r keeps the edge from going spiky), shaded from a
        // bright top-left to a darker rim at the bottom right, with a shine.
        var r = _d / 2;
        var size = 2 * r + 1;
        var rr = r * r + r;
        using var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Transparent);
        for (var yy = -r; yy <= r; yy++)
        for (var xx = -r; xx <= r; xx++)
        {
            var dd = xx * xx + yy * yy;
            if (dd > rr) continue;
            var light = 1.05f - (xx + yy) / (2.2f * r) * 0.35f - dd / (float)rr * 0.25f;
            var edge = dd > (r - 1) * (r - 1) + (r - 1) && xx + yy > 0;
            bmp.SetPixel(xx + r, yy + r, edge ? GameFx.Shade(col, 0.5f) : GameFx.Shade(col, light));
        }
        using (var c = new SKCanvas(bmp))
        using (var shine = new SKPaint { Color = new SKColor(255, 255, 255, 230) })
        {
            var sh = Math.Max(1, r / 3);
            c.DrawRect(r - r / 2, r - r / 2, sh, sh, shine);
        }
        img = SKImage.FromBitmap(bmp);
        _bubbleImages[(_d, col)] = img;
        return img;
    }

    private void ClearBubbleImages()
    {
        foreach (var i in _bubbleImages.Values) i.Dispose();
        _bubbleImages.Clear();
    }

    // d outline, g body, l belly, w/k eye, o snout, y horn, b boot
    private static readonly string[] Dino =
    {
        "..........ddyydd........",
        ".........dyyyyyyd.......",
        "........dyyywyyyyd......",
        "......dddggggggggdd.....",
        ".....dgggggggggggggd....",
        "....dggwwkggggggggggd...",
        "...dggggkkgggggggggggd..",
        "...dggggggggooooooogd...",
        "....dgggggggoooooogd....",
        ".....dllllllggggggd.....",
        "....dllllllllggggd......",
        "....dgggggggggggd.......",
        "...dgggd..dggggd........",
        "..dbbd.....dbbd.........",
        "..dd........dd.........."
    };

    private static readonly string[] DinoB =
    {
        "..........ddyydd........",
        ".........dyyyyyyd.......",
        "........dyyywyyyyd......",
        "......dddggggggggdd.....",
        ".....dgggggggggggggd....",
        "....dggwwkggggggggggd...",
        "...dggggkkgggggggggggd..",
        "...dggggggggooooooogd...",
        "....dgggggggoooooogd....",
        ".....dllllllggggggd.....",
        "....dllllllllggggd......",
        "....dgggggggggggd.......",
        ".....dggd.dggggd........",
        "......dbd..dbd..........",
        ".......d....d..........."
    };

    private static SKColor ChDino(char ch) => ch switch
    {
        'd' => new SKColor(18, 28, 16),
        'g' => new SKColor(72, 220, 64),
        'l' => new SKColor(180, 255, 90),
        'w' => SKColors.White,
        'k' => new SKColor(16, 16, 16),
        'o' => new SKColor(255, 120, 48),
        'y' => new SKColor(255, 214, 48),
        'b' => new SKColor(96, 48, 20),
        _ => SKColors.Transparent
    };
}
