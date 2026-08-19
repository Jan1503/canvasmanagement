using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Pixelation/mosaic filter
/// </summary>
[FilterInfo("Pixelate",
    "Pixelation mosaic effect for retro 8-bit gaming style or privacy masking",
    "Retro",
    IconResourceName = "pixelate.svg")]
public class PixelateFilter : ICanvasFilter
{
    public string Name => "Pixelate";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Calculate pixel size (2-50 pixels based on intensity)
        var pixelSize = Math.Max(2, (int)(Intensity * 50));

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();

            for (var y = 0; y < bitmap.Height; y += pixelSize)
            for (var x = 0; x < bitmap.Width; x += pixelSize)
            {
                // Sample center pixel of block
                var sampleX = Math.Min(x + pixelSize / 2, bitmap.Width - 1);
                var sampleY = Math.Min(y + pixelSize / 2, bitmap.Height - 1);
                var sampleColor = pixels[sampleY * bitmap.Width + sampleX];

                // Fill entire block with sampled color
                for (var by = 0; by < pixelSize && y + by < bitmap.Height; by++)
                for (var bx = 0; bx < pixelSize && x + bx < bitmap.Width; bx++)
                    pixels[(y + by) * bitmap.Width + x + bx] = sampleColor;
            }
        }

        return bitmap;
    }
}