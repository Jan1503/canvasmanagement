using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.DinoRunner;

/// <summary>
///     Self-playing Chrome-style endless runner. Pixel-art dino, cacti and pterodactyls;
///     the AI jumps cacti and stays ducked until a low bird has fully passed.
/// </summary>
[ExtensionInfo("Dino Runner",
    "Endless runner — autopilot, or jump with Space and duck with Down in Studio",
    "Games",
    IconResourceName = "dino-runner.svg")]
public class DinoRunnerExtension : ICanvasExtension, IDisposable
{
    // 1 = pixel on. Sprites are authored at 1x and scaled by _px.
    private static readonly string[] DinoRunA =
    {
        "000001111000",
        "000011111110",
        "000011101111",
        "000011111111",
        "000011111000",
        "000011110000",
        "110011111100",
        "111111111010",
        "011111111000",
        "001111110000",
        "000111100000",
        "000110100000",
        "000100100000",
        "000100010000"
    };

    private static readonly string[] DinoRunB =
    {
        "000001111000",
        "000011111110",
        "000011101111",
        "000011111111",
        "000011111000",
        "000011110000",
        "110011111100",
        "111111111010",
        "011111111000",
        "001111110000",
        "000111100000",
        "000010110000",
        "000010001000",
        "000001000000"
    };

    private static readonly string[] DinoDuck =
    {
        "000000000000",
        "000000000000",
        "000000000000",
        "000000000000",
        "110000111100",
        "111111111111",
        "111111110111",
        "011111111111",
        "001111111000",
        "000110010000"
    };

    private static readonly string[] CactusSmall =
    {
        "00100",
        "00101",
        "10101",
        "10111",
        "11100",
        "00100",
        "00100",
        "00100"
    };

    private static readonly string[] CactusBig =
    {
        "001000100",
        "001000100",
        "101010100",
        "101010100",
        "111111100",
        "001000100",
        "001000100",
        "001000100",
        "001000100",
        "001000100"
    };

    private static readonly string[] BirdA =
    {
        "000110000000",
        "001111000000",
        "011111111110",
        "111111000000",
        "000110000000"
    };

    private static readonly string[] BirdB =
    {
        "000110000000",
        "001111111110",
        "011111000000",
        "111111000000",
        "000000000000"
    };

    // d outline, g body, l belly, y spots, w/k eye, o snout, b boot
    private static readonly string[] ModernDinoA =
    {
        "......ddyydd....",
        ".....dyyyyyyd...",
        "....dyywwkyyyd..",
        "....dyyyyyyyyd..",
        "....dgggggggd...",
        "...dgggggggggd..",
        ".ddgglllllgggd..",
        "dggggggggggggd..",
        ".dggggggggggd...",
        "..dgggd.dggd....",
        "...dbd...dbd....",
        "....d.....d....."
    };

    private static readonly string[] ModernDinoB =
    {
        "......ddyydd....",
        ".....dyyyyyyd...",
        "....dyywwkyyyd..",
        "....dyyyyyyyyd..",
        "....dgggggggd...",
        "...dgggggggggd..",
        ".ddgglllllgggd..",
        "dggggggggggggd..",
        ".dggggggggggd...",
        "...dggd.dggd....",
        "....dbd..dbd....",
        ".....d....d....."
    };

    private static readonly string[] ModernDuck =
    {
        "................",
        "................",
        ".dd....ddyydd...",
        "dggddddyyyyyyd..",
        "dggggggywwkyyd..",
        ".dgggggyyyyyyd..",
        "..dllllllgggd...",
        "...dgggggggd....",
        "....dbd.dbd.....",
        ".....d...d......"
    };

    private static readonly string[] ModernCactusSmall =
    {
        "..dgd.d.",
        ".dggdgd.",
        "dggdggd.",
        "dgggggd.",
        ".dgggd..",
        "..dggd..",
        "..dggd..",
        "..dddd.."
    };

    private static readonly string[] ModernCactusBig =
    {
        "..dgd..dgd.",
        ".dggd.dggd.",
        "dgggd.dgggd",
        "dgggdddgggd",
        ".dgggggggd.",
        "..dgggggd..",
        "..dggggd...",
        "...dggd....",
        "...dggd....",
        "...dddd...."
    };

    private static readonly string[] ModernBirdA =
    {
        "....dmmmd.....",
        "...dmmmmmmd...",
        ".ddmmwwkmmmd..",
        "dmmmmmmmmmmd..",
        ".d.dmmmmmd....",
        ".....dddd....."
    };

    private static readonly string[] ModernBirdB =
    {
        "......dmmmd...",
        "...dddmmmmmd..",
        ".dmmmmwwkmmd..",
        "dmmmmmmmmmmd..",
        ".ddddmmmd.....",
        ".............."
    };

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Obstacle> _obstacles = new();
    private readonly List<Cloud> _clouds = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;
    private int _px = 2;
    private int _frame;

    private float _groundY;
    private float _dinoX;
    private float _dinoY;
    private float _dinoVy;
    private float _gravity;
    private float _jumpV;
    private int _standH, _duckH, _dinoW;
    private bool _ducking;

    private float _speed;
    private float _spawnTimer;
    private int _score;
    private int _best;
    private int _crashTimer;
    private bool _human;
    private bool _wantDuck;

    internal DinoRunnerExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 28,
        MinValue = 16, MaxValue = 80, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 28;

    [ExtensionParameter("Difficulty", "How quickly the world speeds up", DefaultValue = 3, MinValue = 1,
        MaxValue = 10, Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Night Mode", "Dark background with light sprites", DefaultValue = false, Order = 3)]
    public bool NightMode { get; set; }

    [ExtensionParameter("Show Score", "Show the distance score", DefaultValue = true, Order = 4)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render the score with the crisp bitmap (BDF) font", DefaultValue = false,
        Order = 5)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "Score height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px", Order = 6)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI runs until you press a key in Studio", DefaultValue = true, Order = 7)]
    public bool AutoPilot { get; set; } = true;

    [ExtensionParameter("Modern Look", "Colorful sunset style — classic Chrome look stays the default", DefaultValue = false, Order = 8)]
    public bool ModernLook { get; set; }

    public string Name => "Dino Runner";
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
            _scale = DisplayScale.GetScale(_canvas.Width, _canvas.Height);
            _px = ModernLook
                ? PixelArt.Scale(_canvas.Height)
                : Math.Max(1, (int)Math.Round(_canvas.Height / 56f));
            _human = false;
            _wantDuck = false;
            Reset();

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
            try { _canvas.Clear(SKColors.Black); }
            catch { }
        }
    }

    private void Reset()
    {
        _px = ModernLook
            ? PixelArt.Scale(_canvas.Height)
            : Math.Max(1, (int)Math.Round(_canvas.Height / 56f));
        _groundY = _canvas.Height * 0.78f;
        if (ModernLook)
        {
            _dinoW = ModernDinoA[0].Length * _px;
            _standH = ModernDinoA.Length * _px;
            _duckH = ModernDuck.Length * _px;
        }
        else
        {
            _dinoW = 12 * _px;
            _standH = 14 * _px;
            _duckH = 10 * _px;
        }
        _dinoX = _canvas.Width * 0.12f;
        _dinoY = _groundY - _standH;
        _dinoVy = 0;
        _gravity = Math.Max(0.35f, 0.5f * _px);
        _jumpV = -Math.Max(6f, 4.0f * _px);
        _ducking = false;
        _speed = Math.Max(2f, 2.2f * _px);
        // First obstacle waits until there is a full jump+recover gap, so we never open with a cactus pair.
        _spawnTimer = Math.Max(MinObstacleGap(), _canvas.Width * 0.85f);
        _obstacles.Clear();
        _clouds.Clear();
        for (var i = 0; i < 3; i++)
            _clouds.Add(new Cloud
            {
                X = _random.Next(_canvas.Width),
                Y = 6 + _random.Next(Math.Max(8, (int)(_groundY * 0.35f))),
                W = 10 + _random.Next(14)
            });
        _score = 0;
        _crashTimer = 0;
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
                    if (_crashTimer == 0) Reset();
                }
                else
                {
                    Update();
                }

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DinoRunner] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        _score++;
        _best = Math.Max(_best, _score);
        _speed = Math.Min(_canvas.Width * 0.055f,
            (2.2f + Difficulty * 0.35f) * _px + _score * 0.0012f * _px);

        _spawnTimer -= _speed;
        if (_spawnTimer <= 0)
        {
            if (HasRoomToSpawn())
            {
                SpawnObstacle();
                _spawnTimer = MinObstacleGap() + _canvas.Width * (0.12f + (float)_random.NextDouble() * 0.22f);
            }
            else
            {
                _spawnTimer = _speed; // last cactus still too close; try again next frame
            }
        }

        for (var i = _obstacles.Count - 1; i >= 0; i--)
        {
            var o = _obstacles[i];
            o.X -= _speed;
            _obstacles[i] = o;
            if (o.X + o.W < -4) _obstacles.RemoveAt(i);
        }

        for (var i = 0; i < _clouds.Count; i++)
        {
            var c = _clouds[i];
            c.X -= _speed * 0.25f;
            if (c.X + c.W < 0)
            {
                c.X = _canvas.Width + _random.Next(40);
                c.Y = 6 + _random.Next(Math.Max(8, (int)(_groundY * 0.35f)));
            }

            _clouds[i] = c;
        }

        if (AutoPilot && !_human)
            RunAi();
        else
            ApplyPlayer();

        StepDino(ref _dinoY, ref _dinoVy, CurrentH());

        foreach (var o in _obstacles)
        {
            if (Hits(_dinoY, CurrentH(), o.X, o))
            {
                _crashTimer = 40;
                break;
            }
        }
    }

    private int CurrentH() => _ducking ? _duckH : _standH;

    /// <summary>
    ///     Horizontal gap the dino needs to jump a cactus, land, and jump again.
    ///     Two cacti closer than this are an instant crash on every panel size.
    /// </summary>
    private float MinObstacleGap()
    {
        var apex = Math.Abs(_jumpV) / Math.Max(0.01f, _gravity);
        var airFrames = apex * 2f + 8f;
        return _speed * airFrames + _dinoW + 9 * _px + 4 * _px;
    }

    private bool HasRoomToSpawn()
    {
        if (_obstacles.Count == 0) return true;
        var right = 0f;
        foreach (var o in _obstacles)
            right = Math.Max(right, o.X + o.W);
        return _canvas.Width - right >= MinObstacleGap();
    }

    private void SpawnObstacle()
    {
        var bird = _random.Next(100) < 28 && _score > 180;
        if (bird)
        {
            var spr = ModernLook ? ModernBirdA : BirdA;
            var w = spr[0].Length * _px;
            var h = spr.Length * _px;
            var high = _random.Next(2) == 0;
            var y = high
                ? _groundY - _standH - h - 2 * _px
                : _groundY - _standH - h + 2 * _px;
            _obstacles.Add(new Obstacle { X = _canvas.Width, Y = y, W = w, H = h, Kind = high ? Kind.BirdHigh : Kind.BirdLow });
        }
        else
        {
            var big = _random.Next(3) != 0;
            var spr = ModernLook ? (big ? ModernCactusBig : ModernCactusSmall) : (big ? CactusBig : CactusSmall);
            var w = spr[0].Length * _px;
            var h = spr.Length * _px;
            _obstacles.Add(new Obstacle { X = _canvas.Width, Y = _groundY - h, W = w, H = h, Kind = big ? Kind.CactusBig : Kind.CactusSmall });
        }
    }

    private void RunAi()
    {
        Obstacle? next = null;
        var bestDx = float.MaxValue;
        foreach (var obs in _obstacles)
        {
            if (obs.X + obs.W < _dinoX - 2) continue;
            var dx = obs.X - (_dinoX + _dinoW);
            if (dx < bestDx) { bestDx = dx; next = obs; }
        }

        _ducking = false;
        if (next == null) return;

        var o = next.Value;

        if (o.Kind == Kind.BirdLow)
        {
            var inLane = o.X < _dinoX + _dinoW && o.X + o.W > _dinoX - 2 * _px;
            var hitsIfStand = Hits(_dinoY, _standH, o.X, o)
                              || Hits(_dinoY, _standH, o.X - _speed, o)
                              || Hits(_dinoY, _standH, o.X - _speed * 2f, o);
            if (hitsIfStand || inLane)
                _ducking = true;
            if (_ducking && _dinoVy >= 0)
                _dinoY = _groundY - _duckH;
            return;
        }

        if (o.Kind is not (Kind.CactusSmall or Kind.CactusBig)) return;
        if (_dinoY < _groundY - _standH - 1f) return; // already airborne
        if (o.X + o.W < _dinoX) return;

        // Jump on the last frame that still clears — earlier jumps land in the cactus.
        if (JumpWouldClear(o, waitFrames: 0, jump: true))
        {
            if (!JumpWouldClear(o, waitFrames: 1, jump: true))
                DoJump();
        }
        else if (o.X - (_dinoX + _dinoW) < _speed * 1.5f)
        {
            DoJump();
        }
    }

    /// <summary>
    ///     True if (optional wait, then optional jump) gets the dino past <paramref name="o"/> without a hit.
    /// </summary>
    private bool JumpWouldClear(Obstacle o, int waitFrames, bool jump)
    {
        var x = o.X;
        var y = _dinoY;
        var vy = _dinoVy;
        var h = _standH;

        for (var i = 0; i < waitFrames; i++)
        {
            x -= _speed;
            StepDino(ref y, ref vy, h);
            if (Hits(y, h, x, o)) return false;
        }

        if (jump && y >= _groundY - _standH - 1f)
            vy = _jumpV;

        for (var n = 0; n < 160; n++)
        {
            x -= _speed;
            StepDino(ref y, ref vy, h);
            if (Hits(y, h, x, o)) return false;
            if (x + o.W < _dinoX - 2) return true;
        }

        return false;
    }

    private void StepDino(ref float y, ref float vy, int h)
    {
        var onGround = y >= _groundY - h - 0.5f;
        if (!onGround || vy < 0)
        {
            vy += _gravity;
            y += vy;
        }

        var floor = _groundY - h;
        if (y > floor)
        {
            y = floor;
            vy = 0;
        }
    }

    private bool Hits(float dinoY, int dinoH, float obsX, Obstacle o)
    {
        var pad = Math.Max(1, _px);
        var dr = new SKRect(_dinoX + pad, dinoY + pad, _dinoX + _dinoW - pad, dinoY + dinoH - 1);
        var or = new SKRect(obsX + 1, o.Y + 1, obsX + o.W - 1, o.Y + o.H - 1);
        return dr.IntersectsWith(or);
    }

    private void ApplyPlayer()
    {
        var onGround = _dinoY >= _groundY - _standH - 1f;
        _ducking = _wantDuck && onGround && _dinoVy >= 0;
        if (_ducking)
            _dinoY = _groundY - _duckH;
    }

    private void DoJump()
    {
        if (_dinoY >= _groundY - _standH - 1f)
        {
            _ducking = false;
            _wantDuck = false;
            _dinoVy = _jumpV;
        }
    }

    [ExtensionMethod("Jump", "Jump — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Space|Up", Order = 1)]
    public void Jump()
    {
        lock (_lock)
        {
            _human = true;
            if (_crashTimer > 0)
            {
                _crashTimer = 0;
                Reset();
            }

            DoJump();
        }
    }

    [ExtensionMethod("Duck", "Duck under low birds (hold Down)",
        Category = "Controls", KeyboardShortcut = "Down", Order = 2)]
    public void Duck()
    {
        lock (_lock)
        {
            _human = true;
            _wantDuck = true;
        }
    }

    [ExtensionMethod("Stand", "Stop ducking",
        Category = "Controls", KeyboardShortcut = "Down:up", Order = 3)]
    public void Stand()
    {
        lock (_lock) _wantDuck = false;
    }

    private void Render()
    {
        if (ModernLook) RenderModern();
        else RenderClassic();
    }

    private void RenderClassic()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        var bg = NightMode ? new SKColor(18, 18, 28) : new SKColor(247, 247, 247);
        var fg = NightMode ? new SKColor(235, 235, 240) : new SKColor(53, 53, 53);
        var dim = NightMode ? new SKColor(90, 90, 110) : new SKColor(180, 180, 180);

        using var canvas = new SKCanvas(bb);
        canvas.Clear(bg);
        using var paint = new SKPaint { Color = fg, Style = SKPaintStyle.Fill, IsAntialias = false };

        paint.Color = dim;
        foreach (var c in _clouds)
        {
            canvas.DrawRect(c.X, c.Y, c.W, Math.Max(2, _px), paint);
            canvas.DrawRect(c.X + c.W * 0.25f, c.Y - _px, c.W * 0.5f, Math.Max(2, _px), paint);
        }

        paint.Color = fg;
        canvas.DrawRect(0, _groundY, _canvas.Width, Math.Max(1, _px), paint);
        var dash = 8 * _px;
        var scroll = (int)(_frame * _speed) % (dash * 2);
        for (var x = -scroll; x < _canvas.Width; x += dash * 2)
            canvas.DrawRect(x, _groundY + 2 * _px, dash / 2, Math.Max(1, _px), paint);

        var dino = _ducking ? DinoDuck : (_frame / 4 % 2 == 0 ? DinoRunA : DinoRunB);
        DrawSprite(canvas, dino, _dinoX, _dinoY, paint);
        // Eye punch-out so the silhouette reads on both day and night.
        using (var eye = new SKPaint { Color = bg, IsAntialias = false })
            canvas.DrawRect(_dinoX + 8 * _px, _dinoY + 2 * _px, _px, _px, eye);

        foreach (var o in _obstacles)
        {
            paint.Color = fg;
            var sprite = o.Kind switch
            {
                Kind.CactusBig => CactusBig,
                Kind.CactusSmall => CactusSmall,
                _ => _frame / 5 % 2 == 0 ? BirdA : BirdB
            };
            DrawSprite(canvas, sprite, o.X, o.Y, paint);
        }

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, 10f * _px));
            CanvasText.Draw(canvas, _canvas, $"HI {_best:00000}  {_score:00000}", fg,
                _canvas.Width - 4, Math.Max(10f, 11f * _px), size, SKTextAlign.Right, UseBdfFont);
        }

        if (_crashTimer > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(10f, _canvas.Height * 0.12f));
            CanvasText.Draw(canvas, _canvas, "G A M E  O V E R", fg,
                _canvas.Width / 2f, _canvas.Height / 2f, size, SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void RenderModern()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var canvas = new SKCanvas(bb);
        var w = _canvas.Width;
        var h = _canvas.Height;
        using (var sky = new SKPaint())
        {
            sky.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, _groundY),
                new[] { new SKColor(255, 90, 70), new SKColor(255, 160, 60), new SKColor(255, 210, 110) },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, w, _groundY, sky);
        }

        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        paint.Color = new SKColor(255, 230, 80);
        PixelArt.Disc(canvas, paint, w - 18, 14, 8);
        paint.Color = new SKColor(255, 252, 200);
        PixelArt.Disc(canvas, paint, w - 18, 14, 5);

        foreach (var c in _clouds)
        {
            paint.Color = new SKColor(255, 255, 255, 200);
            canvas.DrawRect(c.X, c.Y, c.W, Math.Max(3, 2 * _px), paint);
            canvas.DrawRect(c.X + c.W * 0.2f, c.Y - 3 * _px, c.W * 0.55f, Math.Max(3, 2 * _px), paint);
            paint.Color = new SKColor(255, 180, 200, 90);
            canvas.DrawRect(c.X + 2, c.Y + 1, Math.Max(2, c.W * 0.3f), 1, paint);
        }

        paint.Color = new SKColor(46, 196, 78);
        canvas.DrawRect(0, _groundY, w, Math.Max(2, 2 * _px), paint);
        paint.Color = new SKColor(168, 96, 40);
        canvas.DrawRect(0, _groundY + 2 * _px, w, h - _groundY, paint);
        paint.Color = new SKColor(120, 64, 28);
        var dash = 8 * _px;
        var scroll = (int)(_frame * _speed) % (dash * 2);
        for (var x = -scroll; x < w; x += dash * 2)
            canvas.DrawRect(x, _groundY + 5 * _px, dash / 2, Math.Max(1, _px), paint);

        var dino = _ducking ? ModernDuck : (_frame / 4 % 2 == 0 ? ModernDinoA : ModernDinoB);
        PixelArt.Blit(canvas, dino, _dinoX, _dinoY, ChModernDino, _px);

        foreach (var o in _obstacles)
        {
            if (o.Kind is Kind.CactusBig or Kind.CactusSmall)
                PixelArt.Blit(canvas, o.Kind == Kind.CactusBig ? ModernCactusBig : ModernCactusSmall,
                    o.X, o.Y, ChModernCactus, _px);
            else
                PixelArt.Blit(canvas, _frame / 5 % 2 == 0 ? ModernBirdA : ModernBirdB, o.X, o.Y, ChModernBird, _px);
        }

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, 10f * _px));
            CanvasText.Draw(canvas, _canvas, $"HI {_best:00000}  {_score:00000}", SKColors.White,
                w - 4, Math.Max(10f, 11f * _px), size, SKTextAlign.Right, UseBdfFont);
        }

        if (_crashTimer > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.12f));
            CanvasText.Draw(canvas, _canvas, "G A M E  O V E R", SKColors.White,
                w / 2f, h / 2f, size, SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private static SKColor ChModernDino(char ch) => ch switch
    {
        'd' => new SKColor(22, 28, 18),
        'g' => new SKColor(72, 210, 64),
        'l' => new SKColor(190, 255, 110),
        'y' => new SKColor(255, 214, 48),
        'w' => SKColors.White,
        'k' => new SKColor(16, 16, 16),
        'b' => new SKColor(96, 48, 20),
        _ => SKColors.Transparent
    };

    private static SKColor ChModernCactus(char ch) => ch switch
    {
        'd' => new SKColor(16, 48, 22),
        'g' => new SKColor(36, 180, 70),
        _ => SKColors.Transparent
    };

    private static SKColor ChModernBird(char ch) => ch switch
    {
        'd' => new SKColor(40, 16, 48),
        'm' => new SKColor(255, 80, 170),
        'w' => SKColors.White,
        'k' => new SKColor(16, 16, 16),
        _ => SKColors.Transparent
    };

    private void DrawSprite(SKCanvas canvas, string[] rows, float x, float y, SKPaint paint)
    {
        for (var ry = 0; ry < rows.Length; ry++)
        {
            var row = rows[ry];
            for (var rx = 0; rx < row.Length; rx++)
                if (row[rx] == '1')
                    canvas.DrawRect(x + rx * _px, y + ry * _px, _px, _px, paint);
        }
    }

    private enum Kind { CactusSmall, CactusBig, BirdLow, BirdHigh }

    private struct Obstacle
    {
        public float X;
        public float Y;
        public int W;
        public int H;
        public Kind Kind;
    }

    private struct Cloud
    {
        public float X;
        public float Y;
        public int W;
    }
}
