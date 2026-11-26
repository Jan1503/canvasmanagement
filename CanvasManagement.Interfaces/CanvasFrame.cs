using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     An off-screen frame for code that draws step by step in the immediate style (clear, rect, bitmap,
///     text, ...): everything goes into a private buffer and <see cref="Present"/> hands the finished frame to
///     the canvas in one piece, so the wall never shows half a frame. Follows the canvas size.
///     Not thread-safe: use it from the one task that draws.
/// </summary>
public sealed class CanvasFrame(ICanvas target) : IDisposable
{
    private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { Style = SKPaintStyle.Stroke };
    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;

    // The last image fitted to the canvas: animations draw the same image many times.
    private SKBitmap? _fitSource;
    private SKBitmap? _fitted;

    public int Width => target.Width;
    public int Height => target.Height;

    /// <summary>The Skia canvas of the frame, for drawing this class has no shortcut for.</summary>
    public SKCanvas Surface
    {
        get
        {
            var w = Math.Max(1, target.Width);
            var h = Math.Max(1, target.Height);
            if (_bitmap == null || _bitmap.Width != w || _bitmap.Height != h)
            {
                FreeSurface();
                _bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                _canvas = new SKCanvas(_bitmap);
            }

            return _canvas!;
        }
    }

    public void Clear() => Surface.Clear(SKColors.Black);

    public void Clear(SKColor color) => Surface.Clear(color);

    public void SetPixel(int x, int y, SKColor color)
    {
        _ = Surface;
        if ((uint)x < (uint)_bitmap!.Width && (uint)y < (uint)_bitmap.Height) _bitmap.SetPixel(x, y, color);
    }

    public void DrawRect(int x, int y, int width, int height, SKColor color, SKPaintStyle style)
    {
        var paint = style == SKPaintStyle.Stroke ? _stroke : _fill;
        paint.Color = color;
        Surface.DrawRect(x, y, width, height, paint);
    }

    public void DrawLine(int x1, int y1, int x2, int y2, SKColor color, float strokeWidth = 1)
    {
        _stroke.Color = color;
        _stroke.StrokeWidth = strokeWidth;
        Surface.DrawLine(x1, y1, x2, y2, _stroke);
        _stroke.StrokeWidth = 0;
    }

    public void DrawCircle(float x, float y, float radius, SKColor color)
    {
        _stroke.Color = color;
        Surface.DrawCircle(x, y, radius, _stroke);
    }

    /// <summary>The bitmap stretched to (x, y, width, height), turned/scaled about the canvas centre.</summary>
    public void DrawBitmap(SKBitmap bitmap, int x, int y, int width, int height, float rotateDegrees = 0, float scale = 0)
    {
        var c = Surface;
        var save = c.Save();
        Transform(c, rotateDegrees, scale);
        c.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height));
        c.RestoreToCount(save);
    }

    /// <summary>The bitmap at (x, y), optionally fitted to the canvas size first, turned/scaled about the centre.</summary>
    public void DrawBitmap(SKBitmap bitmap, int x, int y, float rotateDegrees = 0, float scale = 0, bool fitToCanvas = false)
    {
        var c = Surface;
        var image = fitToCanvas ? Fitted(bitmap) : bitmap;
        var save = c.Save();
        Transform(c, rotateDegrees, scale);
        c.DrawBitmap(image, new SKPoint(x, y));
        c.RestoreToCount(save);
    }

    /// <summary>BDF text with its top-left corner at (x, y), like <see cref="ICanvas.DrawBdfText"/>.</summary>
    public void DrawBdfText(string text, int x, int y, SKColor color, string? fontName = null, SKColor? backgroundColor = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var rendered = target.RenderBdfTextToBitmap(text, color, fontName, backgroundColor);
        if (rendered != null) Surface.DrawBitmap(rendered, x, y);
    }

    /// <summary>Hands the current frame to the canvas.</summary>
    public void Present()
    {
        if (_canvas == null || _bitmap == null) return;
        _canvas.Flush();
        target.SubmitCompletedFrame(_bitmap);
    }

    private void Transform(SKCanvas c, float rotateDegrees, float scale)
    {
        if (rotateDegrees != 0) c.RotateDegrees(rotateDegrees, Width / 2f, Height / 2f);
        if (scale > 0) c.Scale(scale, scale, Width / 2f, Height / 2f);
    }

    private SKBitmap Fitted(SKBitmap source)
    {
        if (ReferenceEquals(source, _fitSource) && _fitted != null && _fitted.Width == Width && _fitted.Height == Height)
            return _fitted;
        _fitted?.Dispose();
        _fitted = source.Resize(new SKImageInfo(Math.Max(1, Width), Math.Max(1, Height)),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        _fitSource = source;
        return _fitted;
    }

    private void FreeSurface()
    {
        _canvas?.Dispose();
        _canvas = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public void Dispose()
    {
        FreeSurface();
        _fitted?.Dispose();
        _fitted = null;
        _fitSource = null;
        _fill.Dispose();
        _stroke.Dispose();
    }
}
