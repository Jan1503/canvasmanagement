using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Gaussian blur filter
/// </summary>
[FilterInfo("Blur",
    "Gaussian blur effect for soft focus, depth of field, or motion blur effects",
    "Image Enhancement",
    IconResourceName = "blur.svg")]
public class BlurFilter : ICanvasFilter
{
    public string Name => "Blur";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Calculate blur radius based on intensity (0.5-25 pixels)
        var blurRadius = Math.Max(0.5f, Intensity * 25f);

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        using var canvas = surface.Canvas;

        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius)
        };

        canvas.DrawBitmap(bitmap, 0, 0, paint);

        // Copy blurred result back
        using var snapshot = surface.Snapshot();
        using var blurred = SKBitmap.FromImage(snapshot);

        blurred.CopyTo(bitmap);

        return bitmap;
    }
}