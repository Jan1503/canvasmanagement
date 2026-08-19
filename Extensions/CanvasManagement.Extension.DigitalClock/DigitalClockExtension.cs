using System.Globalization;
using System.Timers;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.DigitalClock;

public enum ClockHAlign { Left, Center, Right }

public enum ClockVAlign { Top, Middle, Bottom }

public enum ClockDateFormat { None, DayMonth, MonthDay, Weekday, WeekdayDayMonth, Iso }

public enum ClockLocale { German, English }

/// <summary>
///     A highly configurable digital clock. Renders 12/24h time with optional seconds, AM/PM, a date line,
///     letter spacing, glow, colour-cycling and a per-second pulse. Digits can be drawn with the system font,
///     a crisp BDF bitmap font, or as a classic seven-segment display. Auto-fits to any canvas size and can be
///     aligned anywhere, so it works as a fullscreen clock or a small floating overlay.
/// </summary>
[ExtensionInfo("Digital Clock",
    "Flexible digital clock (12/24h, date, seven-segment, glow, colour cycle)",
    "Clock",
    IconResourceName = "digital-clock.svg")]
public class DigitalClockExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;

    internal DigitalClockExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    // ── Time ────────────────────────────────────────────────────────────────
    [ExtensionParameter("24-Hour", "Use 24-hour time instead of 12-hour", DefaultValue = true, Order = 1)]
    public bool Show24Hour { get; set; } = true;

    [ExtensionParameter("Show Seconds", "Append seconds to the time", DefaultValue = true, Order = 2)]
    public bool ShowSeconds { get; set; } = true;

    [ExtensionParameter("Leading Zero", "Pad the hour with a leading zero", DefaultValue = true, Order = 3)]
    public bool ShowLeadingZero { get; set; } = true;

    [ExtensionParameter("Show AM/PM", "Show an AM/PM suffix (12-hour mode only)", DefaultValue = false, Order = 4)]
    public bool ShowAmPm { get; set; }

    [ExtensionParameter("Blink Colon", "Blink the separators once per second", DefaultValue = false, Order = 5)]
    public bool BlinkColon { get; set; }

    [ExtensionParameter("TZ Offset", "Minutes to add to local time (e.g. for a second city)",
        DefaultValue = 0, MinValue = -720, MaxValue = 840, Unit = "min", Order = 6)]
    public int TimeZoneOffsetMinutes { get; set; }

    // ── Time / Date visibility ───────────────────────────────────────────────
    [ExtensionParameter("Show Time", "Show the time line", DefaultValue = true, Order = 9)]
    public bool ShowTime { get; set; } = true;

    // ── Date ────────────────────────────────────────────────────────────────
    [ExtensionParameter("Show Date", "Show a date line (beneath the time, or alone if time is hidden)",
        DefaultValue = false, Order = 10)]
    public bool ShowDate { get; set; }

    [ExtensionParameter("Date Format", "How to format the date line", DefaultValue = ClockDateFormat.WeekdayDayMonth,
        Order = 11)]
    public ClockDateFormat DateFormat { get; set; } = ClockDateFormat.WeekdayDayMonth;

    [ExtensionParameter("Locale", "Language for weekday/month names", DefaultValue = ClockLocale.German, Order = 12)]
    public ClockLocale Locale { get; set; } = ClockLocale.German;

    // ── Typography ────────────────────────────────────────────────────────────
    [ExtensionParameter("Seven Segment", "Draw digits as a classic seven-segment display", DefaultValue = false,
        Order = 20)]
    public bool SevenSegment { get; set; }

    [ExtensionParameter("Font Size", "Time text height in px (0 = auto-fit to the canvas)", DefaultValue = 0,
        MinValue = 0, MaxValue = 256, Unit = "px", Order = 21)]
    public int FontSize { get; set; }

    [ExtensionParameter("Use BDF Font", "Render with the crisp bitmap (BDF) font", DefaultValue = false, Order = 22)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Bold", "Use a bold weight (system font only)", DefaultValue = true, Order = 23)]
    public bool Bold { get; set; } = true;

    [ExtensionParameter("Letter Spacing", "Extra spacing between characters, % of glyph width", DefaultValue = 0,
        MinValue = -20, MaxValue = 100, Unit = "%", Order = 24)]
    public int LetterSpacing { get; set; }

    // ── Layout ────────────────────────────────────────────────────────────────
    [ExtensionParameter("H Align", "Horizontal alignment within the canvas", DefaultValue = ClockHAlign.Center,
        Order = 30)]
    public ClockHAlign HAlign { get; set; } = ClockHAlign.Center;

    [ExtensionParameter("V Align", "Vertical alignment within the canvas", DefaultValue = ClockVAlign.Middle,
        Order = 31)]
    public ClockVAlign VAlign { get; set; } = ClockVAlign.Middle;

    [ExtensionParameter("Padding X", "Horizontal padding in px", DefaultValue = 2, MinValue = 0, MaxValue = 64,
        Unit = "px", Order = 32)]
    public int PaddingX { get; set; } = 2;

    [ExtensionParameter("Padding Y", "Vertical padding in px", DefaultValue = 2, MinValue = 0, MaxValue = 64,
        Unit = "px", Order = 33)]
    public int PaddingY { get; set; } = 2;

    // ── Colour & style ──────────────────────────────────────────────────────
    [ExtensionParameter("Text Color", "Time/date colour", DefaultValue = "#FFFFFF", Order = 40)]
    public SKColor TextColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Color", "Canvas background (use alpha for a transparent overlay)",
        DefaultValue = "#FF000000", Order = 41)]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Glow", "Add a soft glow behind the digits", DefaultValue = false, Order = 42)]
    public bool Glow { get; set; }

    [ExtensionParameter("Color Cycle", "Slowly cycle the text colour through the rainbow", DefaultValue = false,
        Order = 43)]
    public bool ColorCycle { get; set; }

    [ExtensionParameter("Pulse", "Briefly pulse the size at the start of each second", DefaultValue = false,
        Order = 44)]
    public bool PulseOnSecond { get; set; }

    public string Name => "Digital Clock";
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

            _timer = new Timer(100) { AutoReset = true };
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
            catch (Exception ex) { Console.WriteLine($"[DigitalClock] render: {ex.Message}"); }
        }
    }

    // ── Presets ─────────────────────────────────────────────────────────────
    [ExtensionMethod("Preset: Minimal", "Clean white, centered, auto-fit", Category = "Presets", Order = 1)]
    public void PresetMinimal()
    {
        SevenSegment = false;
        Glow = false;
        ColorCycle = false;
        PulseOnSecond = false;
        Bold = true;
        TextColor = SKColors.White;
        BackgroundColor = SKColors.Black;
        HAlign = ClockHAlign.Center;
        VAlign = ClockVAlign.Middle;
    }

    [ExtensionMethod("Preset: Neon Night", "Glowing colour-cycling clock on black", Category = "Presets", Order = 2)]
    public void PresetNeonNight()
    {
        SevenSegment = false;
        Glow = true;
        ColorCycle = true;
        PulseOnSecond = false;
        Bold = true;
        BackgroundColor = SKColors.Black;
        HAlign = ClockHAlign.Center;
        VAlign = ClockVAlign.Middle;
    }

    [ExtensionMethod("Preset: Seven-Seg Red", "Classic red seven-segment alarm-clock look", Category = "Presets",
        Order = 3)]
    public void PresetSevenSegRed()
    {
        SevenSegment = true;
        Glow = true;
        ColorCycle = false;
        TextColor = new SKColor(255, 40, 30);
        BackgroundColor = SKColors.Black;
        ShowSeconds = true;
        HAlign = ClockHAlign.Center;
        VAlign = ClockVAlign.Middle;
    }

    [ExtensionMethod("Preset: Big Bold", "Large bold time, no seconds, centered", Category = "Presets", Order = 4)]
    public void PresetBigBold()
    {
        SevenSegment = false;
        Glow = false;
        ColorCycle = false;
        Bold = true;
        ShowSeconds = false;
        FontSize = 0;
        TextColor = SKColors.White;
        HAlign = ClockHAlign.Center;
        VAlign = ClockVAlign.Middle;
    }

    // ── Render ──────────────────────────────────────────────────────────────
    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        var now = DateTime.Now.AddMinutes(TimeZoneOffsetMinutes);
        var color = ColorCycle ? CycleColor(now) : TextColor;
        var pulse = PulseOnSecond ? PulseFactor(now) : 1f;

        // Never render completely blank: if both are off, fall back to showing the time.
        var showTime = ShowTime || !ShowDate;
        var time = showTime ? BuildTime(now) : null;
        var date = ShowDate ? BuildDate(now) : null;

        float w = _canvas.Width, h = _canvas.Height;
        var innerX = (float)PaddingX;
        var innerY = (float)PaddingY;
        var innerW = Math.Max(4f, w - 2 * PaddingX);
        var innerH = Math.Max(4f, h - 2 * PaddingY);

        var dateColor = new SKColor(
            (byte)(color.Red * 0.78f + 40), (byte)(color.Green * 0.78f + 40),
            (byte)(color.Blue * 0.78f + 40), color.Alpha);

        if (time != null && date != null)
        {
            // Both: split the area into a time slot (top) and a date slot (bottom).
            // Clamp with a min that can't exceed the max (avoids a crash on very short canvases).
            var maxDate = innerH * 0.4f;
            var dateH = Math.Clamp(innerH * 0.24f, Math.Min(5f, maxDate), maxDate);
            var gap = Math.Max(1f, innerH * 0.05f);
            var timeSlotH = Math.Max(1f, innerH - dateH - gap);

            // FontSize 0 = auto-fit to the slot; any other value is honoured exactly so the slider directly
            // controls the size (text may overflow/clip if set larger than the canvas — that's the user's call).
            var autoFit = FontSize <= 0;
            var targetH = (autoFit ? timeSlotH : FontSize) * pulse;

            if (SevenSegment)
                DrawSevenSegTime(c, time, color, innerX, innerY, innerW, timeSlotH, targetH, autoFit, Glow);
            else
                DrawTextLine(c, time, color, innerX, innerY, innerW, timeSlotH, targetH, autoFit, Glow);

            DrawTextLine(c, date, dateColor, innerX, innerY + timeSlotH + gap, innerW, dateH, dateH, true, false);
        }
        else if (time != null)
        {
            // Time only: fill the whole area.
            var autoFit = FontSize <= 0;
            var targetH = (autoFit ? innerH : FontSize) * pulse;
            if (SevenSegment)
                DrawSevenSegTime(c, time, color, innerX, innerY, innerW, innerH, targetH, autoFit, Glow);
            else
                DrawTextLine(c, time, color, innerX, innerY, innerW, innerH, targetH, autoFit, Glow);
        }
        else if (date != null)
        {
            // Date only: fill the whole area with the date line.
            var autoFit = FontSize <= 0;
            var targetH = (autoFit ? innerH : FontSize) * pulse;
            DrawTextLine(c, date, dateColor, innerX, innerY, innerW, innerH, targetH, autoFit, Glow);
        }

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private string BuildTime(DateTime now)
    {
        var hour = Show24Hour ? now.Hour : now.Hour % 12 == 0 ? 12 : now.Hour % 12;
        var hh = ShowLeadingZero ? hour.ToString("D2") : hour.ToString(CultureInfo.InvariantCulture);
        var sep = BlinkColon && now.Second % 2 == 1 ? " " : ":";
        var str = $"{hh}{sep}{now.Minute:D2}";
        if (ShowSeconds) str += $"{sep}{now.Second:D2}";
        if (ShowAmPm && !Show24Hour && !SevenSegment) str += now.Hour < 12 ? " AM" : " PM";
        return str;
    }

    private string BuildDate(DateTime now)
    {
        var culture = Locale == ClockLocale.German
            ? CultureInfo.GetCultureInfo("de-DE")
            : CultureInfo.GetCultureInfo("en-US");
        return DateFormat switch
        {
            ClockDateFormat.DayMonth => now.ToString("dd.MM.", culture),
            ClockDateFormat.MonthDay => now.ToString("MMM d", culture),
            ClockDateFormat.Weekday => now.ToString("dddd", culture),
            ClockDateFormat.WeekdayDayMonth => now.ToString("ddd dd.MM.", culture),
            ClockDateFormat.Iso => now.ToString("yyyy-MM-dd", culture),
            _ => string.Empty
        };
    }

    private static SKColor CycleColor(DateTime now)
    {
        var t = (float)(now.TimeOfDay.TotalMilliseconds % 8000 / 8000.0); // 8s rainbow loop
        return SKColor.FromHsv(t * 360f, 90f, 100f);
    }

    private static float PulseFactor(DateTime now)
    {
        // Quick ease-out bump right after each second tick, settling back to 1.0.
        var ms = now.Millisecond;
        if (ms > 280) return 1f;
        var p = 1f - ms / 280f; // 1 -> 0 over 280ms
        return 1f + 0.08f * p * p;
    }

    private float AlignX(float rx, float rw, float cw)
    {
        return HAlign switch
        {
            ClockHAlign.Left => rx,
            ClockHAlign.Right => rx + rw - cw,
            _ => rx + (rw - cw) / 2f
        };
    }

    private float AlignTop(float ry, float rh, float ch)
    {
        return VAlign switch
        {
            ClockVAlign.Top => ry,
            ClockVAlign.Bottom => ry + rh - ch,
            _ => ry + (rh - ch) / 2f
        };
    }

    /// <summary>Draws one line (time or date) with the system font or a BDF bitmap, aligned within a rect.</summary>
    private void DrawTextLine(SKCanvas c, string text, SKColor color,
        float rx, float ry, float rw, float rh, float targetH, bool fit, bool glow)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (UseBdfFont)
        {
            // BDF: pick the bitmap font closest to the requested height. When auto-fitting, scale the
            // rendered bitmap into the rect; otherwise scale so its glyph height matches targetH exactly.
            var fontName = BdfFontRegistry.GetBestFontForHeight(Math.Max(5, (int)Math.Round(targetH)));
            using var bmp = _canvas.RenderBdfTextToBitmap(text, color, fontName);
            if (bmp is not { Width: > 0, Height: > 0 }) return;
            var scale = fit
                ? Math.Min(rw / bmp.Width, rh / bmp.Height)
                : targetH / bmp.Height;
            if (scale <= 0) return;
            var dw = bmp.Width * scale;
            var dh = bmp.Height * scale;
            var bx = AlignX(rx, rw, dw);
            var by = AlignTop(ry, rh, dh);
            c.DrawBitmap(bmp, new SKRect(bx, by, bx + dw, by + dh));
            return;
        }

        using var typeface = SKTypeface.FromFamilyName(null,
            Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface) { Size = targetH, Subpixel = true };

        var spacing = font.Size * (LetterSpacing / 100f);
        var tw = MeasureSpaced(font, text, spacing);
        if (fit && tw > rw && tw > 0)
        {
            var k = rw / tw;
            font.Size *= k;
            spacing *= k;
            tw *= k;
        }

        var metrics = font.Metrics;
        var textH = metrics.Descent - metrics.Ascent;
        var top = AlignTop(ry, rh, textH);
        var baseline = top - metrics.Ascent;
        var left = AlignX(rx, rw, tw);

        using var paint = new SKPaint { Color = color, IsAntialias = true };
        if (glow)
        {
            using var glowPaint = new SKPaint
            {
                Color = color, IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1f, font.Size * 0.07f))
            };
            DrawSpaced(c, font, glowPaint, text, left, baseline, spacing);
        }

        DrawSpaced(c, font, paint, text, left, baseline, spacing);
    }

    private static float MeasureSpaced(SKFont font, string text, float spacing)
    {
        if (Math.Abs(spacing) < 0.001f) return font.MeasureText(text);
        var total = 0f;
        foreach (var ch in text) total += font.MeasureText(ch.ToString()) + spacing;
        return total - spacing;
    }

    private static void DrawSpaced(SKCanvas c, SKFont font, SKPaint paint, string text,
        float left, float baseline, float spacing)
    {
        if (Math.Abs(spacing) < 0.001f)
        {
            c.DrawText(text, left, baseline, SKTextAlign.Left, font, paint);
            return;
        }

        var x = left;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            c.DrawText(s, x, baseline, SKTextAlign.Left, font, paint);
            x += font.MeasureText(s) + spacing;
        }
    }

    // ── Seven-segment rendering ───────────────────────────────────────────────
    // Segment order: a(top) b(top-right) c(bottom-right) d(bottom) e(bottom-left) f(top-left) g(middle)
    private static readonly Dictionary<char, bool[]> SevenSeg = new()
    {
        ['0'] = [true, true, true, true, true, true, false],
        ['1'] = [false, true, true, false, false, false, false],
        ['2'] = [true, true, false, true, true, false, true],
        ['3'] = [true, true, true, true, false, false, true],
        ['4'] = [false, true, true, false, false, true, true],
        ['5'] = [true, false, true, true, false, true, true],
        ['6'] = [true, false, true, true, true, true, true],
        ['7'] = [true, true, true, false, false, false, false],
        ['8'] = [true, true, true, true, true, true, true],
        ['9'] = [true, true, true, true, false, true, true]
    };

    private void DrawSevenSegTime(SKCanvas c, string time, SKColor color,
        float rx, float ry, float rw, float rh, float targetH, bool autoFit, bool glow)
    {
        var digitH = Math.Min(targetH, rh);
        if (digitH < 3f) return;

        float DigitW(float dh) => dh * 0.6f;
        float ColonW(float dh) => dh * 0.32f;
        float Space(float dh) => dh * 0.14f;

        float TotalWidth(float dh)
        {
            var total = 0f;
            for (var i = 0; i < time.Length; i++)
            {
                total += time[i] == ':' || time[i] == ' ' ? ColonW(dh) : DigitW(dh);
                if (i < time.Length - 1) total += Space(dh);
            }

            return total;
        }

        var tw = TotalWidth(digitH);
        if (autoFit && tw > rw && tw > 0)
        {
            digitH *= rw / tw;
            tw = TotalWidth(digitH);
        }

        var x = AlignX(rx, rw, tw);
        var top = AlignTop(ry, rh, digitH);

        void DrawAll(SKPaint p)
        {
            var cx = x;
            foreach (var ch in time)
            {
                if (ch == ':')
                {
                    var cw = ColonW(digitH);
                    var dotR = Math.Max(1f, digitH * 0.07f);
                    c.DrawCircle(cx + cw / 2f, top + digitH * 0.36f, dotR, p);
                    c.DrawCircle(cx + cw / 2f, top + digitH * 0.66f, dotR, p);
                    cx += cw + Space(digitH);
                }
                else if (ch == ' ')
                {
                    cx += ColonW(digitH) + Space(digitH);
                }
                else if (SevenSeg.TryGetValue(ch, out var segs))
                {
                    DrawSevenSegDigit(c, segs, cx, top, DigitW(digitH), digitH, p);
                    cx += DigitW(digitH) + Space(digitH);
                }
            }
        }

        if (glow)
        {
            using var glowPaint = new SKPaint
            {
                Color = color, IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1f, digitH * 0.06f))
            };
            DrawAll(glowPaint);
        }

        using var paint = new SKPaint { Color = color, IsAntialias = true };
        DrawAll(paint);
    }

    private static void DrawSevenSegDigit(SKCanvas c, bool[] segs, float x, float y, float dw, float dh, SKPaint p)
    {
        var t = dh * 0.15f;
        var midY = y + dh / 2f;
        var round = t * 0.35f;
        var hPad = t * 0.6f;
        var vPad = t * 0.6f;
        var vLen = dh / 2f - t * 0.9f;

        void H(float ry) => c.DrawRoundRect(new SKRect(x + hPad, ry, x + dw - hPad, ry + t), round, round, p);
        void V(float vx, float vy) => c.DrawRoundRect(new SKRect(vx, vy, vx + t, vy + vLen), round, round, p);

        if (segs[0]) H(y); // a
        if (segs[6]) H(midY - t / 2f); // g
        if (segs[3]) H(y + dh - t); // d
        if (segs[5]) V(x, y + vPad); // f
        if (segs[1]) V(x + dw - t, y + vPad); // b
        if (segs[4]) V(x, midY + t * 0.3f); // e
        if (segs[2]) V(x + dw - t, midY + t * 0.3f); // c
    }
}

