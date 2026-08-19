using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     CRT-style scanline filter
/// </summary>
[FilterInfo("Scanlines",
    "Retro CRT monitor scanlines for classic arcade or old-school television display effect",
    "Retro",
    IconResourceName = "scanline.svg")]
public class ScanlineFilter : ICanvasFilter
{
    /// <summary>
    ///     Spacing between scanlines in pixels
    /// </summary>
    [FilterParameter("Line Spacing", "Spacing between scanlines in pixels", MinValue = 1, MaxValue = 10,
        DefaultValue = 2)]
    public int LineSpacing { get; set; } = 2;

    public string Name => "Scanlines (CRT)";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)(Intensity * 120)), // Dark scanlines
            IsAntialias = false,
            Style = SKPaintStyle.Fill
        };

        // Draw horizontal scanlines
        for (var y = 0; y < bitmap.Height; y += LineSpacing) canvas.DrawRect(0, y, bitmap.Width, 1, paint);

        return bitmap;
    }
}