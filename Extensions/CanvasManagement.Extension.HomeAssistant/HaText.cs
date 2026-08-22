using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.HomeAssistant;

internal static class HaText
{
    public static SKTextAlign ToSk(HaTileAlign align) => align switch
    {
        HaTileAlign.Left => SKTextAlign.Left,
        HaTileAlign.Right => SKTextAlign.Right,
        _ => SKTextAlign.Center
    };

    public static void Draw(SKCanvas c, ICanvas canvas, string text, SKColor color,
        float rx, float ry, float rw, float rh, float targetH,
        SKTextAlign align = SKTextAlign.Center, bool useBdf = false, bool shrinkToWidth = true)
    {
        if (string.IsNullOrEmpty(text) || rw <= 0 || rh <= 0) return;
        var shown = shrinkToWidth ? text : Fit(canvas, text, rw, targetH, useBdf);

        if (useBdf)
        {
            var fontName = BdfFontRegistry.GetBestFontForHeight(Math.Max(5, (int)Math.Round(targetH)));
            using var bmp = canvas.RenderBdfTextToBitmap(shown, color, fontName);
            if (bmp is not { Width: > 0, Height: > 0 }) return;
            var scale = shrinkToWidth
                ? Math.Min(rw / bmp.Width, rh / bmp.Height)
                : Math.Min(rh / bmp.Height, targetH / bmp.Height);
            if (scale <= 0) return;
            var dw = bmp.Width * scale;
            var dh = bmp.Height * scale;
            if (shrinkToWidth && dw > rw) { /* scale already min'd */ }
            var left = align switch
            {
                SKTextAlign.Left => rx,
                SKTextAlign.Right => rx + rw - dw,
                _ => rx + (rw - dw) / 2f
            };
            var top = ry + (rh - dh) / 2f;
            c.DrawBitmap(bmp, new SKRect(left, top, left + dw, top + dh));
            return;
        }

        using var font = new SKFont { Size = Math.Max(4f, targetH), Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var tw = font.MeasureText(shown);
        if (shrinkToWidth && tw > rw && tw > 0) font.Size *= rw / tw;

        var metrics = font.Metrics;
        var baseline = ry + (rh - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
        var anchorX = align switch
        {
            SKTextAlign.Left => rx,
            SKTextAlign.Right => rx + rw,
            _ => rx + rw / 2f
        };
        c.DrawText(shown, anchorX, baseline, align, font, paint);
    }

    public static float Measure(ICanvas canvas, string text, float targetH, bool useBdf)
    {
        if (string.IsNullOrEmpty(text) || targetH <= 0) return 0;
        if (useBdf)
        {
            var fontName = BdfFontRegistry.GetBestFontForHeight(Math.Max(5, (int)Math.Round(targetH)));
            using var bmp = canvas.RenderBdfTextToBitmap(text, SKColors.White, fontName);
            if (bmp is not { Width: > 0, Height: > 0 }) return 0;
            return bmp.Width * (targetH / bmp.Height);
        }

        using var font = new SKFont { Size = Math.Max(4f, targetH), Subpixel = true };
        return font.MeasureText(text);
    }

    private static string Fit(ICanvas canvas, string text, float rw, float targetH, bool useBdf)
    {
        if (Measure(canvas, text, targetH, useBdf) <= rw) return text;
        var s = text;
        while (s.Length > 1 && Measure(canvas, s + "…", targetH, useBdf) > rw)
            s = s[..^1];
        return s.Length < text.Length ? s + "…" : s;
    }
}
