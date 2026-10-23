using CanvasManagement.Filters;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Tests;

public class FilterSafetyTests
{
    [Theory]
    [InlineData(64, 32)]
    [InlineData(65, 33)]
    public void OilPainting_does_not_detach_the_canvas(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);

        new OilPaintingFilter { Intensity = 1f, BrushRadius = 2 }.Apply(bitmap);

        canvas.Clear(SKColors.Red);

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(width - 1, height - 1));
    }

    [Fact]
    public void Blur_does_not_detach_the_canvas()
    {
        using var bitmap = new SKBitmap(32, 16);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);

        new BlurFilter { Intensity = 0.4f }.Apply(bitmap);

        canvas.Clear(SKColors.Green);

        Assert.Equal(SKColors.Green, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Green, bitmap.GetPixel(31, 15));
    }

    [Fact]
    public void Animated_filters_expose_a_speed_control()
    {
        ICanvasFilter[] filters =
        [
            new ChristmasFilter(),
            new HalloweenFilter(),
            new ValentineFilter(),
            new NewYearFilter(),
            new MatrixOverlayFilter(),
            new MatrixTransformFilter(),
            new MatrixCodeRainFilter(),
            new NeoCodeVisionFilter(),
            new WeatherOverlayFilter()
        ];

        foreach (var filter in filters)
        {
            var speed = filter.GetType().GetProperty("PlaybackSpeed");
            Assert.NotNull(speed);
            Assert.Equal(1f, speed!.GetValue(filter));
        }
    }
}
