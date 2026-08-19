using SkiaSharp;

namespace CanvasManagement.Extension.AnimatedGifPlayer;

/// <summary>
///     Represents a single frame in an animated GIF
/// </summary>
public class GifAnimationFrame : IDisposable
{
    private bool _disposed;

    internal GifAnimationFrame(SKBitmap bitmap, int duration)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Duration = duration;
    }

    /// <summary>
    ///     Duration to display this frame in milliseconds
    /// </summary>
    public int Duration { get; }

    /// <summary>
    ///     The bitmap image for this frame
    /// </summary>
    public SKBitmap Bitmap { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;

        Bitmap?.Dispose();
        Bitmap = null!;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~GifAnimationFrame()
    {
        Dispose();
    }
}