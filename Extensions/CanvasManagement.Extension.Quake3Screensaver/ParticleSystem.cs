using SkiaSharp;

namespace CanvasManagement.Extension.Quake3Screensaver;

/// <summary>
/// Particle system for background smoke/fire effects reminiscent of Q3 menu
/// </summary>
public class ParticleSystem
{
    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private int _width;
    private int _height;

    /// <summary>
    /// Number of particles to maintain
    /// </summary>
    public int ParticleCount { get; set; } = 100;

    /// <summary>
    /// Primary particle color
    /// </summary>
    public SKColor PrimaryColor { get; set; } = new(255, 80, 0); // Dark orange

    /// <summary>
    /// Secondary particle color (for variety)
    /// </summary>
    public SKColor SecondaryColor { get; set; } = new(255, 40, 0); // Red-orange

    /// <summary>
    /// Particle movement speed multiplier
    /// </summary>
    public float SpeedMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Whether particles are enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Particle effect style
    /// </summary>
    public ParticleStyle Style { get; set; } = ParticleStyle.Smoke;

    public void Initialize(int width, int height)
    {
        _width = width;
        _height = height;
        _particles.Clear();

        for (int i = 0; i < ParticleCount; i++)
        {
            _particles.Add(CreateParticle(randomY: true));
        }
    }

    private Particle CreateParticle(bool randomY = false)
    {
        var x = _random.NextSingle() * _width;
        var y = randomY ? _random.NextSingle() * _height : _height + _random.Next(20);
        
        return new Particle
        {
            X = x,
            Y = y,
            VelocityX = (_random.NextSingle() - 0.5f) * 0.5f * SpeedMultiplier,
            VelocityY = -(_random.NextSingle() * 0.8f + 0.2f) * SpeedMultiplier,
            Size = _random.NextSingle() * 3 + 1,
            Life = 1.0f,
            LifeDecay = _random.NextSingle() * 0.01f + 0.005f,
            Color = _random.NextSingle() > 0.5f ? PrimaryColor : SecondaryColor,
            Style = Style
        };
    }

    public void Update()
    {
        if (!Enabled) return;

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            
            // Update position
            p.X += p.VelocityX;
            p.Y += p.VelocityY;
            
            // Add some waviness for smoke effect
            if (p.Style == ParticleStyle.Smoke)
            {
                p.X += MathF.Sin(p.Y * 0.05f + p.Life * 10) * 0.3f;
            }
            
            // Decay life
            p.Life -= p.LifeDecay;
            
            // Shrink over time
            p.Size *= 0.995f;
            
            // Reset particle if dead or off screen
            if (p.Life <= 0 || p.Y < -10 || p.Size < 0.5f)
            {
                _particles[i] = CreateParticle(randomY: false);
            }
        }

        // Maintain particle count
        while (_particles.Count < ParticleCount)
        {
            _particles.Add(CreateParticle(randomY: false));
        }
    }

    public void Render(SKCanvas canvas)
    {
        if (!Enabled) return;

        foreach (var p in _particles)
        {
            var alpha = (byte)(p.Life * 150); // Max 150 alpha for subtle effect
            var color = new SKColor(p.Color.Red, p.Color.Green, p.Color.Blue, alpha);
            
            using var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            if (p.Style == ParticleStyle.Smoke)
            {
                // Soft circle for smoke
                canvas.DrawCircle(p.X, p.Y, p.Size, paint);
            }
            else if (p.Style == ParticleStyle.Sparks)
            {
                // Small bright point for sparks
                paint.Color = new SKColor(255, 255, 200, alpha);
                canvas.DrawCircle(p.X, p.Y, Math.Max(1, p.Size * 0.5f), paint);
            }
            else if (p.Style == ParticleStyle.Embers)
            {
                // Elongated shape for embers
                canvas.DrawRect(p.X, p.Y, Math.Max(1, p.Size * 0.5f), Math.Max(1, p.Size), paint);
            }
        }
    }

    private class Particle
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float Size { get; set; }
        public float Life { get; set; }
        public float LifeDecay { get; set; }
        public SKColor Color { get; set; }
        public ParticleStyle Style { get; set; }
    }
}

public enum ParticleStyle
{
    Smoke,
    Sparks,
    Embers
}
