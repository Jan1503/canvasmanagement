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
        foreach (var name in new[] { "Bloom", "RetroCrt", "Glitch", "RetroPalette", "Kaleidoscope", "Afterglow", "DigitalRain" })
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
        "DigitalRain" => new DigitalRainVisionFilter { Intensity = 1f },
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
    [InlineData("DigitalRain")]
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
    public void Digital_rain_turns_the_picture_green_and_keeps_its_light()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 32, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bitmap))
        {
            c.Clear(new SKColor(10, 10, 10));
            using var light = new SKPaint { Color = SKColors.White };
            c.DrawRect(32, 0, 32, 32, light); // right half bright
        }

        new DigitalRainVisionFilter { Intensity = 1f, Sparkles = 0, Glow = 0 }.Apply(bitmap);

        double Green(int x0) => Enumerable.Range(0, 32 * 32).Select(i => (double)bitmap.GetPixel(x0 + i % 32, i / 32).Green).Average();
        double Red(int x0) => Enumerable.Range(0, 32 * 32).Select(i => (double)bitmap.GetPixel(x0 + i % 32, i / 32).Red).Average();
        Assert.True(Green(32) > Green(0) * 2, "the bright half should give much brighter code");
        Assert.True(Green(32) > Red(32), "code is green");
    }

    [Fact]
    public void Digital_rain_finds_what_enters_the_picture_even_at_the_same_brightness()
    {
        // a plain grey room; then a block of the same brightness but another colour (a person in front of a
        // camera): it must become an object, the rest of the room must not
        var rain = new DigitalRainVisionFilter { Glow = 0 };
        SKBitmap Frame(bool person)
        {
            var bmp = new SKBitmap(new SKImageInfo(96, 48, SKColorType.Bgra8888, SKAlphaType.Premul));
            bmp.Erase(new SKColor(120, 120, 120));
            if (person)
            {
                using var c = new SKCanvas(bmp);
                using var paint = new SKPaint { Color = new SKColor(160, 110, 100) };
                c.DrawRect(36, 8, 24, 40, paint);
            }
            return bmp;
        }
        for (var i = 0; i < 10; i++) { using var empty = Frame(false); rain.Apply(empty); Thread.Sleep(20); }
        for (var i = 0; i < 10; i++) { using var present = Frame(true); rain.Apply(present); Thread.Sleep(20); }

        Assert.True(rain.ObjectAt(48, 30) > 0.6f, $"the person should be an object, was {rain.ObjectAt(48, 30)}");
        Assert.True(rain.ObjectAt(6, 30) < 0.2f, $"the empty room should not be, was {rain.ObjectAt(6, 30)}");
    }

    [Fact]
    public void Digital_rain_glyphs_are_neither_empty_nor_blocks()
    {
        foreach (var small in new[] { false, true })
        {
            var glyphs = DigitalRainGlyphs.Build(small);
            var (w, h) = small ? (3, 5) : (5, 7);
            Assert.Equal(DigitalRainGlyphs.Count, glyphs.Length);
            Assert.All(glyphs, g => Assert.InRange(System.Numerics.BitOperations.PopCount(g), 2, w * h - 1));
            Assert.All(glyphs, g => Assert.Equal(0UL, g & ~MaskOf(w, h)));
            Assert.True(glyphs.Distinct().Count() > (small ? 30 : 55), "the set should be varied");
        }
        Assert.InRange(DigitalRainGlyphs.PickTable().Count(i => i < DigitalRainGlyphs.KatakanaCount) * 1.0
                       / DigitalRainGlyphs.PickTable().Length, 0.6, 0.85);

        static ulong MaskOf(int w, int h)
        {
            ulong m = 0;
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) m |= 1UL << (y * 8 + x);
            return m;
        }
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
        foreach (var name in new[] { "Bloom", "RetroCrt", "Glitch", "RetroPalette", "Kaleidoscope", "Afterglow", "DigitalRain" })
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
