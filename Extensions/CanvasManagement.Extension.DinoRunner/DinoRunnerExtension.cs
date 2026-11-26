using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.DinoRunner;

/// <summary>
///     Self-playing Chrome-style endless runner. Pixel-art dino, cacti and pterodactyls;
///     the AI jumps cacti and stays ducked until a low bird has fully passed.
/// </summary>
[ExtensionInfo("Dino Runner",
    "Endless runner — autopilot, or jump with Space and duck with Down in Studio",
    "Games",
    IconResourceName = "dino-runner.svg")]
public class DinoRunnerExtension : GameExtension
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

    private readonly Random _random = new();
    private readonly List<Obstacle> _obstacles = new();
    private readonly List<Cloud> _clouds = new();

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

    internal DinoRunnerExtension(ICanvas canvas) : base(canvas, GameSong.Adventure)
    {
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

    [ExtensionParameter("Use BDF Font", "Render the score with the crisp bitmap (BDF) font", DefaultValue = true,
        Order = 5)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "Score height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px", Order = 6)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI runs until you press a key in Studio", DefaultValue = true, Order = 7)]
    public bool AutoPilot { get; set; } = true;

    [ExtensionParameter("Modern Look", "Colourful desert with parallax, dust and a day/night cycle; off = the plain monochrome look",
        DefaultValue = true, Order = 8)]
    public bool ModernLook { get; set; } = true;

    public override string Name => "Dino Runner";
    protected override double FrameIntervalMs => GameSpeed;

    protected override void OnStart()
    {
        _scale = DisplayScale.GetScale(Canvas.Width, Canvas.Height);
        _px = ModernLook
            ? PixelArt.Scale(Canvas.Height)
            : Math.Max(1, (int)Math.Round(Canvas.Height / 56f));
        _human = false;
        _wantDuck = false;
        Reset();
    }

    private void Reset()
    {
        _px = ModernLook
            ? PixelArt.Scale(Canvas.Height)
            : Math.Max(1, (int)Math.Round(Canvas.Height / 56f));
        _groundY = Canvas.Height * 0.78f;
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
        _dinoX = Canvas.Width * 0.12f;
        _dinoY = _groundY - _standH;
        _dinoVy = 0;
        _gravity = Math.Max(0.35f, 0.5f * _px);
        _jumpV = -Math.Max(6f, 4.0f * _px);
        _ducking = false;
        _speed = Math.Max(2f, 2.2f * _px);
        // First obstacle waits until there is a full jump+recover gap, so we never open with a cactus pair.
        _spawnTimer = Math.Max(MinObstacleGap(), Canvas.Width * 0.85f);
        _obstacles.Clear();
        _clouds.Clear();
        for (var i = 0; i < 3; i++)
            _clouds.Add(new Cloud
            {
                X = _random.Next(Canvas.Width),
                Y = 6 + _random.Next(Math.Max(8, (int)(_groundY * 0.35f))),
                W = 10 + _random.Next(14)
            });
        _score = 0;
        _crashTimer = 0;
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        if (_crashTimer > 0)
        {
            _crashTimer--;
            if (_crashTimer == 0) Reset();
        }
        else
        {
            Update();
        }

        Draw(canvas);
        Audio.Duck(_crashTimer > 0 ? 0.35f : 1f);
        Audio.Update();
        _frame++;
    }

    private void Update()
    {
        _score++;
        if (_score % 100 == 0) Audio.Play(GameSfx.Score, 0.5f);
        _best = Math.Max(_best, _score);
        _speed = Math.Min(Canvas.Width * 0.055f,
            (2.2f + Difficulty * 0.35f) * _px + _score * 0.0012f * _px);

        _spawnTimer -= _speed;
        if (_spawnTimer <= 0)
        {
            if (HasRoomToSpawn())
            {
                SpawnObstacle();
                _spawnTimer = MinObstacleGap() + Canvas.Width * (0.12f + (float)_random.NextDouble() * 0.22f);
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
                c.X = Canvas.Width + _random.Next(40);
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
                Audio.Play(GameSfx.Hit, 0.7f);
                Audio.Play(GameSfx.GameOver, 0.6f);
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
        return Canvas.Width - right >= MinObstacleGap();
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
            _obstacles.Add(new Obstacle { X = Canvas.Width, Y = y, W = w, H = h, Kind = high ? Kind.BirdHigh : Kind.BirdLow });
        }
        else
        {
            var big = _random.Next(3) != 0;
            var spr = ModernLook ? (big ? ModernCactusBig : ModernCactusSmall) : (big ? CactusBig : CactusSmall);
            var w = spr[0].Length * _px;
            var h = spr.Length * _px;
            _obstacles.Add(new Obstacle { X = Canvas.Width, Y = _groundY - h, W = w, H = h, Kind = big ? Kind.CactusBig : Kind.CactusSmall });
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
            Audio.Play(GameSfx.Jump, 0.5f);
        }
    }

    [ExtensionMethod("Jump", "Jump — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Space|Up", Order = 1)]
    public void Jump()
    {
        lock (Sync)
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
        lock (Sync)
        {
            _human = true;
            _wantDuck = true;
        }
    }

    [ExtensionMethod("Stand", "Stop ducking",
        Category = "Controls", KeyboardShortcut = "Down:up", Order = 3)]
    public void Stand()
    {
        lock (Sync) _wantDuck = false;
    }

    private void Draw(SKCanvas canvas)
    {
        if (ModernLook) RenderModern(canvas);
        else RenderClassic(canvas);
    }

    private void RenderClassic(SKCanvas canvas)
    {
        var bg = NightMode ? new SKColor(18, 18, 28) : new SKColor(247, 247, 247);
        var fg = NightMode ? new SKColor(235, 235, 240) : new SKColor(53, 53, 53);
        var dim = NightMode ? new SKColor(90, 90, 110) : new SKColor(180, 180, 180);

        canvas.Clear(bg);
        using var paint = new SKPaint { Color = fg, Style = SKPaintStyle.Fill, IsAntialias = false };

        paint.Color = dim;
        foreach (var c in _clouds)
        {
            canvas.DrawRect(c.X, c.Y, c.W, Math.Max(2, _px), paint);
            canvas.DrawRect(c.X + c.W * 0.25f, c.Y - _px, c.W * 0.5f, Math.Max(2, _px), paint);
        }

        paint.Color = fg;
        canvas.DrawRect(0, _groundY, Canvas.Width, Math.Max(1, _px), paint);
        var dash = 8 * _px;
        var scroll = (int)(_frame * _speed) % (dash * 2);
        for (var x = -scroll; x < Canvas.Width; x += dash * 2)
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
            CanvasText.Draw(canvas, Canvas, $"HI {_best:00000}  {_score:00000}", fg,
                Canvas.Width - 4, Math.Max(10f, 11f * _px), size, SKTextAlign.Right, UseBdfFont);
        }

        if (_crashTimer > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(10f, Canvas.Height * 0.12f));
            CanvasText.Draw(canvas, Canvas, "G A M E  O V E R", fg,
                Canvas.Width / 2f, Canvas.Height / 2f, size, SKTextAlign.Center, UseBdfFont);
        }
    }

    private readonly GameFx _fx = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastRender;
    private float _scroll;
    private bool _wasGround = true;
    private bool _wasCrash;

    /// <summary>
    ///     Desert run: the sky moves from morning to sunset to a starry night as the score grows, two layers of
    ///     mesas and a row of cactus silhouettes scroll at their own speeds, the ground has pebbles, dust kicks up
    ///     from the feet, a jump and a landing puff, every hundred points sparkle, and a crash shakes and flashes.
    /// </summary>
    private void RenderModern(SKCanvas canvas)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastRender, 0.001, 0.1);
        _lastRender = now;
        _fx.Update(dt);
        if (_crashTimer == 0) _scroll += _speed;

        var w = Canvas.Width;
        var h = Canvas.Height;
        var gy = (int)_groundY;

        // Sky by score: morning, sunset, night, again every 1500 points.
        var phase = _score % 1500 / 1500f;
        var night = phase < 0.45f ? 0f : phase < 0.6f ? (phase - 0.45f) / 0.15f : phase < 0.9f ? 1f : 1 - (phase - 0.9f) / 0.1f;
        var dusk = 1 - MathF.Abs(phase - 0.52f) / 0.12f;
        dusk = Math.Clamp(dusk, 0, 1);
        var top = GameFx.Lerp(GameFx.Lerp(new SKColor(90, 170, 240), new SKColor(255, 90, 70), dusk), new SKColor(8, 10, 34), night);
        var bot = GameFx.Lerp(GameFx.Lerp(new SKColor(200, 230, 250), new SKColor(255, 200, 110), dusk), new SKColor(40, 30, 80), night);
        GameFx.VerticalGradient(canvas, w, h, top, bot, 0, gy);
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        if (night > 0.5f) GameFx.Stars(canvas, w, gy / 2, _fx.Time, _scroll * 0.02f, 0, 180);
        // Sun (left of the score) or moon.
        var sx = (int)(w * 0.62f);
        var sy = (int)(gy * (0.35f + 0.25f * dusk));
        if (night < 0.6f)
        {
            GameFx.GlowDisc(canvas, sx, sy, h * 0.18f, new SKColor(255, 220, 120), 0.35f * (1 - night));
            paint.Color = new SKColor(255, 230, 80);
            PixelArt.Disc(canvas, paint, sx, sy, 8);
            paint.Color = new SKColor(255, 252, 200);
            PixelArt.Disc(canvas, paint, sx, sy, 5);
        }
        else
        {
            paint.Color = new SKColor(240, 240, 220);
            PixelArt.Disc(canvas, paint, sx, (int)(gy * 0.3f), 6);
            paint.Color = top;
            PixelArt.Disc(canvas, paint, sx + 3, (int)(gy * 0.3f) - 2, 5);
        }

        foreach (var c in _clouds)
        {
            paint.Color = GameFx.Lerp(new SKColor(255, 255, 255, 210), new SKColor(80, 80, 120, 160), night);
            canvas.DrawRect(c.X, c.Y, c.W, Math.Max(3, 2 * _px), paint);
            canvas.DrawRect(c.X + c.W * 0.2f, c.Y - 3 * _px, c.W * 0.55f, Math.Max(3, 2 * _px), paint);
        }

        // Far mesas, near mesas, cactus silhouettes.
        Layer(canvas, paint, w, gy, 0.15f, 0.42f, 0.004f, GameFx.Lerp(new SKColor(200, 130, 110), new SKColor(40, 30, 70), night), 3);
        Layer(canvas, paint, w, gy, 0.35f, 0.26f, 0.008f, GameFx.Lerp(new SKColor(170, 90, 60), new SKColor(28, 20, 50), night), 11);
        var sil = GameFx.Lerp(new SKColor(110, 70, 40), new SKColor(16, 12, 30), night);
        var spacing = 70;
        for (var i = -1; i < w / spacing + 2; i++)
        {
            var idx = i + (int)(_scroll * 0.6f / spacing);
            var cx = i * spacing - _scroll * 0.6f % spacing + (idx * 37 % 30);
            var ch = 8 + idx * 13 % 7;
            paint.Color = sil;
            canvas.DrawRect(cx, gy - ch, 2, ch, paint);
            canvas.DrawRect(cx - 3, gy - ch + 3, 3, 1, paint);
            canvas.DrawRect(cx - 3, gy - ch + 1, 1, 2, paint);
            canvas.DrawRect(cx + 2, gy - ch + 5, 3, 1, paint);
            canvas.DrawRect(cx + 4, gy - ch + 3, 1, 2, paint);
        }

        // Ground: grass lip, sand with scrolling pebbles.
        paint.Color = GameFx.Lerp(new SKColor(230, 180, 90), new SKColor(60, 50, 70), night);
        canvas.DrawRect(0, gy, w, h - gy, paint);
        paint.Color = GameFx.Lerp(new SKColor(255, 220, 140), new SKColor(90, 80, 110), night);
        canvas.DrawRect(0, gy, w, Math.Max(1, _px), paint);
        for (var i = 0; i < w / 6 + 2; i++)
        {
            var idx = i + (int)(_scroll / 6);
            var px = i * 6 - _scroll % 6;
            var py = gy + 3 + (idx * 7919 % (Math.Max(1, h - gy - 4)));
            paint.Color = GameFx.Lerp(idx % 3 == 0 ? new SKColor(190, 130, 60) : new SKColor(250, 210, 130), new SKColor(40, 34, 50), night);
            if (idx % 2 == 0) canvas.DrawRect(px, py, idx % 5 == 0 ? 2 : 1, 1, paint);
        }

        _fx.BeginShake(canvas);
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

        // Dust from the feet, puffs on take-off and landing.
        var onGround = _dinoY >= _groundY - _standH - 1f;
        var feetX = _dinoX + 3 * _px;
        var feetY = _groundY - 1;
        if (_crashTimer == 0)
        {
            if (onGround && _frame % 3 == 0)
                _fx.Spark(feetX, feetY, -_speed * 0.4f - _fx.Rand(0, 0.6f), -_fx.Rand(0.1f, 0.6f), new SKColor(240, 210, 150), 0.4f, 1, false, 0.03f);
            if (!onGround && _wasGround) _fx.Spray(feetX + 4, feetY, -MathF.PI * 0.85f, 0.4f, new SKColor(240, 210, 150), 6, 1.4f, 0.4f, 0.05f);
            if (onGround && !_wasGround) _fx.Spray(feetX + 4, feetY, -MathF.PI / 2, 1.3f, new SKColor(240, 210, 150), 10, 1.6f, 0.45f, 0.06f);
        }
        _wasGround = onGround;
        if (_crashTimer > 0 && !_wasCrash)
        {
            _fx.Burst(_dinoX + _dinoW / 2f, _dinoY + _standH / 2f, new SKColor(255, 220, 120), 24, 2.6f, 0.8f, 0.06f);
            _fx.Flash(SKColors.White, 0.6f);
            _fx.Shake(5);
        }
        _wasCrash = _crashTimer > 0;
        if (_crashTimer == 0 && _score > 0 && _score % 100 == 0 && _frame % 2 == 0)
            _fx.Burst(w - 40, 10, new SKColor(255, 240, 120), 3, 1.5f, 0.5f, 0, true);
        _fx.DrawParticles(canvas);
        _fx.EndShake(canvas);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, 10f * _px));
            CanvasText.Draw(canvas, Canvas, $"HI {_best:00000}  {_score:00000}", new SKColor(0, 0, 0, 140),
                w - 3, Math.Max(10f, 11f * _px) + 1, size, SKTextAlign.Right, UseBdfFont);
            CanvasText.Draw(canvas, Canvas, $"HI {_best:00000}  {_score:00000}", SKColors.White,
                w - 4, Math.Max(10f, 11f * _px), size, SKTextAlign.Right, UseBdfFont);
        }

        if (_crashTimer > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.12f));
            CanvasText.Draw(canvas, Canvas, "G A M E  O V E R", new SKColor(0, 0, 0, 160), w / 2f + 1, h / 2f + 1, size, SKTextAlign.Center, UseBdfFont);
            CanvasText.Draw(canvas, Canvas, "G A M E  O V E R", SKColors.White, w / 2f, h / 2f, size, SKTextAlign.Center, UseBdfFont);
        }

        _fx.DrawOverlay(canvas, Canvas, w, h);
    }

    /// <summary>A strip of flat-topped mesas, scrolling at <paramref name="speed"/> of the ground speed.</summary>
    private void Layer(SKCanvas canvas, SKPaint paint, int w, int gy, float speed, float height, float jag, SKColor col, int seed)
    {
        paint.Color = col;
        var off = _scroll * speed;
        for (var x = 0; x < w; x++)
        {
            var u = (x + off) / 40f;
            var cell = (int)MathF.Floor(u);
            var hh = 0.4f + 0.6f * (MathF.Sin(cell * 12.9898f + seed) * 0.5f + 0.5f);
            // Flat tops with sloped edges.
            var f = u - cell;
            var edge = Math.Min(1, Math.Min(f, 1 - f) * 6);
            var top = (int)(gy - gy * height * hh * edge - jag * gy * MathF.Sin(x * 0.7f + seed));
            canvas.DrawRect(x, top, 1, gy - top, paint);
        }
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

    /// <summary>A one-colour sprite ('1' = lit) in the paint's colour, cached as an image by PixelArt.</summary>
    private void DrawSprite(SKCanvas canvas, string[] rows, float x, float y, SKPaint paint)
    {
        var col = paint.Color;
        PixelArt.Blit(canvas, rows, x, y, ch => ch == '1' ? col : SKColors.Transparent, _px);
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
