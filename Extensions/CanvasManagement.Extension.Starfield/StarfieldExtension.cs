using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.Starfield;

[ExtensionInfo("Starfield",
    "Classic 3D starfield effect with parallax scrolling stars",
    "Visual Effects",
    IconResourceName = "starfield.svg")]
public class StarfieldExtension : FrameExtension
{
    private readonly Random _random = new();
    private readonly List<Star> _stars = new();
    private readonly SKPaint _starPaint = new() { Style = SKPaintStyle.Fill };

    internal StarfieldExtension(ICanvas canvas) : base(canvas)
    {
    }

    [ExtensionParameter("Background Color", "Background color for the starfield",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Star Count", "Number of stars in the field",
        DefaultValue = 200, MinValue = 50, MaxValue = 500)]
    public int StarCount { get; set; } = 200;

    [ExtensionParameter("Min Speed", "Minimum star speed",
        DefaultValue = 1, MinValue = 1, MaxValue = 5)]
    public int MinSpeed { get; set; } = 1;

    [ExtensionParameter("Max Speed", "Maximum star speed",
        DefaultValue = 3, MinValue = 2, MaxValue = 10)]
    public int MaxSpeed { get; set; } = 3;

    [ExtensionParameter("Colored Stars", "Use rainbow colors for stars",
        DefaultValue = false)]
    public bool ColoredStars { get; set; } = false;

    public override string Name => "Starfield";

    protected override double FrameIntervalMs => 33;

    protected override bool ClearOnStop => false;

    /// <summary>Stars are added by the first frame (Star Count).</summary>
    protected override void OnStart() => _stars.Clear();

    protected override void OnStop()
    {
        _stars.Clear();
        try { Canvas.Clear(); } catch { /* canvas already gone */ }
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        var centerX = width / 2f;
        var centerY = height / 2f;

        // Apply Star Count changes live.
        while (_stars.Count < StarCount)
            _stars.Add(new Star
            {
                X = _random.Next(Canvas.Width),
                Y = _random.Next(Canvas.Height),
                Z = _random.Next(1, 20),
                Speed = SpeedRange(),
                Hue = ColoredStars ? _random.Next(360) : 0
            });
        if (_stars.Count > StarCount)
            _stars.RemoveRange(StarCount, _stars.Count - StarCount);

        // Clear with background color
        canvas.Clear(BackgroundColor);

        foreach (var star in _stars)
        {
            // Update star position (moving outward from center)
            var dx = star.X - centerX;
            var dy = star.Y - centerY;

            star.X += dx / star.Z * star.Speed;
            star.Y += dy / star.Z * star.Speed;
            star.Z -= 0.1f;

            // Reset star when it goes off screen or too close
            if (star.X < 0 || star.X > Canvas.Width ||
                star.Y < 0 || star.Y > Canvas.Height ||
                star.Z < 1)
            {
                star.X = centerX + _random.Next(-10, 10);
                star.Y = centerY + _random.Next(-10, 10);
                star.Z = 20;
                star.Speed = SpeedRange();
                if (ColoredStars)
                    star.Hue = _random.Next(360);
            }

            // Draw star (size based on depth)
            var size = (int)(5 - star.Z / 4);
            if (size < 1) size = 1;

            SKColor color;
            if (ColoredStars)
            {
                var brightness = (byte)Math.Max(0, Math.Min(255, 255 - star.Z * 10));
                color = SKColor.FromHsl(star.Hue, 100, brightness / 255f * 50);
            }
            else
            {
                var brightness = (byte)Math.Max(0, Math.Min(255, 255 - star.Z * 10));
                color = new SKColor(brightness, brightness, brightness);
            }

            // Apply canvas opacity to star color
            color = new SKColor(color.Red, color.Green, color.Blue,
                (byte)(Canvas.Opacity * 255));

            _starPaint.Color = color;
            canvas.DrawRect((int)star.X, (int)star.Y, size, size, _starPaint);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _starPaint.Dispose();
    }

    /// <summary>Random speed in [MinSpeed, MaxSpeed] (inclusive) that is safe when the two are equal.</summary>
    private int SpeedRange()
    {
        var lo = Math.Max(1, MinSpeed);
        var hi = Math.Max(lo + 1, MaxSpeed + 1);
        return _random.Next(lo, hi);
    }

    private class Star
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int Speed { get; set; }
        public int Hue { get; set; }
    }
}