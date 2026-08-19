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
    [FilterParameter("Brush Radius", "Size of brush strokes", MinValue = 2, MaxValue = 6, DefaultValue = 2)]
    public int BrushRadius { get; set; } = 2; // Reduced max from 8 to 6

    public string Name => "Oil Painting";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var result = new SKBitmap(source.Width, source.Height);
        var radius = Math.Max(1, Math.Min(3, (int)(BrushRadius * Intensity))); // Max 3 instead of 5

        unsafe
        {
            var srcPixels = (uint*)source.GetPixels().ToPointer();
            var dstPixels = (uint*)result.GetPixels().ToPointer();
            var width = source.Width;
            var height = source.Height;

            // Process every 2nd pixel for speed, interpolate the rest
            for (var y = 0; y < height; y += 2)
            {
                for (var x = 0; x < width; x += 2)
                {
                    var idx = y * width + x;

                    // Edge pixels - just copy
                    if (x < radius || x >= width - radius || y < radius || y >= height - radius)
                    {
                        dstPixels[idx] = srcPixels[idx];
                        continue;
                    }

                    // Simplified Kuwahara - only check 4 quadrants
                    var color = FindBestQuadrant(srcPixels, x, y, width, radius);
                    dstPixels[idx] = color;

                    // Fill adjacent pixel
                    if (x + 1 < width)
                        dstPixels[idx + 1] = color;
                }

                // Fill next row by copying current row
                if (y + 1 < height)
                    for (var x = 0; x < width; x++)
                        dstPixels[(y + 1) * width + x] = dstPixels[y * width + x];
            }
        }

        if (inPlace)
        {
            result.CopyTo(source);
            result.Dispose();
            return source;
        }

        return result;
    }

    private unsafe uint FindBestQuadrant(uint* pixels, int cx, int cy, int width, int radius)
    {
        // Fast Kuwahara: sample 9 points per quadrant instead of all pixels
        var quadrants = new (int sumR, int sumG, int sumB, int count, int variance)[]
        {
            (0, 0, 0, 0, 0), // Top-left
            (0, 0, 0, 0, 0), // Top-right
            (0, 0, 0, 0, 0), // Bottom-left
            (0, 0, 0, 0, 0) // Bottom-right
        };

        // Sample points instead of scanning all pixels
        SampleQuadrant(pixels, cx, cy, width, -radius, 0, 0, radius, ref quadrants[0]);
        SampleQuadrant(pixels, cx, cy, width, 0, radius, 0, radius, ref quadrants[1]);
        SampleQuadrant(pixels, cx, cy, width, -radius, 0, -radius, 0, ref quadrants[2]);
        SampleQuadrant(pixels, cx, cy, width, 0, radius, -radius, 0, ref quadrants[3]);

        // Find quadrant with lowest variance
        var bestIdx = 0;
        var minVariance = quadrants[0].variance;

        for (var i = 1; i < 4; i++)
            if (quadrants[i].count > 0 && quadrants[i].variance < minVariance)
            {
                minVariance = quadrants[i].variance;
                bestIdx = i;
            }

        // Return average color of best quadrant
        var best = quadrants[bestIdx];
        if (best.count == 0) return pixels[cy * width + cx];

        var avgR = (byte)(best.sumR / best.count);
        var avgG = (byte)(best.sumG / best.count);
        var avgB = (byte)(best.sumB / best.count);

        return 0xFF000000u | ((uint)avgR << 16) | ((uint)avgG << 8) | avgB;
    }

    private unsafe void SampleQuadrant(uint* pixels, int cx, int cy, int width,
        int x1, int x2, int y1, int y2,
        ref (int sumR, int sumG, int sumB, int count, int variance) result)
    {
        var sumR = 0;
        var sumG = 0;
        var sumB = 0;
        var count = 0;

        // Sample 9 points in a 3x3 grid within quadrant
        for (var dy = y1; dy <= y2; dy += Math.Max(1, (y2 - y1) / 2))
        for (var dx = x1; dx <= x2; dx += Math.Max(1, (x2 - x1) / 2))
        {
            var pixel = pixels[(cy + dy) * width + cx + dx];
            var r = (int)((pixel >> 16) & 0xFF);
            var g = (int)((pixel >> 8) & 0xFF);
            var b = (int)(pixel & 0xFF);

            sumR += r;
            sumG += g;
            sumB += b;
            count++;
        }

        if (count == 0) return;

        // Fast variance approximation using range
        var avgR = sumR / count;
        var avgG = sumG / count;
        var avgB = sumB / count;

        var variance = Math.Abs(avgR - avgG) + Math.Abs(avgG - avgB) + Math.Abs(avgB - avgR);

        result = (sumR, sumG, sumB, count, variance);
    }
}