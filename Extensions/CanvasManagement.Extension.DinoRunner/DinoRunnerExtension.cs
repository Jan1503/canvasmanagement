using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.DinoRunner;

/// <summary>
///     Self-playing Chrome-style endless runner: the dino auto-jumps cacti and ducks under birds while the
///     world scrolls faster and faster. Scales to any panel; input flows through one action path so a
///     controller can drive it later.
/// </summary>
[ExtensionInfo("Dino Runner",
    "The endless-runner dino game, played automatically",
    "Games",
    IconResourceName = "dino-runner.svg")]
public class DinoRunnerExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Obstacle> _obstacles = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;
    private int _frame;

    private float _groundY;
    private float _dinoX;
    private float _dinoY;     // top of dino
    private float _dinoVy;
    private float _gravity;
    private float _jumpV;
    private int _standH, _duckH, _dinoW;
    private bool _ducking;

    private float _speed;      // world scroll speed (px/frame)
    private float _spawnTimer;
    private int _score;
    private int _best;
    private int _crashTimer;

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
        _groundY = _canvas.Height * 0.82f;
        _standH = Sc(20);
        _duckH = Sc(11);
        _dinoW = Sc(18);
        _dinoX = _canvas.Width * 0.14f;
        _dinoY = _groundY - _standH;
        _dinoVy = 0;
        _gravity = Math.Max(0.4f, 0.9f * _scale);
        _jumpV = -Math.Max(4f, 7.5f * _scale);
        _ducking = false;
        _speed = Math.Max(2f, 3f * _scale);
        _spawnTimer = 0;
        _obstacles.Clear();
        _score = 0;
        _crashTimer = 0;
    }

    private int Sc(float v)
    {
        return Math.Max(1, (int)Math.Round(v * _scale));
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
        _speed = Math.Min(_canvas.Width * 0.06f,
            (3f + Difficulty * 0.4f) * _scale + _score * 0.0015f * _scale);

        // Spawn obstacles with spacing that scales to speed.
        _spawnTimer -= _speed;
        if (_spawnTimer <= 0)
        {
            SpawnObstacle();
            _spawnTimer = _canvas.Width * (0.5f + (float)_random.NextDouble() * 0.5f);
        }

        // Move obstacles.
        for (var i = _obstacles.Count - 1; i >= 0; i--)
        {
            var o = _obstacles[i];
            o.X -= _speed;
            _obstacles[i] = o;
            if (o.X + o.W < 0) _obstacles.RemoveAt(i);
        }

        RunAi();

        // Physics.
        var onGround = _dinoY >= _groundY - CurrentH() - 0.5f;
        if (!onGround || _dinoVy < 0)
        {
            _dinoVy += _gravity;
            _dinoY += _dinoVy;
        }

        var floor = _groundY - CurrentH();
        if (_dinoY > floor)
        {
            _dinoY = floor;
            _dinoVy = 0;
        }

        // Collision.
        var dr = new SKRect(_dinoX, _dinoY, _dinoX + _dinoW, _dinoY + CurrentH());
        foreach (var o in _obstacles)
        {
            var or = new SKRect(o.X, o.Y, o.X + o.W, o.Y + o.H);
            if (dr.IntersectsWith(or))
            {
                _crashTimer = 35;
                break;
            }
        }
    }

    private int CurrentH()
    {
        return _ducking ? _duckH : _standH;
    }

    private void SpawnObstacle()
    {
        var bird = _random.Next(100) < 30 && _score > 200;
        if (bird)
        {
            var h = Sc(9);
            // Bird height: sometimes low (must duck), sometimes high (can run under) — keep it duck-worthy.
            var y = _groundY - _standH - Sc(2);
            _obstacles.Add(new Obstacle { X = _canvas.Width, Y = y, W = Sc(14), H = h, Bird = true });
        }
        else
        {
            var big = _random.Next(2) == 0;
            var h = big ? Sc(20) : Sc(13);
            var w = big ? Sc(12) : Sc(8);
            _obstacles.Add(new Obstacle { X = _canvas.Width, Y = _groundY - h, W = w, H = h, Bird = false });
        }
    }

    private void RunAi()
    {
        // Find the nearest obstacle ahead of the dino.
        Obstacle? next = null;
        var bestDx = float.MaxValue;
        foreach (var o in _obstacles)
        {
            var dx = o.X - (_dinoX + _dinoW);
            if (dx < -o.W) continue; // already passed
            if (dx < bestDx) { bestDx = dx; next = o; }
        }

        _ducking = false;
        if (next == null) return;

        var onGround = _dinoY >= _groundY - CurrentH() - 1f;

        if (next.Value.Bird)
        {
            // Duck while the bird is overhead (a little before and during the crossing).
            var duckWindow = _speed * 4f + _dinoW;
            if (bestDx < duckWindow && bestDx > -next.Value.W) _ducking = true;
        }
        else if (onGround && bestDx > 0)
        {
            // Center the obstacle crossing on the jump apex so the dino is at its highest while the
            // obstacle is underneath. time-to-apex = |jumpV|/gravity frames; the obstacle overlaps the dino
            // for `crossing` frames, so jump when it's (apex - crossing/2) frames away.
            var apexFrames = Math.Abs(_jumpV) / Math.Max(0.01f, _gravity);
            var crossing = (_dinoW + next.Value.W) / Math.Max(0.5f, _speed);
            var jumpDist = _speed * (apexFrames - crossing * 0.5f);
            jumpDist = Math.Clamp(jumpDist, _speed * 2f, _speed * apexFrames);
            if (bestDx <= jumpDist) Jump();
        }
    }

    private void Jump()
    {
        if (_dinoY >= _groundY - _standH - 1f) _dinoVy = _jumpV;
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        var bg = NightMode ? new SKColor(15, 15, 25) : new SKColor(247, 247, 247);
        var fg = NightMode ? new SKColor(235, 235, 235) : new SKColor(40, 40, 40);

        using var canvas = new SKCanvas(bb);
        canvas.Clear(bg);
        using var paint = new SKPaint { Color = fg, Style = SKPaintStyle.Fill, IsAntialias = false };

        // Ground line with moving dashes.
        canvas.DrawRect(0, _groundY, _canvas.Width, Math.Max(1, Sc(1)), paint);
        var dash = Sc(6);
        for (var x = -(_frame * (int)_speed % (dash * 2)); x < _canvas.Width; x += dash * 2)
            canvas.DrawRect(x, _groundY + Sc(3), dash, Math.Max(1, Sc(1)), paint);

        // Dino.
        var h = CurrentH();
        canvas.DrawRect(_dinoX, _dinoY, _dinoW, h, paint);
        // Head/eye accent.
        using (var eye = new SKPaint { Color = bg })
            canvas.DrawRect(_dinoX + _dinoW - Sc(5), _dinoY + Sc(2), Sc(2), Sc(2), eye);

        // Obstacles.
        foreach (var o in _obstacles)
        {
            if (o.Bird)
            {
                // Simple flapping bird (V shape).
                var flap = _frame % 12 < 6 ? -Sc(3) : Sc(2);
                canvas.DrawRect(o.X, o.Y + (o.H / 2), o.W, Math.Max(1, Sc(2)), paint);
                canvas.DrawRect(o.X + o.W / 2 - Sc(1), o.Y + o.H / 2 + flap, Sc(2), Math.Abs(flap) + 1, paint);
            }
            else
            {
                canvas.DrawRect(o.X, o.Y, o.W, o.H, paint); // cactus body
                canvas.DrawRect(o.X - Sc(2), o.Y + o.H / 3, Sc(2), o.H / 3, paint); // left arm
                canvas.DrawRect(o.X + o.W, o.Y + o.H / 4, Sc(2), o.H / 3, paint); // right arm
            }
        }

        if (ShowScore)
        {
            using var font = new SKFont { Size = Math.Max(8f, 11f * _scale) };
            using var tp = new SKPaint { Color = fg, IsAntialias = true };
            canvas.DrawText($"{_score:00000}", _canvas.Width - Sc(4), Math.Max(10f, 12f * _scale),
                SKTextAlign.Right, font, tp);
        }

        if (_crashTimer > 0)
        {
            using var font = new SKFont { Size = Math.Max(12f, _canvas.Height * 0.14f) };
            using var tp = new SKPaint { Color = fg, IsAntialias = true };
            canvas.DrawText("G A M E  O V E R", _canvas.Width / 2f, _canvas.Height / 2f, SKTextAlign.Center, font,
                tp);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private struct Obstacle
    {
        public float X;
        public float Y;
        public int W;
        public int H;
        public bool Bird;
    }
}
