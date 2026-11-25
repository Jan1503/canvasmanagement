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

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = true, Order = 4)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI catches until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    private GameSound? _snd;
    private GameSound _sound => _snd ??= new GameSound(_canvas, GameSong.Chill);

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
            _sound.Stop();
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
                _sound.Duck(_crashTimer > 0 ? 0.35f : 1f);
                _sound.Update();
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
                    _sound.Play(GameSfx.Explode, 0.7f);
                    _gfx.Burst(f.X, f.Y, new SKColor(255, 150, 40), 24, 2.6f, 0.7f, 0.05f);
                    _gfx.Flash(new SKColor(255, 60, 30), 0.5f);
                    _gfx.Shake(4);
                    if (_lives <= 0) _sound.Play(GameSfx.GameOver, 0.6f);
                    if (_lives <= 0) _crashTimer = 40;
                }
                else
                {
                    _combo++;
                    _score += (f.Kind == 6 ? 50 : 10) * Math.Min(8, 1 + _combo / 4);
                    // The pitch climbs with the combo.
                    _sound.Play(f.Kind == 6 ? GameSfx.PowerUp : GameSfx.Coin, 0.55f, (f.X / _canvas.Width) * 2 - 1,
                        1f + Math.Min(_combo, 16) * 0.03f, 20);
                    _gfx.Burst(f.X + f.S / 2, f.Y, f.Kind == 6 ? new SKColor(255, 230, 70) : SKColors.White, 8, 1.6f, 0.4f, 0, true);
                    if (_combo > 1 && _combo % 5 == 0)
                        _gfx.Pop($"COMBO x{Math.Min(8, 1 + _combo / 4)}", _bx + _bw / 2, _by - 10, new SKColor(255, 230, 90));
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

    private readonly GameFx _gfx = new();
    private readonly System.Diagnostics.Stopwatch _gclock = System.Diagnostics.Stopwatch.StartNew();
    private double _glast;

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        var gnow = _gclock.Elapsed.TotalSeconds;
        var gdt = (float)Math.Clamp(gnow - _glast, 0.001, 0.1);
        _glast = gnow;
        _gfx.Update(gdt);
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

        DrawScenery(canvas, paint);

        foreach (var f in _falls)
            DrawFruit(canvas, f);

        DrawBasket(canvas, paint);

        foreach (var p in _sparks)
        {
            paint.Color = p.Color.WithAlpha((byte)Math.Clamp(p.Life * 16, 0, 255));
            canvas.DrawRect(p.X, p.Y, _px, _px, paint);
        }
        // Leaves drifting down from the canopy.
        if (_frame % 12 == 0)
            _gfx.Spark(_gfx.Rand(0, _canvas.Width), 0, _gfx.Rand(-0.3f, 0.3f), 0.4f, _gfx.Rand(0, 1) < 0.5f ? new SKColor(80, 170, 60) : new SKColor(200, 160, 50), 4f, 2, false, 0, null, 0);
        _gfx.DrawParticles(canvas);

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

        _gfx.DrawOverlay(canvas, _canvas, _canvas.Width, _canvas.Height, Math.Max(7f, _canvas.Height * 0.07f));
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

    /// <summary>Sun, drifting clouds, a row of trees on a hill and grass under the basket.</summary>
    private void DrawScenery(SKCanvas c, SKPaint paint)
    {
        int w = _canvas.Width, h = _canvas.Height;
        var px = Math.Max(1, (int)_px);
        var t = Environment.TickCount64 / 1000f;

        paint.Color = new SKColor(255, 240, 150);
        PixelArt.Disc(c, paint, w - 22 * px, 16 * px, 7 * px);
        paint.Color = new SKColor(255, 250, 210);
        PixelArt.Disc(c, paint, w - 22 * px, 16 * px, 5 * px);

        paint.Color = new SKColor(250, 252, 255);
        for (var i = 0; i < 4; i++)
        {
            var cx = (int)((i * w / 3.3f + t * (3 + i) * px) % (w + 40 * px)) - 20 * px;
            var cy = (18 + i * 9 % 23) * px;
            c.DrawRect(cx, cy, 16 * px, 3 * px, paint);
            c.DrawRect(cx + 3 * px, cy - 2 * px, 9 * px, 2 * px, paint);
        }

        // Back hill with trees.
        var hill = h - 18 * px;
        paint.Color = new SKColor(56, 150, 70);
        for (var x = 0; x < w; x += px)
        {
            var y = hill - (int)(6 * px * (1 + MathF.Sin(x * 0.03f / px)));
            c.DrawRect(x, y, px, h - y, paint);
        }
        for (var x = 6 * px; x < w; x += 23 * px)
        {
            var y = hill - (int)(6 * px * (1 + MathF.Sin(x * 0.03f / px)));
            paint.Color = new SKColor(110, 70, 40);
            c.DrawRect(x, y - 6 * px, 2 * px, 6 * px, paint);
            paint.Color = new SKColor(30, 110, 50);
            PixelArt.Disc(c, paint, x + px, y - 9 * px, 5 * px);
            paint.Color = new SKColor(50, 140, 60);
            PixelArt.Disc(c, paint, x, y - 10 * px, 3 * px);
        }

        // Grass.
        var ground = (int)(_by + 6 * px);
        paint.Color = new SKColor(70, 170, 60);
        c.DrawRect(0, ground, w, h - ground, paint);
        paint.Color = new SKColor(110, 200, 80);
        for (var x = 0; x < w; x += 3 * px)
            c.DrawRect(x, ground - ((x / px) % 2 == 0 ? px : 0), px, px, paint);
    }

    /// <summary>A woven basket as wide as the catch zone.</summary>
    private void DrawBasket(SKCanvas c, SKPaint paint)
    {
        var px = Math.Max(1, (int)_px);
        int x0 = (int)_bx, y0 = (int)_by, w = (int)_bw, rows = 6;
        var dark = new SKColor(90, 45, 15);
        var mid = new SKColor(185, 100, 35);
        var light = new SKColor(235, 160, 70);
        for (var r = 0; r < rows; r++)
        {
            // Sides lean in towards the bottom.
            var inset = r >= rows - 2 ? (r - rows + 3) * px : 0;
            for (var x = inset; x < w - inset; x += px)
            {
                SKColor col;
                if (r == 0) col = light;
                else if (x == inset || x >= w - inset - px || r == rows - 1) col = dark;
                else col = ((x / px + r) / 2 % 2 == 0) ? mid : light;
                paint.Color = col;
                c.DrawRect(x0 + x, y0 + r * px, px, px, paint);
            }
        }
        // Handle.
        paint.Color = dark;
        var hw = w / 2;
        for (var x = 0; x < hw; x += px)
        {
            var arc = (int)(MathF.Sin(x / (float)hw * MathF.PI) * 5 * px);
            c.DrawRect(x0 + w / 4 + x, y0 - arc, px, px, paint);
        }
    }

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
