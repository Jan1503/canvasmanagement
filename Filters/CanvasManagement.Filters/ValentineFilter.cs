using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Romantic Valentine's Day filter with hearts, pink tones, and soft glow
/// </summary>
[FilterInfo("Valentine Romance",
    "Romantic Valentine effect with floating hearts, rosy pink tones, and dreamy soft-focus glow",
    "Seasonal",
    IconResourceName = "valentine.svg")]
public class ValentineFilter : ICanvasFilter
{
    private readonly List<Heart> _hearts = new();
    private readonly Random _random = new();
    private int _frameCount;
    private bool _initialized;
    private int _width = (int)DisplayScale.ReferenceWidth;

    /// <summary>
    ///     Number of floating hearts
    /// </summary>
    [FilterParameter("Hearts", "Density of floating hearts", MinValue = 0.0f, MaxValue = 1.0f, DefaultValue = 0.6f)]
    public float HeartDensity { get; set; } = 0.6f;

    /// <summary>
    ///     Romantic soft glow intensity
    /// </summary>
    [FilterParameter("Romantic Glow", "Soft dreamy glow intensity", MinValue = 0.0f, MaxValue = 1.0f,
        DefaultValue = 0.5f)]
    public float RomanticGlow { get; set; } = 0.5f;

    public string Name => "Valentine Romance";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();
        _width = bitmap.Width;

        if (!_initialized)
        {
            InitializeHearts(bitmap.Width, bitmap.Height);
            _initialized = true;
        }

        // Apply romantic color grade
        ApplyRomanticColorGrade(bitmap);

        // Add soft glow
        AddSoftGlow(bitmap);

        // Draw floating hearts
        DrawHearts(bitmap);

        // Add sparkles
        AddSparkles(bitmap);

        _frameCount++;
        UpdateHearts(bitmap.Height);

        return bitmap;
    }

    private void InitializeHearts(int width, int height)
    {
        var heartCount = (int)(30 * HeartDensity);
        for (var i = 0; i < heartCount; i++)
            _hearts.Add(new Heart
            {
                X = _random.Next(width),
                Y = _random.Next(height),
                Size = 8 + _random.Next(15),
                Speed = 0.5f + (float)_random.NextDouble() * 1.5f,
                Opacity = 0.3f + (float)_random.NextDouble() * 0.5f,
                SwayPhase = (float)_random.NextDouble() * 360,
                Color = _random.Next(100) < 70
                    ? new SKColor(255, 100, 150)
                    : // Pink
                    new SKColor(255, 50, 50) // Red
            });
    }

    private void ApplyRomanticColorGrade(SKBitmap bitmap)
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

                // Boost reds and add pink tint
                r = (byte)Math.Min(255, r + (int)(20 * Intensity));
                g = (byte)Math.Max(0, g - (int)(5 * Intensity));
                b = (byte)Math.Min(255, b + (int)(10 * Intensity));

                // Slight warmth
                var brightness = (r + g + b) / 3;
                if (brightness < 200) r = (byte)Math.Min(255, r + (int)(5 * Intensity));

                pixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    private void AddSoftGlow(SKBitmap bitmap)
    {
        var glowStrength = RomanticGlow * Intensity;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            // Soft bloom on highlights
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var idx = y * width + x;
                var brightness = GetBrightness(pixels[idx]);

                if (brightness > 150)
                {
                    var pixel = pixels[idx];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Soft pink glow
                    var glow = (int)((brightness - 150) * glowStrength * 0.5f);
                    r = (byte)Math.Min(255, r + glow);
                    g = (byte)Math.Min(255, g + glow * 0.6f);
                    b = (byte)Math.Min(255, b + glow * 0.8f);

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void DrawHearts(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var heart in _hearts)
            {
                if (heart.Y < -heart.Size || heart.Y >= height) continue;

                var swayOffset = (int)(Math.Sin((heart.SwayPhase + _frameCount * 2) * Math.PI / 180) * 4);
                var x = (int)heart.X + swayOffset;

                if (x < -heart.Size || x >= width + heart.Size) continue;

                DrawHeart(pixels, width, height, x, (int)heart.Y, heart.Size,
                    heart.Color, (byte)(255 * heart.Opacity * HeartDensity * Intensity));
            }
        }
    }

    private unsafe void DrawHeart(uint* pixels, int width, int height, int cx, int cy, int size, SKColor color,
        byte alpha)
    {
        // Better heart shape algorithm
        for (var dy = -size; dy <= size; dy++)
        for (var dx = -size; dx <= size; dx++)
        {
            var x = cx + dx;
            var y = cy + dy;

            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            // Normalized coordinates (flip Y to fix upside-down hearts)
            var nx = dx / (float)size * 2f;
            var ny = -dy / (float)size * 2f - 0.3f; // NEGATIVE dy to flip vertically

            // Heart equation: (x^2 + y^2 - 1)^3 - x^2*y^3 <= 0
            // Simplified for better shape
            var x2 = nx * nx;
            var y2 = ny * ny;
            var y3 = ny * ny * ny;

            var heartValue = x2 + y2 - 1.0;
            heartValue = heartValue * heartValue * heartValue - x2 * y3;

            if (heartValue <= 0)
            {
                // Calculate distance from center for softer edges
                var distance = Math.Sqrt(dx * dx + dy * dy) / size;
                var edgeSoftness = Math.Max(0, 1.0f - distance * 0.3f);
                var finalAlpha = (byte)(alpha * edgeSoftness);

                var idx = y * width + x;
                BlendPixel(pixels, idx, new SKColor(color.Red, color.Green, color.Blue, 255), finalAlpha);
            }
        }
    }

    private void AddSparkles(SKBitmap bitmap)
    {
        var sparkleCount = (int)(20 * Intensity);

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            for (var i = 0; i < sparkleCount; i++)
            {
                var x = _random.Next(width);
                var y = _random.Next(height);

                if (_random.Next(100) < 15)
                {
                    var sparkleColor = new SKColor(255, 200, 220, 200);
                    var idx = y * width + x;

                    // Small cross sparkle
                    BlendPixel(pixels, idx, sparkleColor, 200);
                    if (x > 0) BlendPixel(pixels, idx - 1, sparkleColor, 150);
                    if (x < width - 1) BlendPixel(pixels, idx + 1, sparkleColor, 150);
                    if (y > 0) BlendPixel(pixels, idx - width, sparkleColor, 150);
                    if (y < height - 1) BlendPixel(pixels, idx + width, sparkleColor, 150);
                }
            }
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

    private void UpdateHearts(int height)
    {
        foreach (var heart in _hearts)
        {
            heart.Y -= heart.Speed;

            if (heart.Y < -heart.Size)
            {
                heart.Y = height + heart.Size;
                heart.X = _random.Next(Math.Max(1, _width));
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

    private class Heart
    {
        public float X { get; set; }
        public float Y { get; set; }
        public int Size { get; set; }
        public float Speed { get; set; }
        public float Opacity { get; set; }
        public float SwayPhase { get; set; }
        public SKColor Color { get; set; }
    }
}