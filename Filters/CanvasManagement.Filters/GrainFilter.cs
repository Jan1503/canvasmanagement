using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Film grain/noise filter
/// </summary>
[FilterInfo("Film Grain",
    "Add authentic film grain texture for vintage cinema or analog photography aesthetic",
    "Image Enhancement",
    IconResourceName = "grain.svg")]
public class GrainFilter : ICanvasFilter
{
    private readonly Random _random = new();

    public string Name => "Film Grain";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        var grainStrength = (int)(Intensity * 40); // 0-40 grain

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var pixelCount = bitmap.Width * bitmap.Height;

            // Apply grain to random pixels
            var grainCount = (int)(pixelCount * Intensity * 0.3f); // 0-30% of pixels

            for (var i = 0; i < grainCount; i++)
            {
                var pixelIndex = _random.Next(pixelCount);
                var pixel = pixels[pixelIndex];

                var a = (byte)((pixel >> 24) & 0xFF);
                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Add random grain
                var grain = _random.Next(-grainStrength, grainStrength);
                r = (byte)Math.Clamp(r + grain, 0, 255);
                g = (byte)Math.Clamp(g + grain, 0, 255);
                b = (byte)Math.Clamp(b + grain, 0, 255);

                pixels[pixelIndex] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }

        return bitmap;
    }
}