using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extensions.Default;

[ExtensionInfo("Plasma",
    "Animated plasma effect with flowing colors and patterns",
    "Visual Effects",
    IconResourceName = "plasma.svg")]
public class PlasmaExtension : FrameExtension
{
    private readonly SKPaint _paint = new() { Style = SKPaintStyle.Fill };
    private SKColor _backgroundColor = SKColors.Black;
    private double _time;

    internal PlasmaExtension(ICanvas canvas) : base(canvas)
    {
    }

    [ExtensionParameter("Resolution", "Pixel block size (higher = faster but less detail)",
        DefaultValue = 3, MinValue = 1, MaxValue = 10)]
    public int Resolution { get; set; } = 3;

    [ExtensionParameter("Speed", "Animation speed",
        DefaultValue = 0.03, MinValue = 0.01, MaxValue = 0.2)]
    public double Speed { get; set; } = 0.03;

    [ExtensionParameter("Color Shift", "Color range in degrees (360 = full rainbow)",
        DefaultValue = 180, MinValue = 60, MaxValue = 360)]
    public int ColorShift { get; set; } = 180;

    [ExtensionParameter("Background Color", "Background color for plasma",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }

    public override string Name => "Plasma";

    protected override double FrameIntervalMs => 33;

    protected override bool ClearOnStop => false;

    protected override void OnStart() => _time = 0;

    protected override void OnStop()
    {
        try { Canvas.Clear(); } catch { /* canvas already gone */ }
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        // Background (supports transparency, so plasma can sit on a layer below).
        if (_backgroundColor.Alpha == 255)
        {
            canvas.Clear(_backgroundColor);
        }
        else
        {
            canvas.Clear(SKColors.Transparent);
            if (_backgroundColor.Alpha > 0)
            {
                _paint.Color = _backgroundColor;
                canvas.DrawRect(0, 0, width, height, _paint);
            }
        }

        // Wave divisors scale with the panel so the pattern keeps a similar feature count on any size
        // (no change at the 384x192 reference).
        var sc = DisplayScale.GetScale(width, height);
        var d16 = 16.0 * sc;
        var d8 = 8.0 * sc;
        var res = Math.Max(1, Resolution);
        for (var y = 0; y < height; y += res)
        for (var x = 0; x < width; x += res)
        {
            double value = 0;
            value += Math.Sin(x / d16 + _time);
            value += Math.Sin(y / d8 - _time);
            value += Math.Sin((x + y) / d16);
            value += Math.Sin(Math.Sqrt(x * x + y * y) / d8 + _time);
            value /= 4.0;

            var hue = (float)((value + 1) * ColorShift + _time * 30) % 360;
            _paint.Color = SKColor.FromHsl(hue, 100, 50);
            canvas.DrawRect(x, y, res, res, _paint);
        }

        _time += Speed;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _paint.Dispose();
    }
}
