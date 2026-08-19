using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.FlappyBird;

/// <summary>
///     Self-playing Flappy Bird: the bird auto-flaps to thread the pipe gaps. Scales to any panel; the flap
///     decision is a single call so a controller can replace the AI later.
/// </summary>
[ExtensionInfo("Flappy Bird",
    "The flappy bird game, played automatically",
    "Games",
    IconResourceName = "flappy-bird.svg")]
public class FlappyBirdExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Pipe> _pipes = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;
    private int _frame;

    private float _birdX;
    private float _birdY;
    private float _vy;
    private float _gravity;
    private float _flapV;
    private int _birdR;

    private float _pipeW;
    private float _gap;
    private float _speed;
    private float _spawnX;
    private int _score;
    private int _best;
    private int _crashTimer;

    internal FlappyBirdExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 30,
        MinValue = 16, MaxValue = 80, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 30;

    [ExtensionParameter("Gap Size", "Pipe gap (% of height)", DefaultValue = 38, MinValue = 22, MaxValue = 55,
        Unit = "%", Order = 2)]
    public int GapPercent { get; set; } = 38;

    [ExtensionParameter("Difficulty", "Scroll speed & pipe spacing", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 3)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show the score", DefaultValue = true, Order = 4)]
    public bool ShowScore { get; set; } = true;

    public string Name => "Flappy Bird";
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
        _birdR = Math.Max(2, (int)Math.Round(4 * _scale));
        _birdX = _canvas.Width * 0.28f;
        _birdY = _canvas.Height * 0.45f;
        _vy = 0;
        _pipeW = Math.Max(6, 12 * _scale);
        _gap = _canvas.Height * (GapPercent / 100f);
        // Tie physics to the gap so a single flap bobs the bird ~1/3 of the gap (tight, controllable arc)
        // instead of a weak-gravity flap that overshoots out the top.
        _gravity = Math.Max(0.12f, _gap * 0.02f);
        _flapV = -Math.Max(1.8f, _gap * 0.12f);
        _speed = Math.Max(1.2f, (1.5f + Difficulty * 0.25f) * _scale);
        _pipes.Clear();
        _spawnX = _canvas.Width + _pipeW;
        _score = 0;
        _crashTimer = 0;
        SpawnPipe(_canvas.Width * 0.9f);
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
                Console.WriteLine($"[FlappyBird] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        _gap = _canvas.Height * (GapPercent / 100f);
        _gravity = Math.Max(0.12f, _gap * 0.02f);
        _flapV = -Math.Max(1.8f, _gap * 0.12f);
        _speed = Math.Max(1.2f, (1.5f + Difficulty * 0.25f) * _scale);

        // Spacing between pipes scales with gap/difficulty.
        var spacing = _canvas.Width * (0.55f - Difficulty * 0.02f);
        if (_pipes.Count == 0 || _canvas.Width - _pipes[^1].X > spacing)
            SpawnPipe(_canvas.Width + _pipeW);

        for (var i = _pipes.Count - 1; i >= 0; i--)
        {
            var p = _pipes[i];
            p.X -= _speed;
            if (!p.Scored && p.X + _pipeW < _birdX) { p.Scored = true; _score++; _best = Math.Max(_best, _score); }
            _pipes[i] = p;
            if (p.X + _pipeW < 0) _pipes.RemoveAt(i);
        }

        RunAi();

        _vy += _gravity;
        _birdY += _vy;

        // Collisions: ground/ceiling + pipes.
        if (_birdY - _birdR < 0 || _birdY + _birdR > _canvas.Height) { Crash(); return; }
        foreach (var p in _pipes)
        {
            if (_birdX + _birdR < p.X || _birdX - _birdR > p.X + _pipeW) continue;
            if (_birdY - _birdR < p.GapTop || _birdY + _birdR > p.GapTop + _gap) { Crash(); return; }
        }
    }

    private void RunAi()
    {
        // Aim for the centre of the NEXT gap and flap when our near-future position would sink below it.
        var aim = _canvas.Height * 0.5f;
        foreach (var p in _pipes)
        {
            if (p.X + _pipeW < _birdX - _birdR) continue; // already passed
            aim = p.GapTop + _gap * 0.5f;
            break;
        }

        // Don't flap into the ceiling.
        if (_birdY <= _birdR * 2 && _vy < 0) return;

        if (_birdY + _vy * 2f > aim) Flap();
    }

    private void Flap()
    {
        _vy = _flapV;
    }

    private void Crash()
    {
        _crashTimer = 30;
    }

    private void SpawnPipe(float x)
    {
        var margin = _canvas.Height * 0.12f;
        var gapTop = margin + (float)_random.NextDouble() * (_canvas.Height - _gap - margin * 2);
        _pipes.Add(new Pipe { X = x, GapTop = gapTop, Scored = false });
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var canvas = new SKCanvas(bb);
        canvas.Clear(new SKColor(80, 192, 240)); // sky
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        // Pipes.
        foreach (var p in _pipes)
        {
            paint.Color = new SKColor(46, 168, 79);
            canvas.DrawRect(p.X, 0, _pipeW, p.GapTop, paint);
            canvas.DrawRect(p.X, p.GapTop + _gap, _pipeW, _canvas.Height - (p.GapTop + _gap), paint);
            // Lips.
            paint.Color = new SKColor(34, 130, 60);
            var lip = Math.Max(2, _pipeW * 0.25f);
            canvas.DrawRect(p.X - lip / 2, p.GapTop - lip, _pipeW + lip, lip, paint);
            canvas.DrawRect(p.X - lip / 2, p.GapTop + _gap, _pipeW + lip, lip, paint);
        }

        // Bird.
        var flapUp = _vy < 0;
        paint.Color = new SKColor(255, 216, 61);
        canvas.DrawCircle(_birdX, _birdY, _birdR, paint);
        paint.Color = new SKColor(255, 127, 17); // beak
        canvas.DrawRect(_birdX + _birdR * 0.6f, _birdY - _birdR * 0.2f, _birdR, _birdR * 0.5f, paint);
        paint.Color = SKColors.White; // eye
        canvas.DrawCircle(_birdX + _birdR * 0.4f, _birdY - _birdR * 0.4f, Math.Max(1, _birdR * 0.35f), paint);
        paint.Color = new SKColor(255, 183, 3); // wing
        var wingY = _birdY + (flapUp ? -_birdR * 0.3f : _birdR * 0.3f);
        canvas.DrawRect(_birdX - _birdR, wingY, _birdR, Math.Max(1, _birdR * 0.5f), paint);

        if (ShowScore)
        {
            using var font = new SKFont { Size = Math.Max(10f, _canvas.Height * 0.13f) };
            using var tp = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 120), IsAntialias = true };
            canvas.DrawText($"{_score}", _canvas.Width / 2f + 1, _canvas.Height * 0.2f + 1, SKTextAlign.Center, font, shadow);
            canvas.DrawText($"{_score}", _canvas.Width / 2f, _canvas.Height * 0.2f, SKTextAlign.Center, font, tp);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private struct Pipe
    {
        public float X;
        public float GapTop;
        public bool Scored;
    }
}
