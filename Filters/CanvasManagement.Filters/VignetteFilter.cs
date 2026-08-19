using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Vignette (darkened edges) filter
/// </summary>
[FilterInfo("Vignette",
    "Cinematic vignette effect that darkens image edges for a focused, dramatic look",
    "Image Enhancement",
    IconResourceName = "vignette.svg")]
public class VignetteFilter : ICanvasFilter
{
    public string Name => "Vignette";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        using var canvas = new SKCanvas(bitmap);

        var centerX = bitmap.Width / 2f;
        var centerY = bitmap.Height / 2f;
        var maxRadius = (float)Math.Sqrt(centerX * centerX + centerY * centerY);

        // Create radial gradient from center (transparent) to edges (dark)
        var colors = new[]
        {
            SKColors.Transparent,
            new SKColor(0, 0, 0, (byte)(Intensity * 200))
        };

        var positions = new[] { 0.3f, 1.0f };

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(centerX, centerY),
            maxRadius,
            colors,
            positions,
            SKShaderTileMode.Clamp
        );

        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true
        };

        canvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, paint);

        return bitmap;
    }
}