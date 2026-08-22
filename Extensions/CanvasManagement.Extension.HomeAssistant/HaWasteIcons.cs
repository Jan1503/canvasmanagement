using SkiaSharp;

namespace CanvasManagement.Extension.HomeAssistant;

internal enum HaBinKind { Rest, Yellow, Paper, Bio, Glass, Unknown }

/// <summary>Filled wheelie-bin glyphs that stay readable at LED sizes.</summary>
internal static class HaWasteIcons
{
    public static HaBinKind Kind(string label, string id)
    {
        var s = (label + " " + id).ToLowerInvariant();
        if (s.Contains("gelb") || s.Contains("wertstoff") || s.Contains("plastic") || s.Contains("pack") ||
            s.Contains("gruenpunkt") || s.Contains("grünpunkt"))
            return HaBinKind.Yellow;
        if (s.Contains("papier") || s.Contains("paper") || s.Contains("blau") || s.Contains("altpapier"))
            return HaBinKind.Paper;
        if (s.Contains("bio") || s.Contains("grün") || s.Contains("gruen") || s.Contains("organic") ||
            s.Contains("garten"))
            return HaBinKind.Bio;
        if (s.Contains("glas") || s.Contains("glass"))
            return HaBinKind.Glass;
        if (s.Contains("schwarz") || s.Contains("rest") || s.Contains("grau") || s.Contains("grey") ||
            s.Contains("gray") || s.Contains("hausm"))
            return HaBinKind.Rest;
        return HaBinKind.Unknown;
    }

    public static SKColor Color(HaBinKind kind) => kind switch
    {
        HaBinKind.Yellow => new SKColor(230, 190, 40),
        HaBinKind.Paper => new SKColor(55, 115, 210),
        HaBinKind.Bio => new SKColor(70, 150, 45),
        HaBinKind.Glass => new SKColor(30, 155, 140),
        HaBinKind.Rest => new SKColor(48, 48, 52),
        _ => new SKColor(110, 110, 118)
    };

    public static void Draw(SKCanvas c, HaBinKind kind, float cx, float cy, float size)
    {
        if (size < 4f) return;
        var r = size * 0.5f;
        var body = Color(kind);
        var lid = Darken(body, 0.72f);
        var rim = Darken(body, 0.55f);

        using var bodyP = new SKPaint { Color = body, IsAntialias = true };
        using var lidP = new SKPaint { Color = lid, IsAntialias = true };
        using var rimP = new SKPaint { Color = rim, IsAntialias = true };
        using var markP = new SKPaint { Color = Emblem(kind), IsAntialias = true };

        // Handle
        c.DrawRoundRect(cx - r * 0.18f, cy - r * 0.88f, r * 0.36f, r * 0.2f, r * 0.08f, r * 0.08f, lidP);
        // Lid
        c.DrawRoundRect(cx - r * 0.56f, cy - r * 0.7f, r * 1.12f, r * 0.32f, r * 0.12f, r * 0.12f, lidP);
        // Body
        c.DrawRoundRect(cx - r * 0.46f, cy - r * 0.42f, r * 0.92f, r * 1.22f, r * 0.14f, r * 0.14f, bodyP);
        // Rim line
        c.DrawRect(cx - r * 0.46f, cy - r * 0.42f, r * 0.92f, Math.Max(1f, r * 0.08f), rimP);

        DrawEmblem(c, kind, cx, cy + r * 0.18f, r * 0.55f, markP);
    }

    private static void DrawEmblem(SKCanvas c, HaBinKind kind, float cx, float cy, float r, SKPaint p)
    {
        switch (kind)
        {
            case HaBinKind.Yellow:
                // Recycle-style chevrons
                using (var stroke = new SKPaint
                {
                    Color = p.Color, IsAntialias = true, Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1.2f, r * 0.22f), StrokeCap = SKStrokeCap.Round
                })
                {
                    c.DrawLine(cx - r * 0.45f, cy + r * 0.15f, cx, cy - r * 0.5f, stroke);
                    c.DrawLine(cx, cy - r * 0.5f, cx + r * 0.45f, cy + r * 0.15f, stroke);
                    c.DrawLine(cx - r * 0.25f, cy + r * 0.45f, cx + r * 0.25f, cy + r * 0.45f, stroke);
                }
                break;

            case HaBinKind.Paper:
                c.DrawRoundRect(cx - r * 0.4f, cy - r * 0.5f, r * 0.8f, r * 1.0f, r * 0.08f, r * 0.08f, p);
                using (var line = new SKPaint
                {
                    Color = new SKColor(55, 115, 210), IsAntialias = true, StrokeWidth = Math.Max(1f, r * 0.12f),
                    Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round
                })
                {
                    c.DrawLine(cx - r * 0.22f, cy - r * 0.15f, cx + r * 0.22f, cy - r * 0.15f, line);
                    c.DrawLine(cx - r * 0.22f, cy + r * 0.1f, cx + r * 0.18f, cy + r * 0.1f, line);
                    c.DrawLine(cx - r * 0.22f, cy + r * 0.35f, cx + r * 0.08f, cy + r * 0.35f, line);
                }
                break;

            case HaBinKind.Bio:
                using (var leaf = new SKPath())
                {
                    leaf.MoveTo(cx, cy + r * 0.55f);
                    leaf.CubicTo(cx + r * 0.75f, cy + r * 0.1f, cx + r * 0.35f, cy - r * 0.7f, cx, cy - r * 0.55f);
                    leaf.CubicTo(cx - r * 0.35f, cy - r * 0.7f, cx - r * 0.75f, cy + r * 0.1f, cx, cy + r * 0.55f);
                    leaf.Close();
                    c.DrawPath(leaf, p);
                }
                break;

            case HaBinKind.Glass:
                c.DrawRoundRect(cx - r * 0.16f, cy - r * 0.65f, r * 0.32f, r * 0.28f, r * 0.08f, r * 0.08f, p);
                c.DrawRoundRect(cx - r * 0.38f, cy - r * 0.38f, r * 0.76f, r * 0.95f, r * 0.28f, r * 0.28f, p);
                break;

            default:
                // Rest / unknown: slot in the lid area already reads as a bin
                break;
        }
    }

    private static SKColor Emblem(HaBinKind kind) => kind switch
    {
        HaBinKind.Yellow => new SKColor(40, 32, 8),
        HaBinKind.Paper => SKColors.White,
        HaBinKind.Bio => new SKColor(210, 240, 170),
        HaBinKind.Glass => new SKColor(220, 250, 245),
        _ => new SKColor(180, 180, 185)
    };

    private static SKColor Darken(SKColor c, float f) =>
        new((byte)(c.Red * f), (byte)(c.Green * f), (byte)(c.Blue * f), c.Alpha);
}
