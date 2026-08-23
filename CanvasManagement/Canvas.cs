using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement;

public class Canvas : ICanvas, IDisposable
{
    // PERFORMANCE: Resolving a typeface from a family name is relatively expensive;
    // cache resolved typefaces so animated text rendering does not pay that cost per frame.
    private static readonly ConcurrentDictionary<string, SKTypeface> TypefaceCache = new();

    private static SKTypeface GetTypeface(string fontFamily)
    {
        return TypefaceCache.GetOrAdd(fontFamily ?? "Arial", SKTypeface.FromFamilyName);
    }

    // Resolve the analog-clock timezone in a cross-platform way. Windows uses
    // "Central European Standard Time" while Linux/Raspberry Pi use the IANA id
    // "Europe/Berlin". Fall back to local time if neither is available.
    private static readonly TimeZoneInfo ClockTimeZone = ResolveClockTimeZone();

    private static TimeZoneInfo ResolveClockTimeZone()
    {
        foreach (var id in new[] { "Central European Standard Time", "Europe/Berlin" })
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch
            {
                // Try the next identifier
            }

        return TimeZoneInfo.Local;
    }

    private readonly SKBitmap _canvasBackgroundBitmap;
    private readonly SKBitmap _canvasForegroundBitmap;
    private readonly object _drawLock = new(); // Lock for thread-safe drawing

    private readonly SKBitmap _mainBitmap;
    private float _brightness = 1.0f;

    // Cached canvas instance to avoid recreation
    private SKCanvas _cachedBackgroundCanvas;
    private SKPaint _cachedFillPaint;

    // Cached paint instances for common operations
    private SKPaint _cachedStrokePaint;

    // PERFORMANCE: Reusable antialiased stroke paint + path for variable-width stroke methods,
    // avoiding a new SKPaint/SKPath allocation on every draw call.
    private SKPaint _cachedAaStrokePaint;
    private SKPath _cachedReusablePath;

    // Tracks the canvas save point taken before the first clip so ResetClip can correctly
    // restore the clip region (the previous implementation reset the matrix instead).
    private int _clipSaveCount = -1;

    private bool _disposed;
    private float _opacity = 1.0f; // Canvas-level transparency (0.0 = fully transparent, 1.0 = opaque)
    private int _panelColorBits = 14; // Network wall only: 8 (fast) or 14 (video). Default 14.
    private int _zOrder; // Canvas z-order for layering

    internal Canvas(SKBitmap mainBitmap, int xPos, int yPos, int width, int height, string? name = null, int zOrder = 0)
    {
        _mainBitmap = mainBitmap;
        XPos = xPos;
        YPos = yPos;
        _zOrder = zOrder;
        _canvasForegroundBitmap = new SKBitmap(width, height);
        _canvasBackgroundBitmap = new SKBitmap(width, height);

        Width = width;
        Height = height;

        // Generate unique ID and set name
        Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        Name = name ?? Id;

        _cachedBackgroundCanvas = new SKCanvas(_canvasBackgroundBitmap);
        _cachedStrokePaint = new SKPaint { Style = SKPaintStyle.Stroke };
        _cachedFillPaint = new SKPaint { Style = SKPaintStyle.Fill };
        _cachedAaStrokePaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
        _cachedReusablePath = new SKPath();
    }
    
    private int _xPos;
    private int _yPos;

    /// <summary>
    ///     X position of this canvas on the main display. Settable so a canvas can be repositioned live
    ///     (drag-and-drop) without recreating it — the backbuffer is unchanged, only the composite offset moves.
    /// </summary>
    public int XPos
    {
        get { lock (_drawLock) { return _xPos; } }
        internal set { lock (_drawLock) { _xPos = value; } }
    }

    /// <summary>Y position of this canvas on the main display. Settable for live repositioning (see <see cref="XPos" />).</summary>
    public int YPos
    {
        get { lock (_drawLock) { return _yPos; } }
        internal set { lock (_drawLock) { _yPos = value; } }
    }

    public string Name { get; internal set; }
    public string Id { get; }

    public int Width { get; }

    public int Height { get; }
    //public bool NoAutoFrameHandling { get; set; }

    /// <summary>
    ///     Gets or sets the brightness level for this canvas (0.0 = black, 1.0 = full brightness)
    ///     This is applied to this canvas only, independent of global CanvasManager brightness
    /// </summary>
    public float Brightness
    {
        get
        {
            lock (_drawLock)
            {
                return _brightness;
            }
        }
        set
        {
            lock (_drawLock)
            {
                _brightness = Math.Clamp(value, 0.0f, 1.0f);
            }
        }
    }

    /// <summary>
    ///     Gets or sets the opacity level for this canvas (0.0 = invisible, 1.0 = fully visible)
    ///     This is applied during canvas compositing for layered canvas support
    /// </summary>
    public float Opacity
    {
        get
        {
            lock (_drawLock)
            {
                return _opacity;
            }
        }
        set
        {
            lock (_drawLock)
            {
                _opacity = Math.Clamp(value, 0.0f, 1.0f);
            }
        }
    }

    /// <summary>
    ///     Preferred panel colour depth when this canvas is visible on a network LED wall (8 or 14).
    ///     Default 14 so video is not silently quantized. Other outputs ignore this.
    /// </summary>
    public int PanelColorBits
    {
        get
        {
            lock (_drawLock)
            {
                return _panelColorBits;
            }
        }
        set
        {
            lock (_drawLock)
            {
                _panelColorBits = value >= 14 ? 14 : 8;
            }
        }
    }

    /// <summary>
    ///     Gets or sets the Z-order of this canvas (lower values are drawn first, higher values on top)
    ///     Changes to ZOrder require CanvasManager to re-sort canvases
    /// </summary>
    public int ZOrder
    {
        get
        {
            lock (_drawLock)
            {
                return _zOrder;
            }
        }
        set
        {
            lock (_drawLock)
            {
                _zOrder = value;
            }
        }
    }

    /// <summary>
    ///     When true, the compositor blends this canvas over the layers beneath it honouring per-pixel alpha
    ///     (so a transparent background reveals what's underneath) instead of forcing it opaque. Opt-in per
    ///     canvas; the hosted extension must clear to a transparent background for this to have any effect.
    /// </summary>
    public bool TransparentBackground { get; set; }

    public void MakeTransparent()
    {
        GetCanvasBitmap().Erase(SKColors.Transparent);
    }

    public void Show()
    {
        IsHidden = false;
    }

    public void Hide()
    {
        IsHidden = true;
    }

    public bool IsHidden { get; private set; }

    public void Clear()
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            GetCanvasBitmap().Erase(SKColors.Black);
        }
    }

    public void Clear(SKColor color)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            GetCanvasBitmap().Erase(color);
        }
    }

    public void SetPixel(int x, int y, SKColor color)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            GetCanvasBitmap().SetPixel(x, y, color);
        }
    }

    /// <summary>Erases a rectangular region to fully transparent (alpha 0) so the layer beneath shows through.</summary>
    public void ClearRect(int x, int y, int width, int height)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();
            using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
            canvas.DrawRect(x, y, width, height, paint);
        }
    }

    public SKColor GetPixel(int x, int y)
    {
        return GetCanvasBitmap().GetPixel(x, y);
    }

    public IntPtr GetPixels()
    {
        return GetCanvasBitmap().GetPixels();
    }


    public void DrawCircle(float xPos, float yPos, float radius, SKColor color)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();
            _cachedStrokePaint.Color = color;
            canvas.DrawCircle(xPos, yPos, radius, _cachedStrokePaint);
        }
    }


    public void DrawRect(int xPos, int yPos, int width, int height, SKColor color, SKPaintStyle style)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();
            var paint = style == SKPaintStyle.Stroke ? _cachedStrokePaint : _cachedFillPaint;
            paint.Color = color;
            canvas.DrawRect(xPos, yPos, width, height, paint);
        }
    }

    public void Scale(float x, float y)
    {
        using var canvas = new SKCanvas(GetCanvasBitmap());
        canvas.Scale(x, y, Width / 2f, Height / 2f);
        canvas.DrawBitmap(GetCanvasBitmap(), new SKPoint(0, 0));
    }

    public void Rotate(float degrees)
    {
        using var canvas = new SKCanvas(GetCanvasBitmap());
        canvas.RotateDegrees(degrees);
        canvas.DrawBitmap(GetCanvasBitmap(), new SKPoint(0, 0));
    }

    public void DrawLine(int x1Pos, int y1Pos, int x2Pos, int y2Pos, SKColor color)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();
            _cachedStrokePaint.Color = color;
            canvas.DrawLine(x1Pos, y1Pos, x2Pos, y2Pos, _cachedStrokePaint);
        }
    }

    public void DrawPicture(SKPicture picture, float xPos, float yPos)
    {
        var canvas = GetCachedCanvas();
        canvas.DrawPicture(picture, xPos, yPos);
    }


    public void DrawBitmap(SKBitmap bitmap, int xPos, int yPos, int width, int height, float rotateDegrees = 0,
        float scale = 0)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();

            // Save canvas state BEFORE applying transformations
            var saveCount = canvas.Save();

            try
            {
                // Apply transformations
                if (rotateDegrees != 0)
                    canvas.RotateDegrees(rotateDegrees, Width / 2f, Height / 2f);

                if (scale > 0)
                    canvas.Scale(scale, scale, Width / 2f, Height / 2f);

                canvas.DrawBitmap(bitmap, new SKRect(xPos, yPos, xPos + width, yPos + height));
            }
            finally
            {
                // Always restore canvas state to prevent transformation accumulation
                canvas.RestoreToCount(saveCount);
            }
        }
    }

    public SKCanvas DrawBitmap(SKBitmap bitmap, int xPos, int yPos, float rotateDegrees = 0, float scale = 0,
        bool fitToCanvas = false)
    {
        lock (_drawLock)
        {
            if (_disposed) return null!;
            var bitmapToUse = bitmap;
            SKBitmap? resizedBitmap = null;

            try
            {
                if (fitToCanvas)
                {
                    // Use modern SKSamplingOptions instead of obsolete SKFilterQuality
                    var samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    resizedBitmap = bitmap.Resize(new SKImageInfo(Width, Height), samplingOptions);
                    bitmapToUse = resizedBitmap;
                }

                var canvas = GetCachedCanvas();

                // Save canvas state BEFORE applying transformations
                var saveCount = canvas.Save();

                try
                {
                    // Apply transformations
                    if (rotateDegrees != 0)
                        canvas.RotateDegrees(rotateDegrees, Width / 2f, Height / 2f);

                    if (scale > 0)
                        canvas.Scale(scale, scale, Width / 2f, Height / 2f);

                    canvas.DrawBitmap(bitmapToUse, new SKPoint(xPos, yPos));
                }
                finally
                {
                    // Always restore canvas state to prevent transformation accumulation
                    canvas.RestoreToCount(saveCount);
                }

                return canvas;
            }
            finally
            {
                // Dispose resized bitmap to prevent memory leak
                resizedBitmap?.Dispose();
            }
        }
    }

    public void DrawText(string text, int xPos, int yPos, int width, int height, SKPaint paintStyle,
        bool centered = false)
    {
        // Extract text size and typeface before calling GetTextSizeAndBoundaries
        // to avoid accessing obsolete properties multiple times
        var initialSize = 12f; // Default fallback
        SKTypeface? typeface = null;

        // Try to get values if set (these properties are obsolete but we need backward compatibility)
#pragma warning disable CS0618 // Type or member is obsolete
        try
        {
            initialSize = paintStyle.TextSize > 0 ? paintStyle.TextSize : 12f;
        }
        catch
        {
        }

        try
        {
            typeface = paintStyle.Typeface;
        }
        catch
        {
        }
#pragma warning restore CS0618 // Type or member is obsolete

        GetTextSizeAndBoundaries(text, initialSize, typeface, new SKSize(Width, Height),
            out var textSize,
            out var textBoundaries);

        var canvas = GetCachedCanvas();

        // Use modern SKFont for text rendering
        using var font = new SKFont
        {
            Size = textSize,
            Typeface = typeface
        };

        var textWidth = (int)Math.Ceiling(textBoundaries.Width) + (int)Math.Ceiling(textBoundaries.Left);

        if (centered)
        {
            xPos = (width - textWidth) / 2 + xPos;
            yPos = (int)((height + textBoundaries.Top * -1) / 2) + yPos;
        }
        else
        {
            yPos = (int)textBoundaries.Top * -1;
        }

        canvas.DrawText(text, xPos, yPos, SKTextAlign.Left, font, paintStyle);
    }

    public Task DrawAnalogClock(int xPos, int yPos, int radius, SKColor circleColor, SKColor quarterMarkColor,
        SKColor hourHandColor, SKColor minuteHandColor, SKColor secondHandColor, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            int handLength, divisor;
            double clockHelper, angleRadiants, angleSinus, angleCosinus;
            double xSingle, ySingle;
            int xInt, yInt;
            double hourHelper;

            int hourXOld = xPos, hourYOld = yPos, minxold = xPos, minyold = yPos;
            int secxold = xPos, secyold = yPos;

            DrawCircleLegacy(xPos, yPos, radius, circleColor);

            DrawLine(xPos, yPos - radius - 1, xPos, yPos - radius + 2, quarterMarkColor); //Draw zero-mark
            DrawLine(xPos + radius - 1, yPos, xPos + radius + 2, yPos, quarterMarkColor); //Draw 15' - mark
            DrawLine(xPos, yPos + radius - 1, xPos, yPos + radius + 2, quarterMarkColor); //Draw 30' - mark
            DrawLine(xPos - radius - 1, yPos, xPos - radius + 2, yPos, quarterMarkColor); //Draw 45' - mark

            while (!ct.IsCancellationRequested)
            {
                var currentTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, ClockTimeZone);

                //Draw hours
                handLength = radius / 2;
                divisor = 30;
                hourHelper = currentTime.Minute / 60.0; //Make the hourhand smooth
                hourHelper += currentTime.Hour;
                clockHelper = hourHelper * divisor;
                angleRadiants = ConvertToRadians(clockHelper);
                angleSinus = Math.Sin(angleRadiants);
                angleCosinus = Math.Cos(angleRadiants);
                clockHelper = handLength * angleSinus; // X of hand
                xSingle = xPos + clockHelper;
                clockHelper = handLength * angleCosinus; // Y of the hand
                ySingle = yPos - clockHelper;
                xSingle = Math.Round(xSingle);
                ySingle = Math.Round(ySingle);
                xInt = (int)xSingle;
                yInt = (int)ySingle;

                if ((xInt != hourXOld) | (yInt != hourYOld))
                    DrawLine(xPos, yPos, hourXOld, hourYOld, SKColors.Black);

                DrawLine(xPos, yPos, xInt, yInt, hourHandColor);

                hourXOld = xInt;
                hourYOld = yInt;

                //Draw Minutes
                handLength = radius - 4;
                divisor = 6;
                clockHelper = currentTime.Minute * divisor;
                angleRadiants = ConvertToRadians(clockHelper);
                angleSinus = Math.Sin(angleRadiants);
                angleCosinus = Math.Cos(angleRadiants);
                clockHelper = handLength * angleSinus; // X of hand
                xSingle = xPos + clockHelper;
                clockHelper = handLength * angleCosinus; // Y of the hand
                ySingle = yPos - clockHelper;
                xSingle = Math.Round(xSingle);
                ySingle = Math.Round(ySingle);
                xInt = (int)xSingle;
                yInt = (int)ySingle;

                if ((xInt != minxold) | (yInt != minyold))
                    DrawLine(xPos, yPos, minxold, minyold, SKColors.Black);

                DrawLine(xPos, yPos, xInt, yInt, minuteHandColor);

                minxold = xInt;
                minyold = yInt;

                //Draw Seconds
                handLength = radius - 2;
                divisor = 6;
                clockHelper = currentTime.Second * divisor;
                angleRadiants = ConvertToRadians(clockHelper);
                angleSinus = Math.Sin(angleRadiants);
                angleCosinus = Math.Cos(angleRadiants);
                clockHelper = handLength * angleSinus; // X of hand
                xSingle = xPos + clockHelper;
                clockHelper = handLength * angleCosinus; // Y of the hand
                ySingle = yPos - clockHelper;
                xSingle = Math.Round(xSingle);
                ySingle = Math.Round(ySingle);
                xInt = (int)xSingle;
                yInt = (int)ySingle;

                if ((xInt != secxold) | (yInt != secyold))
                    DrawLine(xPos, yPos, secxold, secyold, SKColors.Black);

                DrawLine(xPos, yPos, xInt, yInt, secondHandColor);

                secxold = xInt;
                secyold = yInt;

                try
                {
                    await Task.Delay(50, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    /// <summary>
    ///     Allows extensions to signal they want to update the canvas with a complete frame.
    ///     This ensures atomic updates - the entire frame is written before CanvasManager reads it.
    /// </summary>
    /// <param name="completedFrame">The completed frame to copy to canvas background</param>
    public void SubmitCompletedFrame(SKBitmap completedFrame)
    {
        if (completedFrame == null) return;
        if (completedFrame.Width != Width || completedFrame.Height != Height) return;

        lock (_drawLock)
        {
            // A late frame from an extension's render thread can arrive while the canvas is being disposed
            // during a live resize. Disposing native bitmaps now also takes _drawLock, so checking _disposed
            // here prevents an unsafe MemoryCopy into freed memory (was a segfault).
            if (_disposed) return;

            // Direct memory copy of the complete frame
            var srcPixels = completedFrame.GetPixels();
            var dstPixels = _canvasBackgroundBitmap.GetPixels();
            var totalBytes = Width * Height * 4;

            unsafe
            {
                Buffer.MemoryCopy(
                    (void*)srcPixels,
                    (void*)dstPixels,
                    totalBytes,
                    totalBytes);
            }
        }
    }

    public void Dispose()
    {
        // Take _drawLock so disposal can't race with an in-flight draw/SubmitCompletedFrame from an
        // extension's render thread (that race freed native bitmaps mid-copy -> segfault on resize).
        lock (_drawLock)
        {
            if (_disposed) return;
            _disposed = true;

            _canvasForegroundBitmap.Dispose();
            _canvasBackgroundBitmap.Dispose();
            _cachedBackgroundCanvas.Dispose();
            _cachedStrokePaint.Dispose();
            _cachedFillPaint.Dispose();
            _cachedAaStrokePaint.Dispose();
            _cachedReusablePath.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    internal SKBitmap GetCanvasBitmap()
    {
        // Always return BACKGROUND bitmap for drawing operations
        // Animations should always draw to the background buffer
        return _canvasBackgroundBitmap;
    }

    /// <summary>
    ///     Gets the foreground bitmap (the completed frame for display)
    ///     Used by CanvasManager for alpha compositing
    /// </summary>
    internal SKBitmap GetForegroundBitmap()
    {
        // Return the FOREGROUND bitmap
        // This contains the completed frame ready for display
        return _canvasForegroundBitmap;
    }

    private SKCanvas GetCachedCanvas()
    {
        // Always return BACKGROUND canvas for drawing operations
        return _cachedBackgroundCanvas;
    }

    internal void PrepareNextFrame()
    {
        // If hidden, don't copy anything to the main bitmap
        // Just skip this canvas entirely
        if (IsHidden)
        {
            // Optionally erase to keep canvas clean
            GetCanvasBitmap().Erase(SKColors.Transparent);
            return; // Don't copy to main bitmap!
        }

        // Always composite from FOREGROUND buffer (what was just copied from background)
        CopyWithOffset(_mainBitmap, _canvasForegroundBitmap,
            new SKPoint(XPos, YPos));
    }

    internal void CopyWithOffset(SKBitmap mainBitmap, SKBitmap canvasBitmap, SKPoint targetLocation)
    {
        // SIMD-optimized copy for compositing to main canvas
        var srcPixels = canvasBitmap.GetPixels();
        var dstPixels = mainBitmap.GetPixels();

        var srcWidth = canvasBitmap.Width;
        var srcHeight = canvasBitmap.Height;
        var dstWidth = mainBitmap.Width;
        var xOffset = (int)targetLocation.X;
        var yOffset = (int)targetLocation.Y;

        unsafe
        {
            var src = (uint*)srcPixels;
            var dst = (uint*)dstPixels;

            var vectorSize = Vector<uint>.Count;

            for (var y = 0; y < srcHeight; y++)
            {
                var dstY = y + yOffset;
                if (dstY < 0 || dstY >= mainBitmap.Height) continue;

                var srcRowStart = y * srcWidth;
                var dstRowStart = dstY * dstWidth + xOffset;

                // Check bounds for the entire row
                if (xOffset >= 0 && xOffset + srcWidth <= dstWidth)
                {
                    // Full row copy with SIMD
                    var vectorCount = srcWidth / vectorSize;
                    var remainder = srcWidth % vectorSize;

                    // SIMD copy
                    for (var i = 0; i < vectorCount; i++)
                    {
                        var offset = i * vectorSize;
                        var srcVector = Unsafe.ReadUnaligned<Vector<uint>>(src + srcRowStart + offset);
                        Unsafe.WriteUnaligned(dst + dstRowStart + offset, srcVector);
                    }

                    // Copy remainder
                    var remainderStart = vectorCount * vectorSize;
                    for (var i = 0; i < remainder; i++)
                        dst[dstRowStart + remainderStart + i] = src[srcRowStart + remainderStart + i];
                }
                else
                {
                    // Boundary case - copy pixel by pixel with bounds checking
                    for (var x = 0; x < srcWidth; x++)
                    {
                        var dstX = x + xOffset;
                        if (dstX < 0 || dstX >= dstWidth) continue;

                        var srcIndex = srcRowStart + x;
                        var dstIndex = dstRowStart + x;

                        dst[dstIndex] = src[srcIndex];
                    }
                }
            }
        }
    }

    private static void GetTextSizeAndBoundaries(string text, float initialSize, SKTypeface? typeface,
        SKSize boundingSize,
        out float textSize, out SKRect boundingRectangle)
    {
        // Use modern SKFont for text measurement
        using var font = new SKFont
        {
            Size = initialSize,
            Typeface = typeface
        };

        boundingRectangle = new SKRect();
        font.MeasureText(text, out boundingRectangle);

        while ((int)Math.Ceiling(boundingRectangle.Height) > (int)Math.Ceiling(boundingSize.Height))
        {
            font.Size -= 0.5f;
            font.MeasureText(text, out boundingRectangle);
        }

        textSize = font.Size;
    }

    public void DrawCircleLegacy(int x, int y, int radius, SKColor color)
    {
        var f = 1 - radius;
        var ddfX = 0;
        var ddfY = -2 * radius;
        var x1 = 0;
        var y1 = radius;

        SetPixel(x, y + radius, color);
        SetPixel(x, y - radius, color);
        SetPixel(x + radius, y, color);
        SetPixel(x - radius, y, color);

        while (x1 < y1)
        {
            if (f >= 0)
            {
                y1--;
                ddfY += 2;
                f += ddfY;
            }

            x1++;
            ddfX += 2;
            f += ddfX; // + 1
            f++;

            SetPixel(x + x1, y + y1, color);
            SetPixel(x - x1, y + y1, color);
            SetPixel(x + x1, y - y1, color);
            SetPixel(x - x1, y - y1, color);
            SetPixel(x + y1, y + x1, color);
            SetPixel(x - y1, y + x1, color);
            SetPixel(x + y1, y - x1, color);
            SetPixel(x - y1, y - x1, color);
        }
    }

    private double ConvertToRadians(double angle)
    {
        return Math.PI / 180 * angle;
    }


    internal void CopyBackgroundToForeground()
    {
        // Lock to ensure no drawing happens during copy
        lock (_drawLock)
        {
            if (_disposed) return;

            // SIMD-optimized copy for better performance on ARM (Raspberry Pi)
            var srcPixels = _canvasBackgroundBitmap.GetPixels();
            var dstPixels = _canvasForegroundBitmap.GetPixels();

            var width = _canvasBackgroundBitmap.Width;
            var height = _canvasBackgroundBitmap.Height;
            var totalPixels = width * height;

            unsafe
            {
                var src = (uint*)srcPixels;
                var dst = (uint*)dstPixels;

                // Check if brightness adjustment is needed
                var needsBrightness = _brightness < 1.0f;

                if (!needsBrightness)
                {
                    // Fast path: No brightness adjustment, use SIMD
                    var vectorSize = Vector<uint>.Count;
                    var vectorCount = totalPixels / vectorSize;
                    var remainder = totalPixels % vectorSize;

                    // Process vectors (4 pixels at a time on ARM NEON)
                    for (var i = 0; i < vectorCount; i++)
                    {
                        var offset = i * vectorSize;
                        var srcVector = Unsafe.ReadUnaligned<Vector<uint>>(src + offset);
                        Unsafe.WriteUnaligned(dst + offset, srcVector);
                    }

                    // Handle remaining pixels
                    var remainderStart = vectorCount * vectorSize;
                    for (var i = 0; i < remainder; i++) dst[remainderStart + i] = src[remainderStart + i];
                }
                else if (_brightness <= 0.0f)
                {
                    // Fast path: Black out the canvas
                    var blackPixel = 0xFF000000u; // Black with full alpha
                    for (var i = 0; i < totalPixels; i++) dst[i] = blackPixel;
                }
                else
                {
                    // Apply brightness during copy
                    var srcBytes = (byte*)src;
                    var dstBytes = (byte*)dst;
                    var totalBytes = totalPixels * 4;

                    for (var i = 0; i < totalBytes; i += 4)
                    {
                        // Apply brightness to RGB, preserve alpha
                        dstBytes[i] = (byte)(srcBytes[i] * _brightness); // Red
                        dstBytes[i + 1] = (byte)(srcBytes[i + 1] * _brightness); // Green
                        dstBytes[i + 2] = (byte)(srcBytes[i + 2] * _brightness); // Blue
                        dstBytes[i + 3] = srcBytes[i + 3]; // Alpha (unchanged)
                    }
                }
            }

            // Apply canvas-level opacity if needed
            if (_opacity < 1.0f) ApplyCanvasOpacity(_canvasForegroundBitmap, _opacity);
        }
    }

    /// <summary>
    ///     Applies canvas-level opacity to the foreground bitmap
    /// </summary>
    private void ApplyCanvasOpacity(SKBitmap bitmap, float opacity)
    {
        if (opacity >= 1.0f) return;

        unsafe
        {
            var pixelsAddr = bitmap.GetPixels();
            var pixelCount = bitmap.Width * bitmap.Height;
            var ptr = (byte*)pixelsAddr.ToPointer();

            if (opacity <= 0.0f)
                // Fast path: make fully invisible
                for (var i = 0; i < pixelCount; i++)
                    ptr[i * 4 + 3] = 0; // Set alpha to 0
            else
                // BGRA8888 format: [B, G, R, A] per pixel
                for (var i = 0; i < pixelCount; i++)
                {
                    var offset = i * 4;
                    var alpha = ptr[offset + 3];

                    // Multiply alpha by opacity
                    ptr[offset + 3] = (byte)(alpha * opacity);
                }
        }
    }

    // ===== NEW GRAPHICAL SUPPORT METHODS =====

    #region Bitmap Operations

    public void DrawBitmapWithAlpha(SKBitmap bitmap, int xPos, int yPos, byte alpha = 255)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
            canvas.DrawBitmap(bitmap, new SKPoint(xPos, yPos), paint);
        }
    }

    public void DrawBitmapRegion(SKBitmap bitmap, SKRectI sourceRect, SKRect destRect)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            canvas.DrawBitmap(bitmap, sourceRect, destRect);
        }
    }

    public void DrawBitmapTinted(SKBitmap bitmap, int xPos, int yPos, SKColor tintColor)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            using var paint = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateBlendMode(tintColor, SKBlendMode.Modulate)
            };
            canvas.DrawBitmap(bitmap, new SKPoint(xPos, yPos), paint);
        }
    }

    #endregion

    #region Shape Drawing

    public void DrawFilledCircle(float xPos, float yPos, float radius, SKColor color)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            _cachedFillPaint.Color = color;
            canvas.DrawCircle(xPos, yPos, radius, _cachedFillPaint);
        }
    }

    public void DrawEllipse(float xPos, float yPos, float radiusX, float radiusY, SKColor color,
        SKPaintStyle style = SKPaintStyle.Stroke)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            var paint = style == SKPaintStyle.Stroke ? _cachedStrokePaint : _cachedFillPaint;
            paint.Color = color;
            canvas.DrawOval(new SKRect(xPos - radiusX, yPos - radiusY, xPos + radiusX, yPos + radiusY), paint);
        }
    }

    public void DrawRoundRect(int xPos, int yPos, int width, int height, float cornerRadius, SKColor color,
        SKPaintStyle style = SKPaintStyle.Stroke)
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();
            var paint = style == SKPaintStyle.Stroke ? _cachedStrokePaint : _cachedFillPaint;
            paint.Color = color;
            canvas.DrawRoundRect(new SKRect(xPos, yPos, xPos + width, yPos + height), cornerRadius, cornerRadius,
                paint);
        }
    }

    public void DrawPolygon(SKPoint[] points, SKColor color, SKPaintStyle style = SKPaintStyle.Stroke)
    {
        if (points == null || points.Length < 3) return;

        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            var paint = style == SKPaintStyle.Stroke ? _cachedStrokePaint : _cachedFillPaint;
            paint.Color = color;

            using var path = new SKPath();
            path.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++) path.LineTo(points[i]);
            path.Close();
            canvas.DrawPath(path, paint);
        }
    }

    public void DrawTriangle(SKPoint p1, SKPoint p2, SKPoint p3, SKColor color,
        SKPaintStyle style = SKPaintStyle.Stroke)
    {
        DrawPolygon(new[] { p1, p2, p3 }, color, style);
    }

    public void DrawLine(int x1Pos, int y1Pos, int x2Pos, int y2Pos, SKColor color, float strokeWidth)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            _cachedAaStrokePaint.Color = color;
            _cachedAaStrokePaint.StrokeWidth = strokeWidth;
            canvas.DrawLine(x1Pos, y1Pos, x2Pos, y2Pos, _cachedAaStrokePaint);
        }
    }

    public void DrawPolyline(SKPoint[] points, SKColor color, float strokeWidth = 1)
    {
        if (points == null || points.Length < 2) return;

        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            _cachedAaStrokePaint.Color = color;
            _cachedAaStrokePaint.StrokeWidth = strokeWidth;

            _cachedReusablePath.Reset();
            _cachedReusablePath.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++) _cachedReusablePath.LineTo(points[i]);
            canvas.DrawPath(_cachedReusablePath, _cachedAaStrokePaint);
        }
    }

    public void DrawArc(float xPos, float yPos, float radius, float startAngle, float sweepAngle, SKColor color,
        float strokeWidth = 1)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            _cachedAaStrokePaint.Color = color;
            _cachedAaStrokePaint.StrokeWidth = strokeWidth;

            var rect = new SKRect(xPos - radius, yPos - radius, xPos + radius, yPos + radius);
            canvas.DrawArc(rect, startAngle, sweepAngle, false, _cachedAaStrokePaint);
        }
    }

    #endregion

    #region Text Operations

    public void DrawText(string text, int xPos, int yPos, SKColor color, float fontSize = 12,
        string fontFamily = "Arial")
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();
            using var font = new SKFont
            {
                Size = fontSize,
                Typeface = GetTypeface(fontFamily)
            };
            using var paint = new SKPaint
            {
                Color = color,
                IsAntialias = false // Disable anti-aliasing for better LED display quality
            };
            canvas.DrawText(text, xPos, yPos, SKTextAlign.Left, font, paint);
        }
    }

    public void DrawTextAligned(string text, int xPos, int yPos, int width, int height, SKColor color,
        float fontSize = 12, SKTextAlign alignment = SKTextAlign.Left, string fontFamily = "Arial")
    {
        lock (_drawLock)
        {
            if (_disposed) return;
            var canvas = GetCachedCanvas();
            using var font = new SKFont
            {
                Size = fontSize,
                Typeface = GetTypeface(fontFamily)
            };
            using var paint = new SKPaint
            {
                Color = color,
                IsAntialias = false // Disable anti-aliasing for better LED display quality
            };

            float x = xPos;
            if (alignment == SKTextAlign.Center)
                x = xPos + width / 2f;
            else if (alignment == SKTextAlign.Right)
                x = xPos + width;

            var bounds = new SKRect();
            font.MeasureText(text, out bounds);
            var y = yPos + (height - bounds.Height) / 2 - bounds.Top;

            canvas.DrawText(text, x, y, alignment, font, paint);
        }
    }

    public SKRect MeasureText(string text, float fontSize, string fontFamily = "Arial")
    {
        using var font = new SKFont
        {
            Size = fontSize,
            Typeface = GetTypeface(fontFamily)
        };
        var bounds = new SKRect();
        font.MeasureText(text, out bounds);
        return bounds;
    }

    #endregion

    #region Gradient and Effects

    public void FillGradient(int xPos, int yPos, int width, int height, SKColor startColor, SKColor endColor,
        bool horizontal = true)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            var startPoint = new SKPoint(xPos, yPos);
            var endPoint = horizontal
                ? new SKPoint(xPos + width, yPos)
                : new SKPoint(xPos, yPos + height);

            using var shader = SKShader.CreateLinearGradient(
                startPoint, endPoint,
                new[] { startColor, endColor },
                null,
                SKShaderTileMode.Clamp);

            using var paint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawRect(new SKRect(xPos, yPos, xPos + width, yPos + height), paint);
        }
    }

    public void FillRadialGradient(float centerX, float centerY, float radius, SKColor centerColor, SKColor edgeColor)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(centerX, centerY),
                radius,
                new[] { centerColor, edgeColor },
                null,
                SKShaderTileMode.Clamp);

            using var paint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawCircle(centerX, centerY, radius, paint);
        }
    }

    public void DrawWithShadow(Action drawAction, float offsetX, float offsetY, float blurRadius, SKColor shadowColor)
    {
        if (drawAction == null) return;

        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();

            // Save state
            var saveCount = canvas.Save();

            try
            {
                // Apply shadow filter
                using var shadowPaint = new SKPaint
                {
                    ImageFilter = SKImageFilter.CreateDropShadow(offsetX, offsetY, blurRadius, blurRadius, shadowColor)
                };

                canvas.SaveLayer(shadowPaint);
                drawAction();
                canvas.Restore();
            }
            finally
            {
                canvas.RestoreToCount(saveCount);
            }
        }
    }

    #endregion

    #region Path Operations

    public void DrawBezier(SKPoint start, SKPoint control1, SKPoint control2, SKPoint end, SKColor color,
        float strokeWidth = 1)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            _cachedAaStrokePaint.Color = color;
            _cachedAaStrokePaint.StrokeWidth = strokeWidth;

            _cachedReusablePath.Reset();
            _cachedReusablePath.MoveTo(start);
            _cachedReusablePath.CubicTo(control1, control2, end);
            canvas.DrawPath(_cachedReusablePath, _cachedAaStrokePaint);
        }
    }

    public void DrawPath(SKPath path, SKColor color, SKPaintStyle style = SKPaintStyle.Stroke, float strokeWidth = 1)
    {
        if (path == null) return;

        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            _cachedAaStrokePaint.Color = color;
            _cachedAaStrokePaint.Style = style;
            _cachedAaStrokePaint.StrokeWidth = strokeWidth;
            canvas.DrawPath(path, _cachedAaStrokePaint);
            // Restore default stroke style for subsequent stroke callers
            _cachedAaStrokePaint.Style = SKPaintStyle.Stroke;
        }
    }

    #endregion

    #region Transformation Operations

    public void Translate(float dx, float dy)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            canvas.Translate(dx, dy);
        }
    }

    public int SaveTransform()
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            return canvas.Save();
        }
    }

    public void RestoreTransform(int saveCount)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            canvas.RestoreToCount(saveCount);
        }
    }

    public void ResetTransform()
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            canvas.ResetMatrix();
        }
    }

    #endregion

    #region Image Manipulation

    public void ApplyColorFilter(SKColorFilter filter)
    {
        if (filter == null) return;

        lock (_drawLock)
        {
            var bitmap = GetCanvasBitmap();
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { ColorFilter = filter };
            canvas.DrawBitmap(bitmap, 0, 0, paint);
        }
    }

    public void InvertColors()
    {
        var colorMatrix = new float[]
        {
            -1, 0, 0, 0, 255,
            0, -1, 0, 0, 255,
            0, 0, -1, 0, 255,
            0, 0, 0, 1, 0
        };

        using var filter = SKColorFilter.CreateColorMatrix(colorMatrix);
        ApplyColorFilter(filter);
    }

    public void Grayscale()
    {
        var colorMatrix = new[]
        {
            0.21f, 0.72f, 0.07f, 0, 0,
            0.21f, 0.72f, 0.07f, 0, 0,
            0.21f, 0.72f, 0.07f, 0, 0,
            0, 0, 0, 1, 0
        };

        using var filter = SKColorFilter.CreateColorMatrix(colorMatrix);
        ApplyColorFilter(filter);
    }

    /// <summary>
    ///     Adjusts brightness using color matrix (DEPRECATED - use Brightness property instead)
    /// </summary>
    /// <param name="amount">Amount to adjust (-1.0 to 1.0)</param>
    [Obsolete(
        "Use the Brightness property instead for proper brightness control. This method uses color matrix addition which can cause clipping. " +
        "Example: canvas.Brightness = 0.5f for 50% brightness.")]
    public void AdjustBrightness(float amount)
    {
        // Clamp amount to -1.0 to 1.0
        amount = Math.Max(-1.0f, Math.Min(1.0f, amount));
        var brightness = amount * 255;

        var colorMatrix = new[]
        {
            1, 0, 0, 0, brightness,
            0, 1, 0, 0, brightness,
            0, 0, 1, 0, brightness,
            0, 0, 0, 1, 0
        };

        using var filter = SKColorFilter.CreateColorMatrix(colorMatrix);
        ApplyColorFilter(filter);
    }

    public void AdjustContrast(float amount)
    {
        // Clamp amount to 0.0 to 2.0 (1.0 = normal)
        amount = Math.Max(0.0f, Math.Min(2.0f, amount));
        var translate = (1.0f - amount) / 2.0f * 255;

        var colorMatrix = new[]
        {
            amount, 0, 0, 0, translate,
            0, amount, 0, 0, translate,
            0, 0, amount, 0, translate,
            0, 0, 0, 1, 0
        };

        using var filter = SKColorFilter.CreateColorMatrix(colorMatrix);
        ApplyColorFilter(filter);
    }

    public void ApplyBlur(float sigma)
    {
        lock (_drawLock)
        {
            var bitmap = GetCanvasBitmap();
            using var canvas = new SKCanvas(bitmap);
            using var filter = SKImageFilter.CreateBlur(sigma, sigma);
            using var paint = new SKPaint { ImageFilter = filter };
            canvas.DrawBitmap(bitmap, 0, 0, paint);
        }
    }

    #endregion

    #region Special Drawing

    public void DrawGrid(int cellWidth, int cellHeight, SKColor color, float strokeWidth = 1)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            using var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth
            };

            // Draw vertical lines
            for (var x = 0; x <= Width; x += cellWidth) canvas.DrawLine(x, 0, x, Height, paint);

            // Draw horizontal lines
            for (var y = 0; y <= Height; y += cellHeight) canvas.DrawLine(0, y, Width, y, paint);
        }
    }

    public void FillPattern(SKBitmap patternBitmap, SKShaderTileMode tileMode = SKShaderTileMode.Repeat)
    {
        if (patternBitmap == null) return;

        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            using var shader = SKShader.CreateBitmap(patternBitmap, tileMode, tileMode);
            using var paint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(0, 0, Width, Height, paint);
        }
    }

    public void ClipRect(int xPos, int yPos, int width, int height)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            // Save the pre-clip state once so ResetClip can restore it later.
            if (_clipSaveCount < 0) _clipSaveCount = canvas.Save();
            canvas.ClipRect(new SKRect(xPos, yPos, xPos + width, yPos + height));
        }
    }

    public void ClipCircle(float xPos, float yPos, float radius)
    {
        lock (_drawLock)
        {
            var canvas = GetCachedCanvas();
            if (_clipSaveCount < 0) _clipSaveCount = canvas.Save();
            using var path = new SKPath();
            path.AddCircle(xPos, yPos, radius);
            canvas.ClipPath(path);
        }
    }

    public void ResetClip()
    {
        lock (_drawLock)
        {
            // Restore the canvas to the state captured before the first clip was applied,
            // which removes the active clipping region. (Previously this reset the matrix,
            // which did not actually clear clips.)
            if (_clipSaveCount < 0) return;
            var canvas = GetCachedCanvas();
            canvas.RestoreToCount(_clipSaveCount);
            _clipSaveCount = -1;
        }
    }

    #endregion

    #region BDF Font Operations

    public void DrawBdfText(string text, int xPos, int yPos, SKColor color, string? fontName = null,
        SKColor? backgroundColor = null)
    {
        try
        {
            var bdfManager = GetBdfFontManagerInternal();

            // Set font if specified
            if (!string.IsNullOrWhiteSpace(fontName)) bdfManager.SetFont(fontName);

            // Render text
            bdfManager.RenderText(text, new SKPoint(xPos, yPos), color, backgroundColor);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BDF] Error drawing text: {ex.Message}");
            // Fallback to Skia rendering
            DrawText(text, xPos, yPos + 12, color);
        }
    }

    public SKSize MeasureBdfText(string text, string? fontName = null)
    {
        try
        {
            var bdfManager = GetBdfFontManagerInternal();

            if (!string.IsNullOrWhiteSpace(fontName)) bdfManager.SetFont(fontName);

            // Use GetTextSize method
            return bdfManager.GetTextSize(text);
        }
        catch
        {
            return new SKSize(text.Length * 8, 12); // Fallback estimate
        }
    }

    public SKBitmap? RenderBdfTextToBitmap(string text, SKColor color, string? fontName = null,
        SKColor? backgroundColor = null)
    {
        try
        {
            var bdfManager = GetBdfFontManagerInternal();

            if (!string.IsNullOrWhiteSpace(fontName)) bdfManager.SetFont(fontName);

            // Use BdfFont.RenderText to get pre-rendered bitmap
            var font = BdfFontRegistry.GetFont(fontName);
            return font.RenderText(text, color, backgroundColor);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BDF] Error rendering text to bitmap: {ex.Message}");
            return null;
        }
    }

    private BdfFontManager.BdfFontManager? _bdfFontManager;

    /// <summary>
    ///     Internal method to get BDF font manager
    ///     Used by CanvasBdfExtensions to access advanced features
    /// </summary>
    internal BdfFontManager.BdfFontManager GetBdfFontManagerInternal()
    {
        if (_bdfFontManager == null) _bdfFontManager = new BdfFontManager.BdfFontManager(this);
        return _bdfFontManager;
    }

    #endregion

    #region Extension Discovery

    /// <summary>
    ///     Gets the default extension discovery service instance
    /// </summary>
    public static IExtensionDiscovery ExtensionDiscovery => ExtensionDiscoveryService.Default;

    /// <summary>
    ///     Creates a dynamically accessible extension wrapper
    /// </summary>
    public DynamicExtension? CreateDynamicExtension(string typeName)
    {
        var instance = ExtensionDiscovery.Create(this, typeName);
        return instance != null ? new DynamicExtension(instance) : null;
    }

    /// <summary>
    ///     Creates a dynamically accessible extension wrapper by display name
    /// </summary>
    public DynamicExtension? CreateDynamicExtensionByDisplayName(string displayName)
    {
        var instance = ExtensionDiscovery.CreateByDisplayName(this, displayName);
        return instance != null ? new DynamicExtension(instance) : null;
    }

    #endregion
}