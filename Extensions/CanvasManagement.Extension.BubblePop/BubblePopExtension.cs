using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.BubblePop;

/// <summary>
///     Puzzle-bobble-style shooter: a pastel dino fires colored bubbles. Match 3+ to pop.
///     Autopilot until a key is pressed in Studio.
/// </summary>
[ExtensionInfo("Bubble Pop",
    "Dino bubble-shooter — Left/Right aim, Space shoot, or autopilot",
    "Games",
    IconResourceName = "bubble-pop.svg")]
public class BubblePopExtension : ICanvasExtension, IDisposable
{
    private static readonly SKColor[] Palette =
    {
        new(255, 70, 110),
        new(255, 170, 40),
        new(255, 230, 60),
        new(60, 230, 140),
        new(70, 190, 255),
        new(170, 90, 255)
    };

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _px = 2;
    private int _frame;
    private int _cols, _rows;
    private int[,] _grid = new int[1, 1];
    private float _r, _ox, _oy, _rowH;
    private float _ang;
    private int _hold;
    private bool _human;
    private int _next, _loaded;
    private float _shotX, _shotY, _shotVx, _shotVy;
    private int _shotColor;
    private bool _flying;
    private int _shots;
    private int _score, _best;
    private int _crashTimer;
    private int _dropEvery;

    internal BubblePopExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 22,
        MinValue = 14, MaxValue = 50, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 22;

    [ExtensionParameter("Difficulty", "How often the ceiling drops", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show the HUD", DefaultValue = true, Order = 3)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = false, Order = 4)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI aims until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    public string Name => "Bubble Pop";
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
            _px = PixelArt.Scale(_canvas.Height);
            _human = false;
            _score = 0;
            NewGame();
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

    private void NewGame()
    {
        _r = Math.Max(5, _canvas.Width / 38f);
        _rowH = _r * 1.72f;
        _cols = Math.Max(8, (int)(_canvas.Width / (_r * 2.05f)));
        _rows = Math.Max(10, (int)(_canvas.Height / _rowH) - 1);
        _ox = (_canvas.Width - (_cols * 2 * _r - _r)) / 2f + _r;
        _oy = _r + 2;
        _grid = new int[_cols, _rows];
        var fill = Math.Min(5, 3 + Difficulty / 3);
        for (var r = 0; r < fill; r++)
        for (var c = 0; c < _cols - (r % 2); c++)
            _grid[c, r] = _random.Next(Palette.Length) + 1;
        _ang = -MathF.PI / 2;
        _loaded = _random.Next(Palette.Length);
        _next = _random.Next(Palette.Length);
        _flying = false;
        _shots = 0;
        _dropEvery = Math.Max(6, 14 - Difficulty);
        _crashTimer = 0;
        _hold = 0;
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
                    if (_crashTimer == 0) NewGame();
                }
                else
                    Update();

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BubblePop] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        if (AutoPilot && !_human) RunAi();
        else
        {
            _ang += _hold * 0.045f;
            _ang = Math.Clamp(_ang, -MathF.PI + 0.25f, -0.25f);
        }

        if (_flying)
        {
            _shotX += _shotVx;
            _shotY += _shotVy;
            if (_shotX < _r) { _shotX = _r; _shotVx = Math.Abs(_shotVx); }
            if (_shotX > _canvas.Width - _r) { _shotX = _canvas.Width - _r; _shotVx = -Math.Abs(_shotVx); }
            if (_shotY < _r) SnapShot();
            else if (HitsOccupied()) SnapShot();
            else if (_shotY > _canvas.Height - _r) _flying = false;
        }
    }

    private void RunAi()
    {
        if (_flying) return;
        var best = _ang;
        var bestN = -1;
        for (var a = -MathF.PI + 0.3f; a < -0.3f; a += 0.08f)
        {
            var n = Simulate(a);
            if (n > bestN) { bestN = n; best = a; }
        }

        if (Math.Abs(best - _ang) < 0.05f) Shoot();
        else _ang += Math.Sign(best - _ang) * 0.06f;
    }

    private int Simulate(float ang)
    {
        var x = _canvas.Width / 2f;
        var y = _canvas.Height - Dino.Length * Math.Max(1, _px) - _r - 2;
        var vx = MathF.Cos(ang) * _r * 0.55f;
        var vy = MathF.Sin(ang) * _r * 0.55f;
        for (var i = 0; i < 80; i++)
        {
            x += vx;
            y += vy;
            if (x < _r) vx = Math.Abs(vx);
            if (x > _canvas.Width - _r) vx = -Math.Abs(vx);
            if (y < _r || HitsAt(x, y))
            {
                Nearest(x, y, out var c, out var r);
                return CountMatch(c, r, _loaded + 1);
            }
        }

        return 0;
    }

    private bool HitsOccupied()
    {
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < _cols; c++)
        {
            if (_grid[c, r] == 0) continue;
            Pos(c, r, out var x, out var y);
            var dx = x - _shotX;
            var dy = y - _shotY;
            if (dx * dx + dy * dy < (_r * 1.75f) * (_r * 1.75f)) return true;
        }

        return false;
    }

    private bool HitsAt(float sx, float sy)
    {
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < _cols; c++)
        {
            if (_grid[c, r] == 0) continue;
            Pos(c, r, out var x, out var y);
            var dx = x - sx;
            var dy = y - sy;
            if (dx * dx + dy * dy < (_r * 1.75f) * (_r * 1.75f)) return true;
        }

        return false;
    }

    private void Pos(int c, int r, out float x, out float y)
    {
        x = _ox + c * _r * 2f + (r % 2 == 1 ? _r : 0);
        y = _oy + r * _rowH;
    }

    private void Nearest(float x, float y, out int bc, out int br)
    {
        bc = 0;
        br = 0;
        var best = float.MaxValue;
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < _cols - (r % 2); c++)
        {
            if (_grid[c, r] != 0) continue;
            Pos(c, r, out var px, out var py);
            var d = (px - x) * (px - x) + (py - y) * (py - y);
            if (d < best) { best = d; bc = c; br = r; }
        }
    }

    private void SnapShot()
    {
        _flying = false;
        Nearest(_shotX, _shotY, out var c, out var r);
        _grid[c, r] = _shotColor + 1;
        var group = Flood(c, r, _shotColor + 1);
        if (group.Count >= 3)
        {
            foreach (var (gc, gr) in group) _grid[gc, gr] = 0;
            _score += group.Count * 15;
            DropOrphans();
        }

        _best = Math.Max(_best, _score);
        _loaded = _next;
        _next = _random.Next(Palette.Length);
        _shots++;
        if (_shots % _dropEvery == 0) DropCeiling();
        if (BottomReached()) _crashTimer = 40;
    }

    private List<(int, int)> Flood(int sc, int sr, int color)
    {
        var list = new List<(int, int)>();
        var seen = new bool[_cols, _rows];
        var q = new Queue<(int, int)>();
        q.Enqueue((sc, sr));
        seen[sc, sr] = true;
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            if (_grid[c, r] != color) continue;
            list.Add((c, r));
            foreach (var (nc, nr) in Neigh(c, r))
            {
                if (seen[nc, nr]) continue;
                seen[nc, nr] = true;
                q.Enqueue((nc, nr));
            }
        }

        return list;
    }

    private int CountMatch(int c, int r, int color)
    {
        if (c < 0 || r < 0) return 0;
        var n = 0;
        foreach (var (nc, nr) in Neigh(c, r))
            if (_grid[nc, nr] == color) n++;
        return n + 1;
    }

    private IEnumerable<(int, int)> Neigh(int c, int r)
    {
        var odd = r % 2;
        var d = new (int dc, int dr)[]
        {
            (-1, 0), (1, 0),
            (-1 + odd, -1), (0 + odd, -1),
            (-1 + odd, 1), (0 + odd, 1)
        };
        foreach (var (dc, dr) in d)
        {
            var nc = c + dc;
            var nr = r + dr;
            if (nc >= 0 && nr >= 0 && nr < _rows && nc < _cols - (nr % 2))
                yield return (nc, nr);
        }
    }

    private void DropOrphans()
    {
        var hang = new bool[_cols, _rows];
        var q = new Queue<(int, int)>();
        for (var c = 0; c < _cols; c++)
            if (_grid[c, 0] != 0)
            {
                hang[c, 0] = true;
                q.Enqueue((c, 0));
            }

        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var (nc, nr) in Neigh(c, r))
            {
                if (hang[nc, nr] || _grid[nc, nr] == 0) continue;
                hang[nc, nr] = true;
                q.Enqueue((nc, nr));
            }
        }

        var n = 0;
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < _cols; c++)
            if (_grid[c, r] != 0 && !hang[c, r])
            {
                _grid[c, r] = 0;
                n++;
            }

        _score += n * 25;
    }

    private void DropCeiling()
    {
        for (var r = _rows - 1; r > 0; r--)
        for (var c = 0; c < _cols; c++)
            _grid[c, r] = _grid[c, r - 1];
        for (var c = 0; c < _cols; c++)
            _grid[c, 0] = _random.Next(Palette.Length) + 1;
    }

    private bool BottomReached()
    {
        var limit = _rows - 3;
        for (var c = 0; c < _cols; c++)
            if (_grid[c, limit] != 0) return true;
        return false;
    }

    private void Shoot()
    {
        if (_flying || _crashTimer > 0) return;
        var sp = _r * 0.7f;
            _shotX = _canvas.Width / 2f;
            _shotY = _canvas.Height - Dino.Length * Math.Max(1, _px) - _r - 2;
        _shotVx = MathF.Cos(_ang) * sp;
        _shotVy = MathF.Sin(_ang) * sp;
        _shotColor = _loaded;
        _flying = true;
    }

    [ExtensionMethod("Aim Left", "Rotate aim left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 1)]
    public void AimLeft()
    {
        lock (_lock) { _human = true; _hold = -1; }
    }

    [ExtensionMethod("Aim Right", "Rotate aim right",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 2)]
    public void AimRight()
    {
        lock (_lock) { _human = true; _hold = 1; }
    }

    [ExtensionMethod("Release Left", "Release left",
        Category = "Controls", KeyboardShortcut = "Left:up|A:up", Order = 3)]
    public void ReleaseLeft()
    {
        lock (_lock) { if (_hold < 0) _hold = 0; }
    }

    [ExtensionMethod("Release Right", "Release right",
        Category = "Controls", KeyboardShortcut = "Right:up|D:up", Order = 4)]
    public void ReleaseRight()
    {
        lock (_lock) { if (_hold > 0) _hold = 0; }
    }

    [ExtensionMethod("Shoot", "Fire the loaded bubble",
        Category = "Controls", KeyboardShortcut = "Space|Up", Order = 5)]
    public void Fire()
    {
        lock (_lock)
        {
            _human = true;
            Shoot();
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var canvas = new SKCanvas(bb);
        using (var sky = new SKPaint())
        {
            sky.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, _canvas.Height),
                new[] { new SKColor(40, 16, 70), new SKColor(12, 8, 28) },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, sky);
        }

        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < _cols; c++)
        {
            if (_grid[c, r] == 0) continue;
            Pos(c, r, out var x, out var y);
            DrawBubble(canvas, paint, x, y, Palette[_grid[c, r] - 1]);
        }

        if (_flying)
            DrawBubble(canvas, paint, _shotX, _shotY, Palette[_shotColor]);

        var ax = (int)_canvas.Width / 2;
        var dinoPx = Math.Max(1, _px);
        var dw = Dino[0].Length * dinoPx;
        var dh = Dino.Length * dinoPx;
        var dx = ax - dw / 2f;
        var dy = _canvas.Height - dh - 1;
        var muzzleX = ax + 6 * dinoPx;
        var muzzleY = dy + 8 * dinoPx;
        PixelArt.Blit(canvas, _frame / 8 % 2 == 0 ? Dino : DinoB, dx, dy, ChDino, dinoPx);
        var lx = muzzleX + MathF.Cos(_ang) * _r * 3.4f;
        var ly = muzzleY + MathF.Sin(_ang) * _r * 3.4f;
        using var aim = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = false
        };
        canvas.DrawLine(muzzleX, muzzleY, lx, ly, aim);

        DrawBubble(canvas, paint, ax, dy - _r - 1, Palette[_loaded]);
        DrawBubble(canvas, paint, 10 + _r, _canvas.Height - _r - 2, Palette[_next]);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, _canvas.Height * 0.08f));
            CanvasText.Draw(canvas, _canvas, $"{_score}", SKColors.White,
                4, size + 1, size, SKTextAlign.Left, UseBdfFont);
        }

        if (_crashTimer > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(12f, _canvas.Height * 0.14f));
            CanvasText.Draw(canvas, _canvas, "POPPED", SKColors.White,
                _canvas.Width / 2f, _canvas.Height * 0.5f, size, SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
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

    private void DrawBubble(SKCanvas canvas, SKPaint paint, float x, float y, SKColor col)
    {
        var cx = (int)MathF.Round(x);
        var cy = (int)MathF.Round(y);
        var r = Math.Max(4, (int)MathF.Round(_r));
        paint.Color = new SKColor(20, 12, 28);
        PixelArt.Disc(canvas, paint, cx, cy, r);
        paint.Color = col;
        PixelArt.Disc(canvas, paint, cx, cy, r - 1);
        paint.Color = new SKColor(255, 255, 255, 200);
        canvas.DrawRect(cx - r + 2, cy - r + 2, 2, 2, paint);
        paint.Color = col.WithAlpha(70);
        PixelArt.Ring(canvas, paint, cx, cy, r);
    }
}
