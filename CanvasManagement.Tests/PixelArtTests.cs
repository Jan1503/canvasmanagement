using CanvasManagement.Interfaces;
using SkiaSharp;
using Xunit;

namespace CanvasManagement.Tests;

/// <summary>
///     The cached-image versions of the pixel helpers must give exactly the pixels the old one-rect-per-pixel
///     drawing gave.
/// </summary>
public class PixelArtTests
{
    private static SKBitmap Surface(SKColor background)
    {
        var bmp = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(background);
        return bmp;
    }

    private static void AssertSame(SKBitmap expected, SKBitmap actual, int tolerance = 0)
    {
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            var e = expected.GetPixel(x, y);
            var a = actual.GetPixel(x, y);
            var d = Math.Max(Math.Max(Math.Abs(e.Red - a.Red), Math.Abs(e.Green - a.Green)),
                Math.Max(Math.Abs(e.Blue - a.Blue), Math.Abs(e.Alpha - a.Alpha)));
            Assert.True(d <= tolerance, $"pixel {x},{y}: expected {e}, got {a}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void Disc_and_ring_match_per_pixel_drawing(int r)
    {
        var bg = new SKColor(10, 20, 30);
        using var paint = new SKPaint { Color = new SKColor(200, 100, 50, 180), IsAntialias = false };

        using var expected = Surface(bg);
        using (var c = new SKCanvas(expected))
        {
            for (var y = -r; y <= r; y++)
            for (var x = -r; x <= r; x++)
                if (x * x + y * y <= r * r) c.DrawRect(20 + x, 21 + y, 1, 1, paint);
        }
        using var actual = Surface(bg);
        using (var c = new SKCanvas(actual)) PixelArt.Disc(c, paint, 20, 21, r);
        AssertSame(expected, actual, 1);

        if (r < 1) return;
        using var expRing = Surface(bg);
        using (var c = new SKCanvas(expRing))
        {
            for (var y = -r; y <= r; y++)
            for (var x = -r; x <= r; x++)
            {
                var d = x * x + y * y;
                if (d <= r * r && d > (r - 1) * (r - 1)) c.DrawRect(20 + x, 21 + y, 1, 1, paint);
            }
        }
        using var actRing = Surface(bg);
        using (var c = new SKCanvas(actRing)) PixelArt.Ring(c, paint, 20, 21, r);
        AssertSame(expRing, actRing, 1);
    }

    [Fact]
    public void Blit_matches_per_pixel_drawing_with_scale_and_flip()
    {
        string[] rows = { ".ab.", "abba", "b..a" };
        SKColor Pal(char ch) => ch switch
        {
            'a' => new SKColor(255, 0, 0),
            'b' => new SKColor(0, 255, 0, 128),
            _ => SKColors.Transparent
        };
        var bg = new SKColor(0, 0, 40);
        foreach (var flip in new[] { false, true })
        foreach (var px in new[] { 1, 2, 3 })
        {
            using var expected = Surface(bg);
            using (var c = new SKCanvas(expected))
            using (var p = new SKPaint { IsAntialias = false })
            {
                for (var ry = 0; ry < rows.Length; ry++)
                for (var rx = 0; rx < rows[ry].Length; rx++)
                {
                    p.Color = Pal(rows[ry][rx]);
                    if (p.Color.Alpha == 0) continue;
                    var cx = flip ? 5 + (rows[0].Length - 1 - rx) * px : 5 + rx * px;
                    c.DrawRect(cx, 7 + ry * px, px, px, p);
                }
            }
            using var actual = Surface(bg);
            using (var c = new SKCanvas(actual)) PixelArt.Blit(c, rows, 5, 7, Pal, px, flip);
            AssertSame(expected, actual, 1);
        }
    }

    [Fact]
    public void Blit_follows_an_animated_palette()
    {
        string[] rows = { "x" };
        using var bmp = Surface(SKColors.Black);
        using var c = new SKCanvas(bmp);
        for (var i = 0; i < 30; i++)
        {
            var col = new SKColor((byte)(i * 8), 0, 0);
            PixelArt.Blit(c, rows, 1, 1, _ => col);
            Assert.Equal(col, bmp.GetPixel(1, 1));
        }
    }

    [Fact]
    public void GlowDisc_matches_per_pixel_drawing()
    {
        var bg = new SKColor(5, 5, 5);
        var color = new SKColor(255, 200, 60);
        const float radius = 6.5f, strength = 0.4f;
        using var expected = Surface(bg);
        using (var c = new SKCanvas(expected))
        using (var p = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.Plus })
        {
            var r = (int)MathF.Ceiling(radius);
            for (var y = -r; y <= r; y++)
            for (var x = -r; x <= r; x++)
            {
                var d = MathF.Sqrt(x * x + y * y) / radius;
                if (d > 1) continue;
                var a = strength * (1 - d) * (1 - d);
                p.Color = color.WithAlpha((byte)Math.Clamp(a * 255, 0, 255));
                c.DrawRect(20 + x, 20 + y, 1, 1, p);
            }
        }
        using var actual = Surface(bg);
        using (var c = new SKCanvas(actual)) GameFx.GlowDisc(c, 20, 20, radius, color, strength);
        // Mask × strength is rounded twice instead of once.
        AssertSame(expected, actual, 2);
    }
}
