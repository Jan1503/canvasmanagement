using SkiaSharp;

namespace CanvasManagement.Tests;

public class CanvasPixelTests
{
    [Fact]
    public void SetPixel_round_trips_through_GetPixel()
    {
        using var manager = new CanvasManager(8, 8);
        var canvas = manager.GetCanvas(0, "pixels");

        canvas.SetPixel(3, 5, SKColors.Magenta);
        Assert.Equal(SKColors.Magenta, canvas.GetPixel(3, 5));
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void Brightness_clamps_to_zero_one(float set, float expected)
    {
        using var manager = new CanvasManager(8, 8);
        var canvas = manager.GetCanvas(0, "bright");

        canvas.Brightness = set;

        Assert.Equal(expected, canvas.Brightness);
    }
}
