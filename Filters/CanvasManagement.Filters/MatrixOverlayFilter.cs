using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Matrix-style digital rain overlay filter
/// </summary>
[FilterInfo("Matrix Overlay",
    "Subtle Matrix code overlay effect that adds cascading digital rain on top of your content",
    "Matrix Effects",
    IconResourceName = "matrixoverlay.svg")]
public class MatrixOverlayFilter : ICanvasFilter
{
    private readonly List<DigitalRainDrop> _drops = new();
    private readonly Random _random = new();
    private bool _initialized;

    // Sizing tracked from the bitmap so the rain scales with the panel and respawns on-screen.
    private int _width = (int)DisplayScale.ReferenceWidth;
    private float _fontSize = 12f;
    private int _charSpacing = 15;

    public string Name => "Matrix Overlay";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Track sizing so rain glyphs/spacing scale with the panel and drops respawn on-screen.
        _width = bitmap.Width;
        var scale = Math.Min(bitmap.Width / DisplayScale.ReferenceWidth, bitmap.Height / DisplayScale.ReferenceHeight);
        _fontSize = Math.Max(5f, 12f * scale);
        _charSpacing = Math.Max(3, (int)Math.Round(15 * scale));

        // Initialize drops on first call
        if (!_initialized)
        {
            InitializeDrops(bitmap.Width, bitmap.Height);
            _initialized = true;
        }

        // Apply green tint overlay
        ApplyGreenTint(bitmap);

        // Draw digital rain effect
        DrawDigitalRain(bitmap);

        // Update drops for next frame
        UpdateDrops(bitmap.Height);

        return bitmap;
    }

    private void InitializeDrops(int width, int height)
    {
        var dropCount = (int)(width / 10 * Intensity);
        for (var i = 0; i < dropCount; i++)
            _drops.Add(new DigitalRainDrop
            {
                X = _random.Next(width),
                Y = _random.Next(-height, 0),
                Speed = _random.Next(2, 6),
                Length = _random.Next(5, 15),
                Char = (char)('0' + _random.Next(10))
            });
    }

    private void ApplyGreenTint(SKBitmap bitmap)
    {
        var tintStrength = Intensity * 0.15f; // Subtle green tint

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

                // Add green tint
                g = (byte)Math.Min(255, g + (int)(30 * tintStrength));
                r = (byte)Math.Max(0, r - (int)(10 * tintStrength));
                b = (byte)Math.Max(0, b - (int)(10 * tintStrength));

                pixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    private void DrawDigitalRain(SKBitmap bitmap)
    {
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            Color = new SKColor(0, 255, 0, (byte)(Intensity * 100)),
            IsAntialias = false,
            TextSize = _fontSize
        };

        foreach (var drop in _drops)
            for (var i = 0; i < drop.Length; i++)
            {
                var y = drop.Y - i * _charSpacing;
                if (y < 0 || y > bitmap.Height) continue;

                var alpha = (byte)((drop.Length - i) * 255 / drop.Length * Intensity);
                paint.Color = new SKColor(0, 255, 0, alpha);

                canvas.DrawText(drop.Char.ToString(), drop.X, y, paint);
            }
    }

    private void UpdateDrops(int height)
    {
        foreach (var drop in _drops)
        {
            drop.Y += drop.Speed;

            if (drop.Y > height + drop.Length * _charSpacing)
            {
                drop.Y = -drop.Length * _charSpacing;
                drop.X = _random.Next(Math.Max(1, _width));
                drop.Char = (char)('0' + _random.Next(10));
            }
        }
    }

    private class DigitalRainDrop
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Speed { get; set; }
        public int Length { get; set; }
        public char Char { get; set; }
    }
}
