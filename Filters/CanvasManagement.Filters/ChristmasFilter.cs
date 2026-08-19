using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Christmas festive filter with snow, warm glow, and holiday colors
/// </summary>
[FilterInfo("Christmas Magic",
    "Festive Christmas effect with falling snow, warm golden glow, and holiday color enhancement",
    "Seasonal",
    IconResourceName = "christmas.svg")]
public class ChristmasFilter : ICanvasFilter
{
    private readonly Random _random = new();
    private readonly List<ShootingStar> _shootingStars = new();
    private readonly List<Snowflake> _snowflakes = new();
    private int _frameCount;
    private bool _initialized;
    private int _width = (int)DisplayScale.ReferenceWidth;

    /// <summary>
    ///     Amount of falling snow (0.0 = none, 1.0 = heavy snowfall)
    /// </summary>
    [FilterParameter("Snow Amount", "Density of falling snowflakes", MinValue = 0.0f, MaxValue = 1.0f,
        DefaultValue = 0.7f)]
    public float SnowAmount { get; set; } = 0.7f;

    /// <summary>
    ///     Warm golden glow intensity
    /// </summary>
    [FilterParameter("Golden Glow", "Warm festive glow intensity", MinValue = 0.0f, MaxValue = 1.0f,
        DefaultValue = 0.5f)]
    public float GoldenGlow { get; set; } = 0.5f;

    /// <summary>
    ///     Enable magical shooting stars
    /// </summary>
    [FilterParameter("Shooting Stars", "Enable magical shooting stars across the sky")]
    public bool EnableShootingStars { get; set; } = true;

    /// <summary>
    ///     Enable twinkling star effect
    /// </summary>
    [FilterParameter("Twinkling Stars", "Enable background twinkling stars")]
    public bool EnableTwinklingStars { get; set; } = true;

    /// <summary>
    ///     Occasional magical aurora glow effect
    /// </summary>
    [FilterParameter("Northern Lights", "Enable northern lights aurora effect")]
    public bool EnableAurora { get; set; } = true;

    public string Name => "Christmas Magic";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();
        _width = bitmap.Width;

        // Initialize snowflakes
        if (!_initialized)
        {
            InitializeSnowflakes(bitmap.Width, bitmap.Height);
            _initialized = true;
        }

        // Apply warm golden color grade
        ApplyChristmasColorGrade(bitmap);

        // Add aurora borealis effect (occasional)
        if (EnableAurora && _frameCount % 200 < 100) AddAuroraEffect(bitmap);

        // Add twinkling stars in background
        if (EnableTwinklingStars) AddTwinklingStars(bitmap);

        // Add falling snow
        DrawSnowflakes(bitmap);

        // Add festive sparkles
        AddFestiveSparkles(bitmap);

        // Add shooting stars
        if (EnableShootingStars)
        {
            SpawnShootingStar(bitmap.Width, bitmap.Height);
            DrawShootingStars(bitmap);
            UpdateShootingStars();
        }

        // Occasional magical shimmer wave
        if (_frameCount % 300 < 30) AddMagicalShimmer(bitmap);

        // Update animation
        _frameCount++;
        UpdateSnowflakes(bitmap.Height);

        return bitmap;
    }

    private void InitializeSnowflakes(int width, int height)
    {
        var snowflakeCount = (int)(200 * SnowAmount);
        for (var i = 0; i < snowflakeCount; i++)
            _snowflakes.Add(new Snowflake
            {
                X = _random.Next(width),
                Y = _random.Next(-height, height),
                Speed = 1 + (float)_random.NextDouble() * 3,
                Size = 2 + _random.Next(4),
                Opacity = 0.5f + (float)_random.NextDouble() * 0.5f,
                SwayPhase = (float)_random.NextDouble() * 360
            });
    }

    private void ApplyChristmasColorGrade(SKBitmap bitmap)
    {
        var glowStrength = GoldenGlow * Intensity;

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

                // Enhance reds and golds (Christmas colors)
                r = (byte)Math.Min(255, r + (int)(15 * glowStrength));
                g = (byte)Math.Min(255, g + (int)(10 * glowStrength));

                // Slight warmth
                b = (byte)Math.Max(0, b - (int)(5 * glowStrength));

                // Boost saturation of reds and greens
                var brightness = (r + g + b) / 3;
                if (r > brightness + 30 || g > brightness + 30) // Red or green dominant
                {
                    var boost = 1.15f * Intensity;
                    r = (byte)Math.Min(255, r * boost);
                    g = (byte)Math.Min(255, g * boost);
                }

                pixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    private void AddAuroraEffect(SKBitmap bitmap)
    {
        var auroraIntensity = (float)Math.Sin(_frameCount * 0.02) * 0.5f + 0.5f;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            // Aurora in upper portion of image
            for (var y = 0; y < height / 3; y++)
            for (var x = 0; x < width; x++)
            {
                var wavePattern = Math.Sin(x * 0.05 + _frameCount * 0.1) *
                                  Math.Sin(y * 0.1 - _frameCount * 0.05);

                if (wavePattern > 0.3)
                {
                    var idx = y * width + x;
                    var pixel = pixels[idx];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Green-blue aurora glow
                    var glowAmount = (byte)(wavePattern * 40 * auroraIntensity * Intensity);
                    g = (byte)Math.Min(255, g + glowAmount);
                    b = (byte)Math.Min(255, b + glowAmount * 0.8f);
                    r = (byte)Math.Min(255, r + glowAmount * 0.3f);

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void AddTwinklingStars(SKBitmap bitmap)
    {
        var starCount = (int)(50 * Intensity);

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            for (var i = 0; i < starCount; i++)
            {
                // Fixed star positions based on seed
                var seed = i * 1000 + _frameCount / 30;
                var starRandom = new Random(seed);

                var x = starRandom.Next(width);
                var y = starRandom.Next(height / 2); // Upper half

                // Twinkling effect
                var twinkle = (float)Math.Sin(_frameCount * 0.1 + i) * 0.5f + 0.5f;

                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    var idx = y * width + x;
                    var pixel = pixels[idx];
                    var brightness = GetBrightness(pixel);

                    // Only add stars to darker areas
                    if (brightness < 100)
                    {
                        var starBrightness = (byte)(200 * twinkle * Intensity);
                        var r = (byte)Math.Min(255, ((pixel >> 16) & 0xFF) + starBrightness);
                        var g = (byte)Math.Min(255, ((pixel >> 8) & 0xFF) + starBrightness);
                        var b = (byte)Math.Min(255, (pixel & 0xFF) + starBrightness * 0.9f);

                        pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                    }
                }
            }
        }
    }

    private void SpawnShootingStar(int width, int height)
    {
        // Random chance to spawn shooting star
        if (_random.Next(100) < 2) // 2% chance per frame
            _shootingStars.Add(new ShootingStar
            {
                X = _random.Next(width),
                Y = _random.Next(height / 3), // Upper portion
                VX = 3 + (float)_random.NextDouble() * 4,
                VY = 1 + (float)_random.NextDouble() * 2,
                Life = 30 + _random.Next(20),
                MaxLife = 30 + _random.Next(20),
                Trail = new List<(float X, float Y)>()
            });
    }

    private void DrawShootingStars(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var star in _shootingStars)
            {
                if (star.Life <= 0) continue;

                var alpha = star.Life / (float)star.MaxLife;

                // Draw star head
                var x = (int)star.X;
                var y = (int)star.Y;

                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    var idx = y * width + x;
                    var starColor = new SKColor(255, 255, 200, (byte)(255 * alpha * Intensity));
                    BlendPixel(pixels, idx, starColor);

                    // Bright core
                    DrawGlow(pixels, width, height, x, y, 3, starColor, (byte)(200 * alpha * Intensity));
                }

                // Draw glowing trail
                for (var i = 0; i < Math.Min(15, star.Trail.Count); i++)
                {
                    var trailPoint = star.Trail[star.Trail.Count - 1 - i];
                    var tx = (int)trailPoint.X;
                    var ty = (int)trailPoint.Y;

                    if (tx >= 0 && tx < width && ty >= 0 && ty < height)
                    {
                        var trailAlpha = alpha * (1.0f - i / 15.0f);
                        var tidx = ty * width + tx;
                        var trailColor = new SKColor(255, 240, 180, (byte)(180 * trailAlpha * Intensity));
                        BlendPixel(pixels, tidx, trailColor);
                    }
                }
            }
        }
    }

    private void UpdateShootingStars()
    {
        _shootingStars.RemoveAll(s => s.Life <= 0);

        foreach (var star in _shootingStars)
        {
            star.Trail.Add((star.X, star.Y));
            if (star.Trail.Count > 20) star.Trail.RemoveAt(0);

            star.X += star.VX;
            star.Y += star.VY;
            star.Life--;
        }
    }

    private void AddMagicalShimmer(SKBitmap bitmap)
    {
        var shimmerPhase = _frameCount % 30 / 30f;
        var shimmerIntensity = (float)Math.Sin(shimmerPhase * Math.PI) * 0.4f;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var pixelCount = bitmap.Width * bitmap.Height;

            for (var i = 0; i < pixelCount; i++)
            {
                var pixel = pixels[i];
                var brightness = GetBrightness(pixel);

                if (brightness > 120)
                {
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Golden shimmer wave
                    var shimmer = (int)(shimmerIntensity * 30);
                    r = (byte)Math.Min(255, r + shimmer);
                    g = (byte)Math.Min(255, g + shimmer * 0.8f);

                    pixels[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void DrawSnowflakes(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var flake in _snowflakes)
            {
                if (flake.Y < 0 || flake.Y >= height) continue;

                // Add horizontal sway
                var swayOffset = (int)(Math.Sin((flake.SwayPhase + _frameCount * 2) * Math.PI / 180) * 3);
                var x = (int)flake.X + swayOffset;

                if (x < 0 || x >= width) continue;

                // Draw snowflake with soft edges
                var opacity = (byte)(255 * flake.Opacity * SnowAmount * Intensity);
                DrawSoftSnowflake(pixels, width, height, x, (int)flake.Y, flake.Size, opacity);
            }
        }
    }

    private unsafe void DrawSoftSnowflake(uint* pixels, int width, int height, int cx, int cy, int size, byte opacity)
    {
        for (var dy = -size; dy <= size; dy++)
        for (var dx = -size; dx <= size; dx++)
        {
            var x = cx + dx;
            var y = cy + dy;

            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > size) continue;

            // Soft falloff
            var alpha = (byte)(opacity * (1.0f - distance / size));

            var idx = y * width + x;
            var existing = pixels[idx];
            var er = (byte)((existing >> 16) & 0xFF);
            var eg = (byte)((existing >> 8) & 0xFF);
            var eb = (byte)(existing & 0xFF);

            // Blend white snowflake
            var blend = alpha / 255f;
            var nr = (byte)Math.Min(255, er + (255 - er) * blend);
            var ng = (byte)Math.Min(255, eg + (255 - eg) * blend);
            var nb = (byte)Math.Min(255, eb + (255 - eb) * blend);

            pixels[idx] = 0xFF000000u | ((uint)nr << 16) | ((uint)ng << 8) | nb;
        }
    }

    private void AddFestiveSparkles(SKBitmap bitmap)
    {
        var sparkleCount = (int)(30 * Intensity);

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            for (var i = 0; i < sparkleCount; i++)
            {
                var x = _random.Next(width);
                var y = _random.Next(height);

                // Sparkles appear on bright areas
                var idx = y * width + x;
                var brightness = GetBrightness(pixels[idx]);

                if (brightness > 120 && _random.Next(100) < 5)
                {
                    // Golden sparkle
                    var sparkleSize = 1 + _random.Next(2);
                    var color = new SKColor(255, 215, 100, (byte)(200 * Intensity));

                    DrawSparkle(pixels, width, height, x, y, sparkleSize, color);
                }
            }
        }
    }

    private unsafe void DrawSparkle(uint* pixels, int width, int height, int cx, int cy, int size, SKColor color)
    {
        // Draw a cross-shaped sparkle
        for (var i = -size; i <= size; i++)
        {
            // Horizontal line
            if (cx + i >= 0 && cx + i < width)
            {
                var idx = cy * width + cx + i;
                BlendPixel(pixels, idx, color);
            }

            // Vertical line
            if (cy + i >= 0 && cy + i < height)
            {
                var idx = (cy + i) * width + cx;
                BlendPixel(pixels, idx, color);
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

            BlendPixel(pixels, idx, new SKColor(color.Red, color.Green, color.Blue, falloff));
        }
    }

    private unsafe void BlendPixel(uint* pixels, int idx, SKColor color)
    {
        var existing = pixels[idx];
        var er = (byte)((existing >> 16) & 0xFF);
        var eg = (byte)((existing >> 8) & 0xFF);
        var eb = (byte)(existing & 0xFF);

        var blend = color.Alpha / 255f;
        var nr = (byte)Math.Min(255, er + (color.Red - er) * blend);
        var ng = (byte)Math.Min(255, eg + (color.Green - eg) * blend);
        var nb = (byte)Math.Min(255, eb + (color.Blue - eb) * blend);

        pixels[idx] = 0xFF000000u | ((uint)nr << 16) | ((uint)ng << 8) | nb;
    }

    private void UpdateSnowflakes(int height)
    {
        foreach (var flake in _snowflakes)
        {
            flake.Y += flake.Speed;

            if (flake.Y > height)
            {
                flake.Y = -10;
                flake.X = _random.Next(Math.Max(1, _width));
            }
        }
    }

    private int GetBrightness(uint pixel)
    {
        var r = (pixel >> 16) & 0xFF;
        var g = (pixel >> 8) & 0xFF;
        var b = pixel & 0xFF;
        return (int)(r * 0.299 + g * 0.587 + b * 0.114);
    }

    private class Snowflake
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Speed { get; set; }
        public int Size { get; set; }
        public float Opacity { get; set; }
        public float SwayPhase { get; set; }
    }

    private class ShootingStar
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public int Life { get; set; }
        public int MaxLife { get; set; }
        public List<(float X, float Y)> Trail { get; set; }
    }
}