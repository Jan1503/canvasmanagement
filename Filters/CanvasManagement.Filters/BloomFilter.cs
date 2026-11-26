using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Bright parts glow: everything above a brightness threshold is blurred at half resolution and added on
///     top. On an LED wall this reads as light spilling around clocks, sprites and neon text.
/// </summary>
[FilterInfo("Bloom",
    "Bright areas glow softly into their surroundings, like light on a misty night",
    "Image Enhancement",
    IconResourceName = "bloom.svg")]
public sealed class BloomFilter : ICanvasFilter, IDisposable
{
    [FilterParameter("Threshold", "Brightness from which a pixel starts to glow (0 = everything, 1 = nothing)",
        MinValue = 0.0, MaxValue = 0.95, DefaultValue = 0.55)]
    public float Threshold { get; set; } = 0.55f;

    [FilterParameter("Radius", "How far the glow reaches, in pixels", MinValue = 1, MaxValue = 16, DefaultValue = 5)]
    public int Radius { get; set; } = 5;

    public string Name => "Bloom";
    public float Intensity { get; set; } = 0.7f;
    public bool Enabled { get; set; } = true;

    private SKBitmap? _bright;      // half size: the parts that glow
    private SKBitmap? _glow;        // half size: the blurred glow
    private SKCanvas? _glowCanvas;
    private readonly SKPaint _blurPaint = new();
    private float _paintRadius = -1;

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        var fw = bitmap.Width;
        var fh = bitmap.Height;
        var w = Math.Max(1, fw / 2);
        var h = Math.Max(1, fh / 2);
        if (_bright == null || _bright.Width != w || _bright.Height != h || _bright.ColorType != bitmap.ColorType)
        {
            DisposeBuffers();
            var info = new SKImageInfo(w, h, bitmap.ColorType, SKAlphaType.Premul);
            _bright = new SKBitmap(info);
            _glow = new SKBitmap(info);
            _glowCanvas = new SKCanvas(_glow);
        }

        // 1. Half-size bright pass: average 2x2, keep what is above the threshold (faded in from it).
        var layout = ChannelLayout.Of(bitmap);
        var threshold = (int)(Math.Clamp(Threshold, 0f, 0.95f) * 255);
        var range = Math.Max(1, 255 - threshold);
        var full = (byte*)bitmap.GetPixels();
        var fullStride = bitmap.RowBytes;
        var bright = (byte*)_bright.GetPixels();
        var halfStride = _bright.RowBytes;
        for (var y = 0; y < h; y++)
        {
            var row0 = full + y * 2 * fullStride;
            var row1 = y * 2 + 1 < fh ? row0 + fullStride : row0;
            var outRow = bright + y * halfStride;
            for (var x = 0; x < w; x++)
            {
                var a = row0 + x * 8;
                var b = x * 2 + 1 < fw ? a + 4 : a;
                var c = row1 + x * 8;
                var d = x * 2 + 1 < fw ? c + 4 : c;
                var r = (a[layout.R] + b[layout.R] + c[layout.R] + d[layout.R]) >> 2;
                var g = (a[layout.G] + b[layout.G] + c[layout.G] + d[layout.G]) >> 2;
                var bl = (a[layout.B] + b[layout.B] + c[layout.B] + d[layout.B]) >> 2;
                var lum = (r * 54 + g * 183 + bl * 19) >> 8;
                var o = outRow + x * 4;
                if (lum <= threshold)
                {
                    *(uint*)o = 0;
                    continue;
                }
                var k = ((lum - threshold) << 8) / range;
                o[layout.R] = (byte)((r * k) >> 8);
                o[layout.G] = (byte)((g * k) >> 8);
                o[layout.B] = (byte)((bl * k) >> 8);
                o[layout.A] = 255;
            }
        }

        // 2. Blur the glow at half resolution (four times cheaper than full size, and softer).
        var radius = Math.Clamp(Radius, 1, 16) / 2f;
        if (Math.Abs(radius - _paintRadius) > 0.01f)
        {
            _blurPaint.ImageFilter?.Dispose();
            _blurPaint.ImageFilter = SKImageFilter.CreateBlur(radius, radius, SKShaderTileMode.Decal);
            _paintRadius = radius;
        }
        _glowCanvas!.Clear(SKColors.Transparent);
        _glowCanvas.DrawBitmap(_bright, 0, 0, _blurPaint);
        _glowCanvas.Flush();

        // 3. Add it back: bilinear 2x upsample and additive blend in one pass (fixed point).
        var gain = (int)(Math.Clamp(Intensity, 0f, 1f) * 256);
        var glow = (byte*)_glow!.GetPixels();
        for (var y = 0; y < fh; y++)
        {
            // source position (y - 0.5) / 2 in half-size pixels, as 8.8 fixed point
            var sy = Math.Max(0, (y << 7) - 64);
            var y0 = Math.Min(h - 1, sy >> 8);
            var y1 = Math.Min(h - 1, y0 + 1);
            var fy = sy & 255;
            var r0 = glow + y0 * halfStride;
            var r1 = glow + y1 * halfStride;
            var outRow = full + y * fullStride;
            for (var x = 0; x < fw; x++)
            {
                var sx = Math.Max(0, (x << 7) - 64);
                var x0 = Math.Min(w - 1, sx >> 8);
                var x1 = Math.Min(w - 1, x0 + 1);
                var fx = sx & 255;
                // most of the wall has no glow nearby: nothing to add
                if ((*(uint*)(r0 + x0 * 4) | *(uint*)(r0 + x1 * 4) | *(uint*)(r1 + x0 * 4) | *(uint*)(r1 + x1 * 4)) == 0)
                    continue;
                var p = outRow + x * 4;
                for (var ch = 0; ch < 4; ch++)
                {
                    var top = r0[x0 * 4 + ch] * (256 - fx) + r0[x1 * 4 + ch] * fx;
                    var bottom = r1[x0 * 4 + ch] * (256 - fx) + r1[x1 * 4 + ch] * fx;
                    var v = (top * (256 - fy) + bottom * fy) >> 16;
                    var sum = p[ch] + ((v * gain) >> 8);
                    p[ch] = (byte)(sum > 255 ? 255 : sum);
                }
            }
        }
        return bitmap;
    }

    private void DisposeBuffers()
    {
        _glowCanvas?.Dispose();
        _bright?.Dispose();
        _glow?.Dispose();
        _glowCanvas = null;
        _bright = null;
        _glow = null;
    }

    public void Dispose()
    {
        DisposeBuffers();
        _blurPaint.ImageFilter?.Dispose();
        _blurPaint.Dispose();
    }
}
