using System.Diagnostics;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Byte offsets of the colour channels in a 32-bit pixel. The wall bitmap is BGRA on Windows and RGBA on
///     Linux (the Pi), so effects that treat channels differently must ask instead of assuming.
/// </summary>
internal readonly record struct ChannelLayout(int R, int G, int B, int A)
{
    public static ChannelLayout Of(SKBitmap bitmap) =>
        bitmap.ColorType == SKColorType.Rgba8888 ? new ChannelLayout(0, 1, 2, 3) : new ChannelLayout(2, 1, 0, 3);

    /// <summary>True for the 32-bit formats these effects handle.</summary>
    public static bool Supported(SKBitmap bitmap) =>
        bitmap.BytesPerPixel == 4 && bitmap.ColorType is SKColorType.Rgba8888 or SKColorType.Bgra8888 &&
        bitmap.Width > 0 && bitmap.Height > 0;
}

/// <summary>Seconds since the last call, clamped (a paused or first frame counts as one 20 fps frame).</summary>
internal struct FrameClock
{
    private long _last;

    public float Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = _last == 0 ? 0.05f : (float)((now - _last) / (double)Stopwatch.Frequency);
        _last = now;
        return Math.Clamp(dt, 0f, 0.1f);
    }
}
