using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Afterglow: whatever moves leaves a fading trail, like phosphor or a long exposure. Each pixel keeps the
///     brighter of its new value and its faded previous value; the fade is per second, not per frame, so it
///     looks the same at 30 and 60 fps.
/// </summary>
[FilterInfo("Afterglow",
    "Moving things leave fading light trails, like a long exposure",
    "Ambient",
    IconResourceName = "afterglow.svg")]
public sealed class AfterglowFilter : ICanvasFilter
{
    [FilterParameter("Trail length", "How long trails stay visible, in seconds", MinValue = 0.1, MaxValue = 3.0,
        DefaultValue = 0.6)]
    public float TrailSeconds { get; set; } = 0.6f;

    public string Name => "Afterglow";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    private byte[] _previous = [];
    private int _w, _h;
    private FrameClock _clock;
    private readonly byte[] _fade = new byte[256];

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        var w = bitmap.Width;
        var h = bitmap.Height;
        var dt = _clock.Tick();

        if (w != _w || h != _h)
        {
            // new size: start without trails
            _w = w;
            _h = h;
            _previous = new byte[w * h * 4];
        }

        // remaining fraction after dt: down to 1/256 after TrailSeconds, scaled by Intensity
        var keep = MathF.Pow(1f / 256f, dt / Math.Clamp(TrailSeconds, 0.1f, 3f)) * Math.Clamp(Intensity, 0f, 1f);
        var k = (int)(keep * 256);
        for (var v = 0; v < 256; v++) _fade[v] = (byte)((v * k) >> 8);

        var stride = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();
        fixed (byte* prev = _previous)
        fixed (byte* fade = _fade)
        {
            for (var y = 0; y < h; y++)
            {
                var line = pixels + y * stride;
                var old = prev + y * w * 4;
                for (var i = 0; i < w * 4; i++)
                {
                    // all four channels (premultiplied): a faded trail also fades its alpha
                    var faded = fade[old[i]];
                    var now = line[i];
                    var v = now > faded ? now : faded;
                    line[i] = v;
                    old[i] = v;
                }
            }
        }
        return bitmap;
    }
}
