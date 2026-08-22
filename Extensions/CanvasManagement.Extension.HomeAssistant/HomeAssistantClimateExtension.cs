using System.Globalization;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     Current vs target temperature as an arc, coloured by <c>hvac_action</c>.
/// </summary>
[ExtensionInfo("HA Climate",
    "Thermostat arc from a Home Assistant climate entity",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantClimateExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;

    internal HomeAssistantClimateExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entity ID", "climate.* entity", DefaultValue = "climate.living_room", Order = 1)]
    public string EntityId { get; set; } = "climate.living_room";

    [ExtensionParameter("Label", "Custom title (empty = hide)", DefaultValue = "", Order = 2)]
    public string Label { get; set; } = "";

    [ExtensionParameter("Show Label", "Show the title above the current temperature", DefaultValue = false,
        Order = 3)]
    public bool ShowLabel { get; set; }

    [ExtensionParameter("Min °C", "Arc start (usually 5–10)", DefaultValue = 10, MinValue = 0, MaxValue = 25,
        Order = 4)]
    public int MinC { get; set; } = 10;

    [ExtensionParameter("Max °C", "Arc end (usually 28–35)", DefaultValue = 30, MinValue = 20, MaxValue = 40,
        Order = 5)]
    public int MaxC { get; set; } = 30;

    [ExtensionParameter("Decimals", "Decimals on the current temperature", DefaultValue = 1, MinValue = 0,
        MaxValue = 2, Order = 6)]
    public int Decimals { get; set; } = 1;

    [ExtensionParameter("Use BDF Font", "Render with the crisp bitmap (BDF) font", DefaultValue = false, Order = 7)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Align", "Horizontal alignment of the numbers", DefaultValue = HaTileAlign.Center, Order = 8)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Center;

    [ExtensionParameter("Value Size", "Current-temperature text height in px (0 = auto-fit)", DefaultValue = 0,
        MinValue = 0, MaxValue = 200, Unit = "px", Order = 9)]
    public int ValueSize { get; set; }

    [ExtensionParameter("Value Color", "Colour of the current temperature", DefaultValue = "#FFFFFF", Order = 10)]
    public SKColor ValueColor { get; set; } = SKColors.White;

    [ExtensionParameter("Label Color", "Colour of the setpoint / action line", DefaultValue = "#AAB4BE", Order = 11)]
    public SKColor LabelColor { get; set; } = new(170, 180, 190);

    [ExtensionParameter("Background Color", "Background (alpha 0 for overlay)", DefaultValue = "#0C1014", Order = 12)]
    public SKColor BackgroundColor { get; set; } = new(12, 16, 20);

    public string Name => "HA Climate";
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
            catch (Exception ex) { Console.WriteLine($"[HA Climate] {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        float w = _canvas.Width, h = _canvas.Height;
        var found = HomeAssistantBridge.TryGet(EntityId, out var entity);
        var action = HomeAssistantBridge.Attr(EntityId, "hvac_action") ?? (found ? entity.State : "");
        double current = double.NaN, target = double.NaN;
        if (HomeAssistantBridge.TryAttrDouble(EntityId, "current_temperature", out var cur)) current = cur;
        else if (found && double.TryParse(entity.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            current = s;
        if (HomeAssistantBridge.TryAttrDouble(EntityId, "temperature", out var set)) target = set;

        var color = action switch
        {
            "heating" => new SKColor(255, 120, 50),
            "cooling" => new SKColor(70, 190, 255),
            "drying" => new SKColor(180, 160, 80),
            "fan" => new SKColor(140, 200, 180),
            _ => new SKColor(160, 170, 180)
        };

        var cx = w * 0.5f;
        var cy = h * 0.58f;
        var r = Math.Min(w, h) * 0.42f;
        var sw = Math.Max(4f, r * 0.16f);
        var oval = new SKRect(cx - r, cy - r, cx + r, cy + r);
        const float start = 200f, sweep = 140f;
        var min = Math.Min(MinC, MaxC);
        var max = Math.Max(MinC, MaxC);
        float Frac(double t) => (float)Math.Clamp((t - min) / Math.Max(1, max - min), 0, 1);

        using var track = new SKPaint
        {
            Color = new SKColor(40, 48, 56), IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = sw, StrokeCap = SKStrokeCap.Round
        };
        using var fill = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = sw, StrokeCap = SKStrokeCap.Round
        };
        using var mark = new SKPaint
        {
            Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(2f, sw * 0.25f), StrokeCap = SKStrokeCap.Round
        };

        c.DrawArc(oval, start, sweep, false, track);
        if (!double.IsNaN(current))
            c.DrawArc(oval, start, sweep * Frac(current), false, fill);
        if (!double.IsNaN(target))
        {
            var a = (start + sweep * Frac(target)) * MathF.PI / 180f;
            var x1 = cx + MathF.Cos(a) * (r - sw * 0.7f);
            var y1 = cy + MathF.Sin(a) * (r - sw * 0.7f);
            var x2 = cx + MathF.Cos(a) * (r + sw * 0.55f);
            var y2 = cy + MathF.Sin(a) * (r + sw * 0.55f);
            c.DrawLine(x1, y1, x2, y2, mark);
        }

        var align = HaText.ToSk(Align);
        var fmt = "F" + Math.Clamp(Decimals, 0, 2);
        var main = !HomeAssistantBridge.Connected ? "HA offline"
            : double.IsNaN(current) ? "n/a"
            : current.ToString(fmt, CultureInfo.InvariantCulture) + "°";
        var title = !string.IsNullOrWhiteSpace(Label)
            ? Label
            : found && !string.IsNullOrWhiteSpace(entity.FriendlyName) ? entity.FriendlyName! : "";
        if (ShowLabel && !string.IsNullOrWhiteSpace(title))
            HaText.Draw(c, _canvas, title, LabelColor, 0, h * 0.04f, w, h * 0.16f, h * 0.12f, align, UseBdfFont);

        var tempH = ValueSize > 0 ? ValueSize : h * 0.22f;
        HaText.Draw(c, _canvas, main, ValueColor, 0, h * 0.28f, w, h * 0.28f, tempH, align, UseBdfFont);
        var sub = double.IsNaN(target)
            ? action
            : $"soll {target.ToString(fmt, CultureInfo.InvariantCulture)}° · {action}";
        HaText.Draw(c, _canvas, sub, LabelColor, 0, h * 0.72f, w, h * 0.2f, h * 0.12f, align, UseBdfFont);

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }
}
