using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.FlappyBird;

/// <summary>
///     Flappy Bird with pixel-art bird, pipes and ground. Autopilot until a key is pressed in Studio
///     (Space / Up), then the player takes over.
/// </summary>
[ExtensionInfo("Flappy Bird",
    "Flappy Bird — autopilot, or play with Space / Up in Studio",
    "Games",
    IconResourceName = "flappy-bird.svg")]
public class FlappyBirdExtension : GameExtension
{
    // d outline, y body, B belly, o/n beak, w/k eye, r wing, p blush, .=empty
    private static readonly string[] BirdUp =
    {
        ".....dddddd.....",
        "...ddyyyyyydd...",
        "..dyyyyyyyyyyd..",
        ".dyywwkkyyyyyyd.",
        ".dyywwkkyyyyyyod",
        "ddyyyyypyyyyynod",
        "dyyBBBBBBBByyynd",
        "dyyBBBBBBBByyyd.",
        "drrrryyyyyyyyd..",
        ".drrrdddddddd...",
        "..ddd..........."
    };

    private static readonly string[] BirdDown =
    {
        ".....dddddd.....",
        "...ddyyyyyydd...",
        "..dyyyyyyyyyyd..",
        ".dyywwkkyyyyyyd.",
        ".dyywwkkyyyyyyod",
        "ddyyyyypyyyyynod",
        "dyyBBBBBBBByyynd",
        "dyyBBBBBBBByyyd.",
        "dyyyyyyrrrrrd...",
        ".dddddrrrrrd....",
        ".......dddd....."
    };

    private static readonly string[] Cloud =
    {
        "...11111....",
        ".111111111..",
        "11111111111.",
        ".1111111111."
    };

    private readonly Random _random = new();
    private readonly List<Pipe> _pipes = new();
    private readonly List<CloudPos> _clouds = new();

    private float _scale = 1f;
    private int _px = 2;
    private int _frame;

    private float _birdX;
    private float _birdY;
    private float _vy;
    private float _gravity;
    private float _flapV;
    private int _birdW, _birdH;
    private float _hitR;

    private float _pipeW;
    private float _gap;
    private float _speed;
    private int _groundH;
    private int _score;
    private int _best;
    private int _crashTimer;
    private bool _human;

    internal FlappyBirdExtension(ICanvas canvas) : base(canvas, GameSong.Retro)
    {
    }

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 30,
        MinValue = 16, MaxValue = 80, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 30;

    [ExtensionParameter("Gap Size", "Pipe gap (% of play area)", DefaultValue = 42, MinValue = 28, MaxValue = 58,
        Unit = "%", Order = 2)]
    public int GapPercent { get; set; } = 42;

    [ExtensionParameter("Difficulty", "Scroll speed & pipe spacing", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 3)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show the score", DefaultValue = true, Order = 4)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render the score with the crisp bitmap (BDF) font", DefaultValue = true,
        Order = 5)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "Score height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px", Order = 6)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI flies until you press a key in Studio", DefaultValue = true, Order = 7)]
    public bool AutoPilot { get; set; } = true;

    public override string Name => "Flappy Bird";
    protected override double FrameIntervalMs => GameSpeed;

    private float PlayH => Canvas.Height - _groundH;

    protected override void OnStart()
    {
        _scale = DisplayScale.GetScale(Canvas.Width, Canvas.Height);
        _px = Math.Max(1, (int)Math.Round(Canvas.Height / 64f));
        _human = false;
        Reset();
    }

    private void Reset()
    {
        _groundH = Math.Max(8, Canvas.Height * 14 / 100);
        _px = Math.Max(1, (int)Math.Round(Canvas.Height / 64f));
        _birdW = BirdUp[0].Length * _px;
        _birdH = BirdUp.Length * _px;
        _hitR = Math.Max(2f, Math.Min(_birdW, _birdH) * 0.28f);
        _birdX = Canvas.Width * 0.26f;
        _birdY = PlayH * 0.45f;
        _vy = 0;
        _pipeW = Math.Max(8, 7 * _px);
        TunePhysics();
        _pipes.Clear();
        _clouds.Clear();
        for (var i = 0; i < 3; i++)
            _clouds.Add(new CloudPos
            {
                X = i * Canvas.Width / 3f + _random.Next(20),
                Y = 4 + _random.Next(Math.Max(4, (int)(PlayH * 0.28f)))
            });
        _score = 0;
        _crashTimer = 0;
        // First pipe well to the right, gap near centre so the opening is readable.
        SpawnPipe(Canvas.Width + _pipeW, centerBias: 0.85f);
    }

    private void TunePhysics()
    {
        _gap = PlayH * (GapPercent / 100f);
        var minGap = _hitR * 5f + 6 * _px;
        _gap = Math.Clamp(_gap, minGap, PlayH * 0.62f);
        // One flap peaks at ~30% of the gap — controllable, not a ceiling slam.
        _gravity = Math.Max(0.16f, PlayH * 0.0038f);
        var apex = _gap * 0.30f;
        _flapV = -(float)Math.Sqrt(Math.Max(0.5, 2 * _gravity * apex));
        _speed = Math.Max(1.1f, (1.35f + Difficulty * 0.18f) * Math.Max(0.45f, _scale * 1.4f));
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
        TunePhysics();

        var spacing = Canvas.Width * (0.48f - Difficulty * 0.015f);
        spacing = Math.Max(spacing, _pipeW * 4f + _birdW);
        if (_pipes.Count == 0 || Canvas.Width - _pipes[^1].X > spacing)
            SpawnPipe(Canvas.Width + _pipeW, centerBias: _score < 2 ? 0.55f : 0.15f);

        for (var i = _pipes.Count - 1; i >= 0; i--)
        {
            var p = _pipes[i];
            p.X -= _speed;
            if (!p.Scored && p.X + _pipeW < _birdX)
            {
                p.Scored = true;
                _score++;
                _fx.Burst(p.X + _pipeW, p.GapTop + _gap / 2f, new SKColor(255, 240, 120), 10, 1.8f, 0.5f, 0, true);
                _fx.Pop("+1", _birdX, _birdY - _birdH, new SKColor(255, 250, 200));
                Audio.Play(GameSfx.Coin, 0.5f);
                _best = Math.Max(_best, _score);
            }

            _pipes[i] = p;
            if (p.X + _pipeW < -4) _pipes.RemoveAt(i);
        }

        for (var i = 0; i < _clouds.Count; i++)
        {
            var c = _clouds[i];
            c.X -= _speed * 0.22f;
            if (c.X + 14 * _px < 0)
            {
                c.X = Canvas.Width + _random.Next(30);
                c.Y = 4 + _random.Next(Math.Max(4, (int)(PlayH * 0.28f)));
            }

            _clouds[i] = c;
        }

        if (AutoPilot && !_human) RunAi();

        _vy += _gravity;
        _birdY += _vy;

        if (_birdY - _hitR < 1 || _birdY + _hitR > PlayH - 1)
        {
            Crash();
            return;
        }

        foreach (var p in _pipes)
        {
            if (_birdX + _hitR < p.X || _birdX - _hitR > p.X + _pipeW) continue;
            if (_birdY - _hitR < p.GapTop || _birdY + _hitR > p.GapTop + _gap)
            {
                Crash();
                return;
            }
        }
    }

    private void RunAi()
    {
        Pipe? next = null;
        foreach (var p in _pipes)
        {
            if (p.X + _pipeW < _birdX - _hitR) continue;
            next = p;
            break;
        }

        var target = PlayH * 0.45f;
        var gapTop = 2f;
        var gapBot = PlayH - 2f;
        var frames = 8;
        if (next != null)
        {
            var p = next.Value;
            gapTop = p.GapTop;
            gapBot = p.GapTop + _gap;
            target = p.GapTop + _gap * 0.42f;
            var dist = p.X - (_birdX + _hitR);
            var through = (dist + _pipeW + _hitR * 2f) / Math.Max(0.5f, _speed);
            frames = Math.Clamp((int)Math.Ceiling(through), 2, 28);
        }

        if (_vy < 0 && _birdY - _hitR <= gapTop + _hitR + _px)
            return;

        // Far from the pipe: hold altitude near the upcoming gap. A single flap does not last
        // 20+ frames, so last-moment simulation only kicks in when we are actually at the pipe.
        if (next == null || frames > 12)
        {
            if (_birdY > target + Math.Max(2f, _hitR * 0.6f) && _vy >= 0)
                DoFlap();
            return;
        }

        if (FlapClears(0, frames, gapTop, gapBot))
        {
            if (!FlapClears(1, frames, gapTop, gapBot))
                DoFlap();
            return;
        }

        if (_vy > 0 && _birdY > target)
            DoFlap();
    }

    /// <summary>
    ///     True if flapping after <paramref name="wait"/> idle frames keeps the bird inside the gap
    ///     for <paramref name="frames"/> steps (through the pipe).
    /// </summary>
    private bool FlapClears(int wait, int frames, float gapTop, float gapBot)
    {
        var y = _birdY;
        var v = _vy;
        var margin = _hitR + Math.Max(1, _px);
        var pipeStart = Math.Max(0,
            frames - (int)Math.Ceiling((_pipeW + _hitR * 2f) / Math.Max(0.5f, _speed)) - 1);

        for (var i = 0; i < frames; i++)
        {
            if (i == wait)
                v = _flapV;
            v += _gravity;
            y += v;

            if (y - _hitR < 1 || y + _hitR > PlayH - 1)
                return false;

            if (i >= pipeStart && (y - margin <= gapTop || y + margin >= gapBot))
                return false;
        }

        return true;
    }

    private void DoFlap()
    {
        _vy = _flapV;
        Audio.Play(GameSfx.Flap, 0.5f);
        _fx.Spray(_birdX - _birdW * 0.3f, _birdY, MathF.PI * 0.75f, 0.5f, new SKColor(255, 240, 200), 4, 1.2f, 0.45f, 0.03f);
    }

    [ExtensionMethod("Flap", "Flap the bird — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Space|Up", Order = 1)]
    public void Flap()
    {
        lock (Sync)
        {
            _human = true;
            if (_crashTimer > 0)
            {
                _crashTimer = 0;
                Reset();
            }

            DoFlap();
        }
    }

    private void Crash()
    {
        if (_crashTimer > 0) return;
        _crashTimer = 32;
        _fx.Burst(_birdX, _birdY, new SKColor(255, 220, 80), 18, 2.5f, 0.8f, 0.08f);
        _fx.Spray(_birdX, _birdY, -MathF.PI / 2, 1.2f, new SKColor(255, 255, 240), 8, 2f, 0.9f, 0.05f);
        _fx.Flash(SKColors.White, 0.7f);
        _fx.Shake(5);
        Audio.Play(GameSfx.Hit, 0.7f);
        Audio.Play(GameSfx.Die, 0.5f);
    }

    private void SpawnPipe(float x, float centerBias)
    {
        var margin = Math.Max(_hitR * 2.5f, PlayH * 0.1f);
        var lo = margin;
        var hi = PlayH - _gap - margin;
        if (hi < lo) { lo = 2; hi = Math.Max(lo + 1, PlayH - _gap - 2); }
        var randomTop = lo + (float)_random.NextDouble() * (hi - lo);
        var centerTop = (PlayH - _gap) * 0.5f;
        var gapTop = randomTop + (centerTop - randomTop) * Math.Clamp(centerBias, 0, 1);
        gapTop = Math.Clamp(gapTop, lo, hi);
        _pipes.Add(new Pipe { X = x, GapTop = gapTop, Scored = false });
    }

    private readonly GameFx _fx = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastRender;
    private float _scroll;

    /// <summary>
    ///     The sky runs from day through sunset to night as the score climbs (sun, then moon and stars),
    ///     a city skyline and hedges scroll behind the pipes at their own speeds, feathers puff on every flap,
    ///     a sparkle marks each pipe passed, and a crash shakes and flashes.
    /// </summary>
    private void Draw(SKCanvas canvas)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastRender, 0.001, 0.1);
        _lastRender = now;
        _fx.Update(dt);
        if (_crashTimer == 0) _scroll += _speed;

        var h = Canvas.Height;
        var w = Canvas.Width;

        // Time of day from the score: day, sunset, night, dawn, round and round every 40 pipes.
        var phase = _score % 40 / 40f;
        var dayTop = new SKColor(92, 196, 220);
        var dayBot = new SKColor(168, 224, 236);
        SKColor top, bot;
        float night;
        if (phase < 0.4f) { top = dayTop; bot = dayBot; night = 0; }
        else if (phase < 0.55f)
        {
            var t = (phase - 0.4f) / 0.15f;
            top = GameFx.Lerp(dayTop, new SKColor(70, 60, 140), t);
            bot = GameFx.Lerp(dayBot, new SKColor(255, 150, 90), t);
            night = t * 0.4f;
        }
        else if (phase < 0.85f)
        {
            var t = Math.Min(1, (phase - 0.55f) / 0.1f);
            top = GameFx.Lerp(new SKColor(70, 60, 140), new SKColor(10, 12, 40), t);
            bot = GameFx.Lerp(new SKColor(255, 150, 90), new SKColor(30, 40, 90), t);
            night = 0.4f + 0.6f * t;
        }
        else
        {
            var t = (phase - 0.85f) / 0.15f;
            top = GameFx.Lerp(new SKColor(10, 12, 40), dayTop, t);
            bot = GameFx.Lerp(new SKColor(30, 40, 90), dayBot, t);
            night = 1 - t;
        }
        GameFx.VerticalGradient(canvas, w, h, top, bot, 0, (int)PlayH);

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        if (night > 0.6f)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, w, PlayH * 0.7f));
            GameFx.Stars(canvas, w, (int)(PlayH * 0.7f), _fx.Time, _scroll * 0.05f, 0, 160);
            canvas.Restore();
            paint.Color = new SKColor(240, 240, 210, (byte)(255 * night));
            PixelArt.Disc(canvas, paint, (int)(w * 0.8f), (int)(PlayH * 0.2f), Math.Max(3, h / 18));
        }
        else
        {
            GameFx.GlowDisc(canvas, w * 0.8f, PlayH * 0.22f, h * 0.22f, new SKColor(255, 230, 140), 0.35f);
            paint.Color = new SKColor(255, 240, 160);
            PixelArt.Disc(canvas, paint, (int)(w * 0.8f), (int)(PlayH * 0.22f), Math.Max(3, h / 16));
        }

        paint.Color = GameFx.Lerp(new SKColor(236, 248, 255), new SKColor(70, 80, 120), night);
        foreach (var c in _clouds)
            DrawMask(canvas, Cloud, c.X, c.Y, paint);

        // Far skyline (slow) and near hedges (faster), with lit windows at night.
        var city = GameFx.Lerp(new SKColor(140, 190, 200), new SKColor(20, 24, 60), night);
        var bw = Math.Max(6, w / 24);
        for (var i = -1; i < w / bw + 2; i++)
        {
            var idx = i + (int)(_scroll * 0.25f / bw);
            var bx = i * bw - (_scroll * 0.25f % bw);
            var bh = (int)(PlayH * (0.18f + 0.22f * (MathF.Sin(idx * 12.9898f) * 0.5f + 0.5f)));
            paint.Color = city;
            canvas.DrawRect(bx, PlayH - bh, bw - 1, bh, paint);
            if (night > 0.5f)
            {
                paint.Color = new SKColor(255, 220, 120, (byte)(180 * night));
                for (var wy = (int)(PlayH - bh + 3); wy < PlayH - 3; wy += 4)
                for (var wx = 2; wx < bw - 2; wx += 3)
                    if ((idx * 7 + wx * 3 + wy) % 5 == 0)
                        canvas.DrawRect(bx + wx, wy, 1, 1, paint);
            }
        }
        var hedge = GameFx.Lerp(new SKColor(90, 180, 80), new SKColor(20, 60, 40), night);
        paint.Color = hedge;
        for (var x = 0; x < w; x++)
        {
            var hx = x + _scroll * 0.6f;
            var hh = 4 + (int)(3 * (MathF.Sin(hx * 0.15f) + MathF.Sin(hx * 0.37f)));
            canvas.DrawRect(x, PlayH - hh, 1, hh, paint);
        }

        _fx.BeginShake(canvas);
        foreach (var p in _pipes)
            DrawPipe(canvas, paint, p);

        DrawGround(canvas, paint);

        var sprite = _vy < 0 ? BirdUp : BirdDown;
        DrawBird(canvas, sprite, _birdX - _birdW * 0.45f, _birdY - _birdH * 0.5f);
        _fx.DrawParticles(canvas);
        _fx.EndShake(canvas);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.13f));
            CanvasText.Draw(canvas, Canvas, $"{_score}", new SKColor(0, 0, 0, 140),
                w / 2f + 1, h * 0.16f + 1, size, SKTextAlign.Center, UseBdfFont);
            CanvasText.Draw(canvas, Canvas, $"{_score}", SKColors.White,
                w / 2f, h * 0.16f, size, SKTextAlign.Center, UseBdfFont);
        }

        _fx.DrawOverlay(canvas, Canvas, w, h, Math.Max(7f, h * 0.07f));
    }

    private void DrawPipe(SKCanvas canvas, SKPaint paint, Pipe p)
    {
        var x = p.X;
        var w = _pipeW;
        var lip = Math.Max(2, _px + 1);
        var capH = Math.Max(3, 2 * _px + 1);
        var body = new SKColor(82, 176, 58);
        var hi = new SKColor(168, 224, 86);
        var lo = new SKColor(46, 118, 40);
        var rim = new SKColor(34, 86, 32);

        void Column(float top, float height)
        {
            if (height <= 0) return;
            var edge = Math.Max(1, _px);
            paint.Color = hi;
            canvas.DrawRect(x, top, edge, height, paint);
            paint.Color = body;
            canvas.DrawRect(x + edge, top, w - edge * 2, height, paint);
            paint.Color = lo;
            canvas.DrawRect(x + w - edge, top, edge, height, paint);
            // Brick lines.
            paint.Color = new SKColor(58, 140, 48, 160);
            var step = Math.Max(3, 3 * _px);
            for (var by = top + step; by < top + height - 1; by += step)
                canvas.DrawRect(x + edge, by, w - edge * 2, 1, paint);
        }

        Column(0, p.GapTop - capH);
        Column(p.GapTop + _gap + capH, PlayH - (p.GapTop + _gap + capH));

        void Cap(float top)
        {
            paint.Color = rim;
            canvas.DrawRect(x - lip, top, w + lip * 2, capH, paint);
            paint.Color = hi;
            canvas.DrawRect(x - lip + 1, top + 1, Math.Max(1, _px), capH - 2, paint);
            paint.Color = body;
            canvas.DrawRect(x - lip + 1 + _px, top + 1, w + lip * 2 - 2 - _px * 2, capH - 2, paint);
            paint.Color = lo;
            canvas.DrawRect(x + w + lip - 1 - _px, top + 1, Math.Max(1, _px), capH - 2, paint);
        }

        Cap(p.GapTop - capH);
        Cap(p.GapTop + _gap);
    }

    private void DrawGround(SKCanvas canvas, SKPaint paint)
    {
        var y = PlayH;
        paint.Color = new SKColor(214, 168, 70);
        canvas.DrawRect(0, y, Canvas.Width, _groundH, paint);
        paint.Color = new SKColor(116, 191, 46);
        canvas.DrawRect(0, y, Canvas.Width, Math.Max(2, _px + 1), paint);
        paint.Color = new SKColor(88, 150, 34);
        canvas.DrawRect(0, y + Math.Max(2, _px + 1), Canvas.Width, 1, paint);

        paint.Color = new SKColor(186, 138, 52);
        var tile = Math.Max(6, 5 * _px);
        var scroll = (int)(_frame * _speed) % tile;
        for (var x = -scroll; x < Canvas.Width; x += tile)
            canvas.DrawRect(x, y + _groundH * 0.45f, Math.Max(2, _px), Math.Max(2, _px), paint);
    }

    private void DrawBird(SKCanvas canvas, string[] rows, float x, float y)
    {
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var ry = 0; ry < rows.Length; ry++)
        {
            var row = rows[ry];
            for (var rx = 0; rx < row.Length; rx++)
            {
                paint.Color = row[rx] switch
                {
                    'd' => new SKColor(36, 28, 24),
                    'y' => new SKColor(255, 214, 48),
                    'B' => new SKColor(255, 244, 196),
                    'o' => new SKColor(255, 148, 36),
                    'n' => new SKColor(220, 84, 16),
                    'w' => SKColors.White,
                    'k' => new SKColor(24, 20, 18),
                    'r' => new SKColor(236, 76, 52),
                    'p' => new SKColor(255, 150, 140),
                    _ => SKColors.Transparent
                };
                if (paint.Color.Alpha == 0) continue;
                canvas.DrawRect(x + rx * _px, y + ry * _px, _px, _px, paint);
            }
        }
    }

    private void DrawMask(SKCanvas canvas, string[] rows, float x, float y, SKPaint paint)
    {
        for (var ry = 0; ry < rows.Length; ry++)
        {
            var row = rows[ry];
            for (var rx = 0; rx < row.Length; rx++)
                if (row[rx] == '1')
                    canvas.DrawRect(x + rx * _px, y + ry * _px, _px, _px, paint);
        }
    }

    private struct Pipe
    {
        public float X;
        public float GapTop;
        public bool Scored;
    }

    private struct CloudPos
    {
        public float X;
        public float Y;
    }
}
