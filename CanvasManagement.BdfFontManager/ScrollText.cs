using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.BdfFontManager;

/// <summary>
///     Provides smooth scrolling text using BDF fonts
///     Optimized for LED matrix displays with pixel-perfect rendering
/// </summary>
public class ScrollText
{
    private readonly object _bitmapLock = new();
    private readonly BdfFont _font;
    private readonly ICanvas _parentCanvas;
    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private SKColor? _backgroundColor;
    private volatile bool _bitmapNeedsUpdate;

    private SKColor _color;
    private CancellationTokenSource? _cts;
    private volatile bool _isPaused;
    private volatile bool _isRunning;
    private Task? _scrollTask;
    private SKBitmap? _textBitmap;

    internal ScrollText(BdfFont bdfFont, ICanvas canvas)
    {
        _font = bdfFont;
        _parentCanvas = canvas;
    }

    /// <summary>
    ///     Delay between scroll steps in milliseconds
    /// </summary>
    public int Delay { get; set; } = 10;

    /// <summary>
    ///     Text to scroll
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    ///     Background color for scrolling area (null = transparent for alpha compositing)
    /// </summary>
    public SKColor? BackgroundColor { get; set; }

    /// <summary>
    ///     Number of pixels to scroll per step (default: 1 for smooth scrolling)
    ///     Higher values = faster but less smooth
    /// </summary>
    public int ScrollStep { get; set; } = 1;

    /// <summary>
    ///     Gets whether scrolling is currently active
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    ///     Gets whether scrolling is currently paused
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    ///     Starts scrolling text
    /// </summary>
    /// <param name="text">Text to scroll</param>
    /// <param name="color">Text color</param>
    /// <param name="delay">Delay between steps (ms)</param>
    /// <param name="loops">Number of loops (-1 = infinite)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    public void Start(string text, SKColor color, int delay = 10, int loops = -1, SKColor? backgroundColor = null)
    {
        Stop();

        _cts = new CancellationTokenSource();
        Text = text;
        _color = color;
        _backgroundColor = backgroundColor;
        Delay = delay;

        _isRunning = true;
        _isPaused = false;
        _bitmapNeedsUpdate = false;
        _scrollTask = StartScrolling(loops, _cts.Token);
    }

    /// <summary>
    ///     Pauses scrolling without stopping
    /// </summary>
    public async Task SuspendAsync()
    {
        if (_isPaused || !_isRunning) return;

        _isPaused = true;
        await _pauseSemaphore.WaitAsync();
    }

    /// <summary>
    ///     Pauses scrolling without stopping (synchronous)
    /// </summary>
    public void Suspend()
    {
        if (_isPaused || !_isRunning) return;

        _isPaused = true;
        _pauseSemaphore.Wait();
    }

    /// <summary>
    ///     Resumes paused scrolling
    /// </summary>
    public void Resume()
    {
        if (!_isPaused) return;

        _isPaused = false;

        try
        {
            _pauseSemaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already released, ignore
        }
    }

    /// <summary>
    ///     Stops scrolling and clears the canvas
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts?.Cancel();

        // Release semaphore if paused
        if (_isPaused) Resume();

        try
        {
            _scrollTask?.Wait(1000); // Wait max 1 second
        }
        catch (AggregateException)
        {
            // Expected if cancelled
        }

        // Clean up resources
        lock (_bitmapLock)
        {
            _textBitmap?.Dispose();
            _textBitmap = null;
        }

        _cts?.Dispose();
        _cts = null;
        _scrollTask = null;

        // Clear canvas
        var clearColor = _backgroundColor ?? SKColors.Transparent;
        _parentCanvas.Clear(clearColor);
    }

    private async Task StartScrolling(int loops, CancellationToken ct)
    {
        try
        {
            // OPTIMIZATION: Pre-render text bitmap once (reuse for all frames)
            lock (_bitmapLock)
            {
                _textBitmap = _font.RenderText(Text, _color, _backgroundColor);
            }

            SKBitmap? currentBitmap;
            lock (_bitmapLock)
            {
                currentBitmap = _textBitmap;
            }

            if (currentBitmap == null || currentBitmap.Width == 0)
            {
                _isRunning = false;
                return;
            }

            // OPTIMIZATION: Pre-calculate clear color
            var clearColor = _backgroundColor ?? SKColors.Transparent;

            var infinite = loops == -1;
            var remainingLoops = loops;

            while ((infinite || remainingLoops > 0) && !ct.IsCancellationRequested)
            {
                if (!infinite) remainingLoops--;

                // Check if bitmap needs update
                if (_bitmapNeedsUpdate)
                {
                    lock (_bitmapLock)
                    {
                        currentBitmap = _textBitmap;
                        _bitmapNeedsUpdate = false;
                    }

                    if (currentBitmap == null || currentBitmap.Width == 0)
                    {
                        _isRunning = false;
                        return;
                    }
                }

                var frame = currentBitmap;
                if (frame == null || frame.Width == 0)
                {
                    _isRunning = false;
                    return;
                }

                var startX = _parentCanvas.Width;
                var endX = -frame.Width;

                // Scroll from right to left
                for (var x = startX; x >= endX && !ct.IsCancellationRequested; x -= ScrollStep)
                {
                    // Check if bitmap needs update during scroll
                    if (_bitmapNeedsUpdate)
                    {
                        lock (_bitmapLock)
                        {
                            currentBitmap = _textBitmap;
                            _bitmapNeedsUpdate = false;
                        }

                        if (currentBitmap == null || currentBitmap.Width == 0) break;

                        // Recalculate range with new bitmap
                        endX = -currentBitmap.Width;
                    }

                    // MODERN ASYNC: Wait if paused using SemaphoreSlim
                    if (_isPaused)
                    {
                        await _pauseSemaphore.WaitAsync(ct);
                        _pauseSemaphore.Release(); // Release immediately to stay paused
                    }

                    if (!ct.IsCancellationRequested)
                    {
                        // Clear canvas
                        _parentCanvas.Clear(clearColor);

                        // Draw text at current position (thread-safe read with validation)
                        lock (_bitmapLock)
                        {
                            if (_textBitmap != null && _textBitmap.Width > 0 && _textBitmap.Height > 0 &&
                                !_textBitmap.IsNull)
                            {
                                _parentCanvas.DrawBitmap(_textBitmap, x, 0, fitToCanvas: false);
                            }
                            else
                            {
                                // Bitmap invalid - stop scrolling
                                Console.WriteLine("[BDF ScrollText] Invalid bitmap detected, stopping");
                                ct.ThrowIfCancellationRequested();
                            }
                        }

                        // OPTIMIZATION: Use Task.Delay for smooth timing
                        await Task.Delay(Delay, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BDF ScrollText] Error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
            _isPaused = false;
        }
    }

    /// <summary>
    ///     Disposes resources used by ScrollText
    /// </summary>
    public void Dispose()
    {
        Stop();
        _pauseSemaphore?.Dispose();
    }

    /// <summary>
    ///     Updates text without restarting (re-renders bitmap on next frame)
    /// </summary>
    /// <param name="newText">New text to display</param>
    public void UpdateText(string newText)
    {
        if (Text == newText) return;

        Text = newText;

        if (_isRunning)
            lock (_bitmapLock)
            {
                // Dispose old bitmap
                _textBitmap?.Dispose();

                // Re-render bitmap with new text
                _textBitmap = _font.RenderText(Text, _color, _backgroundColor);
                _bitmapNeedsUpdate = true;
            }
    }

    /// <summary>
    ///     Updates text color without restarting
    /// </summary>
    /// <param name="newColor">New text color</param>
    public void UpdateColor(SKColor newColor)
    {
        if (_color == newColor) return;

        _color = newColor;

        if (_isRunning)
            lock (_bitmapLock)
            {
                // Dispose old bitmap
                _textBitmap?.Dispose();

                // Re-render bitmap with new color
                _textBitmap = _font.RenderText(Text, _color, _backgroundColor);
                _bitmapNeedsUpdate = true;
            }
    }
}