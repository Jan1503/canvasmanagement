using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Ambient weather overlay: animated rain or snow (with wind) and optional lightning flashes drawn on
///     top of whatever content is playing. Particle count/size scale with the panel.
/// </summary>
[FilterInfo("Weather Overlay",
    "Rain or snow with optional lightning, layered over your content",
    "Ambient",
    IconResourceName = "weatheroverlay.svg")]
public class WeatherOverlayFilter : ICanvasFilter
{
    private readonly Random _random = new();
    private readonly List<Drop> _drops = new();
    private int _flash;
    private int _h;
    private int _lightningCooldown;
    private float _scale = 1f;
    private int _w;

    /// <summary>0 = rain, 1 = snow.</summary>
    [FilterParameter("Mode", "0 = rain, 1 = snow", MinValue = 0, MaxValue = 1, DefaultValue = 0)]
    public int Mode { get; set; }

    /// <summary>Sideways wind, -1 (left) .. 1 (right).</summary>
    [FilterParameter("Wind", "Sideways wind (-1 left .. 1 right)", MinValue = -1.0f, MaxValue = 1.0f,
        DefaultValue = 0.2f)]
    public float Wind { get; set; } = 0.2f;

    /// <summary>Lightning frequency (0 = off).</summary>
    [FilterParameter("Lightning", "Lightning frequency (0 = off)", MinValue = 0.0f, MaxValue = 1.0f,
        DefaultValue = 0.0f)]
    public float Lightning { get; set; }

    public string Name => "Weather Overlay";
    public float Intensity { get; set; } = 0.6f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();
        EnsureField(bitmap.Width, bitmap.Height);

        var snow = Mode >= 1;
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { IsAntialias = snow, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, _scale),
            Color = new SKColor(180, 200, 255, 200)
        };

        var baseSpeed = (snow ? 1.2f : 5f) * _scale;

        foreach (var d in _drops)
        {
            d.Y += baseSpeed * d.Speed;
            d.X += Wind * baseSpeed * (snow ? 0.6f : 0.4f) + (snow ? (float)Math.Sin((d.Y + d.Phase) * 0.05) : 0f);

            if (d.Y > _h)
            {
                d.Y = -2;
                d.X = _random.Next(_w);
            }

            if (d.X < 0) d.X += _w;
            else if (d.X > _w) d.X -= _w;

            if (snow)
            {
                paint.Color = new SKColor(255, 255, 255, (byte)(160 + d.Speed * 40));
                canvas.DrawCircle(d.X, d.Y, d.Size, paint);
            }
            else
            {
                var len = d.Size * 4f;
                canvas.DrawLine(d.X, d.Y, d.X - Wind * len, d.Y - len, stroke);
            }
        }

        if (Lightning > 0) UpdateLightning(canvas, bitmap.Width, bitmap.Height);

        return bitmap;
    }

    private void UpdateLightning(SKCanvas canvas, int w, int h)
    {
        if (_flash > 0)
        {
            var a = (byte)(180 * (_flash / 6f) * Intensity);
            using var fp = new SKPaint { Color = new SKColor(255, 255, 255, a) };
            canvas.DrawRect(0, 0, w, h, fp);
            _flash--;
            return;
        }

        if (_lightningCooldown > 0)
        {
            _lightningCooldown--;
            return;
        }

        // Roughly Lightning-scaled chance per frame.
        if (_random.NextDouble() < Lightning * 0.02)
        {
            _flash = 6;
            _lightningCooldown = 40;

            // Draw a jagged bolt.
            using var bolt = new SKPaint
            {
                Color = new SKColor(255, 255, 210),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, 1.5f * _scale),
                IsAntialias = true
            };
            using var path = new SKPath();
            var x = _random.Next((int)(w * 0.2), (int)(w * 0.8));
            float y = 0;
            path.MoveTo(x, y);
            while (y < h * 0.7f)
            {
                y += h * 0.12f;
                x += _random.Next(-(int)(w * 0.08), (int)(w * 0.08));
                path.LineTo(x, y);
            }

            canvas.DrawPath(path, bolt);
        }
    }

    private void EnsureField(int width, int height)
    {
        _scale = DisplayScale.GetScale(width, height);
        var target = (int)(width / 6.0 * (0.4 + Intensity) * (Mode >= 1 ? 0.7 : 1.0));
        target = Math.Clamp(target, 12, 600);

        if (_w == width && _h == height && _drops.Count == target) return;
        _w = width;
        _h = height;

        if (_drops.Count > target) _drops.RemoveRange(target, _drops.Count - target);
        while (_drops.Count < target)
            _drops.Add(new Drop
            {
                X = _random.Next(width),
                Y = _random.Next(height),
                Speed = 0.6f + (float)_random.NextDouble() * 0.9f,
                Size = Math.Max(1f, (0.8f + (float)_random.NextDouble()) * _scale),
                Phase = _random.Next(100)
            });
    }

    private sealed class Drop
    {
        public float X;
        public float Y;
        public float Speed;
        public float Size;
        public int Phase;
    }
}
