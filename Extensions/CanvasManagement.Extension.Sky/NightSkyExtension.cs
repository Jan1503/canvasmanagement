using System.Globalization;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.Sky;

/// <summary>
///     Night sky: stars, milky way, moon phase, optional ISS (open-notify, no key).
/// </summary>
[ExtensionInfo("Night Sky",
    "Stars and moon phase; optional ISS position",
    "Visual Effects",
    IconResourceName = "night-sky.svg")]
public class NightSkyExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _rng = new(7);
    private SKBitmap? _backBuffer;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Star[] _stars = [];
    private double _issLat = double.NaN, _issLon = double.NaN;
    private DateTime _issFetch = DateTime.MinValue;

    internal NightSkyExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Location", "Observer city — use the search button", DefaultValue = "Berlin", Order = 1)]
    public string Location { get; set; } = "Berlin";

    [ExtensionParameter("Latitude", "Filled by the location picker", DefaultValue = "52.52", Order = 2)]
    public string Latitude { get; set; } = "52.52";

    [ExtensionParameter("Longitude", "Filled by the location picker", DefaultValue = "13.405", Order = 3)]
    public string Longitude { get; set; } = "13.405";

    [ExtensionParameter("Show ISS", "Show the ISS when it is above the horizon", DefaultValue = true,
        Order = 4)]
    public bool ShowIss { get; set; } = true;

    [ExtensionParameter("Star Count", "Number of stars", DefaultValue = 180, MinValue = 20, MaxValue = 500, Order = 5)]
    public int StarCount { get; set; } = 180;

    [ExtensionParameter("Background Color", "Zenith colour", DefaultValue = "#050816", Order = 6)]
    public SKColor BackgroundColor { get; set; } = new(5, 8, 22);

    [ExtensionParameter("Use BDF Font", "Render labels with the crisp bitmap (BDF) font", DefaultValue = false,
        Order = 7)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "Label height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px", Order = 8)]
    public int FontSize { get; set; }

    public string Name => "Night Sky";
    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            SeedStars();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            IsRunning = true;
            _loop = Task.Run(() => Run(ct));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); }
            catch { }
        }
    }

    private async Task Run(CancellationToken ct)
    {
        var frame = 0;
        try
        {
            while (!ct.IsCancellationRequested && IsRunning)
            {
                lock (_lock)
                {
                    if (_backBuffer != null) Render(frame);
                }

                if (ShowIss && (DateTime.UtcNow - _issFetch).TotalSeconds > 20)
                    _ = FetchIss();

                frame++;
                await Task.Delay(80, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SeedStars()
    {
        var n = Math.Clamp(StarCount, 20, 500);
        _stars = new Star[n];
        for (var i = 0; i < n; i++)
        {
            var mag = (float)Math.Pow(_rng.NextDouble(), 2.2);
            var tint = _rng.NextDouble();
            SKColor col = tint < 0.12 ? new SKColor(180, 210, 255)
                : tint > 0.88 ? new SKColor(255, 214, 170)
                : new SKColor(236, 240, 255);
            _stars[i] = new Star(
                (float)_rng.NextDouble() * _canvas.Width,
                (float)_rng.NextDouble() * _canvas.Height,
                0.25f + mag * 1.7f,
                (float)_rng.NextDouble() * 6.28f,
                col,
                0.35f + mag * 0.65f);
        }
    }

    private void Render(int frame)
    {
        var bb = _backBuffer;
        if (bb == null) return;
        if (_stars.Length != Math.Clamp(StarCount, 20, 500)) SeedStars();

        using var c = new SKCanvas(bb);
        DrawSkyGradient(c, bb.Width, bb.Height);
        DrawMilkyWay(c, bb.Width, bb.Height);
        DrawStars(c, frame);

        Geo.TryCoord(Latitude, 52.52, out var lat);
        Geo.TryCoord(Longitude, 13.405, out var lon);
        var moonR = Math.Min(bb.Width, bb.Height) * 0.18f;
        var (mx, my) = MoonPos(bb.Width, bb.Height, lat, lon);
        DrawMoon(c, mx, my, moonR);

        if (ShowIss && !double.IsNaN(_issLat))
            DrawIss(c, bb.Width, bb.Height, lat, lon);

        var label = (Location ?? "").Trim();
        if (label.Length > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(7f, bb.Height * 0.09f));
            CanvasText.Draw(c, _canvas, label, new SKColor(200, 210, 230, 180),
                4, bb.Height - 4, size, SKTextAlign.Left, UseBdfFont);
        }

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawSkyGradient(SKCanvas c, int w, int h)
    {
        var zenith = BackgroundColor;
        var horizon = new SKColor(
            (byte)Math.Min(255, zenith.Red + 28),
            (byte)Math.Min(255, zenith.Green + 18),
            (byte)Math.Min(255, zenith.Blue + 8));
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, h),
            [zenith, horizon],
            [0f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        c.DrawRect(0, 0, w, h, paint);
    }

    private static void DrawMilkyWay(SKCanvas c, int w, int h)
    {
        c.Save();
        c.Translate(w * 0.5f, h * 0.45f);
        c.RotateDegrees(-28);
        using var blurBand = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(4, w * 0.04f));
        using var blurCore = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(3, w * 0.02f));
        using var band = new SKPaint
        {
            Color = new SKColor(170, 185, 230, 28), IsAntialias = true, MaskFilter = blurBand
        };
        using var core = new SKPaint
        {
            Color = new SKColor(230, 220, 255, 40), IsAntialias = true, MaskFilter = blurCore
        };
        var bw = w * 1.3f;
        var bh = h * 0.22f;
        c.DrawOval(new SKRect(-bw * 0.5f, -bh * 0.5f, bw * 0.5f, bh * 0.5f), band);
        c.DrawOval(new SKRect(-bw * 0.35f, -bh * 0.18f, bw * 0.35f, bh * 0.18f), core);
        c.Restore();
    }

    private void DrawStars(SKCanvas c, int frame)
    {
        using var star = new SKPaint { IsAntialias = true };
        foreach (var s in _stars)
        {
            var tw = 0.55f + 0.45f * (float)(0.5 + 0.5 * Math.Sin(frame * 0.06 + s.Phase));
            var a = (byte)Math.Clamp(40 + 215 * s.Bright * tw, 0, 255);
            star.Color = new SKColor(s.Color.Red, s.Color.Green, s.Color.Blue, a);
            c.DrawCircle(s.X, s.Y, s.R * tw, star);
            if (s.R > 1.4f)
            {
                star.Color = new SKColor(s.Color.Red, s.Color.Green, s.Color.Blue, (byte)(a * 0.35));
                c.DrawCircle(s.X, s.Y, s.R * tw * 2.1f, star);
            }
        }
    }

    private static (float x, float y) MoonPos(int w, int h, double lat, double lon)
    {
        var phase = MoonPhase();
        var x = w * (0.22f + (float)((lon + 180) / 360.0) * 0.55f);
        var y = h * (0.18f + (float)((90 - lat) / 180.0) * 0.22f);
        x = Math.Clamp(x, w * 0.18f, w * 0.82f);
        y = Math.Clamp(y, h * 0.16f, h * 0.45f);
        // Drift slightly with phase so it doesn't sit glued to one pixel.
        x += (float)((phase - 0.5) * w * 0.06);
        return (x, y);
    }

    private static void DrawMoon(SKCanvas c, float cx, float cy, float r)
    {
        var phase = MoonPhase(); // 0 new → 0.5 full → 1 new
        using var layer = new SKPaint { IsAntialias = true };
        c.Save();
        using var clip = new SKPath();
        clip.AddCircle(cx, cy, r);
        c.ClipPath(clip, SKClipOperation.Intersect, true);

        using var disc = new SKPaint { Color = new SKColor(236, 230, 210), IsAntialias = true };
        c.DrawCircle(cx, cy, r, disc);

        using var mare = new SKPaint { Color = new SKColor(190, 186, 168), IsAntialias = true };
        c.DrawCircle(cx - r * 0.25f, cy - r * 0.1f, r * 0.28f, mare);
        c.DrawCircle(cx + r * 0.18f, cy + r * 0.22f, r * 0.18f, mare);
        c.DrawCircle(cx + r * 0.05f, cy - r * 0.32f, r * 0.12f, mare);

        // Shadow disc: offset so the lit fraction matches the synodic phase.
        var k = (float)((phase - 0.5) * 2); // -1 new-from-left … 0 full … +1 new-from-right
        using var shade = new SKPaint { Color = new SKColor(8, 10, 22), IsAntialias = true };
        c.DrawCircle(cx + k * r * 1.15f, cy, r, shade);
        c.Restore();

        using var rim = new SKPaint
        {
            Color = new SKColor(255, 250, 230, 50), IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, r * 0.06f)
        };
        c.DrawCircle(cx, cy, r, rim);
    }

    private void DrawIss(SKCanvas c, int w, int h, double obsLat, double obsLon)
    {
        var dlat = _issLat - obsLat;
        var dlon = _issLon - obsLon;
        if (dlon > 180) dlon -= 360;
        if (dlon < -180) dlon += 360;
        // Rough "above horizon" if within ~70° of the observer.
        if (Math.Abs(dlat) > 70 || Math.Abs(dlon) > 70) return;

        var x = w * 0.5f + (float)(dlon / 140.0) * w * 0.5f;
        var y = h * 0.5f - (float)(dlat / 80.0) * h * 0.5f;
        if (x < 2 || y < 2 || x > w - 2 || y > h - 2) return;

        using var glow = new SKPaint { Color = new SKColor(255, 120, 90, 70), IsAntialias = true };
        using var core = new SKPaint { Color = new SKColor(255, 230, 210), IsAntialias = true };
        c.DrawCircle(x, y, 5.5f, glow);
        c.DrawCircle(x, y, 1.8f, core);
        var size = CanvasText.ResolveSize(FontSize, Math.Max(6f, h * 0.07f));
        CanvasText.Draw(c, _canvas, "ISS", new SKColor(255, 180, 140),
            x + 4, y - 2, size, SKTextAlign.Left, UseBdfFont);
    }

    private static double MoonPhase()
    {
        var days = (DateTime.UtcNow - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays;
        var p = days / 29.530588;
        return p - Math.Floor(p);
    }

    private async Task FetchIss()
    {
        _issFetch = DateTime.UtcNow;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var json = await http.GetStringAsync("http://api.open-notify.org/iss-now.json");
            var lat = Slice(json, "\"latitude\"");
            var lon = Slice(json, "\"longitude\"");
            if (double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var la) &&
                double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
            {
                _issLat = la;
                _issLon = lo;
            }
        }
        catch
        {
            // tracker optional
        }
    }

    private static string Slice(string json, string key)
    {
        var i = json.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return "";
        var colon = json.IndexOf(':', i);
        if (colon < 0) return "";
        var q1 = json.IndexOf('"', colon);
        var q2 = q1 < 0 ? -1 : json.IndexOf('"', q1 + 1);
        if (q1 < 0 || q2 < 0) return json[(colon + 1)..].Trim().Trim(',', '}', ' ');
        return json[(q1 + 1)..q2];
    }

    private readonly record struct Star(float X, float Y, float R, float Phase, SKColor Color, float Bright);
}
