using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Tests;

/// <summary>Effects on one canvas: applied to that canvas only, per frame, never accumulating.</summary>
public class CanvasEffectsTests
{
    /// <summary>Inverts RGB of every pixel (counts its calls).</summary>
    private sealed class InvertFilter : ICanvasFilter
    {
        public string Name => "Invert";
        public float Intensity { get; set; } = 1f;
        public bool Enabled { get; set; } = true;
        public int Calls;

        public SKBitmap Apply(SKBitmap source, bool inPlace = true)
        {
            Calls++;
            for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                source.SetPixel(x, y, new SKColor((byte)(255 - c.Red), (byte)(255 - c.Green), (byte)(255 - c.Blue), c.Alpha));
            }
            return source;
        }
    }

    private sealed class ThrowingFilter : ICanvasFilter
    {
        public string Name => "Broken";
        public float Intensity { get; set; } = 1f;
        public bool Enabled { get; set; } = true;
        public SKBitmap Apply(SKBitmap source, bool inPlace = true) => throw new InvalidOperationException("boom");
    }

    private static readonly SKColor Red = new(255, 0, 0);
    private static readonly SKColor Cyan = new(0, 255, 255);

    [Fact]
    public void Effect_changes_only_its_own_canvas()
    {
        using var manager = new CanvasManager(32, 16);
        var left = manager.GetCanvas(0, 0, 16, 16, 0, "left");
        var right = manager.GetCanvas(16, 0, 16, 16, 1, "right");
        left.Clear(Red);
        right.Clear(Red);
        right.SetFilters([new InvertFilter()]);

        manager.ComposeFrame();

        Assert.Equal(Red, manager.CompositeBitmap.GetPixel(4, 4));
        Assert.Equal(Cyan, manager.CompositeBitmap.GetPixel(20, 4));
    }

    [Fact]
    public void Effect_applies_on_the_single_full_screen_fast_path()
    {
        using var manager = new CanvasManager(32, 16);
        var main = manager.GetCanvas(0, "Main");
        main.Clear(Red);
        main.SetFilters([new InvertFilter()]);

        manager.ComposeFrame();

        Assert.Equal(Cyan, manager.CompositeBitmap.GetPixel(10, 10));
    }

    [Fact]
    public void Effect_runs_on_the_frame_copy_and_does_not_accumulate()
    {
        using var manager = new CanvasManager(32, 16);
        var main = manager.GetCanvas(0, "Main");
        main.Clear(Red);
        var invert = new InvertFilter();
        main.SetFilters([invert]);

        manager.ComposeFrame();
        manager.ComposeFrame();
        manager.ComposeFrame();

        // Inverting the drawing buffer itself would flip back and forth every frame.
        Assert.Equal(3, invert.Calls);
        Assert.Equal(Cyan, manager.CompositeBitmap.GetPixel(0, 0));
    }

    [Fact]
    public void Disabled_or_zero_intensity_effects_are_skipped()
    {
        using var manager = new CanvasManager(32, 16);
        var main = manager.GetCanvas(0, "Main");
        main.Clear(Red);
        var off = new InvertFilter { Enabled = false };
        var zero = new InvertFilter { Intensity = 0f };
        main.SetFilters([off, zero]);

        manager.ComposeFrame();

        Assert.Equal(0, off.Calls);
        Assert.Equal(0, zero.Calls);
        Assert.Equal(Red, manager.CompositeBitmap.GetPixel(0, 0));
    }

    [Fact]
    public void A_throwing_effect_is_switched_off_and_the_frame_still_composes()
    {
        using var manager = new CanvasManager(32, 16);
        var main = manager.GetCanvas(0, "Main");
        main.Clear(Red);
        var broken = new ThrowingFilter();
        var invert = new InvertFilter();
        main.SetFilters([broken, invert]);

        manager.ComposeFrame();

        Assert.False(broken.Enabled);
        Assert.Equal(Cyan, manager.CompositeBitmap.GetPixel(0, 0));
    }

    [Fact]
    public void Hidden_canvases_do_not_run_their_effects()
    {
        using var manager = new CanvasManager(32, 16);
        var main = manager.GetCanvas(0, "Main");
        var invert = new InvertFilter();
        main.SetFilters([invert]);
        main.Hide();

        manager.ComposeFrame();

        Assert.Equal(0, invert.Calls);
    }

    [Fact]
    public void SetFilters_replaces_the_chain_and_an_empty_one_clears_it()
    {
        using var manager = new CanvasManager(32, 16);
        var main = manager.GetCanvas(0, "Main");
        var a = new InvertFilter();
        var b = new InvertFilter();

        main.SetFilters([a, b]);
        Assert.Equal([a, b], main.Filters);

        main.SetFilters([]);
        Assert.Empty(main.Filters);
    }
}
