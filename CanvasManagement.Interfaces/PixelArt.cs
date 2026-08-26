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

    public static void Blit(SKCanvas canvas, string[] rows, float x, float y,
        Func<char, SKColor> palette, int px = 1, bool flipX = false)
    {
        if (rows.Length == 0 || px < 1) return;
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        var rw = rows[0].Length;
        for (var ry = 0; ry < rows.Length; ry++)
        {
            var row = rows[ry];
            for (var rx = 0; rx < row.Length; rx++)
            {
                paint.Color = palette(row[rx]);
                if (paint.Color.Alpha == 0) continue;
                var cx = flipX ? x + (rw - 1 - rx) * px : x + rx * px;
                canvas.DrawRect(cx, y + ry * px, px, px, paint);
            }
        }
    }

    public static void Dot(SKCanvas canvas, SKPaint paint, int x, int y, int px = 1) =>
        canvas.DrawRect(x, y, px, px, paint);

    /// <summary>Filled disc, integer radius, 1:1 pixels.</summary>
    public static void Disc(SKCanvas canvas, SKPaint paint, int cx, int cy, int r)
    {
        var r2 = r * r;
        for (var y = -r; y <= r; y++)
        for (var x = -r; x <= r; x++)
            if (x * x + y * y <= r2)
                canvas.DrawRect(cx + x, cy + y, 1, 1, paint);
    }

    /// <summary>1px ring. Inner disc is not filled.</summary>
    public static void Ring(SKCanvas canvas, SKPaint paint, int cx, int cy, int r)
    {
        var r2 = r * r;
        var i2 = (r - 1) * (r - 1);
        for (var y = -r; y <= r; y++)
        for (var x = -r; x <= r; x++)
        {
            var d = x * x + y * y;
            if (d <= r2 && d > i2)
                canvas.DrawRect(cx + x, cy + y, 1, 1, paint);
        }
    }

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
