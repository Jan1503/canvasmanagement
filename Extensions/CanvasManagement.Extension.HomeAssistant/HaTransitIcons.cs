using SkiaSharp;

namespace CanvasManagement.Extension.HomeAssistant;

internal enum HaTransitKind { Subway, Suburban, Bus, Ferry, Regional, Tram, Other }

/// <summary>
///     HVV-style line badges (U/S/bus/ferry/regional) drawn as filled rounded rectangles.
/// </summary>
internal static class HaTransitIcons
{
    public static HaTransitKind KindOf(string line, string product)
    {
        var p = (product ?? "").Trim().ToLowerInvariant();
        if (p is "subway" or "u-bahn" or "ubahn" or "metro") return HaTransitKind.Subway;
        if (p is "suburban" or "s-bahn" or "sbahn") return HaTransitKind.Suburban;
        if (p is "bus" or "nbus" or "expressbus") return HaTransitKind.Bus;
        if (p is "ferry" or "ship" or "hadag") return HaTransitKind.Ferry;
        if (p is "regional" or "nationalexpress" or "national" or "re" or "rb" or "ice" or "ic")
            return HaTransitKind.Regional;
        if (p is "tram" or "stadtbahn") return HaTransitKind.Tram;

        var l = (line ?? "").Trim();
        if (l.StartsWith("U", StringComparison.OrdinalIgnoreCase)) return HaTransitKind.Subway;
        if (l.StartsWith("S", StringComparison.OrdinalIgnoreCase)) return HaTransitKind.Suburban;
        if (l.StartsWith("A", StringComparison.OrdinalIgnoreCase) && l.Length <= 3) return HaTransitKind.Suburban;
        if (l.StartsWith("RE", StringComparison.OrdinalIgnoreCase) ||
            l.StartsWith("RB", StringComparison.OrdinalIgnoreCase) ||
            l.StartsWith("IC", StringComparison.OrdinalIgnoreCase))
            return HaTransitKind.Regional;
        if (l.StartsWith("F", StringComparison.OrdinalIgnoreCase) && l.Length <= 3) return HaTransitKind.Ferry;
        return HaTransitKind.Bus;
    }

    public static (SKColor Fill, SKColor Text) Colors(string line, HaTransitKind kind)
    {
        var key = (line ?? "").Trim().ToUpperInvariant();
        if (Hvv.TryGetValue(key, out var c)) return Contrast(c);
        return kind switch
        {
            HaTransitKind.Subway => Contrast(new SKColor(0x00, 0x6A, 0xB2)),
            HaTransitKind.Suburban => Contrast(new SKColor(0x84, 0x13, 0x7D)),
            HaTransitKind.Ferry => Contrast(new SKColor(0x00, 0xA0, 0xE2)),
            HaTransitKind.Regional => Contrast(new SKColor(0xAA, 0x00, 0x33)),
            HaTransitKind.Tram => Contrast(new SKColor(0xC0, 0x3A, 0x2B)),
            _ => Contrast(new SKColor(0xF0, 0xC4, 0x00))
        };
    }

    public static float DrawBadge(SKCanvas c, string line, string product, float x, float y, float h)
    {
        var label = string.IsNullOrWhiteSpace(line) ? "?" : line.Trim();
        if (label.Length > 5) label = label[..5];
        var kind = KindOf(label, product);
        var (fill, text) = Colors(label, kind);
        var fontSize = Math.Max(7f, h * 0.55f);
        using var font = new SKFont { Size = fontSize, Subpixel = false };
        using var paint = new SKPaint { Color = text, IsAntialias = true };
        var tw = Math.Max(font.MeasureText(label), h * 0.7f);
        var w = Math.Max(h, tw + h * 0.45f);
        var radius = Math.Min(h * 0.22f, 4f);
        using var bg = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        c.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), radius), bg);
        var metrics = font.Metrics;
        var baseline = y + (h - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
        c.DrawText(label, x + w / 2f, baseline, SKTextAlign.Center, font, paint);
        return w;
    }

    private static (SKColor Fill, SKColor Text) Contrast(SKColor fill)
    {
        var lum = (fill.Red * 0.3f + fill.Green * 0.59f + fill.Blue * 0.11f) / 255f;
        return (fill, lum > 0.62f ? SKColors.Black : SKColors.White);
    }

    // Official-ish HVV line colours (Hamburg).
    private static readonly Dictionary<string, SKColor> Hvv = new(StringComparer.OrdinalIgnoreCase)
    {
        ["U1"] = new(0x00, 0x67, 0xB1),
        ["U2"] = new(0xE3, 0x06, 0x13),
        ["U3"] = new(0xFF, 0xD1, 0x00),
        ["U4"] = new(0x00, 0xA1, 0x9A),
        ["S1"] = new(0x84, 0x13, 0x7D),
        ["S11"] = new(0x84, 0x13, 0x7D),
        ["S2"] = new(0x00, 0x7C, 0x36),
        ["S21"] = new(0x00, 0x7C, 0x36),
        ["S3"] = new(0x62, 0x21, 0x81),
        ["S31"] = new(0x62, 0x21, 0x81),
        ["S5"] = new(0xE8, 0x73, 0x00),
        ["A1"] = new(0xE2, 0x00, 0x1A),
        ["A2"] = new(0x00, 0x84, 0x3D),
        ["A3"] = new(0x8B, 0x5A, 0x2B)
    };
}
