using System.Diagnostics;
using CanvasManagement.Filters;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Xunit.Abstractions;

namespace CanvasManagement.Tests;

/// <summary>Bloom, Retro CRT, Glitch, Retro Palette, Kaleidoscope, Afterglow.</summary>
public class NewEffectsTests(ITestOutputHelper output)
{
    public static TheoryData<string, int, int, SKColorType> Cases()
    {
        var data = new TheoryData<string, int, int, SKColorType>();
        foreach (var name in new[] { "Bloom", "RetroCrt", "Glitch", "RetroPalette", "Kaleidoscope", "Afterglow" })
        foreach (var type in new[] { SKColorType.Bgra8888, SKColorType.Rgba8888 })
        {
            data.Add(name, 64, 32, type);
            data.Add(name, 65, 33, type);
        }
        return data;
    }

    private static ICanvasFilter Create(string name) => name switch
    {
        "Bloom" => new BloomFilter { Intensity = 1f },
        "RetroCrt" => new RetroCrtFilter { Intensity = 1f },
        "Glitch" => new GlitchFilter { Intensity = 1f, Frequency = 1f },
        "RetroPalette" => new RetroPaletteFilter { Intensity = 1f },
        "Kaleidoscope" => new KaleidoscopeFilter { Intensity = 1f },
        "Afterglow" => new AfterglowFilter { Intensity = 1f },
        _ => throw new ArgumentException(name)
    };

    private static SKBitmap Picture(int w, int h, SKColorType type)
    {
        var bitmap = new SKBitmap(new SKImageInfo(w, h, type, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(30, 40, 60));
        using var paint = new SKPaint { Color = new SKColor(255, 220, 90) };
        canvas.DrawRect(w / 4f, h / 4f, w / 3f, h / 3f, paint);
        paint.Color = new SKColor(40, 200, 255);
        canvas.DrawCircle(w * 0.7f, h * 0.6f, h / 5f, paint);
        return bitmap;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Runs_in_place_on_any_size_and_layout_and_keeps_the_canvas_attached(string name, int w, int h, SKColorType type)
    {
        using var bitmap = Picture(w, h, type);
        using var canvas = new SKCanvas(bitmap);
        var filter = Create(name);

        for (var i = 0; i < 5; i++)
            Assert.Same(bitmap, filter.Apply(bitmap));

        // the compositor keeps drawing into the same bitmap afterwards
        canvas.Clear(SKColors.Red);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(w - 1, h - 1));
        (filter as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData("Bloom")]
    [InlineData("RetroCrt")]
    [InlineData("RetroPalette")]
    [InlineData("Kaleidoscope")]
    [InlineData("Afterglow")]
    public void Off_or_zero_intensity_changes_nothing(string name)
    {
        using var bitmap = Picture(48, 24, SKColorType.Bgra8888);
        using var reference = bitmap.Copy();
        var off = Create(name);
        off.Enabled = false;
        off.Apply(bitmap);
        var zero = Create(name);
        zero.Intensity = 0;
        zero.Apply(bitmap);

        Assert.Equal(reference.Bytes, bitmap.Bytes);
    }

    [Fact]
    public void Bloom_lets_a_bright_spot_glow_into_its_dark_surroundings()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(40, 20, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bitmap))
        {
            c.Clear(SKColors.Black);
            using var white = new SKPaint { Color = SKColors.White };
            c.DrawRect(18, 8, 4, 4, white);
        }

        new BloomFilter { Intensity = 1f, Threshold = 0.5f, Radius = 6 }.Apply(bitmap);

        Assert.True(bitmap.GetPixel(14, 10).Red > 0, "pixel next to the spot should glow");
        Assert.Equal(0, bitmap.GetPixel(0, 0).Red);
    }

    [Fact]
    public void Retro_crt_darkens_every_second_line()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(32, 16, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.White);

        new RetroCrtFilter { Intensity = 1f, Scanlines = 1f, EdgeDarkening = 0f, Flicker = 0f, ColorFringe = 0 }.Apply(bitmap);

        Assert.True(bitmap.GetPixel(16, 7).Green < bitmap.GetPixel(16, 6).Green - 100);
    }

    [Theory]
    [InlineData(SKColorType.Bgra8888)]
    [InlineData(SKColorType.Rgba8888)]
    public void Retro_palette_maps_to_the_palette_whatever_the_pixel_layout(SKColorType type)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, type, SKAlphaType.Premul));
        bitmap.Erase(SKColors.White);

        new RetroPaletteFilter { Intensity = 1f, Palette = RetroPalette.HandheldGreen, Dither = false }.Apply(bitmap);

        Assert.Equal(new SKColor(155, 188, 15), bitmap.GetPixel(3, 3));
    }

    [Fact]
    public void Retro_palette_dither_mixes_two_shades_in_a_mid_grey()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(new SKColor(128, 128, 128));

        new RetroPaletteFilter { Intensity = 1f, Palette = RetroPalette.OneBit, Dither = true }.Apply(bitmap);

        var colours = Enumerable.Range(0, 64).Select(i => bitmap.GetPixel(i % 8, i / 8)).Distinct().ToList();
        Assert.Equal(2, colours.Count);
    }

    [Fact]
    public void Mirror_left_right_copies_the_left_half()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(20, 10, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bitmap))
        {
            c.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            c.DrawRect(0, 0, 10, 10, red);
        }

        new KaleidoscopeFilter { Intensity = 1f, Mode = MirrorMode.MirrorLeftRight }.Apply(bitmap);

        Assert.Equal(SKColors.Red, bitmap.GetPixel(15, 5));
    }

    [Fact]
    public void Afterglow_keeps_a_fading_trail_of_what_was_there()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul));
        var glow = new AfterglowFilter { Intensity = 1f, TrailSeconds = 3f };
        bitmap.Erase(SKColors.White);
        glow.Apply(bitmap);

        bitmap.Erase(SKColors.Black);
        glow.Apply(bitmap);

        var trail = bitmap.GetPixel(4, 4).Red;
        Assert.InRange(trail, 1, 254);
    }

    [Fact]
    public void Each_effect_is_cheap_enough_for_a_384x192_wall()
    {
        foreach (var name in new[] { "Bloom", "RetroCrt", "Glitch", "RetroPalette", "Kaleidoscope", "Afterglow" })
        {
            using var bitmap = Picture(384, 192, SKColorType.Bgra8888);
            var filter = Create(name);
            filter.Apply(bitmap); // warm up (tables, buffers)
            var sw = Stopwatch.StartNew();
            const int frames = 30;
            for (var i = 0; i < frames; i++) filter.Apply(bitmap);
            var ms = sw.Elapsed.TotalMilliseconds / frames;
            output.WriteLine($"{name}: {ms:F2} ms per frame at 384x192");
            // generous ceiling: a dev PC is several times faster than a Pi, this only catches gross mistakes
            Assert.True(ms < 15, $"{name} took {ms:F1} ms per frame");
            (filter as IDisposable)?.Dispose();
        }

        // reference: the stock blur at its default strength
        using var reference = Picture(384, 192, SKColorType.Bgra8888);
        var blur = new BlurFilter();
        blur.Apply(reference);
        var swBlur = Stopwatch.StartNew();
        for (var i = 0; i < 30; i++) blur.Apply(reference);
        output.WriteLine($"(reference) Blur: {swBlur.Elapsed.TotalMilliseconds / 30:F2} ms per frame at 384x192");
    }
}
