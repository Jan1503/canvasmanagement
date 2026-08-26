using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     Draws a string with either the system (Skia) font or a crisp BDF bitmap, so every text-showing
///     extension can offer the same "Use BDF Font" toggle without duplicating the scale/align math.
/// </summary>
public static class CanvasText
{
    private static readonly SKSamplingOptions Nearest =
        new(SKFilterMode.Nearest, SKMipmapMode.None);

    /// <summary>0 = use the auto-fit size; otherwise the user's pixel height.</summary>
    public static float ResolveSize(int configuredPx, float autoPx)
    {
        return configuredPx > 0 ? configuredPx : autoPx;
    }

    /// <summary>
    ///     Draws <paramref name="text"/> at (<paramref name="x"/>, <paramref name="y"/>).
    ///     <paramref name="y"/> is the Skia baseline; BDF bitmaps are aligned so their bottom sits on it.
    /// </summary>
    public static void Draw(SKCanvas c, ICanvas host, string text, SKColor color,
        float x, float y, float size, SKTextAlign align, bool useBdf, float maxWidth = 0)
    {
        if (string.IsNullOrEmpty(text) || size <= 0) return;

        if (useBdf)
        {
            if (!TryRenderBdf(host, text, color, size, maxWidth, out var bmp, out var dw, out var dh))
                return;
            using (bmp)
            {
                var left = align switch
                {
                    SKTextAlign.Center => x - dw / 2f,
                    SKTextAlign.Right => x - dw,
                    _ => x
                };
                Blit(c, bmp, left, y - dh, dw, dh);
            }

            return;
        }

        using var font = new SKFont { Size = size, Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        if (maxWidth > 0)
        {
            var tw = font.MeasureText(text);
            if (tw > maxWidth && tw > 0) font.Size *= maxWidth / tw;
        }

        c.DrawText(text, x, y, align, font, paint);
    }

    /// <summary>Width of <paramref name="text"/> at the given pixel height.</summary>
    public static float Measure(ICanvas host, string text, float size, bool useBdf)
    {
        if (string.IsNullOrEmpty(text) || size <= 0) return 0;
        if (useBdf)
        {
            if (!TryRenderBdf(host, text, SKColors.White, size, 0, out var bmp, out var dw, out _))
                return 0;
            bmp.Dispose();
            return dw;
        }

        using var font = new SKFont { Size = size, Subpixel = true };
        return font.MeasureText(text);
    }

    /// <summary>
    ///     Blits a BDF bitmap with nearest-neighbour sampling so LED pixels stay sharp.
    ///     Fractional scales (the usual source of broken glyphs) are snapped to integer multiples.
    /// </summary>
    public static void Blit(SKCanvas c, SKBitmap bmp, float left, float top, float destW, float destH)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0 || destW <= 0 || destH <= 0) return;
        var scaleX = PixelScale(bmp.Width, destW);
        var scaleY = PixelScale(bmp.Height, destH);
        var dw = bmp.Width * scaleX;
        var dh = bmp.Height * scaleY;
        using var img = SKImage.FromBitmap(bmp);
        c.DrawImage(img,
            new SKRect(0, 0, bmp.Width, bmp.Height),
            new SKRect(left, top, left + dw, top + dh),
            Nearest);
    }

    public static (float w, float h) DestSize(SKBitmap bmp, float targetH, float maxW = 0)
    {
        var scale = PixelScale(bmp.Height, targetH);
        var dw = bmp.Width * scale;
        var dh = bmp.Height * scale;
        if (maxW > 0 && dw > maxW)
        {
            scale = Math.Max(1, (int)Math.Floor(maxW / bmp.Width));
            dw = bmp.Width * scale;
            dh = bmp.Height * scale;
        }

        return (dw, dh);
    }

    private static bool TryRenderBdf(ICanvas host, string text, SKColor color, float size, float maxWidth,
        out SKBitmap bmp, out float dw, out float dh)
    {
        bmp = null!;
        dw = dh = 0;
        var fontName = host.GetBestBdfFontForHeight(Math.Max(5, (int)Math.Round(size)));
        var rendered = host.RenderBdfTextToBitmap(text, color, fontName);
        if (rendered is not { Width: > 0, Height: > 0 }) return false;
        (dw, dh) = DestSize(rendered, size, maxWidth);
        bmp = rendered;
        return true;
    }

    /// <summary>
    ///     Integer pixel scale only. Downscaling a bitmap font with linear filtering (or a 1.3× nearest
    ///     stretch) drops rows/columns and looks like broken glyphs on an LED matrix.
    /// </summary>
    private static int PixelScale(int native, float target)
    {
        if (native <= 0) return 1;
        if (target <= native * 1.4f) return 1;
        return Math.Max(1, (int)Math.Round(target / native));
    }
}
