namespace CanvasManagement.Tests;

public class DisplayResizeTests
{
    [Fact]
    public void Resize_raises_DisplayResized_with_new_size()
    {
        using var manager = new CanvasManager(256, 128);
        DisplayResizedEventArgs? args = null;
        manager.DisplayResized += (_, e) => args = e;

        manager.Resize(512, 128);

        Assert.Equal(512, manager.Width);
        Assert.Equal(128, manager.Height);
        Assert.NotNull(args);
        Assert.Equal(512, args!.Width);
        Assert.Equal(128, args.Height);
    }

    [Fact]
    public void Resize_same_size_does_not_raise()
    {
        using var manager = new CanvasManager(256, 128);
        var raised = 0;
        manager.DisplayResized += (_, _) => raised++;

        manager.Resize(256, 128);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Clamp_keeps_a_256_canvas_on_a_512_wall()
    {
        var (x, y, w, h) = DisplayGeometry.Clamp(0, 0, 256, 128, 512, 128);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(256, w);
        Assert.Equal(128, h);
    }

    [Fact]
    public void Clamp_pulls_overflow_back_onto_the_wall()
    {
        var (x, y, w, h) = DisplayGeometry.Clamp(400, -10, 256, 128, 512, 128);
        Assert.Equal(256, x);
        Assert.Equal(0, y);
        Assert.Equal(256, w);
        Assert.Equal(128, h);
    }

    [Fact]
    public void Clamp_shrinks_a_canvas_larger_than_the_wall()
    {
        var (x, y, w, h) = DisplayGeometry.Clamp(0, 0, 800, 400, 512, 128);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(512, w);
        Assert.Equal(128, h);
    }
}
