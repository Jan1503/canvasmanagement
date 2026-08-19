using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     New Year's Eve celebration filter with fireworks, sparkles, and festive colors
/// </summary>
[FilterInfo("New Year Celebration",
    "Festive New Year effect with bursting fireworks, golden sparkles, and celebration atmosphere",
    "Seasonal",
    IconResourceName = "newyear.svg")]
public class NewYearFilter : ICanvasFilter
{
    private readonly List<Bubble> _bubbles = new();
    private readonly List<Confetti> _confetti = new();
    private readonly List<Firework> _fireworks = new();
    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private int _frameCount;
    private bool _initialized;
    private int _width = (int)DisplayScale.ReferenceWidth;

    /// <summary>
    ///     Firework burst frequency
    /// </summary>
    [FilterParameter("Fireworks", "Frequency of firework bursts", MinValue = 0.0f, MaxValue = 1.0f,
        DefaultValue = 0.7f)]
    public float FireworkIntensity { get; set; } = 0.7f;

    /// <summary>
    ///     Sparkle and confetti amount
    /// </summary>
    [FilterParameter("Sparkles", "Amount of golden sparkles", MinValue = 0.0f, MaxValue = 1.0f, DefaultValue = 0.8f)]
    public float SparkleAmount { get; set; } = 0.8f;

    /// <summary>
    ///     Enable falling confetti
    /// </summary>
    [FilterParameter("Confetti", "Enable colorful falling confetti")]
    public bool EnableConfetti { get; set; } = true;

    /// <summary>
    ///     Enable countdown flash effect
    /// </summary>
    [FilterParameter("Countdown Flash", "Enable periodic bright countdown flashes")]
    public bool EnableCountdownFlash { get; set; } = true;

    /// <summary>
    ///     Enable champagne bubble effect
    /// </summary>
    [FilterParameter("Champagne Bubbles", "Enable rising champagne bubbles")]
    public bool EnableBubbles { get; set; } = true;

    public string Name => "New Year Celebration";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();
        _width = bitmap.Width;

        if (!_initialized)
        {
            InitializeEffects(bitmap.Width, bitmap.Height);
            _initialized = true;
        }

        // Apply festive color enhancement
        ApplyNewYearColorGrade(bitmap);

        // Countdown flash effect (periodic bright flash)
        if (EnableCountdownFlash && _frameCount % 120 < 10) AddCountdownFlash(bitmap);

        // Spawn new fireworks randomly
        if (_random.Next(100) < FireworkIntensity * 30) SpawnFirework(bitmap.Width, bitmap.Height);

        // Draw fireworks and particles
        DrawFireworks(bitmap);
        DrawParticles(bitmap);

        // Draw confetti if enabled
        if (EnableConfetti)
        {
            DrawConfetti(bitmap);
            UpdateConfetti(bitmap.Height);
        }

        // Draw champagne bubbles if enabled
        if (EnableBubbles)
        {
            DrawBubbles(bitmap);
            UpdateBubbles(bitmap.Height);
        }

        // Add golden sparkles
        AddGoldenSparkles(bitmap);

        // Update animations
        UpdateFireworks(bitmap.Width, bitmap.Height);
        UpdateParticles();

        _frameCount++;

        return bitmap;
    }

    private void InitializeEffects(int width, int height)
    {
        // Initialize confetti
        var confettiCount = (int)(30 * Intensity);
        for (var i = 0; i < confettiCount; i++)
        {
            var colors = new[]
            {
                new SKColor(255, 50, 50), // Red
                new SKColor(255, 215, 0), // Gold
                new SKColor(50, 255, 50), // Green
                new SKColor(50, 150, 255), // Blue
                new SKColor(255, 100, 255), // Purple
                new SKColor(255, 255, 255) // White
            };

            _confetti.Add(new Confetti
            {
                X = _random.Next(width),
                Y = _random.Next(-height, 0),
                VX = ((float)_random.NextDouble() - 0.5f) * 2,
                VY = 1 + (float)_random.NextDouble() * 2,
                Color = colors[_random.Next(colors.Length)],
                Rotation = (float)_random.NextDouble() * 360,
                RotationSpeed = ((float)_random.NextDouble() - 0.5f) * 10,
                Size = 3 + _random.Next(5)
            });
        }

        // Initialize bubbles
        var bubbleCount = (int)(15 * Intensity);
        for (var i = 0; i < bubbleCount; i++)
            _bubbles.Add(new Bubble
            {
                X = _random.Next(width),
                Y = height + _random.Next(100),
                Radius = 3 + _random.Next(8),
                Speed = 0.5f + (float)_random.NextDouble() * 1.5f,
                Color = new SKColor(255, 240, 150, 100),
                SwayPhase = (float)_random.NextDouble() * 360
            });
    }

    private void AddCountdownFlash(SKBitmap bitmap)
    {
        var flashIntensity = (float)Math.Sin(_frameCount % 120 * Math.PI / 10) * 0.5f + 0.5f;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var pixelCount = bitmap.Width * bitmap.Height;

            for (var i = 0; i < pixelCount; i++)
            {
                var pixel = pixels[i];
                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Bright white flash
                var flash = (int)(80 * flashIntensity * Intensity);
                r = (byte)Math.Min(255, r + flash);
                g = (byte)Math.Min(255, g + flash);
                b = (byte)Math.Min(255, b + flash);

                pixels[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }
    }

    private void DrawConfetti(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var piece in _confetti)
            {
                if (piece.Y < 0 || piece.Y >= height) continue;

                var x = (int)piece.X;
                var y = (int)piece.Y;

                // Draw small rectangle rotated
                for (var dy = -piece.Size; dy <= piece.Size; dy++)
                for (var dx = -piece.Size; dx <= piece.Size; dx++)
                {
                    // Simple rotation
                    var angle = piece.Rotation * Math.PI / 180;
                    var rx = (int)(dx * Math.Cos(angle) - dy * Math.Sin(angle));
                    var ry = (int)(dx * Math.Sin(angle) + dy * Math.Cos(angle));

                    var px = x + rx;
                    var py = y + ry;

                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        var idx = py * width + px;
                        BlendPixel(pixels, idx, piece.Color, 200);
                    }
                }
            }
        }
    }

    private void UpdateConfetti(int height)
    {
        foreach (var piece in _confetti)
        {
            piece.X += piece.VX;
            piece.Y += piece.VY;
            piece.Rotation += piece.RotationSpeed;

            // Reset if off screen
            if (piece.Y > height)
            {
                piece.Y = -10;
                piece.X = _random.Next(Math.Max(1, _width));
            }
        }
    }

    private void DrawBubbles(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var bubble in _bubbles)
            {
                if (bubble.Y < -bubble.Radius || bubble.Y >= height) continue;

                // Add horizontal sway
                var swayOffset = (int)(Math.Sin((bubble.SwayPhase + _frameCount * 2) * Math.PI / 180) * 3);
                var x = (int)bubble.X + swayOffset;
                var y = (int)bubble.Y;

                // Draw bubble circle
                for (var dy = -(int)bubble.Radius; dy <= (int)bubble.Radius; dy++)
                for (var dx = -(int)bubble.Radius; dx <= (int)bubble.Radius; dx++)
                {
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance > bubble.Radius) continue;

                    var px = x + dx;
                    var py = y + dy;

                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        // Edge glow for bubble effect
                        var edgeFactor = (float)(distance / bubble.Radius);
                        var alpha = edgeFactor > 0.7f ? (byte)(200 * (1 - edgeFactor)) : (byte)50;

                        var idx = py * width + px;
                        BlendPixel(pixels, idx, bubble.Color, alpha);
                    }
                }
            }
        }
    }

    private void UpdateBubbles(int height)
    {
        foreach (var bubble in _bubbles)
        {
            bubble.Y -= bubble.Speed;

            // Pop and respawn at bottom
            if (bubble.Y < -bubble.Radius)
            {
                bubble.Y = height + bubble.Radius;
                bubble.X = _random.Next(Math.Max(1, _width));
            }
        }
    }

    private void ApplyNewYearColorGrade(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var pixelCount = bitmap.Width * bitmap.Height;

            for (var i = 0; i < pixelCount; i++)
            {
                var pixel = pixels[i];
                var a = (byte)((pixel >> 24) & 0xFF);
                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Boost brightness and saturation
                var brightness = (r + g + b) / 3f;
                var boost = 1.1f * Intensity;

                r = (byte)Math.Min(255, r * boost);
                g = (byte)Math.Min(255, g * boost);
                b = (byte)Math.Min(255, b * boost);

                // Add slight golden tint to highlights
                if (brightness > 120)
                {
                    r = (byte)Math.Min(255, r + (int)(10 * Intensity));
                    g = (byte)Math.Min(255, g + (int)(8 * Intensity));
                }

                pixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    private void SpawnFirework(int width, int height)
    {
        var colors = new[]
        {
            new SKColor(255, 50, 50), // Red
            new SKColor(255, 215, 0), // Gold
            new SKColor(50, 255, 50), // Green
            new SKColor(50, 150, 255), // Blue
            new SKColor(255, 100, 255), // Purple
            new SKColor(255, 255, 255) // White
        };

        var firework = new Firework
        {
            X = width * 0.2f + _random.Next((int)(width * 0.6f)),
            Y = height * 0.3f + _random.Next((int)(height * 0.4f)),
            Color = colors[_random.Next(colors.Length)],
            Life = 30 + _random.Next(20),
            MaxLife = 30 + _random.Next(20)
        };

        _fireworks.Add(firework);

        // Create burst particles
        var particleCount = 30 + _random.Next(30);
        for (var i = 0; i < particleCount; i++)
        {
            var angle = i / (float)particleCount * Math.PI * 2;
            var speed = 2 + (float)_random.NextDouble() * 4;

            _particles.Add(new Particle
            {
                X = firework.X,
                Y = firework.Y,
                VX = (float)Math.Cos(angle) * speed,
                VY = (float)Math.Sin(angle) * speed,
                Color = firework.Color,
                Life = firework.Life,
                MaxLife = firework.MaxLife,
                Trail = new List<(float X, float Y)>()
            });
        }
    }

    private void DrawFireworks(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var firework in _fireworks)
            {
                if (firework.Life <= 0) continue;

                var alpha = firework.Life / (float)firework.MaxLife;
                var size = (int)(10 * alpha);

                DrawGlow(pixels, width, height, (int)firework.X, (int)firework.Y,
                    size, firework.Color, (byte)(255 * alpha * Intensity));
            }
        }
    }

    private void DrawParticles(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var particle in _particles)
            {
                if (particle.Life <= 0) continue;

                var alpha = particle.Life / (float)particle.MaxLife;

                // Draw particle
                var x = (int)particle.X;
                var y = (int)particle.Y;

                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    var idx = y * width + x;
                    BlendPixel(pixels, idx, particle.Color, (byte)(255 * alpha * Intensity));

                    // Draw small trail
                    for (var i = 0; i < Math.Min(5, particle.Trail.Count); i++)
                    {
                        var trailPoint = particle.Trail[particle.Trail.Count - 1 - i];
                        var tx = (int)trailPoint.X;
                        var ty = (int)trailPoint.Y;

                        if (tx >= 0 && tx < width && ty >= 0 && ty < height)
                        {
                            var tidx = ty * width + tx;
                            var trailAlpha = (byte)(alpha * (1.0f - i / 5.0f) * 128 * Intensity);
                            BlendPixel(pixels, tidx, particle.Color, trailAlpha);
                        }
                    }
                }
            }
        }
    }

    private void AddGoldenSparkles(SKBitmap bitmap)
    {
        var sparkleCount = (int)(50 * SparkleAmount * Intensity);

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            for (var i = 0; i < sparkleCount; i++)
            {
                var x = _random.Next(width);
                var y = _random.Next(height);

                // Twinkle effect
                if (_random.Next(100) < 30)
                {
                    var idx = y * width + x;
                    var sparkleColor = new SKColor(
                        (byte)(200 + _random.Next(56)),
                        (byte)(180 + _random.Next(76)),
                        (byte)_random.Next(100),
                        (byte)(150 + _random.Next(106))
                    );

                    BlendPixel(pixels, idx, sparkleColor, sparkleColor.Alpha);

                    // Cross sparkle
                    if (x > 0) BlendPixel(pixels, idx - 1, sparkleColor, (byte)(sparkleColor.Alpha * 0.5f));
                    if (x < width - 1) BlendPixel(pixels, idx + 1, sparkleColor, (byte)(sparkleColor.Alpha * 0.5f));
                    if (y > 0) BlendPixel(pixels, idx - width, sparkleColor, (byte)(sparkleColor.Alpha * 0.5f));
                    if (y < height - 1)
                        BlendPixel(pixels, idx + width, sparkleColor, (byte)(sparkleColor.Alpha * 0.5f));
                }
            }
        }
    }

    private unsafe void DrawGlow(uint* pixels, int width, int height, int cx, int cy, int size, SKColor color,
        byte alpha)
    {
        for (var dy = -size; dy <= size; dy++)
        for (var dx = -size; dx <= size; dx++)
        {
            var x = cx + dx;
            var y = cy + dy;

            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > size) continue;

            var falloff = (byte)(alpha * (1.0f - distance / size));
            var idx = y * width + x;

            BlendPixel(pixels, idx, color, falloff);
        }
    }

    private unsafe void BlendPixel(uint* pixels, int idx, SKColor color, byte alpha)
    {
        var existing = pixels[idx];
        var er = (byte)((existing >> 16) & 0xFF);
        var eg = (byte)((existing >> 8) & 0xFF);
        var eb = (byte)(existing & 0xFF);

        var blend = alpha / 255f;
        var nr = (byte)Math.Min(255, er + (color.Red - er) * blend);
        var ng = (byte)Math.Min(255, eg + (color.Green - eg) * blend);
        var nb = (byte)Math.Min(255, eb + (color.Blue - eb) * blend);

        pixels[idx] = 0xFF000000u | ((uint)nr << 16) | ((uint)ng << 8) | nb;
    }

    private void UpdateFireworks(int width, int height)
    {
        _fireworks.RemoveAll(f => f.Life <= 0);
        foreach (var firework in _fireworks) firework.Life--;
    }

    private void UpdateParticles()
    {
        _particles.RemoveAll(p => p.Life <= 0);

        foreach (var particle in _particles)
        {
            particle.Trail.Add((particle.X, particle.Y));
            if (particle.Trail.Count > 10) particle.Trail.RemoveAt(0);

            particle.X += particle.VX;
            particle.Y += particle.VY;
            particle.VY += 0.1f; // Gravity
            particle.VX *= 0.98f; // Air resistance
            particle.VY *= 0.98f;
            particle.Life--;
        }
    }

    private class Firework
    {
        public float X { get; set; }
        public float Y { get; set; }
        public SKColor Color { get; set; }
        public int Life { get; set; }
        public int MaxLife { get; set; }
    }

    private class Particle
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public SKColor Color { get; set; }
        public int Life { get; set; }
        public int MaxLife { get; set; }
        public List<(float X, float Y)> Trail { get; set; }
    }

    private class Confetti
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public SKColor Color { get; set; }
        public float Rotation { get; set; }
        public float RotationSpeed { get; set; }
        public int Size { get; set; }
    }

    private class Bubble
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Radius { get; set; }
        public float Speed { get; set; }
        public SKColor Color { get; set; }
        public float SwayPhase { get; set; }
    }
}