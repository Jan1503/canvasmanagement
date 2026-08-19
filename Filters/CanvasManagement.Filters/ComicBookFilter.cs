using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Optimized comic book filter for Raspberry Pi performance
/// </summary>
[FilterInfo("Comic Book",
    "Transform images into comic book art with posterized colors, bold outlines, and optional halftone dots",
    "Artistic",
    IconResourceName = "comicbook.svg")]
public class ComicBookFilter : ICanvasFilter
{
    [FilterParameter("Color Levels", "Number of color levels for posterization", MinValue = 2, MaxValue = 8,
        DefaultValue = 4)]
    public int ColorLevels { get; set; } = 4; // Reduced max from 16 to 8

    [FilterParameter("Edge Threshold", "Sensitivity for edge detection", MinValue = 20, MaxValue = 100,
        DefaultValue = 50)]
    public byte EdgeThreshold { get; set; } = 50; // Higher threshold for faster processing

    [FilterParameter("Enable Halftone", "Add halftone dot pattern")]
    public bool EnableHalftone { get; set; } = false;

    public string Name => "Comic Book";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Step 1: Posterize colors
        PosterizeColors(bitmap);

        // Step 2: Fast edge detection (no copy needed)
        if (Intensity > 0.4f) DrawFastEdges(bitmap);

        // Step 3: Optional halftone
        if (EnableHalftone && Intensity > 0.7f) AddSubtleHalftone(bitmap);

        return bitmap;
    }

    private void PosterizeColors(SKBitmap bitmap)
    {
        var levels = Math.Max(2, Math.Min(6, (int)(ColorLevels * Intensity)));
        var step = 256f / levels;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var pixelCount = bitmap.Width * bitmap.Height;

            for (var i = 0; i < pixelCount; i++)
            {
                var pixel = pixels[i];
                var a = (byte)((pixel >> 24) & 0xFF);
                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Fast quantization
                r = (byte)(Math.Floor(r / step) * step);
                g = (byte)(Math.Floor(g / step) * step);
                b = (byte)(Math.Floor(b / step) * step);

                pixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    private void DrawFastEdges(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var threshold = EdgeThreshold * Intensity;

            // Fast 4-neighbor edge detection
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var idx = y * width + x;
                var center = GetLuma(pixels[idx]);

                // Check only 4 neighbors for speed
                var diff = Math.Abs(center - GetLuma(pixels[idx - width])) +
                           Math.Abs(center - GetLuma(pixels[idx + width])) +
                           Math.Abs(center - GetLuma(pixels[idx - 1])) +
                           Math.Abs(center - GetLuma(pixels[idx + 1]));

                if (diff > threshold * 2)
                {
                    var a = (byte)((pixels[idx] >> 24) & 0xFF);
                    pixels[idx] = (uint)((a << 24) | 0x000000);
                }
            }
        }
    }

    private void AddSubtleHalftone(SKBitmap bitmap)
    {
        var dotSpacing = 6; // Wider spacing for performance

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            for (var y = 0; y < height; y += dotSpacing)
            for (var x = 0; x < width; x += dotSpacing)
            {
                var idx = y * width + x;
                if (idx >= width * height) continue;

                var luma = GetLuma(pixels[idx]);

                // Only in mid-tones
                if (luma > 60 && luma < 180)
                {
                    var darken = (180 - luma) / 15;

                    var pixel = pixels[idx];
                    var a = (byte)((pixel >> 24) & 0xFF);
                    var r = (byte)Math.Max(0, ((pixel >> 16) & 0xFF) - darken);
                    var g = (byte)Math.Max(0, ((pixel >> 8) & 0xFF) - darken);
                    var b = (byte)Math.Max(0, (pixel & 0xFF) - darken);
                    pixels[idx] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
                }
            }
        }
    }

    private int GetLuma(uint pixel)
    {
        var r = (pixel >> 16) & 0xFF;
        var g = (pixel >> 8) & 0xFF;
        var b = pixel & 0xFF;
        return (int)(r * 0.299f + g * 0.587f + b * 0.114f);
    }
}