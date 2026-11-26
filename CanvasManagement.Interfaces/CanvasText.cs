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
            var glyphs = Cached(host, text, color, size);
            if (glyphs == null) return;
            var (dw, dh) = DestSize(glyphs.Width, glyphs.Height, size, maxWidth);
            var left = align switch
            {
                SKTextAlign.Center => x - dw / 2f,
                SKTextAlign.Right => x - dw,
                _ => x
            };
            c.DrawImage(glyphs.Image, new SKRect(0, 0, glyphs.Width, glyphs.Height),
                new SKRect(left, y - dh, left + dw, y), Nearest);
            return;
        }

        using var font = new SKFont { Size = size, Subpixel = false };
        using var paint = new SKPaint { Color = color, IsAntialias = false };
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
            var glyphs = Cached(host, text, SKColors.White, size);
            return glyphs == null ? 0 : DestSize(glyphs.Width, glyphs.Height, size, 0).w;
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

    public static (float w, float h) DestSize(SKBitmap bmp, float targetH, float maxW = 0) =>
        DestSize(bmp.Width, bmp.Height, targetH, maxW);

    private static (float w, float h) DestSize(int width, int height, float targetH, float maxW)
    {
        var scale = PixelScale(height, targetH);
        var dw = width * scale;
        var dh = height * scale;
        if (maxW > 0 && dw > maxW)
        {
            scale = Math.Max(1, (int)Math.Floor(maxW / width));
            dw = width * scale;
            dh = height * scale;
        }

        return (dw, dh);
    }

    // ------------------------------------------------------------------ rendered-text cache
    // A HUD draws the same few strings every frame and a ticker one very long one. Rendering BDF glyphs
    // into a fresh bitmap each time was the single biggest cost (the news ticker spent ~280 ms per frame
    // on it). Rendered strings are kept as immutable images, shared by all canvases and threads.

    private sealed class Glyphs(SKImage image)
    {
        public readonly SKImage Image = image;
        public int Width => Image.Width;
        public int Height => Image.Height;
        public LinkedListNode<(string Font, string Text, uint Color)>? Node;
    }

    private const int CacheEntries = 512;
    private const long CachePixels = 8_000_000;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<(string Font, string Text, uint Color), Glyphs> Cache = new();
    private static readonly LinkedList<(string Font, string Text, uint Color)> Recent = new();
    private static long _cachedPixels;

    private static Glyphs? Cached(ICanvas host, string text, SKColor color, float size)
    {
        var fontName = host.GetBestBdfFontForHeight(Math.Max(5, (int)Math.Round(size))) ?? "";
        var key = (fontName, text, (uint)color);
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var hit))
            {
                Recent.Remove(hit.Node!);
                Recent.AddFirst(hit.Node!);
                return hit;
            }
        }

        using var rendered = host.RenderBdfTextToBitmap(text, color, fontName.Length > 0 ? fontName : null);
        if (rendered is not { Width: > 0, Height: > 0 }) return null;
        var glyphs = new Glyphs(SKImage.FromBitmap(rendered));
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var raced)) return raced;
            glyphs.Node = Recent.AddFirst(key);
            Cache[key] = glyphs;
            _cachedPixels += (long)glyphs.Width * glyphs.Height;
            // Drop the least recently used. Images are not disposed here: another thread may be drawing one;
            // the finalizer frees it once nothing references it.
            while (Recent.Count > CacheEntries || _cachedPixels > CachePixels && Recent.Count > 1)
            {
                var old = Recent.Last!.Value;
                Recent.RemoveLast();
                if (Cache.Remove(old, out var gone)) _cachedPixels -= (long)gone.Width * gone.Height;
            }
        }
        return glyphs;
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
