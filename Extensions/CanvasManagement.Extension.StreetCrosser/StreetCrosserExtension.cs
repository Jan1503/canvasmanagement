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
        "......dddddddddd..",
        "....ddyyyyyyyydd..",
        "...dyywwyyyywwyd..",
        ".ddyyyyyyyyyyyyyd.",
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

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = true, Order = 4)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI hops until you press a key in Studio", DefaultValue = true, Order = 6)]
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
            _sound.Stop();
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
                _sound.Duck(_lives <= 0 && _crashTimer > 0 ? 0.3f : 1f);
                _sound.Update();
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
            m.X = WrapX(m.X + m.Spd, Width(m));
            _movers[i] = m;
        }

        // In the air: nothing can hit the frog until it lands.
        if (_hop > 0)
        {
            _hop--;
            var t = 1f - _hop / 6f;
            _fx = _sx + (_tx - _sx) * t;
            _fy = _sy + (_ty - _sy) * t;
            if (_hop == 0)
            {
                _fx = _tx;
                _fy = _ty;
            }
            return;
        }

        _time--;
        if (_time <= 0) { Squash("time up"); return; }

        // Landed (or standing): ride the log, then check what is under the frog now.
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
            _gfx.Burst((slot + 0.5f) * _canvas.Width / 5f, _ch / 2f, new SKColor(200, 255, 120), 20, 2f, 0.7f, 0.03f);
            _gfx.Pop($"+{100 + _time}", (slot + 0.5f) * _canvas.Width / 5f, _ch * 2, new SKColor(255, 255, 160));
            _sound.Play(GameSfx.Score);
            if (_homes.All(h => h))
            {
                _score += 500;
                _gfx.Confetti(_canvas.Width / 2f, _ch, 60, 3f);
                _sound.Play(GameSfx.LevelUp);
                Layout();
            }

            NewLife();
            return;
        }

        if (AutoPilot && !_human) AiHop();
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

    private bool HitCar() => CarAt(_fx, _row, 0);

    private bool OnLog() => LogAt(_fx, _row, 0) != null;

    private static float SpriteW(Mover m) => m.Truck ? Truck[0].Length : Car[0].Length;

    private static float Width(Mover m) => m.Water ? m.W : SpriteW(m);

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

    /// <summary>Is a car on <paramref name="row"/> over x, <paramref name="ahead"/> ticks from now?</summary>
    private bool CarAt(float x, int row, int ahead)
    {
        foreach (var m in _movers)
        {
            if (m.Row != row || m.Water) continue;
            var w = SpriteW(m);
            if (FrogOverlaps(WrapX(m.X + m.Spd * ahead, w), w, x)) return true;
        }
        return false;
    }

    /// <summary>Speed of the log under x on <paramref name="row"/>, <paramref name="ahead"/> ticks from now; null if water.</summary>
    private float? LogAt(float x, int row, int ahead)
    {
        foreach (var m in _movers)
        {
            if (m.Row != row || !m.Water) continue;
            if (FrogOverlaps(WrapX(m.X + m.Spd * ahead, m.W), m.W, x)) return m.Spd;
        }
        return null;
    }

    private float WrapX(float x, float w)
    {
        var span = _canvas.Width + w * 2f;
        while (x > _canvas.Width + w) x -= span;
        while (x < -w) x += span;
        return x;
    }

    // ------------------------------------------------------------------ autopilot

    private readonly Queue<(int At, int Dc, int Dr, int Row, float X)> _plan = new();

    /// <summary>
    ///     Plans with the game's own rules: from now, every tick the frog may wait or hop (six ticks in the air,
    ///     checked when it lands). Cars and logs are where they will be at that tick, logs carry the frog.
    ///     A breadth-first search over (tick, row, x) finds the earliest landing on a free pad; the plan is
    ///     followed step by step and redone whenever the frog is not where the plan said.
    /// </summary>
    private void AiHop()
    {
        if (_plan.Count > 0)
        {
            var next = _plan.Peek();
            if (next.At < _frame || next.At == _frame && (next.Row != _row || Math.Abs(next.X - _fx) > 0.75f))
                _plan.Clear();
        }
        if (_plan.Count == 0) Plan();
        if (_plan.Count == 0) return;
        var step = _plan.Peek();
        if (step.At != _frame) return; // waiting is part of the plan
        _plan.Dequeue();
        TryHop(step.Dc, step.Dr);
    }

    private sealed class Node
    {
        public int T, Row;
        public float X;
        public Node? Parent;
        public int Dc, Dr; // the hop that led here (0,0 = waited)
    }

    private void Plan()
    {
        const int horizon = 280;
        const int air = 7;
        var buckets = new List<Node>[horizon + air + 1];
        var seen = new HashSet<long>();
        var start = new Node { T = 0, Row = _row, X = _fx };
        buckets[0] = new List<Node> { start };
        Node? goal = null;
        var best = start;
        (int, int)[] moves = { (0, -1), (-1, 0), (1, 0), (0, 1) };

        bool Visit(Node n)
        {
            var key = ((long)n.T << 24) | ((long)n.Row << 16) | (uint)(int)MathF.Round(n.X * 0.5f) & 0xFFFF;
            return seen.Add(key);
        }

        for (var t = 0; t <= horizon && goal == null; t++)
        {
            var layer = buckets[t];
            if (layer == null) continue;
            foreach (var n in layer)
            {
                if (n.Row < best.Row || n.Row == best.Row && n.T > best.T) best = n;

                // Wait one tick.
                if (n.Row != 0 && Land(n.Row, n.X, t + 1, out var wx))
                    Add(new Node { T = t + 1, Row = n.Row, X = wx, Parent = n });

                foreach (var (dc, dr) in moves)
                {
                    var row = n.Row + dr;
                    if (row < 0 || row >= _rows) continue;
                    var x = Math.Clamp(n.X + dc * _cw, _cw * 0.5f, _canvas.Width - _cw * 0.5f);
                    if (dc != 0 && Math.Abs(x - n.X) < _cw * 0.5f) continue;
                    if (row == 0)
                    {
                        var slot = HomeSlot(x);
                        if (slot < 0 || _homes[slot]) continue;
                        goal = new Node { T = t + air, Row = 0, X = x, Parent = n, Dc = dc, Dr = dr };
                        break;
                    }
                    if (!Land(row, x, t + air, out var lx)) continue;
                    Add(new Node { T = t + air, Row = row, X = lx, Parent = n, Dc = dc, Dr = dr });
                }
                if (goal != null) break;
            }
        }

        var end = goal ?? best;
        var path = new List<Node>();
        for (var n = end; n?.Parent != null; n = n.Parent) path.Add(n);
        path.Reverse();
        foreach (var n in path)
            if (n.Dc != 0 || n.Dr != 0)
                _plan.Enqueue((_frame + n.Parent!.T, n.Dc, n.Dr, n.Parent.Row, n.Parent.X));
        return;

        void Add(Node n)
        {
            if (n.T > horizon + air || !Visit(n)) return;
            (buckets[n.T] ??= new List<Node>()).Add(n);
        }
    }

    /// <summary>
    ///     The checks the game makes on a tick the frog is on the ground: carried by a log, inside the screen,
    ///     no car, not in the water. <paramref name="x"/> becomes the position after the carry.
    /// </summary>
    private bool Land(int row, float x0, int ahead, out float x)
    {
        x = x0;
        if (IsWater(row))
        {
            var spd = LogAt(x0, row, ahead);
            if (spd == null) return false;
            x += spd.Value;
            if (LogAt(x, row, ahead) == null) return false;
        }
        if (x < 4 || x > _canvas.Width - 4) return false;
        if (IsRoad(row) && CarAt(x, row, ahead)) return false;
        return true;
    }

    private void TryHop(int dc, int dr)
    {
        if (_hop > 0 || _crashTimer > 0) return;
        var nr = _row + dr;
        if (nr < 0 || nr >= _rows) return;
        // Sideways hops move one cell from where the frog is (it may be riding a log); up and down keep x.
        var nx = Math.Clamp(_fx + dc * _cw, _cw * 0.5f, _canvas.Width - _cw * 0.5f);
        if (dc != 0 && Math.Abs(nx - _fx) < _cw * 0.5f) return;
        _row = nr;
        _col = Math.Clamp((int)(nx / _cw), 0, _cols - 1);
        _sx = _fx;
        _sy = _fy;
        _tx = nx;
        _ty = CellY(_row);
        _hop = 6;
        if (dr < 0) _score += 10;
        _sound.Play(GameSfx.Hop, 0.6f, (_fx / _canvas.Width) * 2 - 1);
        if (!IsWater(_row)) _gfx.Spray(_fx, _fy + 4, MathF.PI / 2, 1.2f, new SKColor(200, 230, 160), 3, 0.8f, 0.25f, 0.02f);
    }

    private void Squash(string why)
    {
        _lives--;
        _crashTimer = 36;
        _death = why;
        _plan.Clear();
        _sound.Play(why == "fell in" ? GameSfx.Splash : why == "hit by a car" ? GameSfx.Hit : GameSfx.Thud);
        if (why == "fell in")
        {
            _gfx.Spray(_fx, _fy, -MathF.PI / 2, 0.9f, new SKColor(170, 220, 255), 18, 2.4f, 0.7f, 0.12f);
            _gfx.Burst(_fx, _fy, new SKColor(255, 255, 255), 6, 1f, 0.4f, 0.05f, false);
        }
        else
        {
            _gfx.Burst(_fx, _fy, new SKColor(90, 255, 50), 16, 2.2f, 0.7f, 0.08f);
            _gfx.Shake(4);
            _gfx.Flash(new SKColor(255, 60, 60), 0.4f);
        }
        _sound.Play(_lives <= 0 ? GameSfx.GameOver : GameSfx.Die, 0.7f);
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

    private readonly GameFx _gfx = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastRender;

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastRender, 0.001, 0.1);
        _lastRender = now;
        _gfx.Update(dt);
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

        // Glints drifting with each lane's current.
        for (var lane = 1; lane <= 4; lane++)
        {
            var dir = lane % 2 == 0 ? -1 : 1;
            for (var i = 0; i < 10; i++)
            {
                var gx = ((i * 41 + lane * 17) + dir * _frame * 0.6f) % w;
                if (gx < 0) gx += w;
                var gy = lane * _ch + 2 + (i * 7 % Math.Max(1, (int)_ch - 3));
                var tw = MathF.Sin(_frame * 0.2f + i * 1.7f + lane) > 0.3f;
                if (!tw) continue;
                paint.Color = new SKColor(180, 220, 255, 170);
                canvas.DrawRect(gx, gy, 2, 1, paint);
            }
        }

        Band(5, 6, new SKColor(70, 190, 64), new SKColor(70, 190, 64));
        // Flowers on the middle verge.
        for (var i = 0; i < w; i += 13)
        {
            paint.Color = (i / 13) % 3 == 0 ? new SKColor(255, 230, 80) : (i / 13) % 3 == 1 ? new SKColor(255, 120, 180) : SKColors.White;
            canvas.DrawRect(i + 3, 5 * _ch + _ch / 2f + (i % 3) - 1, 1, 1, paint);
        }
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
                // Headlights ahead, tail lights behind.
                var len = spr[0].Length;
                var front = m.Spd > 0 ? m.X + len : m.X - 1;
                var back = m.Spd > 0 ? m.X - 1 : m.X + len;
                GameFx.GlowDisc(canvas, front + (m.Spd > 0 ? 3 : -3), y + 4, 4, new SKColor(255, 240, 180), 0.35f);
                paint.Color = new SKColor(255, 250, 210);
                canvas.DrawRect(front, y + 3, 1, 2, paint);
                paint.Color = new SKColor(255, 40, 40);
                canvas.DrawRect(back, y + 3, 1, 2, paint);
            }
        }

        // Filled pads sparkle.
        for (var i = 0; i < 5; i++)
            if (_homes[i] && _frame % 9 == i)
                _gfx.Spark((i + 0.5f) * w / 5f + _gfx.Rand(-4, 4), _gfx.Rand(1, _ch - 1), 0, -0.2f, new SKColor(200, 255, 120), 0.5f, 1, true);

        if (_crashTimer == 0 || _frame / 2 % 2 == 0)
        {
            // Squash and stretch: a shadow while in the air.
            if (_hop > 0)
            {
                paint.Color = new SKColor(0, 0, 0, 90);
                canvas.DrawRect(_fx - 3, _fy + Frog.Length / 2f + 1, 6, 1, paint);
            }
            var lift = _hop > 0 ? -MathF.Sin(_hop / 6f * MathF.PI) * 2 : 0;
            PixelArt.Blit(canvas, Frog, _fx - Frog[0].Length / 2f, _fy - Frog.Length / 2f + lift, ChFrog);
        }
        _gfx.DrawParticles(canvas);

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

        _gfx.DrawOverlay(canvas, _canvas, w, h, Math.Max(7f, h * 0.07f));
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
