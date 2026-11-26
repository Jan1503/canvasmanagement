using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>Colour sets of <see cref="RetroPaletteFilter" />.</summary>
public enum RetroPalette
{
    /// <summary>Four shades of green, like an early handheld console screen.</summary>
    HandheldGreen,
    /// <summary>Black, cyan, magenta, white: the classic four-colour PC graphics mode.</summary>
    FourColorPc,
    /// <summary>Shades of orange on black, like an amber terminal.</summary>
    Amber,
    /// <summary>A sunset: dark purple through hot pink to warm yellow.</summary>
    Synthwave,
    /// <summary>Warm brown tones of an old photo.</summary>
    Sepia,
    /// <summary>Black and white only (with dithering it shows grey through patterns).</summary>
    OneBit
}

/// <summary>
///     Recolours the picture with a small retro palette. Shade palettes map brightness to steps, the
///     four-colour mode picks the nearest colour; an ordered 4×4 dither keeps gradients readable, which looks
///     especially good on a coarse LED grid. Lookup tables, so one cheap pass per frame.
/// </summary>
[FilterInfo("Retro Palette",
    "Recolour with a retro palette: handheld green, four-colour PC, amber, synthwave, sepia or 1-bit",
    "Retro",
    IconResourceName = "retropalette.svg")]
public sealed class RetroPaletteFilter : ICanvasFilter
{
    [FilterParameter("Palette", "The colour set", DefaultValue = RetroPalette.HandheldGreen)]
    public RetroPalette Palette { get; set; } = RetroPalette.HandheldGreen;

    [FilterParameter("Dither", "Ordered dither pattern for smoother gradients", DefaultValue = true)]
    public bool Dither { get; set; } = true;

    public string Name => "Retro Palette";
    public float Intensity { get; set; } = 1f;
    public bool Enabled { get; set; } = true;

    private static readonly int[] Bayer4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    private static readonly SKColor[] FourColor =
        [new(0, 0, 0), new(85, 255, 255), new(255, 85, 255), new(255, 255, 255)];

    private RetroPalette _built = (RetroPalette)(-1);
    private SKColor[] _shades = [];   // brightness steps for the shade palettes
    private byte[] _nearest = [];     // 15-bit RGB -> palette index (four-colour mode)

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        if (_built != Palette) Build(Palette);

        var layout = ChannelLayout.Of(bitmap);
        var mix = (int)(Math.Clamp(Intensity, 0f, 1f) * 256);
        var dither = Dither;
        var steps = _shades.Length;
        var fourColor = Palette == RetroPalette.FourColorPc;
        var w = bitmap.Width;
        var h = bitmap.Height;
        var stride = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();

        for (var y = 0; y < h; y++)
        {
            var line = pixels + y * stride;
            for (var x = 0; x < w; x++)
            {
                var p = line + x * 4;
                var a = p[layout.A];
                if (a == 0) continue;
                int r = p[layout.R], g = p[layout.G], b = p[layout.B];
                SKColor c;
                if (fourColor)
                {
                    // nearest of four colours: a small per-pixel nudge (-32..+28) before the lookup dithers
                    var d = dither ? (Bayer4[(y & 3) * 4 + (x & 3)] - 8) * 4 : 0;
                    var rr = Math.Clamp(r + d, 0, 255);
                    var gg = Math.Clamp(g + d, 0, 255);
                    var bb = Math.Clamp(b + d, 0, 255);
                    c = FourColor[_nearest[((rr >> 3) << 10) | ((gg >> 3) << 5) | (bb >> 3)]];
                }
                else
                {
                    // ordered dither in the middle of a step only (threshold 64..184): values near a step stay
                    // solid, so a dark background does not turn into a noisy pattern on the wall
                    var lum = (r * 54 + g * 183 + b * 19) >> 8;
                    var threshold = dither ? 64 + Bayer4[(y & 3) * 4 + (x & 3)] * 8 : 127;
                    c = _shades[Math.Clamp((lum * (steps - 1) + threshold) / 255, 0, steps - 1)];
                }
                // mix with the original by Intensity; keep premultiplied (channel <= alpha)
                var nr = r + (((c.Red * a / 255) - r) * mix >> 8);
                var ng = g + (((c.Green * a / 255) - g) * mix >> 8);
                var nb = b + (((c.Blue * a / 255) - b) * mix >> 8);
                p[layout.R] = (byte)Math.Clamp(nr, 0, a);
                p[layout.G] = (byte)Math.Clamp(ng, 0, a);
                p[layout.B] = (byte)Math.Clamp(nb, 0, a);
            }
        }
        return bitmap;
    }

    private void Build(RetroPalette palette)
    {
        _built = palette;
        _shades = palette switch
        {
            RetroPalette.HandheldGreen => [new(15, 56, 15), new(48, 98, 48), new(139, 172, 15), new(155, 188, 15)],
            RetroPalette.Amber => Ramp(new SKColor(0, 0, 0), new SKColor(255, 176, 0), 6),
            // a sunset ramp, ordered by brightness (the mapping goes by brightness)
            RetroPalette.Synthwave =>
                [new(18, 4, 38), new(64, 12, 104), new(150, 18, 140), new(240, 40, 130), new(255, 120, 90), new(255, 214, 110)],
            RetroPalette.Sepia => Ramp(new SKColor(30, 18, 8), new SKColor(255, 236, 196), 7),
            RetroPalette.OneBit => [new(0, 0, 0), new(255, 255, 255)],
            _ => [new(0, 0, 0), new(255, 255, 255)]
        };
        if (palette == RetroPalette.FourColorPc && _nearest.Length == 0)
        {
            _nearest = new byte[1 << 15];
            for (var i = 0; i < _nearest.Length; i++)
            {
                var r = ((i >> 10) & 31) * 255 / 31;
                var g = ((i >> 5) & 31) * 255 / 31;
                var b = (i & 31) * 255 / 31;
                var best = 0;
                var bestDist = int.MaxValue;
                for (var k = 0; k < FourColor.Length; k++)
                {
                    var dr = r - FourColor[k].Red;
                    var dg = g - FourColor[k].Green;
                    var db = b - FourColor[k].Blue;
                    var dist = 2 * dr * dr + 4 * dg * dg + 3 * db * db; // rough perceptual weights
                    if (dist < bestDist) { bestDist = dist; best = k; }
                }
                _nearest[i] = (byte)best;
            }
        }
    }

    private static SKColor[] Ramp(SKColor from, SKColor to, int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var t = i / (float)(count - 1);
            return new SKColor(
                (byte)(from.Red + (to.Red - from.Red) * t),
                (byte)(from.Green + (to.Green - from.Green) * t),
                (byte)(from.Blue + (to.Blue - from.Blue) * t));
        }).ToArray();
}
