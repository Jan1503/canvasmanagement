using SkiaSharp;

namespace CanvasManagement.Filters;

internal static class FilterBitmap
{
    /// <summary>
    ///     Copy pixels into <paramref name="to"/> without replacing its buffer.
    ///     <see cref="SKBitmap.CopyTo(SKBitmap)"/> swaps the destination's pixels and detaches any
    ///     <see cref="SKCanvas"/> already drawing into that bitmap, which kills the compositor.
    /// </summary>
    public static unsafe void Blit(SKBitmap from, SKBitmap to)
    {
        var width = Math.Min(from.Width, to.Width);
        var height = Math.Min(from.Height, to.Height);
        if (width <= 0 || height <= 0) return;

        var bpp = from.BytesPerPixel;
        if (bpp <= 0 || to.BytesPerPixel != bpp) return;

        var row = width * bpp;
        var srcStride = from.RowBytes;
        var dstStride = to.RowBytes;
        if (srcStride < row || dstStride < row) return;

        var src = (byte*)from.GetPixels();
        var dst = (byte*)to.GetPixels();
        if (src == null || dst == null) return;

        for (var y = 0; y < height; y++)
            Buffer.MemoryCopy(src + y * srcStride, dst + y * dstStride, row, row);
    }
}
