using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>Modes of <see cref="KaleidoscopeFilter" />.</summary>
public enum MirrorMode
{
    /// <summary>The left half, mirrored onto the right.</summary>
    MirrorLeftRight,
    /// <summary>The top half, mirrored onto the bottom.</summary>
    MirrorTopBottom,
    /// <summary>The top-left quarter, mirrored into all four corners.</summary>
    FourWay,
    /// <summary>A slowly turning wedge, repeated around the centre.</summary>
    Kaleidoscope
}

/// <summary>
///     Mirror and kaleidoscope: any content (a clock, a game, a video) becomes a symmetric pattern. The
///     kaleidoscope folds every pixel's angle into one wedge that slowly turns; polar coordinates are cached
///     per size and sin/cos come from a table, so a frame is one pass.
/// </summary>
[FilterInfo("Kaleidoscope",
    "Mirror the picture or turn it into a slowly rotating kaleidoscope",
    "Artistic",
    IconResourceName = "kaleidoscope.svg")]
public sealed class KaleidoscopeFilter : ICanvasFilter, IDisposable
{
    [FilterParameter("Mode", "Mirror halves, four corners or kaleidoscope", DefaultValue = MirrorMode.Kaleidoscope)]
    public MirrorMode Mode { get; set; } = MirrorMode.Kaleidoscope;

    [FilterParameter("Segments", "Number of mirrored wedges (kaleidoscope)", MinValue = 3, MaxValue = 12,
        DefaultValue = 6)]
    public int Segments { get; set; } = 6;

    [FilterParameter("Speed", "Rotation speed of the kaleidoscope", MinValue = 0.0, MaxValue = 4.0,
        DefaultValue = 1.0)]
    public float PlaybackSpeed { get; set; } = 1f;

    public string Name => "Kaleidoscope";
    public float Intensity { get; set; } = 1f;
    public bool Enabled { get; set; } = true;

    private const int TableSize = 4096;
    private static readonly float[] Sin = BuildSin();

    private SKBitmap? _copy;
    private float[] _radius = [];
    private float[] _angle = [];   // 0..1 = one turn
    private int _polarW, _polarH;
    private FrameClock _clock;
    private float _turn;           // rotation, in turns

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        var w = bitmap.Width;
        var h = bitmap.Height;

        if (_copy == null || _copy.Width != w || _copy.Height != h || _copy.ColorType != bitmap.ColorType)
        {
            _copy?.Dispose();
            _copy = new SKBitmap(new SKImageInfo(w, h, bitmap.ColorType, bitmap.AlphaType));
        }
        FilterBitmap.Blit(bitmap, _copy);

        _turn += _clock.Tick() * Math.Clamp(PlaybackSpeed, 0f, 4f) * 0.02f; // speed 1: one turn in 50 s
        _turn -= MathF.Floor(_turn);

        var mix = (int)(Math.Clamp(Intensity, 0f, 1f) * 256);
        var src = (uint*)_copy.GetPixels();
        var dst = (byte*)bitmap.GetPixels();
        var stride = bitmap.RowBytes;
        var srcStride = _copy.RowBytes / 4;
        var mode = Mode;
        if (mode == MirrorMode.Kaleidoscope && (_polarW != w || _polarH != h)) BuildPolar(w, h);
        var segment = 1f / Math.Clamp(Segments, 3, 12);
        var cx = (w - 1) / 2f;
        var cy = (h - 1) / 2f;

        for (var y = 0; y < h; y++)
        {
            var line = dst + y * stride;
            for (var x = 0; x < w; x++)
            {
                int sx, sy;
                switch (mode)
                {
                    case MirrorMode.MirrorLeftRight:
                        sx = x < w / 2 ? x : w - 1 - x; sy = y; break;
                    case MirrorMode.MirrorTopBottom:
                        sx = x; sy = y < h / 2 ? y : h - 1 - y; break;
                    case MirrorMode.FourWay:
                        sx = x < w / 2 ? x : w - 1 - x; sy = y < h / 2 ? y : h - 1 - y; break;
                    default:
                    {
                        var i = y * w + x;
                        // fold the angle into [0, segment/2], mirrored every other wedge
                        var a = _angle[i] + _turn;
                        a -= MathF.Floor(a / segment) * segment;
                        if (a > segment / 2) a = segment - a;
                        var t = (int)(a * TableSize) & (TableSize - 1);
                        var r = _radius[i];
                        sx = (int)(cx + r * Sin[(t + TableSize / 4) & (TableSize - 1)]);
                        sy = (int)(cy + r * Sin[t]);
                        // outside the picture: mirror back in (keeps the pattern full)
                        sx = Reflect(sx, w);
                        sy = Reflect(sy, h);
                        break;
                    }
                }

                var s = src[sy * srcStride + sx];
                var p = (uint*)(line + x * 4);
                if (mix >= 256) { *p = s; continue; }
                *p = Lerp(*p, s, mix);
            }
        }
        return bitmap;
    }

    private void BuildPolar(int w, int h)
    {
        _polarW = w;
        _polarH = h;
        _radius = new float[w * h];
        _angle = new float[w * h];
        var cx = (w - 1) / 2f;
        var cy = (h - 1) / 2f;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var dx = x - cx;
            var dy = y - cy;
            _radius[y * w + x] = MathF.Sqrt(dx * dx + dy * dy);
            var a = MathF.Atan2(dy, dx) / (2 * MathF.PI);
            _angle[y * w + x] = a < 0 ? a + 1 : a;
        }
    }

    private static float[] BuildSin()
    {
        var table = new float[TableSize];
        for (var i = 0; i < TableSize; i++) table[i] = MathF.Sin(i * 2 * MathF.PI / TableSize);
        return table;
    }

    private static int Reflect(int v, int size)
    {
        if (size <= 1) return 0;
        var period = 2 * (size - 1);
        v = ((v % period) + period) % period;
        return v < size ? v : period - v;
    }

    /// <summary>Per-channel blend of two packed pixels, <paramref name="t" /> in 0..256.</summary>
    private static uint Lerp(uint a, uint b, int t)
    {
        var inv = 256 - t;
        var rb = (((a & 0x00FF00FF) * (uint)inv + (b & 0x00FF00FF) * (uint)t) >> 8) & 0x00FF00FF;
        var ga = ((((a >> 8) & 0x00FF00FF) * (uint)inv + ((b >> 8) & 0x00FF00FF) * (uint)t) >> 8) & 0x00FF00FF;
        return rb | (ga << 8);
    }

    public void Dispose() => _copy?.Dispose();
}
