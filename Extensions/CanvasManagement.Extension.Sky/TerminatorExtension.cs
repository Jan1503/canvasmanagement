using System.Globalization;
using System.Runtime.InteropServices;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.Sky;

/// <summary>
///     Live sun terminator on a schematic cylindrical world map (original coastlines, no Earth bitmap).
/// </summary>
[ExtensionInfo("Day/Night Terminator",
    "Live sun terminator on a cylindrical world map",
    "Visual Effects",
    IconResourceName = "terminator.svg")]
public class TerminatorExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private byte[]? _land;
    private int _landW, _landH;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    internal TerminatorExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Location", "City or place — use the search button", DefaultValue = "Berlin", Order = 1)]
    public string Location { get; set; } = "Berlin";

    [ExtensionParameter("Latitude", "Filled by the location picker", DefaultValue = "52.52", Order = 2)]
    public string Latitude { get; set; } = "52.52";

    [ExtensionParameter("Longitude", "Filled by the location picker", DefaultValue = "13.405", Order = 3)]
    public string Longitude { get; set; } = "13.405";

    [ExtensionParameter("Show Marker", "Draw a ping at the chosen place", DefaultValue = true, Order = 4)]
    public bool ShowMarker { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render the place name with the crisp bitmap (BDF) font",
        DefaultValue = false, Order = 5)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "Place-name height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px", Order = 6)]
    public int FontSize { get; set; }

    public string Name => "Day/Night Terminator";
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
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            RebuildLand();
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
            _land = null;
            try { _canvas.Clear(SKColors.Black); }
            catch { }
        }
    }

    private void RebuildLand()
    {
        _landW = _canvas.Width;
        _landH = _canvas.Height;
        _land = TerminatorMap.BuildLand(_landW, _landH);
    }

    private async Task Run(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsRunning)
            {
                lock (_lock)
                {
                    if (_backBuffer != null) Render();
                }

                await Task.Delay(1500, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Render()
    {
        var bb = _backBuffer;
        var land = _land;
        if (bb == null || land == null) return;
        if (_landW != bb.Width || _landH != bb.Height) RebuildLand();
        land = _land!;

        var w = bb.Width;
        var h = bb.Height;
        var utc = DateTime.UtcNow;
        var ptr = bb.GetPixels();
        var row = bb.RowBytes;

        for (var y = 0; y < h; y++)
        {
            var lat = 90.0 - (y + 0.5) / h * 180.0;
            for (var x = 0; x < w; x++)
            {
                var lon = (x + 0.5) / w * 360.0 - 180.0;
                var alt = SolarAltitude(lat, lon, utc);
                var c = TerminatorMap.Shade(land[y * w + x], alt, lat, lon, x, y);
                var o = y * row + x * 4;
                Marshal.WriteByte(ptr, o, c.Blue);
                Marshal.WriteByte(ptr, o + 1, c.Green);
                Marshal.WriteByte(ptr, o + 2, c.Red);
                Marshal.WriteByte(ptr, o + 3, 255);
            }
        }

        bb.NotifyPixelsChanged();

        using var canvas = new SKCanvas(bb);

        using var eq = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 28), IsAntialias = true, StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawLine(0, h * 0.5f, w, h * 0.5f, eq);

        if (ShowMarker &&
            Geo.TryCoord(Latitude, 52.52, out var mlat) &&
            Geo.TryCoord(Longitude, 13.405, out var mlon))
        {
            var mx = (float)((mlon + 180) / 360.0 * w);
            var my = (float)((90 - mlat) / 180.0 * h);
            using var glow = new SKPaint { Color = new SKColor(255, 220, 80, 80), IsAntialias = true };
            using var core = new SKPaint { Color = new SKColor(255, 70, 70), IsAntialias = true };
            using var ring = new SKPaint
            {
                Color = new SKColor(255, 240, 180), IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f
            };
            canvas.DrawCircle(mx, my, 5.5f, glow);
            canvas.DrawCircle(mx, my, 2.2f, core);
            canvas.DrawCircle(mx, my, 3.6f, ring);

            var label = (Location ?? "").Trim();
            if (label.Length > 0)
            {
                var size = CanvasText.ResolveSize(FontSize, Math.Max(7f, h * 0.08f));
                var tx = Math.Clamp(mx + 5, 2, w - 4);
                var ty = Math.Clamp(my - 4, size + 1, h - 2);
                CanvasText.Draw(canvas, _canvas, label, new SKColor(0, 0, 0, 180),
                    tx + 1, ty + 1, size, SKTextAlign.Left, UseBdfFont);
                CanvasText.Draw(canvas, _canvas, label, new SKColor(255, 245, 220),
                    tx, ty, size, SKTextAlign.Left, UseBdfFont);
            }
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private static double SolarAltitude(double lat, double lon, DateTime utc)
    {
        var n = utc.DayOfYear + utc.TimeOfDay.TotalDays;
        var decl = 23.44 * Math.Sin(2 * Math.PI * (n - 81) / 365.0) * Math.PI / 180.0;
        var latr = lat * Math.PI / 180.0;
        var solarTime = utc.TimeOfDay.TotalHours + lon / 15.0;
        var ha = (solarTime - 12) * 15.0 * Math.PI / 180.0;
        var sinAlt = Math.Sin(latr) * Math.Sin(decl) + Math.Cos(latr) * Math.Cos(decl) * Math.Cos(ha);
        return Math.Asin(Math.Clamp(sinAlt, -1, 1)) * 180.0 / Math.PI;
    }
}
