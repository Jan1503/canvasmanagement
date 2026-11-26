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
public class TerminatorExtension : FrameExtension
{
    private byte[]? _land;
    private int _landW, _landH;

    internal TerminatorExtension(ICanvas canvas) : base(canvas)
    {
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
        DefaultValue = true, Order = 5)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "Place-name height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px", Order = 6)]
    public int FontSize { get; set; }

    public override string Name => "Day/Night Terminator";

    /// <summary>The terminator moves a pixel every few minutes: a frame every 1.5 s is plenty.</summary>
    protected override double FrameIntervalMs => 1500;

    /// <summary>The land mask is built for the canvas size.</summary>
    protected override void OnResize(int width, int height) => RebuildLand();

    protected override void OnStop() => _land = null;

    private void RebuildLand()
    {
        _landW = Canvas.Width;
        _landH = Canvas.Height;
        _land = TerminatorMap.BuildLand(_landW, _landH);
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        var bb = BackBuffer;
        var land = _land;
        if (bb == null || land == null) return;
        if (_landW != bb.Width || _landH != bb.Height) RebuildLand();
        land = _land!;

        var w = bb.Width;
        var h = bb.Height;
        var utc = DateTime.UtcNow;
        var pixels = MemoryMarshal.Cast<byte, uint>(bb.GetPixelSpan());
        var stride = bb.RowBytes / 4;

        // Solar altitude = asin(sin(lat)·sin(decl) + cos(lat)·cos(decl)·cos(hourAngle)). The declination is the
        // same for the whole frame, the latitude terms per row and the hour angle per column: only the asin is
        // left per pixel (this used to be ~42 ms per frame at 256×128).
        var n = utc.DayOfYear + utc.TimeOfDay.TotalDays;
        var decl = 23.44 * Math.Sin(2 * Math.PI * (n - 81) / 365.0) * Math.PI / 180.0;
        var sinDecl = Math.Sin(decl);
        var cosDecl = Math.Cos(decl);
        var hours = utc.TimeOfDay.TotalHours;
        var lons = new double[w];
        var cosHa = new double[w];
        for (var x = 0; x < w; x++)
        {
            lons[x] = (x + 0.5) / w * 360.0 - 180.0;
            cosHa[x] = Math.Cos((hours + lons[x] / 15.0 - 12) * 15.0 * Math.PI / 180.0);
        }

        for (var y = 0; y < h; y++)
        {
            var lat = 90.0 - (y + 0.5) / h * 180.0;
            var latr = lat * Math.PI / 180.0;
            var a = Math.Sin(latr) * sinDecl;
            var b = Math.Cos(latr) * cosDecl;
            var line = pixels.Slice(y * stride, w);
            for (var x = 0; x < w; x++)
            {
                var alt = Math.Asin(Math.Clamp(a + b * cosHa[x], -1, 1)) * 180.0 / Math.PI;
                var c = TerminatorMap.Shade(land[y * w + x], alt, lat, lons[x], x, y);
                line[x] = (uint)c.WithAlpha(255); // BGRA in memory = ARGB as uint on little-endian
            }
        }

        bb.NotifyPixelsChanged();

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
                CanvasText.Draw(canvas, Canvas, label, new SKColor(0, 0, 0, 180),
                    tx + 1, ty + 1, size, SKTextAlign.Left, UseBdfFont);
                CanvasText.Draw(canvas, Canvas, label, new SKColor(255, 245, 220),
                    tx, ty, size, SKTextAlign.Left, UseBdfFont);
            }
        }
    }

}
