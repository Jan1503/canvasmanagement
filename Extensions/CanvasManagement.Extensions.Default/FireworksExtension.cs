using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extensions.Default;

[ExtensionInfo("Fireworks",
    "Colorful fireworks explosions with particles and gravity physics",
    "Visual Effects",
    IconResourceName = "fireworks.svg")]
public class FireworksExtension : IDisposable
{
    private readonly ICanvas _canvas;
    private readonly List<Firework> _fireworks = new();
    private readonly Random _random = new();
    private Task? _animationTask;
    private SKBitmap? _backBuffer;
    private SKColor _backgroundColor = SKColors.Black;
    private CancellationTokenSource? _cancellationTokenSource;
    private float _scale = 1f;

    internal FireworksExtension(ICanvas canvas)
    {
        _canvas = canvas;
        _scale = DisplayScale.GetScale(canvas.Width, canvas.Height);
    }

    [ExtensionParameter("Launch Interval", "Frames between firework launches (lower = more frequent)",
        DefaultValue = 30, MinValue = 10, MaxValue = 100)]
    public int LaunchInterval { get; set; } = 30;

    [ExtensionParameter("Particles Per Explosion", "Number of particles per explosion",
        DefaultValue = 50, MinValue = 20, MaxValue = 150)]
    public int ParticlesPerExplosion { get; set; } = 50;

    [ExtensionParameter("Min Speed", "Minimum particle speed",
        DefaultValue = 2, MinValue = 1, MaxValue = 10)]
    public int MinSpeed { get; set; } = 2;

    [ExtensionParameter("Max Speed", "Maximum particle speed",
        DefaultValue = 5, MinValue = 2, MaxValue = 15)]
    public int MaxSpeed { get; set; } = 5;

    [ExtensionParameter("Random Colors", "Use random colors for each firework",
        DefaultValue = true)]
    public bool RandomColors { get; set; } = true;

    [ExtensionParameter("Background Color", "Background color for fireworks",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }
    public string Name => "Fireworks";

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        if (IsRunning) return;

        IsRunning = true;

        // Create back buffer
        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        _animationTask = Task.Run(async () =>
        {
            var frameCount = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_backBuffer == null) break;

                    // Render to back buffer
                    using var canvas = new SKCanvas(_backBuffer);

                    // Clear with background color (supports transparency)
                    if (_backgroundColor.Alpha == 0)
                    {
                        canvas.Clear(SKColors.Transparent);
                    }
                    else if (_backgroundColor.Alpha == 255)
                    {
                        canvas.Clear(_backgroundColor);
                    }
                    else
                    {
                        canvas.Clear(SKColors.Transparent);
                        using var bgPaint = new SKPaint { Color = _backgroundColor, Style = SKPaintStyle.Fill };
                        canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, bgPaint);
                    }

                    if (frameCount++ % LaunchInterval == 0) LaunchFirework();

                    for (var i = _fireworks.Count - 1; i >= 0; i--)
                    {
                        var firework = _fireworks[i];
                        firework.Update();

                        if (firework.IsDead())
                            _fireworks.RemoveAt(i);
                        else
                            firework.Draw(canvas, _canvas.Width, _canvas.Height);
                    }

                    canvas.Flush();// Atomic submission
                    _canvas.SubmitCompletedFrame(_backBuffer);

                    await Task.Delay(33, ct); // ~30 FPS
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            finally
            {
                _canvas.Clear();
                _fireworks.Clear();
                IsRunning = false;
            }
        }, ct);
    }

    private void LaunchFirework()
    {
        var x = _random.Next(_canvas.Width / 8, _canvas.Width * 7 / 8);
        var color = RandomColors ? SKColor.FromHsl(_random.Next(360), 100, 50) : SKColors.Red;
        _fireworks.Add(new Firework(x, _canvas.Height, color, _random, ParticlesPerExplosion, MinSpeed, MaxSpeed,
            _scale));
    }

    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            _cancellationTokenSource?.Cancel();
            _animationTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"Error stopping fireworks: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _animationTask = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            _canvas.Clear();
            IsRunning = false;
        }
    }

    private class Firework
    {
        private readonly SKColor _color;
        private readonly float _explodeY;
        private readonly int _particleCount;
        private readonly List<Particle> _particles = new();
        private readonly Random _random;
        private readonly float _rocketSpeed;
        private readonly float _scale;
        private readonly float _minSpeed;
        private readonly float _maxSpeed;
        private readonly float _x;
        private bool _exploded;
        private float _rocketY;

        public Firework(float x, float y, SKColor color, Random random, int particleCount, int minSpeed, int maxSpeed,
            float scale)
        {
            _x = x;
            _rocketY = y;
            _color = color;
            _random = random;
            _particleCount = particleCount;
            _scale = scale <= 0 ? 1f : scale;

            // Speeds and rocket rise are scaled so explosions stay on-screen at any resolution.
            _rocketSpeed = Math.Max(1f, 5f * _scale);
            _minSpeed = minSpeed * _scale;
            _maxSpeed = Math.Max(_minSpeed + 0.5f, maxSpeed * _scale);

            // Explode somewhere in the upper portion of the panel (relative to launch height),
            // instead of a fixed 50-120px threshold that never triggers correctly on small panels.
            _explodeY = y * (0.2f + (float)random.NextDouble() * 0.35f);
        }

        public void Update()
        {
            if (!_exploded)
            {
                _rocketY -= _rocketSpeed;

                if (_rocketY <= _explodeY) Explode();
            }
            else
            {
                foreach (var particle in _particles) particle.Update();
            }
        }

        private void Explode()
        {
            _exploded = true;

            for (var i = 0; i < _particleCount; i++)
            {
                var angle = (float)(i * Math.PI * 2 / _particleCount);
                var speed = _minSpeed + (float)_random.NextDouble() * (_maxSpeed - _minSpeed);
                _particles.Add(new Particle(
                    _x, _rocketY,
                    (float)Math.Cos(angle) * speed,
                    (float)Math.Sin(angle) * speed,
                    _color,
                    _scale
                ));
            }
        }

        public void Draw(SKCanvas canvas, int canvasWidth, int canvasHeight)
        {
            if (!_exploded)
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1f, 2f * _scale)
                };
                canvas.DrawLine(_x, _rocketY, _x, _rocketY + Math.Max(2f, 10f * _scale), paint);
            }
            else
            {
                foreach (var particle in _particles)
                    particle.Draw(canvas);
            }
        }

        public bool IsDead()
        {
            return _exploded && _particles.All(p => p.Life <= 0);
        }
    }

    private class Particle(float x, float y, float vx, float vy, SKColor baseColor, float scale)
    {
        private readonly float _gravity = 0.3f * (scale <= 0 ? 1f : scale);
        private readonly int _size = Math.Max(1, (int)Math.Round(2 * (scale <= 0 ? 1f : scale)));
        private float _vy = vy;
        private float _x = x;
        private float _y = y;

        public int Life { get; private set; } = 60;

        public void Update()
        {
            _x += vx;
            _y += _vy;
            _vy += _gravity; // Gravity (scaled)
            Life--;
        }

        public void Draw(SKCanvas canvas)
        {
            if (Life > 0)
            {
                var alpha = (byte)(Life * 255 / 60);
                var color = baseColor.WithAlpha(alpha);
                using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(_x, _y, _size, _size, paint);
            }
        }
    }
}