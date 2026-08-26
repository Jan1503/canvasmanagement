using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.RainbowBreakout;

/// <summary>
///     Neon Breakout: rainbow brick rows, glowing ball, power-ups. Autopilot until a key is pressed.
/// </summary>
[ExtensionInfo("Rainbow Breakout",
    "Neon brick-breaker — Left/Right + Space, or autopilot",
    "Games",
    IconResourceName = "rainbow-breakout.svg")]
public class RainbowBreakoutExtension : ICanvasExtension, IDisposable
{
    private static readonly SKColor[] Rainbow =
    {
        new(255, 60, 90),
        new(255, 140, 30),
        new(255, 220, 50),
        new(50, 230, 110),
        new(50, 190, 255),
        new(160, 80, 255),
        new(255, 80, 200)
    };

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Brick> _bricks = new();
    private readonly List<Ball> _balls = new();
    private readonly List<Drop> _drops = new();
    private readonly List<Spark> _sparks = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _px = 2;
    private int _frame;
    private float _padX, _padW, _padH, _padY, _padSpd;
    private int _hold;
    private bool _human;
    private bool _wide, _laser;
    private int _wideTimer, _laserTimer;
    private int _score, _best, _lives, _level;
    private int _crashTimer;
    private bool _served;

    internal RainbowBreakoutExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 16,
        MinValue = 12, MaxValue = 40, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 16;

    [ExtensionParameter("Difficulty", "Ball speed and brick density", DefaultValue = 3, MinValue = 1, MaxValue = 10,
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

    public string Name => "Rainbow Breakout";
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
            _hold = 0;
            _level = 1;
            _lives = 3;
            _score = 0;
            NewBoard();
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

    private void NewBoard()
    {
        _padH = 5;
        _padW = Math.Max(28, _canvas.Width * 0.18f);
        _padY = _canvas.Height - Math.Max(8, 5 * _px);
        _padX = (_canvas.Width - _padW) / 2f;
        _padSpd = Math.Max(2.5f, (3f + Difficulty * 0.25f) * _px);
        _wide = _laser = false;
        _wideTimer = _laserTimer = 0;
        _served = false;
        _bricks.Clear();
        _balls.Clear();
        _drops.Clear();
        _sparks.Clear();
        _crashTimer = 0;

        var cols = Math.Clamp(_canvas.Width / 18, 10, 14);
        var rows = Math.Clamp(5 + _level / 2, 5, 7);
        var gap = 2;
        var bw = (_canvas.Width - gap * (cols + 1)) / (float)cols;
        var bh = 8f;
        var top = 14;
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            if (_random.Next(100) < 6) continue;
            _bricks.Add(new Brick
            {
                X = gap + c * (bw + gap),
                Y = top + r * (bh + gap),
                W = bw,
                H = bh,
                Hits = r == 0 && _level > 2 ? 2 : 1,
                Color = Rainbow[r % Rainbow.Length]
            });
        }

        StickBall();
    }

    private void StickBall()
    {
        _balls.Clear();
        _balls.Add(new Ball
        {
            X = _padX + PadW() / 2f,
            Y = _padY - 4 * _px,
            Vx = 0,
            Vy = 0,
                R = 3
        });
        _served = false;
    }

    private float PadW() => _wide ? _padW * 1.45f : _padW;

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
                        if (_lives <= 0) { _lives = 3; _level = 1; _score = 0; }
                        NewBoard();
                    }
                }
                else
                    Update();

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RainbowBreakout] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        if (_wideTimer > 0 && --_wideTimer == 0) _wide = false;
        if (_laserTimer > 0 && --_laserTimer == 0) _laser = false;

        var want = 0;
        if (AutoPilot && !_human) RunAi(out want);
        else want = _hold;
        _padX = Math.Clamp(_padX + want * _padSpd, 0, _canvas.Width - PadW());

        if (!_served)
        {
            var b = _balls[0];
            b.X = _padX + PadW() / 2f;
            b.Y = _padY - b.R - 1;
            _balls[0] = b;
            if (AutoPilot && !_human) Serve();
            TickDrops();
            return;
        }

        var speed = Math.Max(2.2f, (2.1f + Difficulty * 0.18f + _level * 0.12f) * _px);
        for (var i = _balls.Count - 1; i >= 0; i--)
        {
            var b = _balls[i];
            var mag = MathF.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
            if (mag > 0.1f)
            {
                b.Vx = b.Vx / mag * speed;
                b.Vy = b.Vy / mag * speed;
            }

            b.X += b.Vx;
            b.Y += b.Vy;
            if (b.X - b.R < 0) { b.X = b.R; b.Vx = Math.Abs(b.Vx); }
            if (b.X + b.R > _canvas.Width) { b.X = _canvas.Width - b.R; b.Vx = -Math.Abs(b.Vx); }
            if (b.Y - b.R < 0) { b.Y = b.R; b.Vy = Math.Abs(b.Vy); }

            if (b.Y + b.R >= _padY && b.Y - b.R <= _padY + _padH &&
                b.X >= _padX - b.R && b.X <= _padX + PadW() + b.R && b.Vy > 0)
            {
                var hit = (b.X - (_padX + PadW() / 2f)) / (PadW() / 2f);
                b.Vy = -Math.Abs(b.Vy);
                b.Vx += hit * speed * 0.55f;
                b.Y = _padY - b.R - 1;
            }

            HitBricks(ref b);

            if (b.Y - b.R > _canvas.Height + 4)
                _balls.RemoveAt(i);
            else
                _balls[i] = b;
        }

        if (_balls.Count == 0)
        {
            _lives--;
            _crashTimer = 28;
            return;
        }

        TickDrops();

        if (_bricks.Count == 0)
        {
            _level++;
            _score += 100;
            NewBoard();
        }
    }

    private void HitBricks(ref Ball b)
    {
        for (var i = _bricks.Count - 1; i >= 0; i--)
        {
            var br = _bricks[i];
            if (b.X + b.R < br.X || b.X - b.R > br.X + br.W || b.Y + b.R < br.Y || b.Y - b.R > br.Y + br.H)
                continue;
            var cx = br.X + br.W / 2f;
            var cy = br.Y + br.H / 2f;
            if (Math.Abs(b.X - cx) * br.H > Math.Abs(b.Y - cy) * br.W)
                b.Vx = -b.Vx;
            else
                b.Vy = -b.Vy;
            br.Hits--;
            Burst(b.X, b.Y, br.Color);
            if (br.Hits <= 0)
            {
                _score += 10;
                _best = Math.Max(_best, _score);
                if (_random.Next(100) < 18)
                    _drops.Add(new Drop { X = cx, Y = cy, Kind = _random.Next(3), Vy = 1.2f * _px });
                _bricks.RemoveAt(i);
            }
            else _bricks[i] = br;
            break;
        }
    }

    private void TickDrops()
    {
        for (var i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.Y += d.Vy;
            if (d.Y > _padY && d.Y < _padY + _padH + 4 && d.X > _padX && d.X < _padX + PadW())
            {
                if (d.Kind == 0) { _wide = true; _wideTimer = 400; }
                else if (d.Kind == 1) Multi();
                else { _laser = true; _laserTimer = 280; }
                _drops.RemoveAt(i);
                continue;
            }

            if (d.Y > _canvas.Height) _drops.RemoveAt(i);
            else _drops[i] = d;
        }

        for (var i = _sparks.Count - 1; i >= 0; i--)
        {
            var p = _sparks[i];
            p.X += p.Vx;
            p.Y += p.Vy;
            p.Life--;
            if (p.Life <= 0) _sparks.RemoveAt(i);
            else _sparks[i] = p;
        }
    }

    private void Multi()
    {
        if (_balls.Count == 0) return;
        var src = _balls[0];
        if (_balls.Count >= 3) return;
        _balls.Add(new Ball { X = src.X, Y = src.Y, Vx = src.Vx * 0.7f - 0.8f, Vy = src.Vy, R = src.R });
        _balls.Add(new Ball { X = src.X, Y = src.Y, Vx = src.Vx * 0.7f + 0.8f, Vy = src.Vy, R = src.R });
    }

    private void RunAi(out int want)
    {
        want = 0;
        if (_balls.Count == 0) return;
        Ball best = _balls[0];
        foreach (var b in _balls)
            if (b.Vy > 0 && b.Y > best.Y) best = b;
        var target = best.X - PadW() / 2f;
        if (Math.Abs(target - _padX) > _px) want = target > _padX ? 1 : -1;
    }

    private void Serve()
    {
        if (_served || _balls.Count == 0) return;
        var b = _balls[0];
        var a = -0.7f - (float)_random.NextDouble() * 0.6f;
        var sp = Math.Max(2.2f, (2.1f + Difficulty * 0.18f) * _px);
        b.Vx = MathF.Cos(a) * sp * (_random.Next(2) == 0 ? 1 : -1);
        b.Vy = -Math.Abs(MathF.Sin(a) * sp);
        _balls[0] = b;
        _served = true;
    }

    private void FireLaser()
    {
        if (!_laser) return;
        var x1 = _padX + PadW() * 0.3f;
        var x2 = _padX + PadW() * 0.7f;
        Slice(x1);
        Slice(x2);
    }

    private void Slice(float x)
    {
        for (var i = _bricks.Count - 1; i >= 0; i--)
        {
            var br = _bricks[i];
            if (x < br.X || x > br.X + br.W) continue;
            Burst(x, br.Y + br.H / 2f, br.Color);
            _score += 5;
            _bricks.RemoveAt(i);
            break;
        }
    }

    private void Burst(float x, float y, SKColor col)
    {
        for (var i = 0; i < 6; i++)
            _sparks.Add(new Spark
            {
                X = x, Y = y,
                Vx = (float)(_random.NextDouble() * 2 - 1) * _px,
                Vy = (float)(_random.NextDouble() * 2 - 1) * _px,
                Life = 8 + _random.Next(6),
                Color = col
            });
    }

    [ExtensionMethod("Move Left", "Hold left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 1)]
    public void MoveLeft()
    {
        lock (_lock) { _human = true; _hold = -1; }
    }

    [ExtensionMethod("Move Right", "Hold right — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 2)]
    public void MoveRight()
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

    [ExtensionMethod("Serve", "Launch ball or fire laser",
        Category = "Controls", KeyboardShortcut = "Space|Up", Order = 5)]
    public void ServeOrFire()
    {
        lock (_lock)
        {
            _human = true;
            if (!_served) Serve();
            else FireLaser();
        }
    }

    private static readonly string[] WideIcon =
    {
        ".wwwwww.",
        "w......w",
        "wwwwwwww"
    };

    private static readonly string[] MultiIcon =
    {
        ".oo.oo.",
        "ooooooo",
        ".oo.oo."
    };

    private static readonly string[] LaserIcon =
    {
        "..y..",
        ".yyy.",
        "yyyyy",
        "..y..",
        "..y.."
    };

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var canvas = new SKCanvas(bb);
        canvas.Clear(new SKColor(10, 6, 22));
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        // starfield dots
        paint.Color = new SKColor(80, 50, 120);
        for (var i = 0; i < 40; i++)
            canvas.DrawRect((i * 37 + _frame / 2) % Math.Max(1, _canvas.Width), (i * 17) % Math.Max(1, _canvas.Height), 1, 1, paint);

        foreach (var br in _bricks)
            DrawBrick(canvas, paint, br);

        foreach (var d in _drops)
        {
            var spr = d.Kind switch { 0 => WideIcon, 1 => MultiIcon, _ => LaserIcon };
            Func<char, SKColor> pal = ch => ch switch
            {
                'w' => new SKColor(80, 255, 220),
                'o' => new SKColor(255, 80, 200),
                'y' => new SKColor(255, 220, 50),
                _ => SKColors.Transparent
            };
            PixelArt.Blit(canvas, spr, d.X - spr[0].Length / 2f, d.Y, pal);
        }

        foreach (var b in _balls)
        {
            paint.Color = new SKColor(20, 20, 30);
            PixelArt.Disc(canvas, paint, (int)b.X, (int)b.Y + 1, (int)b.R);
            paint.Color = SKColors.White;
            PixelArt.Disc(canvas, paint, (int)b.X, (int)b.Y, (int)b.R);
            paint.Color = new SKColor(180, 255, 255);
            canvas.DrawRect((int)b.X - 1, (int)b.Y - 1, 1, 1, paint);
        }

        DrawPaddle(canvas, paint);

        if (_laser)
        {
            paint.Color = new SKColor(255, 90, 180);
            var x1 = (int)(_padX + PadW() * 0.3f);
            var x2 = (int)(_padX + PadW() * 0.7f);
            for (var y = 0; y < (int)_padY; y += 2)
            {
                canvas.DrawRect(x1, y, 1, 1, paint);
                canvas.DrawRect(x2, y, 1, 1, paint);
            }
        }

        foreach (var p in _sparks)
        {
            paint.Color = p.Color.WithAlpha((byte)Math.Clamp(p.Life * 16, 0, 255));
            canvas.DrawRect((int)p.X, (int)p.Y, 1, 1, paint);
        }

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, _canvas.Height * 0.08f));
            CanvasText.Draw(canvas, _canvas, $"{_score}  L{_level}  x{_lives}", SKColors.White,
                4, size + 1, size, SKTextAlign.Left, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawBrick(SKCanvas canvas, SKPaint paint, Brick br)
    {
        var x = (int)MathF.Round(br.X);
        var y = (int)MathF.Round(br.Y);
        var w = Math.Max(4, (int)MathF.Round(br.W));
        var h = Math.Max(6, (int)MathF.Round(br.H));
        paint.Color = new SKColor(16, 8, 24);
        canvas.DrawRect(x, y, w, h, paint);
        paint.Color = br.Color;
        canvas.DrawRect(x + 1, y + 1, w - 2, h - 2, paint);
        paint.Color = new SKColor(255, 255, 255, 180);
        canvas.DrawRect(x + 1, y + 1, w - 2, 1, paint);
        canvas.DrawRect(x + 1, y + 1, 1, h - 2, paint);
        paint.Color = br.Color.WithAlpha(90);
        canvas.DrawRect(x + 2, y + h - 2, w - 3, 1, paint);
        if (br.Hits > 1)
        {
            paint.Color = SKColors.White;
            canvas.DrawRect(x + w / 2 - 1, y + h / 2 - 1, 2, 2, paint);
        }
    }

    private void DrawPaddle(SKCanvas canvas, SKPaint paint)
    {
        var x = (int)MathF.Round(_padX);
        var y = (int)MathF.Round(_padY);
        var w = (int)MathF.Round(PadW());
        var col = _laser ? new SKColor(255, 80, 180) : new SKColor(80, 240, 255);
        var dark = _laser ? new SKColor(120, 20, 80) : new SKColor(20, 80, 110);
        paint.Color = dark;
        canvas.DrawRect(x + 1, y, w - 2, 5, paint);
        paint.Color = col;
        canvas.DrawRect(x + 2, y + 1, w - 4, 3, paint);
        paint.Color = SKColors.White;
        canvas.DrawRect(x + 2, y + 1, w - 4, 1, paint);
        paint.Color = dark;
        canvas.DrawRect(x, y + 1, 2, 3, paint);
        canvas.DrawRect(x + w - 2, y + 1, 2, 3, paint);
    }

    private struct Brick
    {
        public float X, Y, W, H;
        public int Hits;
        public SKColor Color;
    }

    private struct Ball
    {
        public float X, Y, Vx, Vy, R;
    }

    private struct Drop
    {
        public float X, Y, Vy;
        public int Kind;
    }

    private struct Spark
    {
        public float X, Y, Vx, Vy;
        public int Life;
        public SKColor Color;
    }
}
