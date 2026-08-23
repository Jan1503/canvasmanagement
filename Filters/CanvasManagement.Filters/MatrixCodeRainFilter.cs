using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Advanced Matrix "Code Rain" effect - content dissolves into cascading digital code
/// </summary>
[FilterInfo("Matrix Code Rain",
    "Animated Matrix code rain effect where content dissolves into cascading digital characters",
    "Matrix Effects",
    IconResourceName = "matrixcoderain.svg")]
public class MatrixCodeRainFilter : ICanvasFilter
{
    private readonly Random _random = new();
    private byte[,] _brightnessMap = new byte[0, 0];
    private int _frameCount;
    private bool _initialized;

    /// <summary>
    ///     How much of original image structure remains (0.0 = pure code, 1.0 = image visible)
    /// </summary>
    [FilterParameter("Image Retention", "How much of original image structure remains", MinValue = 0.0f,
        MaxValue = 1.0f, DefaultValue = 0.3f)]
    public float ImageRetention { get; set; } = 0.3f;

    /// <summary>
    ///     Code characters fall speed
    /// </summary>
    [FilterParameter("Fall Speed", "Code characters fall speed", MinValue = 1, MaxValue = 10, DefaultValue = 2)]
    public int FallSpeed { get; set; } = 2;

    public string Name => "Matrix Code Rain";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Initialize brightness map from source
        if (!_initialized ||
            _brightnessMap.GetLength(0) != bitmap.Width ||
            _brightnessMap.GetLength(1) != bitmap.Height)
        {
            InitializeBrightnessMap(source);
            _initialized = true;
        }

        // Transform to Matrix code rain
        ApplyMatrixCodeRain(bitmap, source);

        _frameCount++;

        return bitmap;
    }

    private void InitializeBrightnessMap(SKBitmap source)
    {
        _brightnessMap = new byte[source.Width, source.Height];

        unsafe
        {
            var pixels = (uint*)source.GetPixels().ToPointer();

            for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var idx = y * source.Width + x;
                var brightness = GetBrightness(pixels[idx]);
                _brightnessMap[x, y] = (byte)brightness;
            }
        }
    }

    private void ApplyMatrixCodeRain(SKBitmap target, SKBitmap source)
    {
        var width = target.Width;
        var height = target.Height;

        unsafe
        {
            var srcPixels = (uint*)source.GetPixels().ToPointer();
            var dstPixels = (uint*)target.GetPixels().ToPointer();

            // First pass: Apply green tint and darkness
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var srcBrightness = _brightnessMap[x, y];

                // Calculate "falling" effect based on frame and position
                var fallOffset = (_frameCount * FallSpeed + x * 3) % height;
                var distanceFromFall = Math.Abs(y - fallOffset);
                var isFalling = distanceFromFall < 20;

                // Blend between dark background and bright falling code
                byte greenIntensity;
                if (isFalling)
                {
                    // Bright falling code
                    var fallFactor = 1.0f - distanceFromFall / 20.0f;
                    greenIntensity = (byte)(srcBrightness * Intensity * (0.8f + fallFactor * 0.5f));
                }
                else
                {
                    // Dark background with slight image retention
                    greenIntensity = (byte)(srcBrightness * ImageRetention * 0.4f);
                }

                // Matrix green color scheme
                var r = (byte)(greenIntensity * 0.05f);
                var g = greenIntensity;
                var b = (byte)(greenIntensity * 0.1f);

                dstPixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }

            // Second pass: Add character glyphs where bright
            DrawDigitalCharacters(dstPixels, width, height);

            // Third pass: Add bright "scan lines" for extra Matrix feel
            AddScanLines(dstPixels, width, height);
        }
    }

    private unsafe void DrawDigitalCharacters(uint* pixels, int width, int height)
    {
        var charSize = 6;
        var chars = new[] { '0', '1', '?', '?', '?', ':', '=', '|' };

        for (var y = 0; y < height; y += charSize)
        for (var x = 0; x < width; x += charSize)
        {
            // Sample brightness
            var brightness = GetBrightness(pixels[y * width + x]);

            // Calculate if this column is "active" based on frame
            var columnPhase = (_frameCount * FallSpeed + x * 3) % height;
            var isActiveColumn = Math.Abs(y - columnPhase) < 30;

            // Draw character if bright enough
            if (brightness > 40 && (isActiveColumn || _random.Next(100) < brightness / 5))
            {
                var charIntensity = isActiveColumn ? (byte)Math.Min(255, brightness * 1.5f) : (byte)(brightness * 0.8f);

                // Occasional bright white character (like Matrix "lead" characters)
                if (isActiveColumn && _random.Next(100) < 10) charIntensity = 255;

                DrawSmallChar(pixels, width, height, x, y,
                    chars[_random.Next(chars.Length)], charIntensity);
            }
        }
    }

    private unsafe void DrawSmallChar(uint* pixels, int width, int height,
        int x, int y, char c, byte intensity)
    {
        // Simple 3x5 mini character
        var pattern = new[,]
        {
            { false, true, false },
            { true, false, true },
            { true, true, true },
            { true, false, true },
            { true, false, true }
        };

        for (var py = 0; py < 5 && y + py < height; py++)
        for (var px = 0; px < 3 && x + px < width; px++)
            if (pattern[py, px])
            {
                var idx = (y + py) * width + x + px;
                if (idx >= 0 && idx < width * height)
                {
                    var r = (byte)(intensity * 0.1f);
                    var g = intensity;
                    var b = (byte)(intensity * 0.15f);

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
    }

    private unsafe void AddScanLines(uint* pixels, int width, int height)
    {
        // Moving scan line effect
        var scanY = _frameCount * 2 % height;

        for (var dy = -2; dy <= 2; dy++)
        {
            var y = scanY + dy;
            if (y < 0 || y >= height) continue;

            var alpha = 1.0f - Math.Abs(dy) / 3.0f;

            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var pixel = pixels[idx];

                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Brighten scan line
                g = (byte)Math.Min(255, g + (byte)(50 * alpha * Intensity));
                r = (byte)Math.Min(255, r + (byte)(20 * alpha * Intensity));

                pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
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
}
