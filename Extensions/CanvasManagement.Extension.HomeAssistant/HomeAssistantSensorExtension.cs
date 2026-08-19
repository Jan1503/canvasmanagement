using System.Globalization;
using System.Timers;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

public enum HaTileAlign { Left, Center, Right }

public enum HaWarnMode { Off, Above, Below, Outside }

public enum HaBdfMode { None, Value, Label, Both }

/// <summary>
///     Shows a single Home Assistant entity (label + value + unit) as a tile. The host's
///     HomeAssistantService keeps the live state in <see cref="HomeAssistantBridge" />; this extension only
///     reads it, so no token is needed here. Auto-fits to any panel and supports a transparent background for
///     layering over other content.
/// </summary>
[ExtensionInfo("HA Sensor",
    "Show a Home Assistant entity value (requires HomeAssistant enabled in appsettings)",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantSensorExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;

    internal HomeAssistantSensorExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entity ID", "Home Assistant entity, e.g. sensor.og_wohnzimmer_fernseher_energy",
        DefaultValue = "sensor.og_wohnzimmer_fernseher_energy", Order = 1)]
    public string EntityId { get; set; } = "sensor.og_wohnzimmer_fernseher_energy";

    [ExtensionParameter("Label", "Custom label (empty = use the entity's friendly name)", DefaultValue = "",
        Order = 2)]
    public string Label { get; set; } = "";

    [ExtensionParameter("Show Label", "Show the label line above the value", DefaultValue = true, Order = 3)]
    public bool ShowLabel { get; set; } = true;

    [ExtensionParameter("Unit Override", "Custom unit (empty = use the entity's unit)", DefaultValue = "", Order = 4)]
    public string UnitOverride { get; set; } = "";

    [ExtensionParameter("Show Unit", "Append the unit to the value", DefaultValue = true, Order = 5)]
    public bool ShowUnit { get; set; } = true;

    [ExtensionParameter("Decimals", "Round a numeric value to N decimals (-1 = leave as-is)", DefaultValue = -1,
        MinValue = -1, MaxValue = 4, Order = 6)]
    public int Decimals { get; set; } = -1;

    [ExtensionParameter("Value Size", "Value text height in px (0 = auto-fit)", DefaultValue = 0, MinValue = 0,
        MaxValue = 200, Unit = "px", Order = 7)]
    public int ValueSize { get; set; }

    [ExtensionParameter("BDF Font", "Use the crisp bitmap (BDF) font for which text", DefaultValue = HaBdfMode.None,
        Order = 8)]
    public HaBdfMode BdfFont { get; set; } = HaBdfMode.None;

    [ExtensionParameter("Align", "Horizontal alignment", DefaultValue = HaTileAlign.Center, Order = 9)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Center;

    [ExtensionParameter("Value Color", "Colour of the value", DefaultValue = "#FFFFFF", Order = 10)]
    public SKColor ValueColor { get; set; } = SKColors.White;

    [ExtensionParameter("Label Color", "Colour of the label/unit", DefaultValue = "#7FB7FF", Order = 11)]
    public SKColor LabelColor { get; set; } = new(127, 183, 255);

    [ExtensionParameter("Background Color", "Background (use alpha 0 for a transparent overlay)",
        DefaultValue = "#FF000000", Order = 12)]
    public SKColor BackgroundColor { get; set; } = new(0, 0, 0);

    [ExtensionParameter("Show Icon", "Draw an icon (from the entity's mdi icon / domain) beside the value",
        DefaultValue = true, Order = 13)]
    public bool ShowIcon { get; set; } = true;

    [ExtensionParameter("Binary Badge", "Colour on/off-style entities with the on/off colour", DefaultValue = true,
        Order = 14)]
    public bool BinaryBadge { get; set; } = true;

    [ExtensionParameter("Off Color", "Colour for the 'off' state of binary entities", DefaultValue = "#FF606060",
        Order = 15)]
    public SKColor OffColor { get; set; } = new(96, 96, 96);

    [ExtensionParameter("Warn Mode", "Colour the value when a numeric reading crosses a limit",
        DefaultValue = HaWarnMode.Off, Order = 16)]
    public HaWarnMode WarnMode { get; set; } = HaWarnMode.Off;

    [ExtensionParameter("Warn Above", "Upper limit number for Warn Mode (e.g. 500); empty = unset",
        DefaultValue = "", Order = 17)]
    public string WarnAbove { get; set; } = "";

    [ExtensionParameter("Warn Below", "Lower limit number for Warn Mode; empty = unset", DefaultValue = "", Order = 18)]
    public string WarnBelow { get; set; } = "";

    [ExtensionParameter("Warn Color", "Colour used when the warn limit is crossed", DefaultValue = "#FFFF4030",
        Order = 19)]
    public SKColor WarnColor { get; set; } = new(255, 64, 48);

    [ExtensionParameter("Show Age", "Show how long ago the value last changed (e.g. '2m ago')",
        DefaultValue = false, Order = 20)]
    public bool ShowAge { get; set; }

    [ExtensionParameter("State Map", "Remap displayed states, e.g. 'on=ON;off=OFF;home=Home'", DefaultValue = "",
        Order = 21)]
    public string StateMap { get; set; } = "";

    [ExtensionParameter("Show Graph", "Draw a history sparkline behind the value (numeric entities)",
        DefaultValue = false, Order = 22)]
    public bool ShowGraph { get; set; }

    [ExtensionParameter("Graph Color", "Colour of the history sparkline", DefaultValue = "#553FA7FF", Order = 23)]
    public SKColor GraphColor { get; set; } = new(63, 167, 255, 85);

    public string Name => "HA Sensor";
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
            catch (Exception ex) { Console.WriteLine($"[HA Sensor] render: {ex.Message}"); }
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

        // History sparkline behind the content (registers interest so the host seeds + buffers it).
        if (ShowGraph)
        {
            HomeAssistantBridge.RequestHistory(EntityId);
            var hist = HomeAssistantBridge.GetHistory(EntityId);
            var fill = new SKColor(GraphColor.Red, GraphColor.Green, GraphColor.Blue, (byte)(GraphColor.Alpha / 3));
            HaGraph.Draw(c, hist, new SKRect(pad, h * 0.45f, w - pad, h - pad), GraphColor, fill,
                Math.Max(1f, _scale), true);
        }

        var found = HomeAssistantBridge.TryGet(EntityId, out var entity);

        var label = !string.IsNullOrWhiteSpace(Label)
            ? Label
            : found && !string.IsNullOrWhiteSpace(entity.FriendlyName)
                ? entity.FriendlyName!
                : EntityId;

        // Value text + colour (numeric thresholds and binary on/off colouring).
        string valueText;
        var valueColor = ValueColor;
        var numeric = found && IsNumeric(entity.State);
        var isBinary = false;

        if (!HomeAssistantBridge.Connected)
        {
            valueText = "HA offline";
            valueColor = SKColors.Gray;
        }
        else if (!found)
        {
            valueText = "n/a";
            valueColor = SKColors.Gray;
        }
        else if (numeric)
        {
            valueText = FormatValue(entity.State);
            if (ShowUnit)
            {
                var unit = !string.IsNullOrWhiteSpace(UnitOverride) ? UnitOverride : entity.Unit;
                if (!string.IsNullOrWhiteSpace(unit)) valueText += " " + unit;
            }

            valueColor = ResolveWarnColor(double.Parse(entity.State, NumberStyles.Float, CultureInfo.InvariantCulture));
        }
        else
        {
            valueText = MapState(entity.State);
            if (TryBinary(entity.State, out var on))
            {
                isBinary = true;
                if (BinaryBadge) valueColor = on ? ValueColor : OffColor;
            }
        }

        var iconColor = isBinary && BinaryBadge ? valueColor : ValueColor;
        var labelBdf = BdfFont is HaBdfMode.Label or HaBdfMode.Both;
        var valueBdf = BdfFont is HaBdfMode.Value or HaBdfMode.Both;

        // Layout: label (top) / value row (middle, optional icon) / age (bottom).
        var showLabel = ShowLabel && !string.IsNullOrWhiteSpace(label);
        var labelH = showLabel ? Math.Clamp(h * 0.22f, 6f, h * 0.4f) : 0f;
        var ageText = ShowAge && found && entity.LastChangedUtc.HasValue ? FormatAge(entity.LastChangedUtc.Value) : null;
        var ageH = ageText != null ? Math.Clamp(h * 0.16f, 5f, h * 0.3f) : 0f;
        var gapTop = showLabel ? Math.Max(1f, h * 0.04f) : 0f;

        var midY = pad + labelH + gapTop;
        var midH = h - midY - ageH - pad;

        if (showLabel)
            DrawText(c, label, LabelColor, pad, pad, w - pad * 2, labelH, labelH, true, labelBdf);

        var rowX = pad;
        var rowW = w - pad * 2;
        if (ShowIcon)
        {
            var iconSize = Math.Min(midH * 0.9f, rowW * 0.3f);
            var iconCx = pad + iconSize * 0.5f;
            HaIcons.Draw(c, found ? entity.Icon : null, found ? entity.DeviceClass : null, DomainOf(EntityId),
                iconCx, midY + midH * 0.5f, iconSize, iconColor);
            rowX = iconCx + iconSize * 0.5f + pad;
            rowW = w - rowX - pad;
        }

        var valueTargetH = ValueSize > 0 ? ValueSize : midH;
        DrawText(c, valueText, valueColor, rowX, midY, rowW, midH, valueTargetH, ValueSize <= 0, valueBdf);

        if (ageText != null)
            DrawText(c, ageText, LabelColor, pad, h - ageH - pad, w - pad * 2, ageH, ageH, true, labelBdf);

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private string MapState(string raw)
    {
        if (string.IsNullOrWhiteSpace(StateMap)) return raw;
        foreach (var pair in StateMap.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && string.Equals(pair[..eq].Trim(), raw, StringComparison.OrdinalIgnoreCase))
                return pair[(eq + 1)..].Trim();
        }

        return raw;
    }

    private SKColor ResolveWarnColor(double value)
    {
        var above = ParseD(WarnAbove);
        var below = ParseD(WarnBelow);
        return WarnMode switch
        {
            HaWarnMode.Above => above.HasValue && value >= above.Value ? WarnColor : ValueColor,
            HaWarnMode.Below => below.HasValue && value <= below.Value ? WarnColor : ValueColor,
            HaWarnMode.Outside => (above.HasValue && value >= above.Value) || (below.HasValue && value <= below.Value)
                ? WarnColor
                : ValueColor,
            _ => ValueColor
        };
    }

    private static double? ParseD(string s)
    {
        return double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture,
            out var v)
            ? v
            : null;
    }

    private static bool TryBinary(string state, out bool isOn)
    {
        switch ((state ?? "").Trim().ToLowerInvariant())
        {
            case "on" or "open" or "home" or "detected" or "motion" or "unlocked" or "active" or "playing"
                or "true" or "charging" or "wet" or "occupied":
                isOn = true;
                return true;
            case "off" or "closed" or "not_home" or "clear" or "no_motion" or "locked" or "idle" or "standby"
                or "false" or "discharging" or "dry" or "unoccupied":
                isOn = false;
                return true;
            default:
                isOn = false;
                return false;
        }
    }

    private static string FormatAge(DateTime lastChangedUtc)
    {
        var span = DateTime.UtcNow - lastChangedUtc;
        if (span.TotalSeconds < 60) return $"{Math.Max(0, (int)span.TotalSeconds)}s ago";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    private static string DomainOf(string entityId)
    {
        var dot = (entityId ?? "").IndexOf('.');
        return dot > 0 ? entityId![..dot] : "";
    }

    private string FormatValue(string raw)
    {
        if (Decimals >= 0 &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return num.ToString("F" + Decimals, CultureInfo.InvariantCulture);
        return raw;
    }

    private static bool IsNumeric(string s)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private float AlignX(float rx, float rw, float cw)
    {
        return Align switch
        {
            HaTileAlign.Left => rx,
            HaTileAlign.Right => rx + rw - cw,
            _ => rx + (rw - cw) / 2f
        };
    }

    /// <summary>Draws a line with the system font or a BDF bitmap; auto-fits to the rect unless fit is false.</summary>
    private void DrawText(SKCanvas c, string text, SKColor color,
        float rx, float ry, float rw, float rh, float targetH, bool fit, bool useBdf)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (useBdf)
        {
            var fontName = BdfFontRegistry.GetBestFontForHeight(Math.Max(5, (int)Math.Round(targetH)));
            using var bmp = _canvas.RenderBdfTextToBitmap(text, color, fontName);
            if (bmp is not { Width: > 0, Height: > 0 }) return;
            var scale = fit ? Math.Min(rw / bmp.Width, rh / bmp.Height) : targetH / bmp.Height;
            if (scale <= 0) return;
            var dw = bmp.Width * scale;
            var dh = bmp.Height * scale;
            var left = AlignX(rx, rw, dw);
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
        var anchorX = Align switch
        {
            HaTileAlign.Left => rx,
            HaTileAlign.Right => rx + rw,
            _ => rx + rw / 2f
        };
        var alignTo = Align switch
        {
            HaTileAlign.Left => SKTextAlign.Left,
            HaTileAlign.Right => SKTextAlign.Right,
            _ => SKTextAlign.Center
        };
        c.DrawText(text, anchorX, baseline, alignTo, font, paint);
    }
}
