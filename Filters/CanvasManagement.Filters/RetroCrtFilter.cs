using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     An old tube TV: dark scanlines, red and blue slightly out of register, darker edges and a faint flicker.
///     One pass over the pixels with a precomputed edge-darkening table.
/// </summary>
[FilterInfo("Retro CRT",
    "Old tube TV look: scanlines, colour fringes, darker edges and a faint flicker",
    "Retro",
    IconResourceName = "retrocrt.svg")]
public sealed class RetroCrtFilter : ICanvasFilter
{
    [FilterParameter("Scanlines", "How dark every second line is", MinValue = 0.0, MaxValue = 1.0, DefaultValue = 0.35)]
    public float Scanlines { get; set; } = 0.35f;

    [FilterParameter("Colour fringe", "How many pixels red and blue are shifted apart", MinValue = 0, MaxValue = 3,
        DefaultValue = 1)]
    public int ColorFringe { get; set; } = 1;

    [FilterParameter("Edge darkening", "How much darker the corners are", MinValue = 0.0, MaxValue = 1.0,
        DefaultValue = 0.45)]
    public float EdgeDarkening { get; set; } = 0.45f;

    [FilterParameter("Flicker", "Faint brightness flicker of the tube", MinValue = 0.0, MaxValue = 1.0,
        DefaultValue = 0.3)]
    public float Flicker { get; set; } = 0.3f;

    [FilterParameter("Speed", "Speed of the flicker", MinValue = 0.2, MaxValue = 4.0, DefaultValue = 1.0)]
    public float PlaybackSpeed { get; set; } = 1f;

    public string Name => "Retro CRT";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    private byte[] _edge = [];        // per pixel 0..255 brightness factor for the edge darkening
    private int _edgeW, _edgeH;
    private float _edgeAmount = -1;
    private byte[] _row = [];         // copy of the current row for the colour fringe
    private FrameClock _clock;
    private float _time;
    private readonly Random _random = new();

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        var w = bitmap.Width;
        var h = bitmap.Height;
        var intensity = Math.Clamp(Intensity, 0f, 1f);

        var edge = Math.Clamp(EdgeDarkening, 0f, 1f) * intensity;
        if (w != _edgeW || h != _edgeH || Math.Abs(edge - _edgeAmount) > 0.001f) BuildEdge(w, h, edge);
        if (_row.Length < w * 4) _row = new byte[w * 4];

        _time += _clock.Tick() * Math.Clamp(PlaybackSpeed, 0.2f, 4f);
        // slow wave plus a little noise; at most ~8 % darker
        var flicker = 1f - Math.Clamp(Flicker, 0f, 1f) * intensity *
            (0.04f + 0.03f * MathF.Sin(_time * 9f) + 0.01f * (float)_random.NextDouble());
        var flick = (int)(flicker * 256);
        var dark = 256 - (int)(Math.Clamp(Scanlines, 0f, 1f) * intensity * 200);
        var fringe = (int)Math.Round(Math.Clamp(ColorFringe, 0, 3) * intensity);
        var layout = ChannelLayout.Of(bitmap);
        var stride = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();

        fixed (byte* rowCopy = _row)
        fixed (byte* edgeTable = _edge)
        {
            for (var y = 0; y < h; y++)
            {
                var line = pixels + y * stride;
                Buffer.MemoryCopy(line, rowCopy, w * 4, w * 4);
                var lineFactor = (y & 1) == 1 ? (dark * flick) >> 8 : flick;
                var edgeRow = edgeTable + y * w;
                for (var x = 0; x < w; x++)
                {
                    var p = line + x * 4;
                    var m = (lineFactor * edgeRow[x]) >> 8; // 0..256
                    var rx = Math.Max(0, x - fringe);
                    var bx = Math.Min(w - 1, x + fringe);
                    p[layout.R] = (byte)((rowCopy[rx * 4 + layout.R] * m) >> 8);
                    p[layout.G] = (byte)((rowCopy[x * 4 + layout.G] * m) >> 8);
                    p[layout.B] = (byte)((rowCopy[bx * 4 + layout.B] * m) >> 8);
                    // premultiplied: a channel never exceeds alpha
                    var a = p[layout.A];
                    if (a < 255)
                    {
                        if (p[layout.R] > a) p[layout.R] = a;
                        if (p[layout.G] > a) p[layout.G] = a;
                        if (p[layout.B] > a) p[layout.B] = a;
                    }
                }
            }
        }
        return bitmap;
    }

    private void BuildEdge(int w, int h, float amount)
    {
        _edgeW = w;
        _edgeH = h;
        _edgeAmount = amount;
        if (_edge.Length != w * h) _edge = new byte[w * h];
        var cx = (w - 1) / 2f;
        var cy = (h - 1) / 2f;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            // elliptic distance to the centre, 0 in the middle, 1 at the corners
            var dx = (x - cx) / Math.Max(1f, cx);
            var dy = (y - cy) / Math.Max(1f, cy);
            var d = Math.Min(1f, (dx * dx + dy * dy) / 2f);
            var f = 1f - amount * d * d * 1.6f;
            _edge[y * w + x] = (byte)(Math.Clamp(f, 0f, 1f) * 255);
        }
    }
}
