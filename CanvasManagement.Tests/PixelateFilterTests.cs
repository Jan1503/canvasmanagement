using CanvasManagement.Filters;
using SkiaSharp;

namespace CanvasManagement.Tests;

public class PixelateFilterTests
{
    [Fact]
    public void Apply_returns_source_when_disabled_or_zero_intensity()
    {
        using var source = Solid(4, 4, SKColors.Red);
        var filter = new PixelateFilter { Enabled = false, Intensity = 1f };

        var result = filter.Apply(source);

        Assert.Same(source, result);
        Assert.Equal(SKColors.Red, source.GetPixel(0, 0));

        filter.Enabled = true;
        filter.Intensity = 0;
        Assert.Same(source, filter.Apply(source));
    }

    [Fact]
    public void Apply_fills_each_block_from_the_block_center()
    {
        using var bitmap = new SKBitmap(4, 4);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            bitmap.SetPixel(x, y, x < 2 ? SKColors.Red : SKColors.Blue);

        var filter = new PixelateFilter { Enabled = true, Intensity = 0.04f };
        filter.Apply(bitmap);

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(1, 1));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(2, 0));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(3, 3));
    }

    [Fact]
    public void Apply_with_full_intensity_collapses_a_small_bitmap_to_one_color()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Black);
        bitmap.SetPixel(7, 7, SKColors.Lime);

        var filter = new PixelateFilter { Enabled = true, Intensity = 1f };
        filter.Apply(bitmap);

        Assert.Equal(SKColors.Lime, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(4, 4));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(7, 7));
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return bitmap;
    }
}
