using System.Globalization;
using System.Timers;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     A compact multi-entity Home Assistant dashboard: a grid of label + value cells (optionally with icons),
///     reading live state from <see cref="HomeAssistantBridge" />.
/// </summary>
[ExtensionInfo("HA Grid",
    "Home Assistant dashboard: several entities on one canvas",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantGridExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;

    internal HomeAssistantGridExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entities", "The entities to display, in order", Order = 1)]
    public List<HaGridItem> Entities { get; set; } = new();

    [ExtensionParameter("Columns", "Number of columns", DefaultValue = 1, MinValue = 1, MaxValue = 4, Order = 2)]
    public int Columns { get; set; } = 1;

    [ExtensionParameter("Decimals", "Round numeric values to N decimals (-1 = as-is)", DefaultValue = 1,
        MinValue = -1, MaxValue = 4, Order = 3)]
    public int Decimals { get; set; } = 1;

    [ExtensionParameter("Show Icons", "Draw an icon beside each value", DefaultValue = true, Order = 4)]
    public bool ShowIcons { get; set; } = true;

    [ExtensionParameter("Show Labels", "Show the label line in each cell", DefaultValue = true, Order = 5)]
    public bool ShowLabels { get; set; } = true;

    [ExtensionParameter("Show Unit", "Append the unit to the value", DefaultValue = true, Order = 6)]
    public bool ShowUnit { get; set; } = true;

    [ExtensionParameter("BDF Font", "Use the crisp bitmap (BDF) font for which text", DefaultValue = HaBdfMode.None,
        Order = 7)]
    public HaBdfMode BdfFont { get; set; } = HaBdfMode.None;

    [ExtensionParameter("Align", "Horizontal alignment inside each cell", DefaultValue = HaTileAlign.Left, Order = 8)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Left;

    [ExtensionParameter("Value Size", "Value text height in px (0 = auto-fit)", DefaultValue = 0, MinValue = 0,
        MaxValue = 200, Unit = "px", Order = 9)]
    public int ValueSize { get; set; }

    [ExtensionParameter("Label Color", "Colour of the labels", DefaultValue = "#7FB7FF", Order = 10)]
    public SKColor LabelColor { get; set; } = new(127, 183, 255);

    [ExtensionParameter("Value Color", "Colour of the values", DefaultValue = "#FFFFFF", Order = 11)]
    public SKColor ValueColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Color", "Background (alpha 0 for transparent overlay)", DefaultValue = "#FF000000",
        Order = 12)]
    public SKColor BackgroundColor { get; set; } = new(0, 0, 0);

    public string Name => "HA Grid";
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
            catch (Exception ex) { Console.WriteLine($"[HA Grid] render: {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        var items = Entities;
        float w = _canvas.Width, h = _canvas.Height;

        if (items == null || items.Count == 0)
        {
            DrawText(c, HomeAssistantBridge.Connected ? "No entities configured" : "HA offline",
                SKColors.Gray, 0, 0, w, h, Math.Max(8f, 12f * _scale), true, SKTextAlign.Center, false);
            c.Flush();
            _canvas.SubmitCompletedFrame(bb);
            return;
        }

        var cols = Math.Clamp(Columns, 1, 4);
        var rows = (int)Math.Ceiling(items.Count / (float)cols);
        var cellW = w / cols;
        var cellH = h / rows;
        var pad = Math.Max(1f, 2f * _scale);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.EntityId)) continue;

            var col = i % cols;
            var row = i / cols;
            var cx = col * cellW;
            var cy = row * cellH;

            var found = HomeAssistantBridge.TryGet(item.EntityId, out var entity);
            var label = !string.IsNullOrWhiteSpace(item.Label)
                ? item.Label
                : found && !string.IsNullOrWhiteSpace(entity.FriendlyName)
                    ? entity.FriendlyName!
                    : item.EntityId;

            string value;
            if (!HomeAssistantBridge.Connected) value = "—";
            else if (!found) value = "n/a";
            else value = FormatValue(entity.State, found ? entity.Unit : null, item.UnitOverride);

            var labelH = ShowLabels ? Math.Clamp(cellH * 0.34f, 5f, cellH * 0.45f) : 0f;
            var valueH = cellH - labelH - pad * 2;
            var labelBdf = BdfFont is HaBdfMode.Label or HaBdfMode.Both;
            var valueBdf = BdfFont is HaBdfMode.Value or HaBdfMode.Both;
            var textAlign = Align switch
            {
                HaTileAlign.Right => SKTextAlign.Right,
                HaTileAlign.Center => SKTextAlign.Center,
                _ => SKTextAlign.Left
            };

            var ix = cx + pad;
            var iw = cellW - pad * 2;
            if (ShowIcons)
            {
                var iconSize = Math.Min(valueH * 0.9f, cellW * 0.28f);
                var iconCx = cx + pad + iconSize * 0.5f;
                HaIcons.Draw(c, found ? entity.Icon : null, found ? entity.DeviceClass : null,
                    DomainOf(item.EntityId), iconCx, cy + labelH + valueH * 0.5f + pad, iconSize, ValueColor);
                ix = iconCx + iconSize * 0.5f + pad;
                iw = cx + cellW - pad - ix;
            }

            if (ShowLabels)
                DrawText(c, label, LabelColor, cx + pad, cy + pad, cellW - pad * 2, labelH, labelH, true,
                    textAlign, labelBdf);
            var valueTarget = ValueSize > 0 ? ValueSize : valueH;
            DrawText(c, value, ValueColor, ix, cy + labelH + pad, iw, valueH, valueTarget, ValueSize <= 0,
                textAlign, valueBdf);
        }

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private string FormatValue(string raw, string? unit, string? unitOverride)
    {
        var text = raw;
        if (Decimals >= 0 && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            text = num.ToString("F" + Decimals, CultureInfo.InvariantCulture);

        if (ShowUnit)
        {
            var u = !string.IsNullOrWhiteSpace(unitOverride) ? unitOverride : unit;
            if (!string.IsNullOrWhiteSpace(u)) text += " " + u;
        }

        return text;
    }

    private static string DomainOf(string entityId)
    {
        var dot = (entityId ?? "").IndexOf('.');
        return dot > 0 ? entityId![..dot] : "";
    }

    private void DrawText(SKCanvas c, string text, SKColor color, float rx, float ry, float rw, float rh,
        float targetH, bool fit, SKTextAlign align, bool useBdf)
    {
        if (string.IsNullOrEmpty(text) || rw <= 1 || rh <= 1) return;

        if (useBdf)
        {
            var fontName = BdfFontRegistry.GetBestFontForHeight(Math.Max(5, (int)Math.Round(targetH)));
            using var bmp = _canvas.RenderBdfTextToBitmap(text, color, fontName);
            if (bmp is not { Width: > 0, Height: > 0 }) return;
            var scale = fit ? Math.Min(rw / bmp.Width, rh / bmp.Height) : targetH / bmp.Height;
            if (scale <= 0) return;
            var dw = bmp.Width * scale;
            var dh = bmp.Height * scale;
            var left = align == SKTextAlign.Center ? rx + (rw - dw) / 2f
                : align == SKTextAlign.Right ? rx + rw - dw : rx;
            var top = ry + (rh - dh) / 2f;
            c.DrawBitmap(bmp, new SKRect(left, top, left + dw, top + dh));
            return;
        }

        using var font = new SKFont { Size = Math.Max(4f, targetH), Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var tw = font.MeasureText(text);
        if (fit && tw > rw && tw > 0) font.Size *= rw / tw;

        var metrics = font.Metrics;
        var baseline = ry + (rh - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
        var anchorX = align == SKTextAlign.Center ? rx + rw / 2f
            : align == SKTextAlign.Right ? rx + rw : rx;
        c.DrawText(text, anchorX, baseline, align, font, paint);
    }
}
