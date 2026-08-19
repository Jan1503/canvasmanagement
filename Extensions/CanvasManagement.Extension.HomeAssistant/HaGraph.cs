using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     Shared line/area chart renderer used by the HA Graph extension and the HA Sensor sparkline. Auto-ranges
///     the Y axis to the data and maps the X axis by sample time.
/// </summary>
internal static class HaGraph
{
    public static void Draw(SKCanvas c, HaSample[] samples, SKRect rect, SKColor lineColor, SKColor fillColor,
        float lineWidth, bool fill)
    {
        if (samples is not { Length: >= 2 } || rect.Width < 2 || rect.Height < 2) return;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in samples)
        {
            if (s.Value < min) min = s.Value;
            if (s.Value > max) max = s.Value;
        }

        if (max <= min) max = min + 1; // flat-line guard
        var headroom = (max - min) * 0.08;
        min -= headroom;
        max += headroom;

        var t0 = samples[0].Utc;
        var span = (samples[^1].Utc - t0).TotalSeconds;
        if (span <= 0) span = 1;

        float X(DateTime t) => rect.Left + (float)((t - t0).TotalSeconds / span) * rect.Width;
        float Y(double v) => rect.Bottom - (float)((v - min) / (max - min)) * rect.Height;

        using var path = new SKPath();
        path.MoveTo(X(samples[0].Utc), Y(samples[0].Value));
        for (var i = 1; i < samples.Length; i++)
            path.LineTo(X(samples[i].Utc), Y(samples[i].Value));

        if (fill)
        {
            using var fillPath = new SKPath(path);
            fillPath.LineTo(X(samples[^1].Utc), rect.Bottom);
            fillPath.LineTo(X(samples[0].Utc), rect.Bottom);
            fillPath.Close();
            using var fp = new SKPaint { Style = SKPaintStyle.Fill, Color = fillColor, IsAntialias = true };
            c.DrawPath(fillPath, fp);
        }

        using var lp = new SKPaint
        {
            Style = SKPaintStyle.Stroke, Color = lineColor, StrokeWidth = lineWidth, IsAntialias = true,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        c.DrawPath(path, lp);
    }

    public static (double Min, double Max, double Last)? Range(HaSample[] samples)
    {
        if (samples is not { Length: > 0 }) return null;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in samples)
        {
            if (s.Value < min) min = s.Value;
            if (s.Value > max) max = s.Value;
        }

        return (min, max, samples[^1].Value);
    }
}
