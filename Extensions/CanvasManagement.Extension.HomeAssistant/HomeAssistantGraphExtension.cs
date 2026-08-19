using System.Globalization;
using System.Timers;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     A history graph for one Home Assistant numeric entity (e.g. a temperature trend). Live samples come
///     from <see cref="HomeAssistantBridge" />; the host also seeds the backlog from the HA History API.
/// </summary>
[ExtensionInfo("HA Graph",
    "Home Assistant history graph for a numeric entity (temperature, power, ...)",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantGraphExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private float _scale = 1f;
    private Timer? _timer;

    internal HomeAssistantGraphExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entity ID", "Numeric Home Assistant entity to plot", DefaultValue = "", Order = 1)]
    public string EntityId { get; set; } = "";

    [ExtensionParameter("Label", "Title (empty = entity friendly name)", DefaultValue = "", Order = 2)]
    public string Label { get; set; } = "";

    [ExtensionParameter("Show Title", "Show the title line", DefaultValue = true, Order = 3)]
    public bool ShowTitle { get; set; } = true;

    [ExtensionParameter("Show Current", "Show the current value", DefaultValue = true, Order = 4)]
    public bool ShowCurrent { get; set; } = true;

    [ExtensionParameter("Show Min/Max", "Show the min and max over the window", DefaultValue = true, Order = 5)]
    public bool ShowMinMax { get; set; } = true;

    [ExtensionParameter("Decimals", "Decimals for the labels", DefaultValue = 1, MinValue = 0, MaxValue = 3,
        Order = 6)]
    public int Decimals { get; set; } = 1;

    [ExtensionParameter("Fill", "Fill the area under the line", DefaultValue = true, Order = 7)]
    public bool Fill { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render labels with the crisp bitmap (BDF) font", DefaultValue = false,
        Order = 8)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Line Color", "Colour of the graph line", DefaultValue = "#41BDF5", Order = 9)]
    public SKColor LineColor { get; set; } = new(65, 189, 245);

    [ExtensionParameter("Label Color", "Colour of the labels", DefaultValue = "#C8D2E6", Order = 10)]
    public SKColor LabelColor { get; set; } = new(200, 210, 230);

    [ExtensionParameter("Background Color", "Background (alpha 0 for transparent overlay)", DefaultValue = "#FF0C1420",
        Order = 11)]
    public SKColor BackgroundColor { get; set; } = new(12, 20, 32);

    public string Name => "HA Graph";
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
            catch (Exception ex) { Console.WriteLine($"[HA Graph] render: {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        float w = _canvas.Width, h = _canvas.Height;
        var pad = Math.Max(2f, 3f * _scale);

        HomeAssistantBridge.RequestHistory(EntityId);
        var found = HomeAssistantBridge.TryGet(EntityId, out var entity);
        var samples = HomeAssistantBridge.GetHistory(EntityId);
        var unit = found ? entity.Unit : null;

        var title = !string.IsNullOrWhiteSpace(Label)
            ? Label
            : found && !string.IsNullOrWhiteSpace(entity.FriendlyName)
                ? entity.FriendlyName!
                : EntityId;

        var headerH = ShowTitle || ShowCurrent ? Math.Clamp(h * 0.2f, 6f, h * 0.35f) : 0f;

        if (ShowTitle && !string.IsNullOrWhiteSpace(title))
            DrawText(c, title, LabelColor, pad, pad, w * 0.62f, headerH, headerH * 0.9f, SKTextAlign.Left);

        if (ShowCurrent && samples.Length > 0)
        {
            var cur = samples[^1].Value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(unit)) cur += " " + unit;
            DrawText(c, cur, LineColor, w * 0.4f, pad, w * 0.6f - pad, headerH, headerH * 0.95f, SKTextAlign.Right);
        }

        var graphRect = new SKRect(pad, headerH + pad, w - pad, h - pad);

        if (samples.Length < 2)
        {
            var msg = !HomeAssistantBridge.Connected ? "HA offline" :
                !found ? "n/a" : "collecting…";
            DrawText(c, msg, SKColors.Gray, pad, graphRect.Top, w - pad * 2, graphRect.Height,
                Math.Max(8f, 12f * _scale), SKTextAlign.Center);
            c.Flush();
            _canvas.SubmitCompletedFrame(bb);
            return;
        }

        var fillColor = new SKColor(LineColor.Red, LineColor.Green, LineColor.Blue, 60);
        HaGraph.Draw(c, samples, graphRect, LineColor, fillColor, Math.Max(1f, 1.5f * _scale), Fill);

        if (ShowMinMax)
        {
            var range = HaGraph.Range(samples);
            if (range.HasValue)
            {
                var lblH = Math.Max(5f, h * 0.13f);
                DrawText(c, range.Value.Max.ToString("F" + Decimals, CultureInfo.InvariantCulture), LabelColor,
                    pad, graphRect.Top, w * 0.4f, lblH, lblH, SKTextAlign.Left);
                DrawText(c, range.Value.Min.ToString("F" + Decimals, CultureInfo.InvariantCulture), LabelColor,
                    pad, graphRect.Bottom - lblH, w * 0.4f, lblH, lblH, SKTextAlign.Left);
            }
        }

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawText(SKCanvas c, string text, SKColor color, float rx, float ry, float rw, float rh,
        float targetH, SKTextAlign align)
    {
        if (string.IsNullOrEmpty(text) || rw <= 1 || rh <= 1) return;

        if (UseBdfFont)
        {
            var fontName = BdfFontRegistry.GetBestFontForHeight(Math.Max(5, (int)Math.Round(targetH)));
            using var bmp = _canvas.RenderBdfTextToBitmap(text, color, fontName);
            if (bmp is not { Width: > 0, Height: > 0 }) return;
            var scale = Math.Min(rw / bmp.Width, rh / bmp.Height);
            if (scale <= 0) return;
            var dw = bmp.Width * scale;
            var dh = bmp.Height * scale;
            var left = align == SKTextAlign.Right ? rx + rw - dw : align == SKTextAlign.Center ? rx + (rw - dw) / 2f : rx;
            var top = ry + (rh - dh) / 2f;
            c.DrawBitmap(bmp, new SKRect(left, top, left + dw, top + dh));
            return;
        }

        using var font = new SKFont { Size = Math.Max(4f, targetH), Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var tw = font.MeasureText(text);
        if (tw > rw && tw > 0) font.Size *= rw / tw;
        var metrics = font.Metrics;
        var baseline = ry + (rh - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
        var anchorX = align == SKTextAlign.Right ? rx + rw : align == SKTextAlign.Center ? rx + rw / 2f : rx;
        c.DrawText(text, anchorX, baseline, align, font, paint);
    }
}
