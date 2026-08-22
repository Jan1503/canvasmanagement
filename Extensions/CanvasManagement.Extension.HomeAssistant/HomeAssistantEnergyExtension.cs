using System.Globalization;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     Live house / solar / grid / battery power as a ring (or a split bar). Reads numeric HA sensors;
///     typical German setups use W or kW on <c>sensor.*_power</c>.
/// </summary>
[ExtensionInfo("HA Energy",
    "House / solar / grid / battery power as a ring (Home Assistant sensors)",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantEnergyExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;

    internal HomeAssistantEnergyExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("House Entity", "House load / consumption (pick a power sensor)",
        DefaultValue = "", Order = 1)]
    public string HouseEntity { get; set; } = "";

    [ExtensionParameter("Solar Entity", "PV production (pick a power sensor)",
        DefaultValue = "", Order = 2)]
    public string SolarEntity { get; set; } = "";

    [ExtensionParameter("Grid Entity", "Grid import (+) / export (−) (pick a power sensor)",
        DefaultValue = "", Order = 3)]
    public string GridEntity { get; set; } = "";

    [ExtensionParameter("Invert Grid", "Flip grid sign (use when the sensor is export-positive)",
        DefaultValue = false, Order = 4)]
    public bool InvertGrid { get; set; }

    [ExtensionParameter("Battery Entity", "Battery charge/discharge (optional, pick a power sensor)",
        DefaultValue = "", Order = 5)]
    public string BatteryEntity { get; set; } = "";

    [ExtensionParameter("House Label", "Override label for house load (empty = house)", DefaultValue = "", Order = 6)]
    public string HouseLabel { get; set; } = "";

    [ExtensionParameter("Solar Label", "Override label for PV (empty = PV)", DefaultValue = "", Order = 7)]
    public string SolarLabel { get; set; } = "";

    [ExtensionParameter("Grid Label", "Override label for grid (empty = grid in / grid out)", DefaultValue = "",
        Order = 8)]
    public string GridLabel { get; set; } = "";

    [ExtensionParameter("Battery Label", "Override label for battery (empty = batt)", DefaultValue = "", Order = 9)]
    public string BatteryLabel { get; set; } = "";

    [ExtensionParameter("Split Bar", "Horizontal bars instead of a ring", DefaultValue = false, Order = 10)]
    public bool SplitBar { get; set; }

    [ExtensionParameter("Use BDF Font", "Render with the crisp bitmap (BDF) font", DefaultValue = false, Order = 11)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Align", "Horizontal alignment of the centre value", DefaultValue = HaTileAlign.Center,
        Order = 12)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Center;

    [ExtensionParameter("Value Size", "Centre / value text height in px (0 = auto-fit)", DefaultValue = 0,
        MinValue = 0, MaxValue = 200, Unit = "px", Order = 13)]
    public int ValueSize { get; set; }

    [ExtensionParameter("Value Color", "Colour of the numbers", DefaultValue = "#FFFFFF", Order = 14)]
    public SKColor ValueColor { get; set; } = SKColors.White;

    [ExtensionParameter("Label Color", "Colour of the labels", DefaultValue = "#96A5B4", Order = 15)]
    public SKColor LabelColor { get; set; } = new(150, 165, 180);

    [ExtensionParameter("Solar Color", "PV / production colour", DefaultValue = "#F0BE28", Order = 16)]
    public SKColor SolarColor { get; set; } = new(240, 190, 40);

    [ExtensionParameter("Grid Import Color", "Colour when importing from the grid", DefaultValue = "#FF5A32",
        Order = 17)]
    public SKColor GridImportColor { get; set; } = new(255, 90, 50);

    [ExtensionParameter("Grid Export Color", "Colour when exporting to the grid", DefaultValue = "#46AAFF",
        Order = 18)]
    public SKColor GridExportColor { get; set; } = new(70, 170, 255);

    [ExtensionParameter("House Color", "House-load colour", DefaultValue = "#B4C8D2", Order = 19)]
    public SKColor HouseColor { get; set; } = new(180, 200, 210);

    [ExtensionParameter("Battery Color", "Battery colour", DefaultValue = "#50DC8C", Order = 20)]
    public SKColor BatteryColor { get; set; } = new(80, 220, 140);

    [ExtensionParameter("Background Color", "Background (alpha 0 for overlay)",
        DefaultValue = "#FF0A1218", Order = 21)]
    public SKColor BackgroundColor { get; set; } = new(10, 18, 24);

    public string Name => "HA Energy";
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
            _scale = DisplayScale.GetScale(_canvas.Width, _canvas.Height);
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            _timer = new Timer(1000) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); }
            catch { }
        }
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try { Render(); }
            catch (Exception ex) { Console.WriteLine($"[HA Energy] {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        float w = _canvas.Width, h = _canvas.Height;
        var house = ReadWatts(HouseEntity);
        var solar = ReadWatts(SolarEntity);
        var grid = ReadWatts(GridEntity);
        if (InvertGrid) grid = -grid;
        var battery = ReadWatts(BatteryEntity);
        var max = Math.Max(1000, Math.Max(Math.Abs(house), Math.Max(Math.Abs(solar),
            Math.Max(Math.Abs(grid), Math.Abs(battery)))));

        if (!HomeAssistantBridge.Connected)
        {
            HaText.Draw(c, _canvas, "HA offline", LabelColor, 0, 0, w, h, Math.Max(8f, h * 0.2f),
                HaText.ToSk(Align), UseBdfFont);
            c.Flush();
            _canvas.SubmitCompletedFrame(bb);
            return;
        }

        if (SplitBar) DrawBars(c, w, h, house, solar, grid, battery, max);
        else DrawRing(c, w, h, house, solar, grid, battery, max);

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawRing(SKCanvas c, float w, float h, double house, double solar, double grid, double battery,
        double max)
    {
        var cx = w * 0.38f;
        var cy = h * 0.52f;
        var r = Math.Min(w * 0.34f, h * 0.42f);
        var sw = Math.Max(3f, r * 0.18f);

        using var track = new SKPaint
        {
            Color = new SKColor(40, 50, 58), IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = sw, StrokeCap = SKStrokeCap.Round
        };
        using var solarP = new SKPaint
        {
            Color = SolarColor, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = sw, StrokeCap = SKStrokeCap.Round
        };
        using var gridP = new SKPaint
        {
            Color = grid >= 0 ? GridImportColor : GridExportColor,
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = sw * 0.55f, StrokeCap = SKStrokeCap.Round
        };

        var oval = new SKRect(cx - r, cy - r, cx + r, cy + r);
        c.DrawArc(oval, -90, 360, false, track);
        c.DrawArc(oval, -90, (float)(360 * Math.Clamp(solar / max, 0, 1)), false, solarP);
        var inner = oval;
        inner.Inflate(-sw * 0.85f, -sw * 0.85f);
        c.DrawArc(inner, -90, (float)(360 * Math.Clamp(Math.Abs(grid) / max, 0, 1)), false, gridP);

        var centreH = ValueSize > 0 ? ValueSize : r * 0.32f;
        var align = HaText.ToSk(Align);
        HaText.Draw(c, _canvas, Fmt(house), ValueColor,
            cx - r, cy - r * 0.22f, r * 2, r * 0.4f, centreH, align, UseBdfFont);
        HaText.Draw(c, _canvas, NameOr(HouseLabel, "house"), LabelColor,
            cx - r, cy + r * 0.08f, r * 2, r * 0.22f, r * 0.16f, align, UseBdfFont);

        var listX = w * 0.68f;
        var rowH = h / 4f;
        DrawLegend(c, listX, 0, w - listX, rowH, SolarColor, NameOr(SolarLabel, "PV"), Fmt(solar));
        DrawLegend(c, listX, rowH, w - listX, rowH,
            grid >= 0 ? GridImportColor : GridExportColor,
            GridName(grid), Fmt(Math.Abs(grid)));
        DrawLegend(c, listX, rowH * 2, w - listX, rowH, HouseColor, NameOr(HouseLabel, "house"), Fmt(house));
        if (!string.IsNullOrWhiteSpace(BatteryEntity))
            DrawLegend(c, listX, rowH * 3, w - listX, rowH, BatteryColor, NameOr(BatteryLabel, "batt"), Fmt(battery));
    }

    private void DrawBars(SKCanvas c, float w, float h, double house, double solar, double grid, double battery,
        double max)
    {
        var rows = string.IsNullOrWhiteSpace(BatteryEntity) ? 3 : 4;
        var rowH = h / rows;
        DrawBar(c, 0, 0, w, rowH, solar, max, SolarColor, NameOr(SolarLabel, "PV"), Fmt(solar));
        DrawBar(c, 0, rowH, w, rowH, Math.Abs(grid), max,
            grid >= 0 ? GridImportColor : GridExportColor,
            GridName(grid), Fmt(Math.Abs(grid)));
        DrawBar(c, 0, rowH * 2, w, rowH, house, max, HouseColor, NameOr(HouseLabel, "house"), Fmt(house));
        if (rows == 4)
            DrawBar(c, 0, rowH * 3, w, rowH, Math.Abs(battery), max, BatteryColor, NameOr(BatteryLabel, "batt"),
                Fmt(battery));
    }

    private void DrawLegend(SKCanvas c, float x, float y, float w, float h, SKColor color, string label, string value)
    {
        using var dot = new SKPaint { Color = color, IsAntialias = true };
        var r = Math.Max(2f, h * 0.12f);
        c.DrawCircle(x + r * 2, y + h * 0.5f, r, dot);
        HaText.Draw(c, _canvas, label, LabelColor, x + r * 4, y, w * 0.4f, h * 0.45f, h * 0.28f,
            SKTextAlign.Left, UseBdfFont);
        HaText.Draw(c, _canvas, value, ValueColor, x + r * 4, y + h * 0.4f, w - r * 5, h * 0.5f, h * 0.36f,
            SKTextAlign.Left, UseBdfFont);
    }

    private void DrawBar(SKCanvas c, float x, float y, float w, float h, double value, double max, SKColor color,
        string label, string text)
    {
        var pad = Math.Max(2f, 3f * _scale);
        using var track = new SKPaint { Color = new SKColor(40, 50, 58), IsAntialias = true };
        using var fill = new SKPaint { Color = color, IsAntialias = true };
        var barY = y + h * 0.55f;
        var barH = Math.Max(3f, h * 0.28f);
        var barW = w - pad * 2;
        c.DrawRect(x + pad, barY, barW, barH, track);
        c.DrawRect(x + pad, barY, (float)(barW * Math.Clamp(value / max, 0, 1)), barH, fill);
        HaText.Draw(c, _canvas, label, LabelColor, x + pad, y, w * 0.4f, h * 0.5f, h * 0.32f,
            SKTextAlign.Left, UseBdfFont);
        HaText.Draw(c, _canvas, text, ValueColor, x + w * 0.4f, y, w * 0.6f - pad, h * 0.5f, h * 0.36f,
            SKTextAlign.Right, UseBdfFont);
    }

    private string GridName(double grid)
    {
        if (!string.IsNullOrWhiteSpace(GridLabel)) return GridLabel;
        return grid >= 0 ? "grid in" : "grid out";
    }

    private static string NameOr(string overrideLabel, string fallback)
    {
        return string.IsNullOrWhiteSpace(overrideLabel) ? fallback : overrideLabel;
    }

    private static double ReadWatts(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId) || !HomeAssistantBridge.TryGet(entityId, out var st)) return 0;
        if (!double.TryParse(st.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
        var unit = (st.Unit ?? "").ToLowerInvariant();
        if (unit.Contains("kw")) v *= 1000;
        return v;
    }

    private static string Fmt(double watts)
    {
        var a = Math.Abs(watts);
        if (a >= 1000) return $"{watts / 1000:0.0} kW";
        return $"{watts:0} W";
    }
}
