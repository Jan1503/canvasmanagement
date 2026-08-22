using System.Globalization;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

public enum HaDepartTimeStyle { Minutes, Clock }

/// <summary>
///     Next public-transport departures from a Home Assistant sensor (HVV / HAFAS / RMV / similar).
///     Reads a <c>departures</c> (or <c>next</c>) attribute list and draws HVV-coloured line badges.
/// </summary>
[ExtensionInfo("HA Departures",
    "HVV / HAFAS departure board: line badge, destination, countdown",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantDeparturesExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;

    internal HomeAssistantDeparturesExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entity ID",
        "HA sensor with a departures/next list (HVV, HAFAS, RMV, …)",
        DefaultValue = "sensor.hvv", Order = 1)]
    public string EntityId { get; set; } = "sensor.hvv";

    [ExtensionParameter("Station", "Header (empty = entity friendly name)", DefaultValue = "", Order = 2)]
    public string Station { get; set; } = "";

    [ExtensionParameter("Show Station", "Draw the station name as a header", DefaultValue = true, Order = 3)]
    public bool ShowStation { get; set; } = true;

    [ExtensionParameter("Max Rows", "How many departures to show", DefaultValue = 4, MinValue = 1, MaxValue = 10,
        Order = 4)]
    public int MaxRows { get; set; } = 4;

    [ExtensionParameter("Time Style", "Countdown in minutes, or clock time", DefaultValue = HaDepartTimeStyle.Minutes,
        Order = 5)]
    public HaDepartTimeStyle TimeStyle { get; set; } = HaDepartTimeStyle.Minutes;

    [ExtensionParameter("Show Delay", "Show +N min when delayed", DefaultValue = true, Order = 6)]
    public bool ShowDelay { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render with the crisp bitmap (BDF) font", DefaultValue = false, Order = 7)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Align", "Horizontal alignment of the destination", DefaultValue = HaTileAlign.Left, Order = 8)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Left;

    [ExtensionParameter("Value Size", "Text height in px (0 = auto-fit)", DefaultValue = 0, MinValue = 0,
        MaxValue = 200, Unit = "px", Order = 9)]
    public int ValueSize { get; set; }

    [ExtensionParameter("Value Color", "Colour of the countdown / time", DefaultValue = "#FFFFFF", Order = 10)]
    public SKColor ValueColor { get; set; } = SKColors.White;

    [ExtensionParameter("Label Color", "Colour of the destination", DefaultValue = "#D2DCE6", Order = 11)]
    public SKColor LabelColor { get; set; } = new(210, 220, 230);

    [ExtensionParameter("Delay Color", "Colour of the delay suffix", DefaultValue = "#FF8A4A", Order = 12)]
    public SKColor DelayColor { get; set; } = new(255, 138, 74);

    [ExtensionParameter("Background Color", "Background (alpha 0 for overlay)", DefaultValue = "#101418", Order = 13)]
    public SKColor BackgroundColor { get; set; } = new(16, 20, 24);

    public string Name => "HA Departures";
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
            try { Render(); }
            catch (Exception ex) { Console.WriteLine($"[HA Departures] render: {ex.Message}"); }
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
            catch (Exception ex) { Console.WriteLine($"[HA Departures] {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        float w = _canvas.Width, h = _canvas.Height;
        var pad = Math.Max(2f, Math.Min(w, h) * 0.03f);
        var header = ShowStation ? (string.IsNullOrWhiteSpace(Station)
            ? (HomeAssistantBridge.TryGet(EntityId, out var st) ? st.FriendlyName ?? st.EntityId : "")
            : Station) : "";
        var headerH = string.IsNullOrWhiteSpace(header) ? 0f : Math.Max(10f, h * 0.18f);

        var rows = HaDepartures.FromEntity(EntityId)
            .Where(d => d.When >= DateTime.Now.AddMinutes(-1))
            .OrderBy(d => d.When)
            .Take(Math.Clamp(MaxRows, 1, 10))
            .ToList();

        if (headerH > 0)
        {
            HaText.Draw(c, _canvas, header!, LabelColor, pad, 0, w - pad * 2, headerH,
                ValueSize > 0 ? ValueSize * 0.85f : headerH * 0.7f, HaText.ToSk(Align), UseBdfFont,
                shrinkToWidth: false);
        }

        if (rows.Count == 0)
        {
            var msg = HomeAssistantBridge.Connected ? "No departures" : "HA offline";
            HaText.Draw(c, _canvas, msg, LabelColor, pad, headerH, w - pad * 2, Math.Max(12f, h - headerH),
                Math.Max(10f, (h - headerH) * 0.28f), HaText.ToSk(Align), UseBdfFont, shrinkToWidth: false);
            c.Flush();
            _canvas.SubmitCompletedFrame(bb);
            return;
        }

        var bodyH = Math.Max(12f, h - headerH);
        var rowH = bodyH / rows.Count;
        var badgeH = Math.Max(10f, rowH * 0.72f);
        var textH = ValueSize > 0 ? ValueSize : Math.Max(8f, rowH * 0.42f);
        var align = HaText.ToSk(Align);

        for (var i = 0; i < rows.Count; i++)
        {
            var d = rows[i];
            var y = headerH + i * rowH;
            var badgeY = y + (rowH - badgeH) / 2f;
            var badgeW = HaTransitIcons.DrawBadge(c, d.Line, d.Product, pad, badgeY, badgeH);
            var x = pad + badgeW + pad;
            var dest = string.IsNullOrWhiteSpace(d.Direction) ? d.Line : d.Direction;
            if (d.Cancelled) dest = "fällt aus";

            var timeText = FormatTime(d);
            var timeW = Math.Max(28f, w * 0.22f);
            var destW = Math.Max(8f, w - x - timeW - pad);
            var destColor = d.Cancelled ? DelayColor : LabelColor;
            HaText.Draw(c, _canvas, dest, destColor, x, y, destW, rowH, textH, align, UseBdfFont,
                shrinkToWidth: false);

            var timeColor = d.Cancelled ? DelayColor : ValueColor;
            HaText.Draw(c, _canvas, timeText, timeColor, w - pad - timeW, y, timeW, rowH, textH,
                SKTextAlign.Right, UseBdfFont, shrinkToWidth: false);

            if (ShowDelay && !d.Cancelled && d.DelayMin >= 2)
            {
                var delay = $"+{d.DelayMin}";
                HaText.Draw(c, _canvas, delay, DelayColor, w - pad - timeW, y + rowH * 0.55f, timeW, rowH * 0.4f,
                    Math.Max(6f, textH * 0.7f), SKTextAlign.Right, UseBdfFont, shrinkToWidth: false);
            }
        }

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private string FormatTime(HaDeparture d)
    {
        if (d.Cancelled) return "—";
        if (TimeStyle == HaDepartTimeStyle.Clock)
            return d.When.ToString("HH:mm", CultureInfo.InvariantCulture);
        var mins = (int)Math.Round((d.When - DateTime.Now).TotalMinutes);
        if (mins <= 0) return "now";
        return mins < 60 ? $"{mins} min" : d.When.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
