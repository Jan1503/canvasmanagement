using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>Glyph sizes of <see cref="DigitalRainVisionFilter" />.</summary>
public enum RainGlyphSize
{
    /// <summary>3×5 pixel glyphs in 4×6 cells: fine streams, best for walls up to 256 px high.</summary>
    Small,
    /// <summary>5×7 pixel glyphs in 6×8 cells: readable symbols on tall walls.</summary>
    Large
}

/// <summary>
///     The whole picture rebuilt from falling green code. Every pixel of a glyph takes the brightness of the
///     picture beneath it, so surfaces, faces and light become dense streams of symbols; outlines glow, drops
///     run down each column and brighten what they pass, glyphs flicker, single cells spark white, and a soft
///     glow lies over it all. Works on any content: a clock, a video, a game.
///     The symbols are generated (small original pixel glyphs), not a copied character set.
/// </summary>
[FilterInfo("Digital Rain Vision",
    "See everything as falling green code: the picture is rebuilt from glowing glyph streams",
    "Matrix Effects",
    IconResourceName = "digitalrainvision.svg")]
public sealed class DigitalRainVisionFilter : ICanvasFilter, IDisposable
{
    [FilterParameter("Glyph Size", "Small: fine streams; Large: readable symbols on tall walls",
        DefaultValue = RainGlyphSize.Small)]
    public RainGlyphSize GlyphSize { get; set; } = RainGlyphSize.Small;

    [FilterParameter("Rain", "How many drops run down the columns", MinValue = 0.0, MaxValue = 1.0,
        DefaultValue = 0.6)]
    public float RainDensity { get; set; } = 0.6f;

    [FilterParameter("Outline Glow", "How strongly edges and silhouettes light up", MinValue = 0.0,
        MaxValue = 1.0, DefaultValue = 0.7)]
    public float EdgeGlow { get; set; } = 0.7f;

    [FilterParameter("Sparkles", "Bright single cells that flash", MinValue = 0.0, MaxValue = 1.0,
        DefaultValue = 0.4)]
    public float Sparkles { get; set; } = 0.4f;

    [FilterParameter("Glow", "Soft green glow over the bright code", MinValue = 0.0, MaxValue = 1.0,
        DefaultValue = 0.6)]
    public float Glow { get; set; } = 0.6f;

    [FilterParameter("Speed", "Speed of the rain and the flicker", MinValue = 0.2, MaxValue = 4.0,
        DefaultValue = 1.0)]
    public float PlaybackSpeed { get; set; } = 1f;

    public string Name => "Digital Rain Vision";
    public float Intensity { get; set; } = 1f;
    public bool Enabled { get; set; } = true;

    private const int GlyphCount = 48;

    private readonly Random _random = new();
    private readonly BloomFilter _glow = new() { Threshold = 0.35f, Radius = 6 };
    private FrameClock _clock;

    // per size
    private int _w, _h, _cellW, _cellH, _cols, _rows;
    private RainGlyphSize _builtSize = (RainGlyphSize)(-1);
    private ulong[] _glyphs = [];        // bit masks, one per glyph (up to 8x8)
    private int _glyphW, _glyphH;
    private byte[] _luma = [];           // picture brightness per pixel
    private byte[] _edge = [];           // outline strength per pixel
    private byte[] _cellGlyph = [];      // glyph index per cell
    private float[] _cellFlip = [];      // seconds until the cell's glyph changes
    private float[] _sparkle = [];       // remaining sparkle per cell (0..1)
    private Drop[] _drops = [];          // per column, several drops
    private const int DropsPerColumn = 3;

    // brightness 0..255 -> colour, from black through deep and bright green to almost white
    private readonly uint[] _rampRgba = new uint[256];
    private readonly uint[] _rampBgra = new uint[256];

    private struct Drop
    {
        public float Y;      // head position in cells (can be above the top)
        public float Speed;  // cells per second
        public float Length; // trail length in cells
    }

    // picture brightness -> code brightness: a gamma curve, so shadows go deep and light glows
    private readonly int[] _contrast = new int[256];

    public DigitalRainVisionFilter()
    {
        for (var i = 0; i < 256; i++) _contrast[i] = (int)(MathF.Pow(i / 255f, 1.35f) * 235);
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            // green rises first, then red/blue come in for the hot, almost white heads
            var g = Math.Clamp(t * 1.25f, 0f, 1f);
            var r = Math.Clamp((t - 0.62f) * 2.4f, 0f, 1f) * 0.85f;
            var b = Math.Clamp((t - 0.55f) * 2.0f, 0f, 1f) * 0.75f;
            byte R = (byte)(r * 255), G = (byte)(g * 255), B = (byte)(b * 255);
            _rampRgba[i] = 0xFF000000u | ((uint)B << 16) | ((uint)G << 8) | R;
            _rampBgra[i] = 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | B;
        }
    }

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w != _w || h != _h || GlyphSize != _builtSize) Build(w, h);

        var dt = _clock.Tick() * Math.Clamp(PlaybackSpeed, 0.2f, 4f);
        var layout = ChannelLayout.Of(bitmap);
        var stride = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();

        // 1. picture brightness and outlines (Sobel on the brightness)
        fixed (byte* luma = _luma)
        fixed (byte* edge = _edge)
        {
            for (var y = 0; y < h; y++)
            {
                var line = pixels + y * stride;
                var l = luma + y * w;
                for (var x = 0; x < w; x++)
                {
                    var p = line + x * 4;
                    l[x] = (byte)((p[layout.R] * 54 + p[layout.G] * 183 + p[layout.B] * 19) >> 8);
                }
            }
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) { edge[y * w + x] = 0; continue; }
                var i = y * w + x;
                var gx = -luma[i - w - 1] - 2 * luma[i - 1] - luma[i + w - 1] + luma[i - w + 1] + 2 * luma[i + 1] + luma[i + w + 1];
                var gy = -luma[i - w - 1] - 2 * luma[i - w] - luma[i - w + 1] + luma[i + w - 1] + 2 * luma[i + w] + luma[i + w + 1];
                var m = (Math.Abs(gx) + Math.Abs(gy)) >> 2;
                edge[i] = (byte)(m > 255 ? 255 : m);
            }
        }

        // 2. rain, flicker, sparkles
        StepCells(dt);

        // 3. draw: every lit glyph pixel takes the picture's brightness, lifted by rain, outline and sparkle
        var ramp = layout.R == 0 ? _rampRgba : _rampBgra;
        var mix = (int)(Math.Clamp(Intensity, 0f, 1f) * 256);
        var edgeGain = (int)(Math.Clamp(EdgeGlow, 0f, 1f) * 400);
        var gw = _glyphW;
        var gh = _glyphH;
        fixed (byte* luma = _luma)
        fixed (byte* edge = _edge)
        fixed (uint* rampPtr = ramp)
        {
            for (var row = 0; row < _rows; row++)
            for (var col = 0; col < _cols; col++)
            {
                var cell = row * _cols + col;
                var mask = _glyphs[_cellGlyph[cell]];
                var rain = RainAt(col, row);                   // 0..1, 1 at a drop's head
                var spark = _sparkle[cell];
                var x0 = col * _cellW;
                var y0 = row * _cellH;
                for (var gy = 0; gy < _cellH; gy++)
                {
                    var y = y0 + gy;
                    if (y >= h) break;
                    var line = (uint*)(pixels + y * stride);
                    for (var gx = 0; gx < _cellW; gx++)
                    {
                        var x = x0 + gx;
                        if (x >= w) break;
                        var i = y * w + x;
                        var on = gx < gw && gy < gh && ((mask >> (gy * 8 + gx)) & 1) != 0;
                        int v;
                        if (on)
                        {
                            // the picture shows through the code: dim surfaces give dim code, light gives bright code;
                            // rain lifts what it passes, its head is almost white
                            var baseLight = 10 + _contrast[luma[i]];
                            v = (int)(baseLight * (0.35f + 1.05f * rain)) + (edge[i] * edgeGain >> 8);
                            if (rain > 0.92f) v += 60 + (luma[i] >> 1);
                            v += (int)(spark * 200);
                        }
                        else
                        {
                            // between the glyphs: a faint trace of the picture keeps its shapes readable
                            v = (luma[i] >> 4) + (edge[i] * edgeGain >> 11);
                        }
                        if (v > 255) v = 255;
                        var coded = rampPtr[v];
                        var old = line[x];
                        // keep the canvas's transparency (premultiplied): transparent areas stay see-through
                        var alpha = old >> 24;
                        if (alpha < 255) coded = Premultiply(coded, alpha);
                        line[x] = mix >= 256 ? coded : Lerp(old, coded, mix);
                    }
                }
            }
        }

        // 4. soft glow over the bright code
        if (Glow > 0)
        {
            _glow.Intensity = Math.Clamp(Glow, 0f, 1f) * 0.8f * Math.Clamp(Intensity, 0f, 1f);
            _glow.Apply(bitmap);
        }
        return bitmap;
    }

    /// <summary>Rain brightness at a cell: 1 at a drop's head, fading along its trail above it.</summary>
    private float RainAt(int col, int row)
    {
        var best = 0f;
        for (var k = 0; k < DropsPerColumn; k++)
        {
            ref var d = ref _drops[col * DropsPerColumn + k];
            if (d.Length <= 0) continue;
            var behind = d.Y - row; // 0 at the head, positive along the trail
            if (behind < -0.5f || behind > d.Length) continue;
            var v = behind < 0.5f ? 1f : 1f - behind / d.Length;
            if (v > best) best = v;
        }
        return best;
    }

    private void StepCells(float dt)
    {
        var density = Math.Clamp(RainDensity, 0f, 1f);
        for (var col = 0; col < _cols; col++)
        for (var k = 0; k < DropsPerColumn; k++)
        {
            ref var d = ref _drops[col * DropsPerColumn + k];
            if (d.Length > 0)
            {
                d.Y += d.Speed * dt;
                if (d.Y - d.Length > _rows) d.Length = 0; // gone below the bottom
            }
            // (re)start: more drops with higher density, staggered above the top
            else if (_random.NextDouble() < dt * (0.05 + density * 0.9) * (k == 0 ? 1 : density))
            {
                d.Length = 6 + (float)_random.NextDouble() * Math.Max(6, _rows * 0.8f);
                d.Speed = 6 + (float)_random.NextDouble() * 14;
                d.Y = -(float)_random.NextDouble() * 4;
            }
        }

        var sparkRate = Math.Clamp(Sparkles, 0f, 1f) * 0.08f;
        for (var cell = 0; cell < _cellGlyph.Length; cell++)
        {
            _cellFlip[cell] -= dt;
            if (_cellFlip[cell] <= 0)
            {
                _cellGlyph[cell] = (byte)_random.Next(GlyphCount);
                _cellFlip[cell] = 0.15f + (float)_random.NextDouble() * 2.5f;
            }
            _sparkle[cell] = Math.Max(0f, _sparkle[cell] - dt * 4f);
            if (sparkRate > 0 && _random.NextDouble() < sparkRate * dt) _sparkle[cell] = 1f;
        }

        // glyphs at the heads change fast, which reads as the code being "written"
        for (var col = 0; col < _cols; col++)
        for (var k = 0; k < DropsPerColumn; k++)
        {
            var d = _drops[col * DropsPerColumn + k];
            var row = (int)d.Y;
            if (d.Length > 0 && row >= 0 && row < _rows && _random.NextDouble() < 0.5)
                _cellGlyph[row * _cols + col] = (byte)_random.Next(GlyphCount);
        }
    }

    private void Build(int w, int h)
    {
        _w = w;
        _h = h;
        _builtSize = GlyphSize;
        (_glyphW, _glyphH, _cellW, _cellH) = GlyphSize == RainGlyphSize.Large ? (5, 7, 6, 8) : (3, 5, 4, 6);
        _cols = (w + _cellW - 1) / _cellW;
        _rows = (h + _cellH - 1) / _cellH;
        _luma = new byte[w * h];
        _edge = new byte[w * h];
        _cellGlyph = new byte[_cols * _rows];
        _cellFlip = new float[_cols * _rows];
        _sparkle = new float[_cols * _rows];
        _drops = new Drop[_cols * DropsPerColumn];
        _glyphs = MakeGlyphs(_glyphW, _glyphH, new Random(1234));
        for (var i = 0; i < _cellGlyph.Length; i++)
        {
            _cellGlyph[i] = (byte)_random.Next(GlyphCount);
            _cellFlip[i] = (float)_random.NextDouble() * 2f;
        }
        // start with drops spread over the whole height, so the first second is already raining
        for (var col = 0; col < _cols; col++)
        {
            ref var d = ref _drops[col * DropsPerColumn];
            d.Length = 6 + (float)_random.NextDouble() * Math.Max(6, _rows * 0.8f);
            d.Speed = 6 + (float)_random.NextDouble() * 14;
            d.Y = (float)_random.NextDouble() * (_rows + d.Length);
        }
    }

    /// <summary>
    ///     Original pixel symbols: random strokes (a vertical spine with branches, hooks and bars) so they read
    ///     as "code" rather than noise. Deterministic, so every wall shows the same set.
    /// </summary>
    internal static ulong[] MakeGlyphs(int gw, int gh, Random random)
    {
        var glyphs = new ulong[GlyphCount];
        for (var n = 0; n < GlyphCount; n++)
        {
            ulong mask;
            int lit;
            do
            {
                mask = 0;
                void Set(int x, int y)
                {
                    if (x >= 0 && y >= 0 && x < gw && y < gh) mask |= 1UL << (y * 8 + x);
                }

                // a spine (vertical stroke) somewhere
                var sx = random.Next(gw);
                var top = random.Next(0, 2);
                for (var y = top; y < gh - random.Next(0, 2); y++) Set(sx, y);
                // two or three branches: horizontal bars and short diagonals
                var parts = 2 + random.Next(2);
                for (var p = 0; p < parts; p++)
                {
                    var y = random.Next(gh);
                    switch (random.Next(3))
                    {
                        case 0:
                            for (var x = 0; x < gw; x++) if (random.Next(4) != 0) Set(x, y);
                            break;
                        case 1:
                            var dx = random.Next(2) == 0 ? 1 : -1;
                            for (int i = 0, x = sx; i < 3; i++, x += dx) Set(x, y + i);
                            break;
                        default:
                            Set(random.Next(gw), y);
                            Set(random.Next(gw), Math.Min(gh - 1, y + 1));
                            break;
                    }
                }
                lit = System.Numerics.BitOperations.PopCount(mask);
            } while (lit < gw * gh * 0.35 || lit > gw * gh * 0.7); // not too empty, not a block
            glyphs[n] = mask;
        }
        return glyphs;
    }

    /// <summary>An opaque packed colour with alpha <paramref name="alpha" />, channels scaled to match.</summary>
    private static uint Premultiply(uint color, uint alpha)
    {
        var rb = ((color & 0x00FF00FF) * alpha >> 8) & 0x00FF00FF;
        var g = ((color & 0x0000FF00) * alpha >> 8) & 0x0000FF00;
        return (alpha << 24) | rb | g;
    }

    private static uint Lerp(uint a, uint b, int t)
    {
        var inv = 256 - t;
        var rb = (((a & 0x00FF00FF) * (uint)inv + (b & 0x00FF00FF) * (uint)t) >> 8) & 0x00FF00FF;
        var ga = ((((a >> 8) & 0x00FF00FF) * (uint)inv + ((b >> 8) & 0x00FF00FF) * (uint)t) >> 8) & 0x00FF00FF;
        return rb | (ga << 8);
    }

    public void Dispose() => _glow.Dispose();
}
