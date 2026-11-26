using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     1:1 LED pixel helpers. Sprites are authored in characters; each glyph is one panel pixel
///     (or an integer scale of 1 on a 128-tall wall, 2 on 256-tall). No anti-aliasing.
/// </summary>
public static class PixelArt
{
    /// <summary>1 on a 128px-tall panel, 2 on 256px, never a blurry fraction.</summary>
    public static int Scale(int canvasHeight) =>
        Math.Max(1, (int)Math.Round(canvasHeight / 128f));

    /// <summary>
    ///     Draws a character sprite: each character is one <paramref name="px"/>×<paramref name="px"/> block in
    ///     the colour <paramref name="palette"/> gives it (transparent = not drawn). The coloured sprite is
    ///     cached as an image, so this is one draw call however big the sprite is.
    /// </summary>
    public static void Blit(SKCanvas canvas, string[] rows, float x, float y,
        Func<char, SKColor> palette, int px = 1, bool flipX = false)
    {
        if (rows.Length == 0 || px < 1) return;
        var img = PixelSprites.Sprite(rows, palette, flipX);
        if (img == null) return;
        canvas.DrawImage(img, SKRect.Create(x, y, img.Width * px, img.Height * px), Nearest);
    }

    public static void Dot(SKCanvas canvas, SKPaint paint, int x, int y, int px = 1) =>
        canvas.DrawRect(x, y, px, px, paint);

    /// <summary>Filled disc, integer radius, 1:1 pixels, in the paint's colour and blend mode.</summary>
    public static void Disc(SKCanvas canvas, SKPaint paint, int cx, int cy, int r)
    {
        if (r < 0) return;
        canvas.DrawImage(PixelSprites.Disc(r), cx - r, cy - r, Nearest, paint);
    }

    /// <summary>1px ring. Inner disc is not filled.</summary>
    public static void Ring(SKCanvas canvas, SKPaint paint, int cx, int cy, int r)
    {
        if (r < 1) return;
        canvas.DrawImage(PixelSprites.Ring(r), cx - r, cy - r, Nearest, paint);
    }

    /// <summary>Pixel-exact sampling: LED pixels are never smoothed.</summary>
    internal static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    public static SKColor Hsv(int h, float s, float v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60f % 2 - 1));
        var m = v - c;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return new SKColor((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
