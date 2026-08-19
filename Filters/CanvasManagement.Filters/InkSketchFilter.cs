using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Optimized ink sketch filter for Raspberry Pi performance
/// </summary>
[FilterInfo("Ink Sketch",
    "Hand-drawn ink sketch with variable line weights and optional cross-hatching for realistic pen-and-ink illustration",
    "Artistic",
    IconResourceName = "inksketch.svg")]
public class InkSketchFilter : ICanvasFilter
{
    [FilterParameter("Edge Sensitivity", "Sensitivity for edge detection", MinValue = 20, MaxValue = 100,
        DefaultValue = 40)]
    public int EdgeSensitivity { get; set; } = 40; // Higher for faster processing

    [FilterParameter("Enable CrossHatch", "Add cross-hatching for shading")]
    public bool EnableCrossHatch { get; set; } = true;

    public string Name => "Ink Sketch";
    public float Intensity { get; set; } = 0.7f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Create off-white paper background
        ClearToPaper(bitmap);

        // Draw ink edges
        DrawFastInkEdges(bitmap, source);

        // Add cross-hatching
        if (EnableCrossHatch && Intensity > 0.5f) AddFastCrossHatch(bitmap, source);

        return bitmap;
    }

    private void ClearToPaper(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var pixelCount = bitmap.Width * bitmap.Height;
            var paperColor = 0xFFFAF8F0u;

            for (var i = 0; i < pixelCount; i++) pixels[i] = paperColor;
        }
    }

    private void DrawFastInkEdges(SKBitmap target, SKBitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var threshold = EdgeSensitivity * (1.0f - Intensity * 0.3f);

        unsafe
        {
            var srcPixels = (uint*)source.GetPixels().ToPointer();
            var dstPixels = (uint*)target.GetPixels().ToPointer();

            // Fast 4-neighbor edge detection
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var idx = y * width + x;
                var center = GetLuma(srcPixels[idx]);

                var diff = Math.Abs(center - GetLuma(srcPixels[idx - width])) +
                           Math.Abs(center - GetLuma(srcPixels[idx + width])) +
                           Math.Abs(center - GetLuma(srcPixels[idx - 1])) +
                           Math.Abs(center - GetLuma(srcPixels[idx + 1]));

                if (diff > threshold)
                {
                    // Variable line weight
                    var inkIntensity = diff > threshold * 2 ? (byte)0 : (byte)60;
                    dstPixels[idx] = 0xFF000000u | ((uint)inkIntensity << 16) | ((uint)inkIntensity << 8) |
                                     inkIntensity;
                }
            }
        }
    }

    private void AddFastCrossHatch(SKBitmap target, SKBitmap source)
    {
        var width = target.Width;
        var height = target.Height;
        var spacing = 4; // Wider spacing for performance

        unsafe
        {
            var srcPixels = (uint*)source.GetPixels().ToPointer();
            var dstPixels = (uint*)target.GetPixels().ToPointer();

            for (var y = 0; y < height; y += 2) // Process every 2nd row
            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var luma = GetLuma(srcPixels[idx]);

                // Skip if already has ink line
                if ((dstPixels[idx] & 0xFFFFFF) < 0xA0A0A0) continue;

                // Simple single-direction hatching based on darkness
                if (luma < 100 && (x + y) % spacing == 0)
                    dstPixels[idx] = 0xFF404040u;
                else if (luma < 150 && (x + y) % (spacing * 2) == 0) dstPixels[idx] = 0xFF808080u;
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