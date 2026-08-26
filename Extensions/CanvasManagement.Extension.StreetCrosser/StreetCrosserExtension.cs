using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.StreetCrosser;

/// <summary>
///     Original frogger-style hopper: neon cars, river logs, lily-pad homes.
///     Autopilot until a key is pressed in Studio.
/// </summary>
[ExtensionInfo("Street Crosser",
    "Hop across neon traffic and a river — arrows / WASD, or autopilot",
    "Games",
    IconResourceName = "street-crosser.svg")]
public class StreetCrosserExtension : ICanvasExtension, IDisposable
{
    // d outline, g body, w/k eye, p blush
    private static readonly string[] Frog =
    {
        "..dggggd..",
        ".dggwwggd.",
        "dgggkkgggd",
        "dggggggggd",
        ".dggppggd.",
        "..dggggd..",
        ".d.d..d.d.",
        "d.......d."
    };

    private static readonly string[] Car =
    {
        "....dddddd....",
        "..ddccccccdd..",
        ".dccwwccwwccd.",
        "dccccccccccccd",
        "dccccccccccccd",
        ".dbbddbbddbbd.",
        "..d..d..d..d.."
    };

    private static readonly string[] Truck =
    {
        "......dddddddddd",
        "....ddyyyyyyyydd",
        "...dyywwyyyywwyd",
        ".ddyyyyyyyyyyyyyd",
        "dccccccccccccccccd",
        "dbbddbbddbbddbbdd.",
        ".d..d..d..d..d..d."
    };

    private static readonly string[] Log =
    {
        ".dddddddddddddddddd.",
        "dnnnnnnnnnnnnnnnnnnd",
        "dnnnwnnnnnnnnwnnnnnd",
        "dnnnnnnnnnnnnnnnnnnd",
        ".dddddddddddddddddd."
    };

    private static readonly string[] Pad =
    {
        "..dggggd..",
        ".dggggggd.",
        "dggggggggd",
        ".dggggggd.",
        "..dggggd.."
    };

    private static readonly SKColor[] CarColors =
    {
        new(255, 70, 90), new(255, 160, 30), new(80, 255, 140),
        new(60, 190, 255), new(200, 80, 255), new(255, 80, 210)
    };

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Mover> _movers = new();
    private readonly bool[] _homes = new bool[5];

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _px = 2;
    private int _cols, _rows;
    private float _cw, _ch;
    private int _col, _row;
    private float _fx, _fy, _tx, _ty, _sx, _sy;
    private int _hop;
    private int _score, _lives, _time;
    private int _crashTimer;
    private string _death = "";
    private bool _human;
    private int _frame;

    internal StreetCrosserExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 28,
        MinValue = 16, MaxValue = 60, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 28;

    [ExtensionParameter("Difficulty", "Traffic speed and density", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show the HUD", DefaultValue = true, Order = 3)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = false, Order = 4)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI hops until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    public string Name => "Street Crosser";
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
            _lives = 3;
            Layout();
            NewLife();
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

    private void Layout()
    {
        _cols = 16;
        _rows = 13;
        _cw = _canvas.Width / (float)_cols;
        _ch = _canvas.Height / (float)_rows;
        _movers.Clear();
        for (var i = 0; i < _homes.Length; i++) _homes[i] = false;

        void Lane(int row, float spd, int len, bool water, int count)
        {
            var truck = !water && len >= 3;
            for (var i = 0; i < count; i++)
            {
                var visW = water
                    ? len * _cw
                    : (truck ? Truck[0].Length : Car[0].Length);
                _movers.Add(new Mover
                {
                    Row = row,
                    X = i * (_canvas.Width / (float)count) + _random.Next(Math.Max(1, (int)_cw)),
                    W = visW,
                    Spd = spd,
                    Water = water,
                    Truck = truck,
                    Color = CarColors[(row + i) % CarColors.Length]
                });
            }
        }

        var d = 0.6f + Difficulty * 0.12f;
        Lane(10, 1.1f * d * _px, 2, false, 3);
        Lane(9, -1.4f * d * _px, 3, false, 3);
        Lane(8, 1.8f * d * _px, 2, false, 4);
        Lane(7, -1.0f * d * _px, 4, false, 2);
        Lane(6, 1.5f * d * _px, 2, false, 3);
        Lane(4, 0.9f * d * _px, 3, true, 3);
        Lane(3, -1.1f * d * _px, 2, true, 4);
        Lane(2, 0.8f * d * _px, 4, true, 2);
        Lane(1, -1.3f * d * _px, 3, true, 3);
    }

    private void NewLife()
    {
        _col = _cols / 2;
        _row = _rows - 1;
        _fx = _tx = CellX(_col);
        _fy = _ty = CellY(_row);
        _hop = 0;
        _time = 320 - Difficulty * 10;
        _crashTimer = 0;
        _death = "";
    }

    private float CellX(int c) => c * _cw + _cw * 0.5f;
    private float CellY(int r) => r * _ch + _ch * 0.5f;

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
                        if (_lives <= 0) { _lives = 3; _score = 0; Layout(); }
                        NewLife();
                    }
                }
                else
                    Update();

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StreetCrosser] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        for (var i = 0; i < _movers.Count; i++)
        {
            var m = _movers[i];
            m.X += m.Spd;
            if (m.Spd > 0 && m.X > _canvas.Width + m.W) m.X = -m.W;
            if (m.Spd < 0 && m.X + m.W < 0) m.X = _canvas.Width;
            _movers[i] = m;
        }

        if (_hop > 0)
        {
            _hop--;
            var t = 1f - _hop / 6f;
            _fx = _sx + (_tx - _sx) * t;
            _fy = _sy + (_ty - _sy) * t;
            if (_hop == 0) { _fx = _tx; _fy = _ty; }
            return;
        }

        _time--;
        if (_time <= 0) { Squash("time up"); return; }

        if (AutoPilot && !_human) AiHop();

        CarryOnLog();
        if (_fx < 4 || _fx > _canvas.Width - 4) { Squash("swept away"); return; }
        if (IsRoad(_row) && HitCar()) { Squash("hit by a car"); return; }
        if (IsWater(_row) && !OnLog()) { Squash("fell in"); return; }

        if (_row == 0)
        {
            var slot = HomeSlot(_fx);
            if (slot < 0) { Squash("missed the pad"); return; }
            if (_homes[slot]) { Squash("pad taken"); return; }
            _homes[slot] = true;
            _score += 100 + _time;
            if (_homes.All(h => h))
            {
                _score += 500;
                Layout();
            }

            NewLife();
        }
    }

    private void CarryOnLog()
    {
        if (!IsWater(_row) || _hop > 0) return;
        foreach (var m in _movers)
        {
            if (m.Row != _row || !m.Water) continue;
            if (FrogOverlaps(m.X, m.W, _fx))
            {
                _fx += m.Spd;
                _col = Math.Clamp((int)(_fx / _cw), 0, _cols - 1);
                return;
            }
        }
    }

    private bool HitCar()
    {
        foreach (var m in _movers)
        {
            if (m.Row != _row || m.Water) continue;
            if (FrogOverlaps(m.X, SpriteW(m), _fx)) return true;
        }

        return false;
    }

    private bool OnLog()
    {
        foreach (var m in _movers)
        {
            if (m.Row != _row || !m.Water) continue;
            if (FrogOverlaps(m.X, m.W, _fx)) return true;
        }

        return false;
    }

    private static float SpriteW(Mover m) => m.Truck ? Truck[0].Length : Car[0].Length;

    private bool FrogOverlaps(float mx, float mw, float frogX)
    {
        var half = Frog[0].Length * 0.35f;
        return frogX + half > mx + 1 && frogX - half < mx + mw - 1;
    }

    private int HomeSlot(float x)
    {
        var band = _canvas.Width / 5f;
        var slot = Math.Clamp((int)(x / band), 0, 4);
        var cx = (slot + 0.5f) * band;
        return Math.Abs(x - cx) <= band * 0.34f ? slot : -1;
    }

    private static bool IsRoad(int r) => r is >= 6 and <= 10;
    private static bool IsWater(int r) => r is >= 1 and <= 4;

    private void AiHop()
    {
        if (_hop > 0) return;
        var options = new (int dc, int dr)[] { (0, -1), (1, 0), (-1, 0), (0, 1) };
        var bestScore = float.MinValue;
        var best = (dc: 0, dr: 0);
        var found = false;

        foreach (var (dc, dr) in options)
        {
            var nc = _col + dc;
            var nr = _row + dr;
            if (nc < 0 || nc >= _cols || nr < 0 || nr >= _rows) continue;
            var score = ScoreHop(nc, nr, dc, dr);
            if (score > bestScore)
            {
                bestScore = score;
                best = (dc, dr);
                found = true;
            }
        }

        var stay = ScoreStay();
        if (!found || stay >= bestScore) return; // wait for a gap
        TryHop(best.dc, best.dr);
    }

    private float ScoreStay()
    {
        if (IsRoad(_row) && CarHits(_fx, _row, 0)) return -50;
        if (IsWater(_row) && !LogHits(_fx, _row, 0)) return -50;
        return IsRoad(_row) || IsWater(_row) ? 1.2f : 0.4f;
    }

    private float ScoreHop(int c, int r, int dc, int dr)
    {
        var x = CellX(c);
        const int land = 7; // hop lasts 6 frames, check the landing beat
        if (r == 0)
        {
            var slot = HomeSlot(x);
            if (slot < 0 || _homes[slot]) return float.MinValue;
            return 40f;
        }

        if (IsRoad(r))
        {
            if (CarHits(x, r, land) || CarHits(x, r, land + 3)) return float.MinValue;
        }
        else if (IsWater(r))
        {
            if (!LogHits(x, r, land)) return float.MinValue;
            var lx = PredictRide(x, r, land);
            if (lx < 8 || lx > _canvas.Width - 8) return float.MinValue;
            if (!LogHits(lx, r, land + 6)) return -5f;
        }

        var score = 0f;
        if (dr < 0) score += 12f;
        if (dr > 0) score -= 6f;
        if (dc != 0) score += 2f;
        // Prefer the column closest to an open home.
        var homeX = BestHomeX();
        score -= Math.Abs(x - homeX) / _cw * 0.35f;
        return score;
    }

    private float BestHomeX()
    {
        var band = _canvas.Width / 5f;
        var best = _canvas.Width / 2f;
        var bestD = float.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            if (_homes[i]) continue;
            var cx = (i + 0.5f) * band;
            var d = Math.Abs(cx - _fx);
            if (d < bestD) { bestD = d; best = cx; }
        }

        return best;
    }

    private bool CarHits(float x, int row, int frames)
    {
        foreach (var m in _movers)
        {
            if (m.Row != row || m.Water) continue;
            var mx = WrapX(m.X + m.Spd * frames, SpriteW(m));
            if (FrogOverlaps(mx, SpriteW(m), x)) return true;
        }

        return false;
    }

    private bool LogHits(float x, int row, int frames)
    {
        foreach (var m in _movers)
        {
            if (m.Row != row || !m.Water) continue;
            var mx = WrapX(m.X + m.Spd * frames, m.W);
            if (FrogOverlaps(mx, m.W, x)) return true;
        }

        return false;
    }

    private float PredictRide(float x, int row, int frames)
    {
        foreach (var m in _movers)
        {
            if (m.Row != row || !m.Water) continue;
            var mx = WrapX(m.X + m.Spd * frames, m.W);
            if (FrogOverlaps(mx, m.W, x)) return x + m.Spd * 8f;
        }

        return x;
    }

    private float WrapX(float x, float w)
    {
        var span = _canvas.Width + w * 2f;
        while (x > _canvas.Width + w) x -= span;
        while (x < -w) x += span;
        return x;
    }

    private void TryHop(int dc, int dr)
    {
        if (_hop > 0 || _crashTimer > 0) return;
        var nc = _col + dc;
        var nr = _row + dr;
        if (nc < 0 || nc >= _cols || nr < 0 || nr >= _rows) return;
        _col = nc;
        _row = nr;
        _sx = _fx;
        _sy = _fy;
        _tx = CellX(_col);
        _ty = CellY(_row);
        _hop = 6;
        if (dr < 0) _score += 10;
    }

    private void Squash(string why)
    {
        _lives--;
        _crashTimer = 36;
        _death = why;
    }

    [ExtensionMethod("Hop Up", "Hop forward — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Up|W", Order = 1)]
    public void HopUp()
    {
        lock (_lock) { _human = true; TryHop(0, -1); }
    }

    [ExtensionMethod("Hop Down", "Hop back",
        Category = "Controls", KeyboardShortcut = "Down|S", Order = 2)]
    public void HopDown()
    {
        lock (_lock) { _human = true; TryHop(0, 1); }
    }

    [ExtensionMethod("Hop Left", "Hop left",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 3)]
    public void HopLeft()
    {
        lock (_lock) { _human = true; TryHop(-1, 0); }
    }

    [ExtensionMethod("Hop Right", "Hop right",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 4)]
    public void HopRight()
    {
        lock (_lock) { _human = true; TryHop(1, 0); }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var canvas = new SKCanvas(bb);
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        var w = _canvas.Width;
        var h = _canvas.Height;

        void Band(int r0, int r1, SKColor a, SKColor b)
        {
            for (var r = r0; r < r1; r++)
            {
                paint.Color = r % 2 == 0 ? a : b;
                canvas.DrawRect(0, r * _ch, w, _ch, paint);
            }
        }

        Band(0, 1, new SKColor(18, 90, 32), new SKColor(18, 90, 32));
        // Hedge between home pads so a miss is obvious.
        paint.Color = new SKColor(12, 70, 24);
        var band = w / 5f;
        for (var i = 0; i < 5; i++)
        {
            var left = i * band;
            var right = (i + 1) * band;
            var padL = left + band * 0.18f;
            var padR = right - band * 0.18f;
            canvas.DrawRect(left, 0, padL - left, _ch, paint);
            canvas.DrawRect(padR, 0, right - padR, _ch, paint);
        }
        for (var y = (int)_ch; y < (int)(5 * _ch); y++)
        {
            paint.Color = (y + _frame / 8) % 3 == 0 ? new SKColor(36, 110, 220) : new SKColor(28, 90, 200);
            canvas.DrawRect(0, y, w, 1, paint);
        }

        Band(5, 6, new SKColor(70, 190, 64), new SKColor(70, 190, 64));
        Band(6, 11, new SKColor(32, 32, 40), new SKColor(24, 24, 30));
        paint.Color = new SKColor(255, 214, 48);
        for (var x = 0; x < w; x += 8)
            canvas.DrawRect(x, 8 * _ch + _ch / 2f, 4, 1, paint);
        Band(11, 13, new SKColor(70, 190, 64), new SKColor(62, 170, 54));

        for (var i = 0; i < 5; i++)
        {
            var hx = (i + 0.5f) * w / 5f - Pad[0].Length / 2f;
            PixelArt.Blit(canvas, Pad, hx, 1, ch => ch switch
            {
                'd' => new SKColor(16, 60, 24),
                'g' => _homes[i] ? new SKColor(90, 255, 70) : new SKColor(24, 90, 40),
                _ => SKColors.Transparent
            });
        }

        foreach (var m in _movers)
        {
            var y = m.Row * _ch + 1;
            if (m.Water)
            {
                var logW = Log[0].Length;
                for (var lx = 0; lx < m.W; lx += logW - 2)
                    PixelArt.Blit(canvas, Log, m.X + lx, y + 2, ChLog);
            }
            else
            {
                var spr = m.Truck ? Truck : Car;
                PixelArt.Blit(canvas, spr, m.X, y, ch => ChCar(ch, m.Color), flipX: m.Spd < 0);
            }
        }

        if (_crashTimer == 0 || _frame / 2 % 2 == 0)
            PixelArt.Blit(canvas, Frog, _fx - Frog[0].Length / 2f, _fy - Frog.Length / 2f, ChFrog);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, h * 0.08f));
            CanvasText.Draw(canvas, _canvas, $"{_score}  x{_lives}  {_time}", SKColors.White,
                4, size + 1, size, SKTextAlign.Left, UseBdfFont);
        }

        if (_crashTimer > 0 && !string.IsNullOrEmpty(_death))
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.11f));
            CanvasText.Draw(canvas, _canvas, _death.ToUpperInvariant(), SKColors.White,
                w / 2f, h * 0.48f, size, SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private static SKColor ChFrog(char ch) => ch switch
    {
        'd' => new SKColor(16, 70, 20),
        'g' => new SKColor(90, 255, 50),
        'w' => SKColors.White,
        'k' => new SKColor(16, 16, 16),
        'p' => new SKColor(40, 180, 40),
        _ => SKColors.Transparent
    };

    private static SKColor ChLog(char ch) => ch switch
    {
        'd' => new SKColor(70, 36, 16),
        'n' => new SKColor(168, 96, 40),
        'w' => new SKColor(196, 130, 70),
        _ => SKColors.Transparent
    };

    private static SKColor ChCar(char ch, SKColor body) => ch switch
    {
        'd' => new SKColor(16, 16, 20),
        'c' => body,
        'y' => new SKColor(255, 214, 48),
        'w' => new SKColor(180, 230, 255),
        'b' => new SKColor(30, 30, 36),
        _ => SKColors.Transparent
    };

    private struct Mover
    {
        public int Row;
        public float X, W, Spd;
        public bool Water;
        public bool Truck;
        public SKColor Color;
    }
}
