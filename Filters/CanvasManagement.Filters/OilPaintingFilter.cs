using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Optimized oil painting filter for Raspberry Pi performance
/// </summary>
[FilterInfo("Oil Painting",
    "Oil painting effect with brush strokes and color blending using Kuwahara algorithm for impressionist art style",
    "Artistic",
    IconResourceName = "oilpainting.svg")]
public class OilPaintingFilter : ICanvasFilter
{
    private SKBitmap? _scratch;

    [FilterParameter("Brush Radius", "Size of brush strokes", MinValue = 2, MaxValue = 6, DefaultValue = 2)]
    public int BrushRadius { get; set; } = 2;

    public string Name => "Oil Painting";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;
        if (source.BytesPerPixel != 4 || source.Width < 2 || source.Height < 2) return source;

        var scratch = ScratchFor(source);
        var radius = Math.Max(1, Math.Min(3, (int)(BrushRadius * Intensity)));
        var width = source.Width;
        var height = source.Height;
        var stride = source.RowBytes / 4;
        if (stride < width) return source;

        unsafe
        {
            var srcPixels = (uint*)source.GetPixels();
            var dstPixels = (uint*)scratch.GetPixels();
            if (srcPixels == null || dstPixels == null) return source;

            for (var y = 0; y < height; y += 2)
            {
                var row = y * stride;
                for (var x = 0; x < width; x += 2)
                {
                    uint color;
                    if (x < radius || x >= width - radius || y < radius || y >= height - radius)
                        color = srcPixels[row + x];
                    else
                        color = FindBestQuadrant(srcPixels, x, y, stride, width, height, radius);

                    dstPixels[row + x] = color;
                    if (x + 1 < width)
                        dstPixels[row + x + 1] = color;
                }

                if (y + 1 < height)
                    Buffer.MemoryCopy(dstPixels + row, dstPixels + (y + 1) * stride, width * 4, width * 4);
            }
        }

        if (!inPlace)
            return scratch.Copy();

        FilterBitmap.Blit(scratch, source);
        return source;
    }

    private SKBitmap ScratchFor(SKBitmap source)
    {
        if (_scratch == null ||
            _scratch.Width != source.Width ||
            _scratch.Height != source.Height ||
            _scratch.ColorType != source.ColorType ||
            _scratch.AlphaType != source.AlphaType ||
            _scratch.RowBytes != source.RowBytes)
        {
            _scratch?.Dispose();
            _scratch = new SKBitmap(source.Info);
        }

        return _scratch;
    }

    private static unsafe uint FindBestQuadrant(uint* pixels, int cx, int cy, int stride, int width, int height,
        int radius)
    {
        Span<(int sumR, int sumG, int sumB, int count, int variance)> quadrants = stackalloc (int, int, int, int, int)[4];

        SampleQuadrant(pixels, cx, cy, stride, width, height, -radius, 0, 0, radius, ref quadrants[0]);
        SampleQuadrant(pixels, cx, cy, stride, width, height, 0, radius, 0, radius, ref quadrants[1]);
        SampleQuadrant(pixels, cx, cy, stride, width, height, -radius, 0, -radius, 0, ref quadrants[2]);
        SampleQuadrant(pixels, cx, cy, stride, width, height, 0, radius, -radius, 0, ref quadrants[3]);

        var bestIdx = 0;
        var minVariance = int.MaxValue;
        for (var i = 0; i < 4; i++)
        {
            if (quadrants[i].count <= 0) continue;
            if (quadrants[i].variance < minVariance)
            {
                minVariance = quadrants[i].variance;
                bestIdx = i;
            }
        }

        var best = quadrants[bestIdx];
        if (best.count == 0) return pixels[cy * stride + cx];

        var avgR = (byte)(best.sumR / best.count);
        var avgG = (byte)(best.sumG / best.count);
        var avgB = (byte)(best.sumB / best.count);
        return 0xFF000000u | ((uint)avgR << 16) | ((uint)avgG << 8) | avgB;
    }

    private static unsafe void SampleQuadrant(uint* pixels, int cx, int cy, int stride, int width, int height,
        int x1, int x2, int y1, int y2,
        ref (int sumR, int sumG, int sumB, int count, int variance) result)
    {
        var sumR = 0;
        var sumG = 0;
        var sumB = 0;
        var count = 0;

        var yStep = Math.Max(1, (y2 - y1) / 2);
        var xStep = Math.Max(1, (x2 - x1) / 2);
        for (var dy = y1; dy <= y2; dy += yStep)
        for (var dx = x1; dx <= x2; dx += xStep)
        {
            var px = cx + dx;
            var py = cy + dy;
            if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;

            var pixel = pixels[py * stride + px];
            sumR += (int)((pixel >> 16) & 0xFF);
            sumG += (int)((pixel >> 8) & 0xFF);
            sumB += (int)(pixel & 0xFF);
            count++;
        }

        if (count == 0) return;

        var avgR = sumR / count;
        var avgG = sumG / count;
        var avgB = sumB / count;
        var variance = Math.Abs(avgR - avgG) + Math.Abs(avgG - avgB) + Math.Abs(avgB - avgR);
        result = (sumR, sumG, sumB, count, variance);
    }
}
