using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.FruitFall;

/// <summary>
///     Catch saturated fruit in a basket; dodge bombs. Autopilot until a key is pressed in Studio.
/// </summary>
[ExtensionInfo("Fruit Fall",
    "Catch the rainbow fruit — Left/Right, or autopilot",
    "Games",
    IconResourceName = "fruit-fall.svg")]
public class FruitFallExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Fall> _falls = new();
    private readonly List<Spark> _sparks = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _px = 2;
    private int _frame;
    private float _bx, _bw, _by, _spd;
    private int _hold;
    private bool _human;
    private int _score, _best, _lives, _combo, _spawn;
    private int _crashTimer;

    internal FruitFallExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 22,
        MinValue = 14, MaxValue = 50, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 22;

    [ExtensionParameter("Difficulty", "Fall speed and bomb chance", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show the HUD", DefaultValue = true, Order = 3)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = false, Order = 4)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI catches until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    public string Name => "Fruit Fall";
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
            _lives = 3;
            _score = 0;
            _combo = 0;
            ResetBasket();
            _bx = (_canvas.Width - _bw) / 2f;
            _falls.Clear();
            _sparks.Clear();
            _spawn = 0;
            _crashTimer = 0;
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

    private void ResetBasket()
    {
        _bw = Math.Max(16, _canvas.Width * 0.16f);
        _by = _canvas.Height - Math.Max(10, 7 * _px);
        _spd = Math.Max(2.4f, (2.6f + Difficulty * 0.2f) * _px);
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
                        if (_lives <= 0) { _lives = 3; _score = 0; _combo = 0; }
                    }
                }
                else
                    Update();

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FruitFall] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        ResetBasket();
        var want = 0;
        if (AutoPilot && !_human) RunAi(out want);
        else want = _hold;
        _bx = Math.Clamp(_bx + want * _spd, 0, _canvas.Width - _bw);

        _spawn--;
        if (_spawn <= 0)
        {
            Spawn();
            _spawn = Math.Max(8, 22 - Difficulty - _score / 400);
        }

        var fallSpd = Math.Max(1.4f, (1.2f + Difficulty * 0.18f) * _px + _score * 0.0008f);
        for (var i = _falls.Count - 1; i >= 0; i--)
        {
            var f = _falls[i];
            f.Y += fallSpd + f.Kind * 0.08f;
            f.Rot += 0.12f;
            if (f.Y + f.S > _by && f.Y < _by + 6 * _px && f.X + f.S > _bx && f.X < _bx + _bw)
            {
                if (f.Kind == 0)
                {
                    _lives--;
                    _combo = 0;
                    Burst(f.X, f.Y, new SKColor(255, 60, 40));
                    if (_lives <= 0) _crashTimer = 40;
                }
                else
                {
                    _combo++;
                    _score += (f.Kind == 6 ? 50 : 10) * Math.Min(8, 1 + _combo / 4);
                    _best = Math.Max(_best, _score);
                    Burst(f.X, f.Y, f.Kind switch
                    {
                        1 => new SKColor(255, 50, 50),
                        2 => new SKColor(255, 140, 20),
                        3 => new SKColor(255, 230, 40),
                        4 => new SKColor(160, 50, 255),
                        5 => new SKColor(50, 220, 80),
                        _ => new SKColor(255, 80, 180)
                    });
                }

                _falls.RemoveAt(i);
                continue;
            }

            if (f.Y > _canvas.Height)
            {
                if (f.Kind != 0) { _combo = 0; _lives = Math.Max(0, _lives); }
                _falls.RemoveAt(i);
            }
            else _falls[i] = f;
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

    private void Spawn()
    {
        var bomb = _random.Next(100) < 10 + Difficulty;
        var kind = bomb ? 0 : 1 + _random.Next(6);
        var s = 12;
        _falls.Add(new Fall
        {
            X = 4 + _random.Next(Math.Max(4, _canvas.Width - 10)),
            Y = -s,
            S = s,
            Kind = kind,
            Rot = 0
        });
    }

    private void RunAi(out int want)
    {
        want = 0;
        Fall? target = null;
        var best = float.MaxValue;
        foreach (var f in _falls)
        {
            if (f.Kind == 0)
            {
                if (f.X + f.S > _bx && f.X < _bx + _bw && f.Y > _canvas.Height * 0.45f)
                {
                    want = f.X + f.S / 2f < _bx + _bw / 2f ? 1 : -1;
                    return;
                }

                continue;
            }

            var eta = (_by - f.Y) / Math.Max(0.5f, 2f);
            var d = Math.Abs(f.X - (_bx + _bw / 2f)) + eta;
            if (d < best) { best = d; target = f; }
        }

        if (target == null) return;
        var tx = target.Value.X + target.Value.S / 2f - _bw / 2f;
        if (Math.Abs(tx - _bx) > _px) want = tx > _bx ? 1 : -1;
    }

    private void Burst(float x, float y, SKColor col)
    {
        for (var i = 0; i < 7; i++)
            _sparks.Add(new Spark
            {
                X = x, Y = y,
                Vx = (float)(_random.NextDouble() * 2 - 1) * _px,
                Vy = (float)(_random.NextDouble() * -2) * _px,
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

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var canvas = new SKCanvas(bb);
        using (var sky = new SKPaint())
        {
            sky.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, _canvas.Height),
                new[] { new SKColor(80, 190, 255), new SKColor(40, 140, 70) },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, sky);
        }

        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        // Trees / canopy strip
        paint.Color = new SKColor(20, 120, 50);
        canvas.DrawRect(0, 0, _canvas.Width, 3, paint);

        foreach (var f in _falls)
            DrawFruit(canvas, f);

        PixelArt.Blit(canvas, Basket, _bx, _by, ChBasket);
        // extend rim to the real catch width
        using (var rim = new SKPaint { Color = new SKColor(180, 90, 30), IsAntialias = false })
        {
            canvas.DrawRect(_bx, _by + 1, _bw, 1, rim);
            canvas.DrawRect(_bx, _by + 3, _bw, 1, rim);
            canvas.DrawRect(_bx, _by, 1, 5, rim);
            canvas.DrawRect(_bx + _bw - 1, _by, 1, 5, rim);
        }

        foreach (var p in _sparks)
        {
            paint.Color = p.Color.WithAlpha((byte)Math.Clamp(p.Life * 16, 0, 255));
            canvas.DrawRect(p.X, p.Y, _px, _px, paint);
        }

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, _canvas.Height * 0.09f));
            var combo = _combo >= 4 ? $"  x{_combo}" : "";
            CanvasText.Draw(canvas, _canvas, $"{_score}{combo}  {_lives}", SKColors.White,
                4, size + 1, size, SKTextAlign.Left, UseBdfFont);
        }

        if (_crashTimer > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(12f, _canvas.Height * 0.14f));
            CanvasText.Draw(canvas, _canvas, "SPLAT", SKColors.White,
                _canvas.Width / 2f, _canvas.Height * 0.45f, size, SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private static readonly string[] Apple =
    {
        "....gg....",
        "...drrrd..",
        "..drrwrrd.",
        ".drrrrrrrd",
        ".drrrrrrrd",
        "..drrrrrd.",
        "...dddd..."
    };

    private static readonly string[] Orange =
    {
        "...doood..",
        "..doowoood",
        ".doooooood",
        ".doooooood",
        "..doooood.",
        "...dddd..."
    };

    private static readonly string[] Banana =
    {
        "......gg..",
        ".....dyyd.",
        "....dyyyd.",
        "...dyyyyd.",
        "..dyyyyd..",
        ".dyyyd....",
        ".dddd....."
    };

    private static readonly string[] Grape =
    {
        "....gg....",
        "...dppd...",
        "..dppppd..",
        ".dppdpppd.",
        "..dppppd..",
        "...dddd..."
    };

    private static readonly string[] Melon =
    {
        "...dgggd..",
        "..dgrrrgd.",
        ".dgrrrrrgd",
        ".dgrrrrrgd",
        "..dgrrrgd.",
        "...dddd..."
    };

    private static readonly string[] Star =
    {
        "....y.....",
        "...yyy....",
        "yyyyyyyyy.",
        ".yyyyyyy..",
        "..yyyyy...",
        ".yy...yy.."
    };

    private static readonly string[] BombSpr =
    {
        "....ff....",
        "...dkkd...",
        "..dkwkkd..",
        ".dkkkkkdk.",
        "..dkkkkd..",
        "...dddd..."
    };

    private static readonly string[] Basket =
    {
        "n..............n",
        "nnnnnnnnnnnnnnnn",
        "nNnnNnnNnnNnnNnN",
        "nnnnnnnnnnnnnnnn",
        ".nnnnnnnnnnnnnn."
    };

    private void DrawFruit(SKCanvas canvas, Fall f)
    {
        var spr = f.Kind switch
        {
            0 => BombSpr,
            1 => Apple,
            2 => Orange,
            3 => Banana,
            4 => Grape,
            5 => Melon,
            _ => Star
        };
        PixelArt.Blit(canvas, spr, f.X, f.Y, ChFruit);
    }

    private static SKColor ChFruit(char ch) => ch switch
    {
        'd' => new SKColor(28, 18, 12),
        'g' => new SKColor(40, 180, 50),
        'r' => new SKColor(255, 50, 50),
        'w' => new SKColor(255, 220, 200),
        'o' => new SKColor(255, 140, 20),
        'y' => new SKColor(255, 230, 40),
        'p' => new SKColor(160, 50, 255),
        'k' => new SKColor(40, 40, 48),
        'f' => new SKColor(255, 80, 40),
        _ => SKColors.Transparent
    };

    private static SKColor ChBasket(char ch) => ch switch
    {
        'n' => new SKColor(180, 90, 30),
        'N' => new SKColor(230, 140, 50),
        _ => SKColors.Transparent
    };

    private struct Fall
    {
        public float X, Y, S, Rot;
        public int Kind;
    }

    private struct Spark
    {
        public float X, Y, Vx, Vy;
        public int Life;
        public SKColor Color;
    }
}
