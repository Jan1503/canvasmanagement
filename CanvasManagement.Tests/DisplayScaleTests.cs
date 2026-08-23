using CanvasManagement.Interfaces;

namespace CanvasManagement.Tests;

public class DisplayScaleTests
{
    [Theory]
    [InlineData(0, 0, 1f)]
    [InlineData(-1, 192, 1f)]
    [InlineData(384, 192, 1f)]
    [InlineData(256, 128, 256f / 384f)]
    [InlineData(128, 64, 128f / 384f)]
    public void GetScale_fits_the_more_constrained_axis(int width, int height, float expected)
    {
        Assert.Equal(expected, DisplayScale.GetScale(width, height), precision: 5);
    }

    [Theory]
    [InlineData(384, 192, 24, 24)]
    [InlineData(256, 128, 24, 16)]
    [InlineData(128, 64, 24, 8)]
    public void ScaleSize_converts_design_pixels(int width, int height, float design, int expected)
    {
        using var manager = new CanvasManager(width, height);
        var canvas = manager.GetCanvas(0, "scale");
        Assert.Equal(expected, canvas.ScaleSize(design));
    }

    [Fact]
    public void ScaleSize_on_zero_size_uses_identity_scale_and_never_returns_zero()
    {
        using var main = new SkiaSharp.SKBitmap(1, 1);
        using var canvas = new Canvas(main, 0, 0, 0, 0, "zero");
        Assert.Equal(1f, canvas.Scale());
        Assert.Equal(24, canvas.ScaleSize(24));
        Assert.Equal(1, canvas.ScaleSize(0.1f));
    }
}
