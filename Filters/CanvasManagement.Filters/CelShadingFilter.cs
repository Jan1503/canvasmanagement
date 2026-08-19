using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Optimized cel-shading filter for Raspberry Pi performance
/// </summary>
[FilterInfo("Cel Shading",
    "Anime-style cel shading with smooth color transitions and clean outlines for that hand-drawn look",
    "Artistic",
    IconResourceName = "celshading.svg")]
public class CelShadingFilter : ICanvasFilter
{
    [FilterParameter("Shading Levels", "Number of shading levels", MinValue = 2, MaxValue = 6, DefaultValue = 3)]
    public int ShadingLevels { get; set; } = 3; // Reduced from 4 for performance

    [FilterParameter("Edge Thickness", "Thickness of outlines", MinValue = 1, MaxValue = 3, DefaultValue = 1)]
    public int EdgeThickness { get; set; } = 1; // Reduced max from 5 to 3

    public string Name => "Cel-Shading (Anime)";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Step 1: Quantize to levels (faster HSV conversion)
        QuantizeToLevels(bitmap);

        // Step 2: Draw edges only if intensity is high enough
        if (Intensity > 0.5f) DrawFastEdges(bitmap);

        return bitmap;
    }

    private void QuantizeToLevels(SKBitmap bitmap)
    {
        var levels = Math.Max(2, Math.Min(4, (int)(ShadingLevels * Intensity))); // Max 4 levels
        var step = 255f / levels;

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

                // Fast luminance-based quantization instead of full HSV
                var luma = (byte)(r * 0.299f + g * 0.587f + b * 0.114f);
                var quantized = (byte)(Math.Round(luma / step) * step);

                // Apply quantization while preserving hue
                if (luma > 0)
                {
                    var scale = quantized / (float)luma;
                    r = (byte)Math.Min(255, r * scale);
                    g = (byte)Math.Min(255, g * scale);
                    b = (byte)Math.Min(255, b * scale);
                }

                pixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    private void DrawFastEdges(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();

            // Single-pass edge detection with Sobel
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var idx = y * width + x;
                var center = pixels[idx];

                // Fast 4-neighbor edge detection
                var top = pixels[idx - width];
                var bottom = pixels[idx + width];
                var left = pixels[idx - 1];
                var right = pixels[idx + 1];

                var diff = Math.Abs(GetLuma(center) - GetLuma(top)) +
                           Math.Abs(GetLuma(center) - GetLuma(bottom)) +
                           Math.Abs(GetLuma(center) - GetLuma(left)) +
                           Math.Abs(GetLuma(center) - GetLuma(right));

                // Draw edge if difference is significant
                if (diff > 100 * Intensity)
                {
                    var a = (byte)((center >> 24) & 0xFF);
                    pixels[idx] = (uint)((a << 24) | 0x000000);
                }
            }
        }
    }

    private int GetLuma(uint pixel)
    {
        var r = (pixel >> 16) & 0xFF;
        var g = (pixel >> 8) & 0xFF;
        var b = pixel & 0xFF;
        return (int)(r * 0.299f + g * 0.587f + b * 0.114f);
    }
}