using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extensions.Default;

[ExtensionInfo("Fireworks",
    "Colorful fireworks explosions with particles and gravity physics",
    "Visual Effects",
    IconResourceName = "fireworks.svg")]
public class FireworksExtension : FrameExtension
{
    private readonly List<Firework> _fireworks = new();
    private readonly Random _random = new();
    private readonly SKPaint _bgPaint = new() { Style = SKPaintStyle.Fill };
    private SKColor _backgroundColor = SKColors.Black;
    private float _scale = 1f;
    private int _frameCount;

    internal FireworksExtension(ICanvas canvas) : base(canvas)
    {
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

    public override string Name => "Fireworks";

    protected override double FrameIntervalMs => 33;

    protected override bool ClearOnStop => false;

    protected override void OnStart()
    {
        _frameCount = 0;
        _fireworks.Clear();
    }

    protected override void OnResize(int width, int height) => _scale = DisplayScale.GetScale(width, height);

    protected override void OnStop()
    {
        _fireworks.Clear();
        try { Canvas.Clear(); } catch { /* canvas already gone */ }
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        // Background (supports transparency).
        if (_backgroundColor.Alpha == 255)
        {
            canvas.Clear(_backgroundColor);
        }
        else
        {
            canvas.Clear(SKColors.Transparent);
            if (_backgroundColor.Alpha > 0)
            {
                _bgPaint.Color = _backgroundColor;
                canvas.DrawRect(0, 0, width, height, _bgPaint);
            }
        }

        if (_frameCount++ % Math.Max(1, LaunchInterval) == 0) LaunchFirework();

        for (var i = _fireworks.Count - 1; i >= 0; i--)
        {
            var firework = _fireworks[i];
            firework.Update();

            if (firework.IsDead())
                _fireworks.RemoveAt(i);
            else
                firework.Draw(canvas, width, height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _bgPaint.Dispose();
    }

    private void LaunchFirework()
    {
        var x = _random.Next(Canvas.Width / 8, Canvas.Width * 7 / 8);
        var color = RandomColors ? SKColor.FromHsl(_random.Next(360), 100, 50) : SKColors.Red;
        _fireworks.Add(new Firework(x, Canvas.Height, color, _random, ParticlesPerExplosion, MinSpeed, MaxSpeed,
            _scale));
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