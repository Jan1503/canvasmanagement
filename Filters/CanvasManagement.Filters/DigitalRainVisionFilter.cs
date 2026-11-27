using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>Glyph sizes of <see cref="DigitalRainVisionFilter" />.</summary>
public enum RainGlyphSize
{
    /// <summary>3×5 pixel glyphs in 4×6 cells: fine streams.</summary>
    Small,
    /// <summary>5×7 pixel glyphs in 6×8 cells: readable katakana, digits and signs.</summary>
    Large
}

/// <summary>
///     The whole picture rebuilt from falling green code. Every pixel of a glyph takes the brightness of the
///     picture beneath it, so surfaces, faces and light become streams of symbols. Each column rains at its own
///     speed; objects (whatever stands out from its surroundings) are lit more strongly and carry their own rain,
///     at their own speed, so they separate from the background. Glyphs are half-width katakana (mirrored),
///     digits and a few signs. Outlines glow, single cells spark white, and a soft glow lies over it all.
/// </summary>
[FilterInfo("Digital Rain Vision",
    "See everything as falling green code: the picture is rebuilt from glowing glyph streams",
    "Matrix Effects",
    IconResourceName = "digitalrainvision.svg")]
public sealed class DigitalRainVisionFilter : ICanvasFilter, IDisposable
{
    [FilterParameter("Glyph Size", "Large: readable katakana; Small: fine streams",
        DefaultValue = RainGlyphSize.Large)]
    public RainGlyphSize GlyphSize { get; set; } = RainGlyphSize.Large;

    [FilterParameter("Rain", "How many drops run down the columns", MinValue = 0.0, MaxValue = 1.0,
        DefaultValue = 0.6)]
    public float RainDensity { get; set; } = 0.6f;

    [FilterParameter("Object Contrast", "How clearly objects stand out from the background code",
        MinValue = 0.0, MaxValue = 1.0, DefaultValue = 0.75)]
    public float ObjectContrast { get; set; } = 0.75f;

    [FilterParameter("Object Rain Speed", "Speed of the rain inside objects, relative to the background rain",
        MinValue = 0.1, MaxValue = 3.0, DefaultValue = 0.4)]
    public float ObjectRainSpeed { get; set; } = 0.4f;

    [FilterParameter("Object Hold", "Seconds an object that stands still stays highlighted before it becomes background",
        MinValue = 2.0, MaxValue = 120.0, DefaultValue = 20.0)]
    public float ObjectHold { get; set; } = 20f;

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

    private const int DropsPerColumn = 3;

    private readonly Random _random = new();
    private readonly BloomFilter _glow = new() { Threshold = 0.35f, Radius = 6 };
    private readonly byte[] _picks = DigitalRainGlyphs.PickTable();
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
    private float[] _cellLuma = [];      // mean brightness per cell
    private float[] _cellEdge = [];      // mean outline strength per cell
    private float[] _cellColor = [];     // mean colour per cell (r, g, b)
    private float[] _bgColor = [];       // learned background colour per cell (r, g, b)
    private float[] _bgEdge = [];        // learned background outline strength per cell
    private float[] _objectAge = [];     // seconds a cell has been part of an object
    private bool _backgroundReady;
    private float[] _scratch = [];       // per-cell work buffer
    private float[] _blurTmp = [];       // per-cell work buffer of the blur
    private float[] _surround = [];      // brightness of a cell's surroundings
    private float[] _object = [];        // how much a cell belongs to an object (0..1), smoothed over time
    private Drop[] _drops = [];          // background rain: per column, several drops
    private Drop[] _objectDrops = [];    // rain inside objects: its own drops and speeds
    private float[] _columnSpeed = [];   // each column rains at its own pace (cells per second)
    private float[] _objectColumnSpeed = [];

    // brightness 0..255 -> colour, from black through deep and bright green to almost white
    // one ramp per object level (0 = background ... ObjectLevels = full object): objects turn a lighter,
    // paler green, so they read as their own shapes
    private const int ObjectLevels = 8;
    private readonly uint[] _rampRgba = new uint[256 * (ObjectLevels + 1)];
    private readonly uint[] _rampBgra = new uint[256 * (ObjectLevels + 1)];

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
        for (var level = 0; level <= ObjectLevels; level++)
        {
            var o = level / (float)ObjectLevels;
            for (var i = 0; i < 256; i++)
            {
                var t = i / 255f;
                // background: green rises first, then red/blue come in for the hot, almost white heads
                var g = Math.Clamp(t * 1.25f, 0f, 1f);
                var r = Math.Clamp((t - 0.62f) * 2.4f, 0f, 1f) * 0.85f;
                var b = Math.Clamp((t - 0.55f) * 2.0f, 0f, 1f) * 0.75f;
                // objects: a light mint green that reaches white sooner
                var og = Math.Clamp(t * 1.45f, 0f, 1f);
                var or = Math.Clamp(t * 0.55f + Math.Max(0f, t - 0.55f) * 1.4f, 0f, 1f);
                var ob = Math.Clamp(t * 0.6f + Math.Max(0f, t - 0.55f) * 1.3f, 0f, 1f);
                byte R = (byte)((r + (or - r) * o) * 255), G = (byte)((g + (og - g) * o) * 255), B = (byte)((b + (ob - b) * o) * 255);
                _rampRgba[level * 256 + i] = 0xFF000000u | ((uint)B << 16) | ((uint)G << 8) | R;
                _rampBgra[level * 256 + i] = 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | B;
            }
        }
    }

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w != _w || h != _h || GlyphSize != _builtSize) Build(w, h);

        var realDt = _clock.Tick();
        var dt = realDt * Math.Clamp(PlaybackSpeed, 0.2f, 4f);
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

        // 2. objects: cells that stand out from their surroundings
        FindObjects(pixels, stride, layout, realDt);

        // 3. rain, flicker, sparkles
        StepCells(dt);

        // 4. draw: every lit glyph pixel takes the picture's brightness, lifted by rain, outline and sparkle;
        //    objects are lifted and carry their own rain, the background sinks back
        var ramp = layout.R == 0 ? _rampRgba : _rampBgra;
        var mix = (int)(Math.Clamp(Intensity, 0f, 1f) * 256);
        var edgeGain = (int)(Math.Clamp(EdgeGlow, 0f, 1f) * 400);
        var contrast = Math.Clamp(ObjectContrast, 0f, 1f);
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
                var ob = _object[cell];
                var rainBg = RainAt(_drops, col, row);
                var rainObj = RainAt(_objectDrops, col, row);
                var rain = rainBg + (rainObj - rainBg) * ob;               // 0..1, 1 at a drop's head
                var scale = (1f - 0.6f * contrast) + ob * contrast * 1.3f; // background sinks, objects rise
                var lift = ob * contrast;
                var floor = lift * (80 + 100 * rain);                      // objects glow even where they are dark
                var resting = 0.35f + 0.5f * lift;                         // objects stay lit between their drops
                var cellEdgeGain = (int)(edgeGain * (1f + ob * contrast));
                var spark = _sparkle[cell];
                var cellRamp = rampPtr + (int)(lift * ObjectLevels + 0.5f) * 256;
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
                            var lit = baseLight * scale * (resting + 1.05f * rain) + lift * 25;
                            if (lit < floor) lit = floor;
                            v = (int)lit + (edge[i] * cellEdgeGain >> 8);
                            if (rain > 0.92f) v += 60 + (luma[i] >> 1);
                            v += (int)(spark * 200);
                        }
                        else
                        {
                            // between the glyphs: a faint trace of the picture keeps its shapes readable
                            v = (int)((luma[i] >> 4) * scale) + (edge[i] * edgeGain >> 11);
                        }
                        if (v > 255) v = 255;
                        var coded = cellRamp[v];
                        var old = line[x];
                        // keep the canvas's transparency (premultiplied): transparent areas stay see-through
                        var alpha = old >> 24;
                        if (alpha < 255) coded = Premultiply(coded, alpha);
                        line[x] = mix >= 256 ? coded : Lerp(old, coded, mix);
                    }
                }
            }
        }

        // 5. soft glow over the bright code
        if (Glow > 0)
        {
            _glow.Intensity = Math.Clamp(Glow, 0f, 1f) * 0.8f * Math.Clamp(Intensity, 0f, 1f);
            _glow.Apply(bitmap);
        }
        return bitmap;
    }

    /// <summary>
    ///     Marks the cells that belong to an object. Two cues, the stronger wins:
    ///     <list type="bullet">
    ///         <item>
    ///             Change against a learned background (colour and outlines per cell): whatever enters the
    ///             picture, a person in front of a camera, a car, a moving figure, is an object. The background is
    ///             learned where nothing is and kept under an object, so someone standing still stays an object
    ///             for <see cref="ObjectHold" /> seconds and someone walking away leaves no trace. Changes of the whole picture
    ///             (a camera adjusting its exposure) are taken out first.
    ///         </item>
    ///         <item>
    ///             Contrast to the surroundings, for still pictures such as a clock: what is much brighter or
    ///             darker than its neighbourhood.
    ///         </item>
    ///     </list>
    ///     Smoothed in space and over time, so objects are solid and do not flicker.
    /// </summary>
    private unsafe void FindObjects(byte* pixels, int stride, ChannelLayout layout, float dt)
    {
        int cols = _cols, rows = _rows, w = _w, h = _h;
        double total = 0;
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            int sum = 0, edges = 0, n = 0, r = 0, g = 0, b = 0;
            var y1 = Math.Min(h, (row + 1) * _cellH);
            var x1 = Math.Min(w, (col + 1) * _cellW);
            for (var y = row * _cellH; y < y1; y++)
            {
                var line = pixels + y * stride;
                for (var x = col * _cellW; x < x1; x++)
                {
                    var i = y * w + x;
                    sum += _luma[i];
                    edges += _edge[i];
                    var p = line + x * 4;
                    r += p[layout.R];
                    g += p[layout.G];
                    b += p[layout.B];
                    n++;
                }
            }
            var cell = row * cols + col;
            var inv = n > 0 ? 1f / n : 0f;
            _cellLuma[cell] = sum * inv;
            _cellEdge[cell] = edges * inv;
            _cellColor[cell * 3] = r * inv;
            _cellColor[cell * 3 + 1] = g * inv;
            _cellColor[cell * 3 + 2] = b * inv;
            total += _cellLuma[cell];
        }
        var cells = cols * rows;
        var mean = (float)(total / Math.Max(1, cells));

        if (!_backgroundReady)
        {
            Array.Copy(_cellColor, _bgColor, _cellColor.Length);
            Array.Copy(_cellEdge, _bgEdge, _cellEdge.Length);
            _backgroundReady = true;
        }

        // the whole picture brighter or darker (exposure, a light switched on): not an object
        float shiftR = 0, shiftG = 0, shiftB = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            shiftR += _cellColor[cell * 3] - _bgColor[cell * 3];
            shiftG += _cellColor[cell * 3 + 1] - _bgColor[cell * 3 + 1];
            shiftB += _cellColor[cell * 3 + 2] - _bgColor[cell * 3 + 2];
        }
        shiftR /= cells;
        shiftG /= cells;
        shiftB /= cells;

        // surroundings: a wide box blur over the cells
        var radius = Math.Max(3, Math.Min(cols, rows) / 3);
        BoxBlur(_cellLuma, _surround, radius);

        for (var cell = 0; cell < cells; cell++)
        {
            // change against the background
            var dr = Math.Abs(_cellColor[cell * 3] - _bgColor[cell * 3] - shiftR);
            var dg = Math.Abs(_cellColor[cell * 3 + 1] - _bgColor[cell * 3 + 1] - shiftG);
            var db = Math.Abs(_cellColor[cell * 3 + 2] - _bgColor[cell * 3 + 2] - shiftB);
            var change = Math.Max(dr, Math.Max(dg, db));
            var edgeChange = Math.Abs(_cellEdge[cell] - _bgEdge[cell]);
            var moving = Math.Max((change - 12f) / 26f, (edgeChange - 10f) / 25f * 0.8f);

            // contrast to the surroundings (still pictures)
            var local = Math.Abs(_cellLuma[cell] - _surround[cell]);
            var global = Math.Abs(_cellLuma[cell] - mean);
            var still = Math.Max((local - 14f) / 45f, (global - 40f) / 90f * 0.7f);
            still = Math.Max(still, (_cellEdge[cell] - 25f) / 60f * 0.6f);

            _scratch[cell] = Math.Clamp(Math.Max(moving, still * 0.7f), 0f, 1f);
        }
        // close small gaps so an object reads as one shape
        BoxBlur(_scratch, _surround, 1);
        var rise = Math.Clamp(dt * 10f, 0f, 1f);
        var fall = Math.Clamp(dt * 6f, 0f, 1f);
        // learn the background where nothing is; under an object it stays as it was (so a person who walks
        // away leaves no ghost), until the object has stood still for ObjectHold seconds and becomes background
        var hold = Math.Clamp(ObjectHold, 2f, 120f);
        var learnFree = 1f - MathF.Exp(-dt * 1.5f);
        for (var cell = 0; cell < cells; cell++)
        {
            var target = Math.Clamp(Math.Max(_surround[cell] * 1.8f - 0.15f, _scratch[cell]), 0f, 1f);
            _object[cell] += (target - _object[cell]) * (target > _object[cell] ? rise : fall);

            _objectAge[cell] = _object[cell] > 0.3f ? _objectAge[cell] + dt : 0f;
            var learn = _object[cell] <= 0.3f ? learnFree : _objectAge[cell] > hold ? learnFree * 0.5f : 0f;
            _bgColor[cell * 3] += (_cellColor[cell * 3] - _bgColor[cell * 3]) * learn;
            _bgColor[cell * 3 + 1] += (_cellColor[cell * 3 + 1] - _bgColor[cell * 3 + 1]) * learn;
            _bgColor[cell * 3 + 2] += (_cellColor[cell * 3 + 2] - _bgColor[cell * 3 + 2]) * learn;
            _bgEdge[cell] += (_cellEdge[cell] - _bgEdge[cell]) * learn;
        }
    }

    /// <summary>How much the cell at pixel (<paramref name="x" />, <paramref name="y" />) counts as an object (0..1).</summary>
    internal float ObjectAt(int x, int y) => _object.Length == 0 ? 0f : _object[y / _cellH * _cols + x / _cellW];

    /// <summary>Separable box blur over the cell grid, from <paramref name="src" /> into <paramref name="dst" />.</summary>
    private void BoxBlur(float[] src, float[] dst, int radius)
    {
        int cols = _cols, rows = _rows;
        var tmp = _blurTmp;
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            float s = 0;
            int c0 = Math.Max(0, col - radius), c1 = Math.Min(cols - 1, col + radius);
            for (var c = c0; c <= c1; c++) s += src[row * cols + c];
            tmp[row * cols + col] = s / (c1 - c0 + 1);
        }
        for (var col = 0; col < cols; col++)
        for (var row = 0; row < rows; row++)
        {
            float s = 0;
            int r0 = Math.Max(0, row - radius), r1 = Math.Min(rows - 1, row + radius);
            for (var r = r0; r <= r1; r++) s += tmp[r * cols + col];
            dst[row * cols + col] = s / (r1 - r0 + 1);
        }
    }

    /// <summary>Rain brightness at a cell: 1 at a drop's head, fading along its trail above it.</summary>
    private static float RainAt(Drop[] drops, int col, int row)
    {
        var best = 0f;
        for (var k = 0; k < DropsPerColumn; k++)
        {
            ref var d = ref drops[col * DropsPerColumn + k];
            if (d.Length <= 0) continue;
            var behind = d.Y - row; // 0 at the head, positive along the trail
            if (behind < -0.5f || behind > d.Length) continue;
            var v = behind < 0.5f ? 1f : 1f - behind / d.Length;
            if (v > best) best = v;
        }
        return best;
    }

    /// <summary>A column's pace: mostly calm, some quick, a few racing (cells per second).</summary>
    private float NewColumnSpeed() => 3f + 22f * MathF.Pow((float)_random.NextDouble(), 1.7f);

    private void StepDrops(Drop[] drops, float[] columnSpeed, float speedFactor, float rate, float dt)
    {
        var density = Math.Clamp(RainDensity, 0f, 1f);
        for (var col = 0; col < _cols; col++)
        for (var k = 0; k < DropsPerColumn; k++)
        {
            ref var d = ref drops[col * DropsPerColumn + k];
            if (d.Length > 0)
            {
                d.Y += d.Speed * speedFactor * dt;
                if (d.Y - d.Length > _rows) d.Length = 0; // gone below the bottom
            }
            // (re)start: more drops with higher density, staggered above the top
            else if (_random.NextDouble() < dt * rate * (0.05 + density * 0.9) * (k == 0 ? 1 : density))
            {
                if (_random.NextDouble() < 0.35) columnSpeed[col] = NewColumnSpeed(); // columns change pace now and then
                StartDrop(ref d, columnSpeed[col], -(float)_random.NextDouble() * 4);
            }
        }
    }

    private void StartDrop(ref Drop d, float columnSpeed, float y)
    {
        d.Length = 5 + (float)_random.NextDouble() * Math.Max(6, _rows * 0.9f);
        d.Speed = columnSpeed * (0.85f + 0.3f * (float)_random.NextDouble());
        d.Y = y;
    }

    private void StepCells(float dt)
    {
        StepDrops(_drops, _columnSpeed, 1f, 1f, dt);
        // objects: their own rain, denser, at their own speed
        StepDrops(_objectDrops, _objectColumnSpeed, Math.Clamp(ObjectRainSpeed, 0.1f, 3f), 2.5f, dt);

        var sparkRate = Math.Clamp(Sparkles, 0f, 1f) * 0.08f;
        for (var cell = 0; cell < _cellGlyph.Length; cell++)
        {
            _cellFlip[cell] -= dt;
            if (_cellFlip[cell] <= 0)
            {
                _cellGlyph[cell] = Pick();
                _cellFlip[cell] = 0.15f + (float)_random.NextDouble() * 2.5f;
            }
            _sparkle[cell] = Math.Max(0f, _sparkle[cell] - dt * 4f);
            if (sparkRate > 0 && _random.NextDouble() < sparkRate * dt) _sparkle[cell] = 1f;
        }

        // glyphs at the heads change fast, which reads as the code being "written"
        WriteHeads(_drops);
        WriteHeads(_objectDrops);
    }

    private void WriteHeads(Drop[] drops)
    {
        for (var col = 0; col < _cols; col++)
        for (var k = 0; k < DropsPerColumn; k++)
        {
            var d = drops[col * DropsPerColumn + k];
            var row = (int)d.Y;
            if (d.Length > 0 && row >= 0 && row < _rows && _random.NextDouble() < 0.5)
                _cellGlyph[row * _cols + col] = Pick();
        }
    }

    private byte Pick() => _picks[_random.Next(_picks.Length)];

    private void Build(int w, int h)
    {
        _w = w;
        _h = h;
        _builtSize = GlyphSize;
        (_glyphW, _glyphH, _cellW, _cellH) = GlyphSize == RainGlyphSize.Large ? (5, 7, 6, 8) : (3, 5, 4, 6);
        _cols = (w + _cellW - 1) / _cellW;
        _rows = (h + _cellH - 1) / _cellH;
        var cells = _cols * _rows;
        _luma = new byte[w * h];
        _edge = new byte[w * h];
        _cellGlyph = new byte[cells];
        _cellFlip = new float[cells];
        _sparkle = new float[cells];
        _cellLuma = new float[cells];
        _cellEdge = new float[cells];
        _cellColor = new float[cells * 3];
        _bgColor = new float[cells * 3];
        _bgEdge = new float[cells];
        _objectAge = new float[cells];
        _backgroundReady = false;
        _scratch = new float[cells];
        _blurTmp = new float[cells];
        _surround = new float[cells];
        _object = new float[cells];
        _drops = new Drop[_cols * DropsPerColumn];
        _objectDrops = new Drop[_cols * DropsPerColumn];
        _columnSpeed = new float[_cols];
        _objectColumnSpeed = new float[_cols];
        _glyphs = DigitalRainGlyphs.Build(small: GlyphSize == RainGlyphSize.Small);
        for (var i = 0; i < cells; i++)
        {
            _cellGlyph[i] = Pick();
            _cellFlip[i] = (float)_random.NextDouble() * 2f;
        }
        // start with drops spread over the whole height, so the first second is already raining
        for (var col = 0; col < _cols; col++)
        {
            _columnSpeed[col] = NewColumnSpeed();
            _objectColumnSpeed[col] = NewColumnSpeed();
            StartDrop(ref _drops[col * DropsPerColumn], _columnSpeed[col], (float)_random.NextDouble() * _rows * 1.5f);
            StartDrop(ref _objectDrops[col * DropsPerColumn], _objectColumnSpeed[col], (float)_random.NextDouble() * _rows * 1.5f);
            StartDrop(ref _objectDrops[col * DropsPerColumn + 1], _objectColumnSpeed[col], (float)_random.NextDouble() * _rows * 1.5f);
        }
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
