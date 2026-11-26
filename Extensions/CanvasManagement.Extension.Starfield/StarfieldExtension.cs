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
    private readonly SKPaint _starPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = false };

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

    /// <summary>
    ///     Flight through a field of stars: each star has a place in space (x, y in -1..1, depth z in 0..1)
    ///     and comes towards the viewer; its screen position is the perspective projection, so stars
    ///     spread from the centre and speed up as they come close. Near stars are bigger and brighter
    ///     and draw a short streak from where they were.
    /// </summary>
    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        var cx = width / 2f;
        var cy = height / 2f;
        var focal = Math.Max(width, height) * 0.5f;

        // Apply Star Count changes live; the first stars are spread over all depths.
        while (_stars.Count < StarCount)
            _stars.Add(NewStar((float)_random.NextDouble()));
        if (_stars.Count > StarCount)
            _stars.RemoveRange(StarCount, _stars.Count - StarCount);

        canvas.Clear(BackgroundColor);
        var alpha = (byte)(Canvas.Opacity * 255);

        foreach (var star in _stars)
        {
            var (px, py) = Project(star, cx, cy, focal);
            star.Z -= star.Speed * 0.12f * dt;
            var (sx, sy) = Project(star, cx, cy, focal);
            if (star.Z <= 0.02f || sx < -2 || sx > width + 2 || sy < -2 || sy > height + 2)
            {
                var fresh = NewStar(1f);
                star.X = fresh.X;
                star.Y = fresh.Y;
                star.Z = fresh.Z;
                star.Speed = fresh.Speed;
                star.Hue = fresh.Hue;
                continue;
            }

            // Nearness 0 (far) .. 1 (at the viewer): size, brightness and streak follow it.
            var near = 1f - star.Z;
            var brightness = (byte)Math.Clamp(40 + 215 * near * near, 0, 255);
            var color = ColoredStars
                ? SKColor.FromHsl(star.Hue, 100, 20 + 40 * near)
                : new SKColor(brightness, brightness, (byte)Math.Min(255, brightness + 20));
            color = color.WithAlpha(alpha);

            if (near > 0.45f)
            {
                _starPaint.Color = color.WithAlpha((byte)(alpha * 0.45f));
                _starPaint.StrokeWidth = 1;
                canvas.DrawLine(px, py, sx, sy, _starPaint);
            }

            var size = near > 0.85f ? 2 : 1;
            _starPaint.Color = color;
            canvas.DrawRect((int)sx, (int)sy, size, size, _starPaint);
        }
    }

    private static (float X, float Y) Project(Star s, float cx, float cy, float focal) =>
        (cx + s.X / s.Z * focal * 0.5f, cy + s.Y / s.Z * focal * 0.5f);

    private Star NewStar(float z) => new()
    {
        X = (float)(_random.NextDouble() * 2 - 1),
        Y = (float)(_random.NextDouble() * 2 - 1),
        Z = Math.Max(0.05f, z),
        Speed = SpeedRange(),
        Hue = ColoredStars ? _random.Next(360) : 0
    };

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