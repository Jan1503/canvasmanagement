using System.Diagnostics;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement;

public class CanvasManager : IDisposable
{
    // PERFORMANCE: Reuse paint object instead of creating per frame
    private readonly SKPaint _compositePaint;

    // Composites a canvas treating its content as fully opaque (forces alpha = 1 via a colour matrix). The
    // physical display has no alpha and extension output alpha is unreliable, so we layer by the canvas's
    // own Opacity instead of per-pixel alpha. Without this, content vanishes the moment a second
    // (overlapping) canvas forces the alpha-compositing path.
    private readonly SKPaint _opaqueCompositePaint;

    // Alpha-aware composite paint for canvases in transparent-background mode: honours the source's
    // per-pixel alpha (SrcOver) so transparent regions reveal the layer beneath instead of forcing opaque.
    private readonly SKPaint _alphaCompositePaint;
    private readonly List<ICanvasFilter> _filters = new();
    private readonly SKCanvas _mainCanvas;
    private readonly SKBitmap _mainCanvasBitmap;
    private readonly object _swapLock = new();
    private float _brightness = 1.0f;

    // PERFORMANCE: Pre-sorted render snapshots reused across frames to avoid per-frame LINQ allocations.
    // Rebuilt only when the canvas set or z-order changes (tracked via _canvasesDirty).
    private readonly List<Canvas> _renderSnapshot = new();
    private readonly List<Canvas> _visibleSnapshot = new();
    private bool _canvasesDirty = true;

    // PERFORMANCE: 256-entry lookup table for global brightness (rebuilt only when brightness changes).
    private readonly byte[] _brightnessLut = new byte[256];
    private float _brightnessLutValue = -1f;

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _displayRefresh;
    private volatile bool _fullyDisposed;
    private volatile bool _stopped;

    // Frame pacing: target frame interval in microseconds, consumed by the drift-free scheduler in
    // Run(). Read via Volatile each frame so it can be changed live (e.g. from the settings API).
    private int _frameIntervalMicros = 1_000_000 / 60; // 60 fps default

    public CanvasManager(int width, int height)
    {
        _mainCanvasBitmap = new SKBitmap(width, height);
        _mainCanvas = new SKCanvas(_mainCanvasBitmap);

        // PERFORMANCE: Create paint once and reuse
        _compositePaint = new SKPaint
        {
            BlendMode = SKBlendMode.SrcOver,
            IsAntialias = false // Disable AA for LED displays - better for low resolution
        };

        _opaqueCompositePaint = MakeOpaquePaint(1f);
        _alphaCompositePaint = new SKPaint { BlendMode = SKBlendMode.SrcOver, IsAntialias = false };
    }

    /// <summary>
    ///     A composite paint that forces the source alpha to <paramref name="alpha" /> (RGB preserved), so a
    ///     canvas is layered by its Opacity rather than its (unreliable) per-pixel alpha.
    /// </summary>
    private static SKPaint MakeOpaquePaint(float alpha)
    {
        return new SKPaint
        {
            BlendMode = SKBlendMode.SrcOver,
            IsAntialias = false,
            ColorFilter = SKColorFilter.CreateColorMatrix(new[]
            {
                1f, 0, 0, 0, 0,
                0, 1f, 0, 0, 0,
                0, 0, 1f, 0, 0,
                0, 0, 0, 0, alpha
            })
        };
    }

    /// <summary>
    ///     A composite paint that KEEPS the source's per-pixel alpha (scaled by <paramref name="opacity" />),
    ///     so a transparent-background canvas blends over what is beneath it (SrcOver).
    /// </summary>
    private static SKPaint MakeAlphaPaint(float opacity)
    {
        return new SKPaint
        {
            BlendMode = SKBlendMode.SrcOver,
            IsAntialias = false,
            ColorFilter = SKColorFilter.CreateColorMatrix(new[]
            {
                1f, 0, 0, 0, 0,
                0, 1f, 0, 0, 0,
                0, 0, 1f, 0, 0,
                0, 0, 0, opacity, 0
            })
        };
    }

    /// <summary>
    ///     Gets or sets the global brightness level (0.0 = black, 1.0 = full brightness)
    ///     This is applied after all filters and affects the entire display
    /// </summary>
    public float Brightness
    {
        get
        {
            lock (_swapLock)
            {
                return _brightness;
            }
        }
        set
        {
            lock (_swapLock)
            {
                _brightness = Math.Clamp(value, 0.0f, 1.0f);
            }
        }
    }

    public List<(int Key, Canvas Value)> GetCanvases { get; } = new();

    /// <summary>
    ///     Width of the display / main render surface in pixels.
    /// </summary>
    public int Width => _mainCanvasBitmap.Width;

    /// <summary>
    ///     Height of the display / main render surface in pixels.
    /// </summary>
    public int Height => _mainCanvasBitmap.Height;

    #region Filter Discovery

    /// <summary>
    ///     Gets the default filter discovery service instance
    /// </summary>
    public static IFilterDiscovery FilterDiscovery => FilterDiscoveryService.Default;

    #endregion

    #region Filter Management

    public void AddFilter(ICanvasFilter filter)
    {
        lock (_swapLock)
        {
            _filters.Add(filter);
        }
    }

    public void RemoveFilter(ICanvasFilter filter)
    {
        lock (_swapLock)
        {
            _filters.Remove(filter);
        }
    }

    public void ClearFilters()
    {
        lock (_swapLock)
        {
            _filters.Clear();
        }
    }

    public IReadOnlyList<ICanvasFilter> GetFilters()
    {
        lock (_swapLock)
        {
            return _filters.AsReadOnly();
        }
    }

    public int GetFilterCount()
    {
        lock (_swapLock)
        {
            return _filters.Count;
        }
    }

    public ICanvasFilter? GetFilterAt(int index)
    {
        lock (_swapLock)
        {
            if (index < 0 || index >= _filters.Count)
                return null;
            return _filters[index];
        }
    }

    public ICanvasFilter? GetFilterByName(string name)
    {
        lock (_swapLock)
        {
            return _filters.FirstOrDefault(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool HasFilterOfType<T>() where T : ICanvasFilter
    {
        lock (_swapLock)
        {
            return _filters.Any(f => f is T);
        }
    }

    public IEnumerable<T> GetFiltersOfType<T>() where T : ICanvasFilter
    {
        lock (_swapLock)
        {
            return _filters.OfType<T>().ToList();
        }
    }

    /// <summary>
    ///     Gets only the currently active (enabled) filters
    /// </summary>
    public IReadOnlyList<ICanvasFilter> GetActiveFilters()
    {
        lock (_swapLock)
        {
            return _filters.Where(f => f.Enabled).ToList().AsReadOnly();
        }
    }

    #endregion

    #region Canvas Management

    public Canvas GetCanvas(int zOrder, string? name = null)
    {
        return GetCanvas(0, 0, _mainCanvasBitmap.Width, _mainCanvasBitmap.Height, zOrder, name);
    }

    public Canvas GetCanvas(int xPos, int yPos, int zOrder, string? name = null)
    {
        return GetCanvas(xPos, yPos, _mainCanvasBitmap.Width, _mainCanvasBitmap.Height, zOrder, name);
    }

    public Canvas GetCanvas(int xPos, int yPos, int width, int height, int zOrder, string? name = null)
    {
        var canvas = new Canvas(_mainCanvasBitmap, xPos, yPos, width, height, name, zOrder);

        lock (_swapLock)
        {
            GetCanvases.Add((zOrder, canvas));
            _canvasesDirty = true;
        }

        return canvas;
    }

    /// <summary>
    ///     Finds a canvas by its name
    /// </summary>
    public Canvas? GetCanvasByName(string name)
    {
        lock (_swapLock)
        {
            return GetCanvases.FirstOrDefault(c => c.Value.Name == name).Value;
        }
    }

    /// <summary>
    ///     Finds a canvas by its unique ID
    /// </summary>
    public Canvas? GetCanvasById(string id)
    {
        lock (_swapLock)
        {
            return GetCanvases.FirstOrDefault(c => c.Value.Id == id).Value;
        }
    }

    /// <summary>
    ///     Gets all canvases with their z-order and identification info
    /// </summary>
    public IEnumerable<(int ZOrder, string Name, string Id, Canvas Canvas)> GetCanvasesWithInfo()
    {
        lock (_swapLock)
        {
            return GetCanvases.Select(c => (c.Key, c.Value.Name, c.Value.Id, c.Value)).ToList();
        }
    }

    /// <summary>
    ///     Updates the Z-order of a canvas (lower values drawn first, higher on top)
    /// </summary>
    /// <param name="canvas">Canvas to update</param>
    /// <param name="newZOrder">New Z-order value</param>
    public void SetCanvasZOrder(Canvas canvas, int newZOrder)
    {
        lock (_swapLock)
        {
            // Find and remove the existing entry
            var existing = GetCanvases.FirstOrDefault(c => c.Value == canvas);
            if (existing.Value != null)
            {
                GetCanvases.Remove(existing);
                // Update canvas ZOrder property
                canvas.ZOrder = newZOrder;
                // Add back with new ZOrder
                GetCanvases.Add((newZOrder, canvas));
                _canvasesDirty = true;
            }
        }
    }

    /// <summary>
    ///     Renames a canvas in place (the backbuffer and content are untouched).
    /// </summary>
    public void RenameCanvas(Canvas canvas, string newName)
    {
        lock (_swapLock)
        {
            canvas.Name = newName;
        }
    }

    /// <summary>
    ///     Repositions a canvas live. Only the composite offset changes (the backbuffer is untouched), so
    ///     this is cheap and does not require recreating the canvas or restarting its extension.
    /// </summary>
    public void MoveCanvas(Canvas canvas, int x, int y)
    {
        lock (_swapLock)
        {
            canvas.XPos = x;
            canvas.YPos = y;
            // No snapshot rebuild needed: position is read fresh at composite time.
        }
    }

    /// <summary>
    ///     Brings a canvas to the front (highest Z-order)
    /// </summary>
    public void BringToFront(Canvas canvas)
    {
        lock (_swapLock)
        {
            var maxZOrder = GetCanvases.Any() ? GetCanvases.Max(c => c.Key) : 0;
            SetCanvasZOrder(canvas, maxZOrder + 1);
        }
    }

    /// <summary>
    ///     Sends a canvas to the back (lowest Z-order)
    /// </summary>
    public void SendToBack(Canvas canvas)
    {
        lock (_swapLock)
        {
            var minZOrder = GetCanvases.Any() ? GetCanvases.Min(c => c.Key) : 0;
            SetCanvasZOrder(canvas, minZOrder - 1);
        }
    }

    /// <summary>
    ///     Moves a canvas one level up in Z-order
    /// </summary>
    public void MoveUp(Canvas canvas)
    {
        lock (_swapLock)
        {
            var current = GetCanvases.FirstOrDefault(c => c.Value == canvas);
            if (current.Value != null)
            {
                var higherCanvases = GetCanvases.Where(c => c.Key > current.Key).OrderBy(c => c.Key).ToList();
                if (higherCanvases.Any())
                {
                    // Swap with the next higher canvas
                    var next = higherCanvases.First();
                    SetCanvasZOrder(canvas, next.Key);
                    SetCanvasZOrder(next.Value, current.Key);
                }
            }
        }
    }

    /// <summary>
    ///     Moves a canvas one level down in Z-order
    /// </summary>
    public void MoveDown(Canvas canvas)
    {
        lock (_swapLock)
        {
            var current = GetCanvases.FirstOrDefault(c => c.Value == canvas);
            if (current.Value != null)
            {
                var lowerCanvases = GetCanvases.Where(c => c.Key < current.Key).OrderByDescending(c => c.Key).ToList();
                if (lowerCanvases.Any())
                {
                    // Swap with the next lower canvas
                    var prev = lowerCanvases.First();
                    SetCanvasZOrder(canvas, prev.Key);
                    SetCanvasZOrder(prev.Value, current.Key);
                }
            }
        }
    }

    /// <summary>
    ///     Removes a canvas from the manager
    /// </summary>
    public bool RemoveCanvas(Canvas canvas)
    {
        lock (_swapLock)
        {
            var existing = GetCanvases.FirstOrDefault(c => c.Value == canvas);
            if (existing.Value != null)
            {
                GetCanvases.Remove(existing);
                _canvasesDirty = true;
                canvas.Dispose();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    ///     Gets canvases sorted by Z-order (low to high)
    /// </summary>
    public IEnumerable<Canvas> GetCanvasesByZOrder()
    {
        lock (_swapLock)
        {
            return GetCanvases.OrderBy(c => c.Key).Select(c => c.Value).ToList();
        }
    }

    #endregion

    #region Rendering

    public event EventHandler<SKBitmap> RenderCompleted;

    protected virtual void OnRenderCompleted(SKBitmap renderedBitmap)
    {
        if (_stopped || renderedBitmap == null) return;

        try
        {
            RenderCompleted?.Invoke(this, renderedBitmap);
        }
        catch (ObjectDisposedException)
        {
            // Bitmap was disposed during event handling - ignore
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error in RenderCompleted handler: {ex.Message}");
        }
    }

    /// <summary>
    ///     Target render frame rate (frames per second). Backed by a drift-free scheduler in the
    ///     render loop (absolute per-frame deadlines + hybrid sleep/spin), so a change takes effect on
    ///     the next frame. Clamped to 1..240.
    /// </summary>
    public int TargetFps
    {
        get
        {
            var us = Volatile.Read(ref _frameIntervalMicros);
            return us > 0 ? 1_000_000 / us : 0;
        }
        set
        {
            var fps = Math.Clamp(value, 1, 240);
            Volatile.Write(ref _frameIntervalMicros, 1_000_000 / fps);
        }
    }

    public void Run()
    {
        if (_displayRefresh is
            { Status: not (TaskStatus.RanToCompletion or TaskStatus.Canceled or TaskStatus.Faulted) })
            return;

        // Reset stopped flag to allow the loop to run
        _stopped = false;

        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;
        _displayRefresh = Task.Factory.StartNew(() =>
        {
            Console.WriteLine("Display refreshing started with copy-on-render and filter support");

            var stopwatch = Stopwatch.StartNew();
            var ticksPerMicro = Stopwatch.Frequency / 1_000_000.0;
            var nextFrameTicks = stopwatch.ElapsedTicks;

            while (!ct.IsCancellationRequested && !_stopped)
            {
                lock (_swapLock)
                {
                    // Check cancellation again after acquiring lock
                    if (ct.IsCancellationRequested || _stopped) break;

                    // PERFORMANCE: Rebuild the z-ordered snapshot only when the canvas set or
                    // z-order changed, instead of allocating a sorted list every frame.
                    if (_canvasesDirty)
                    {
                        _renderSnapshot.Clear();
                        foreach (var canvas in GetCanvases.OrderBy(a => a.Key))
                            _renderSnapshot.Add(canvas.Value);
                        _canvasesDirty = false;
                    }

                    // PERFORMANCE: Copy all canvases (background -> foreground)
                    for (var i = 0; i < _renderSnapshot.Count; i++)
                        _renderSnapshot[i].CopyBackgroundToForeground();

                    // Build the visible list into a reused buffer (no per-frame allocation)
                    _visibleSnapshot.Clear();
                    for (var i = 0; i < _renderSnapshot.Count; i++)
                    {
                        var canvas = _renderSnapshot[i];
                        if (!canvas.IsHidden) _visibleSnapshot.Add(canvas);
                    }

                    if (_visibleSnapshot.Count != 0)
                    {
                        // PERFORMANCE OPTIMIZATION: Use unsafe direct pixel copy for single full-screen canvas
                        if (_visibleSnapshot.Count == 1 &&
                            _visibleSnapshot[0].XPos == 0 &&
                            _visibleSnapshot[0].YPos == 0 &&
                            _visibleSnapshot[0].Width == _mainCanvasBitmap.Width &&
                            _visibleSnapshot[0].Height == _mainCanvasBitmap.Height)
                        {
                            // Fast path: Single full-screen canvas - direct memory copy
                            var canvasBitmap = _visibleSnapshot[0].GetForegroundBitmap();
                            unsafe
                            {
                                var src = (byte*)canvasBitmap.GetPixels();
                                var dst = (byte*)_mainCanvasBitmap.GetPixels();
                                var byteCount = _mainCanvasBitmap.Width * _mainCanvasBitmap.Height * 4;
                                Buffer.MemoryCopy(src, dst, byteCount, byteCount);
                            }
                        }
                        else
                        {
                            // Slow path: Multiple/positioned canvases. Layer them by each canvas's Opacity,
                            // forcing the source opaque so content always shows (the LED display has no
                            // real alpha and extension pixel-alpha is unreliable). A canvas with Opacity < 1
                            // genuinely blends with what's beneath it.
                            _mainCanvas.Clear(SKColors.Black);

                            for (var i = 0; i < _visibleSnapshot.Count; i++)
                            {
                                var canvas = _visibleSnapshot[i];
                                var canvasBitmap = canvas.GetForegroundBitmap();

                                if (canvas.TransparentBackground)
                                {
                                    // Honour per-pixel alpha so transparent areas reveal the layer beneath.
                                    if (canvas.Opacity >= 0.999f)
                                    {
                                        _mainCanvas.DrawBitmap(canvasBitmap, canvas.XPos, canvas.YPos,
                                            _alphaCompositePaint);
                                    }
                                    else
                                    {
                                        using var p = MakeAlphaPaint(canvas.Opacity);
                                        _mainCanvas.DrawBitmap(canvasBitmap, canvas.XPos, canvas.YPos, p);
                                    }
                                }
                                else if (canvas.Opacity >= 0.999f)
                                {
                                    _mainCanvas.DrawBitmap(canvasBitmap, canvas.XPos, canvas.YPos,
                                        _opaqueCompositePaint);
                                }
                                else
                                {
                                    using var p = MakeOpaquePaint(canvas.Opacity);
                                    _mainCanvas.DrawBitmap(canvasBitmap, canvas.XPos, canvas.YPos, p);
                                }
                            }
                        }
                    }

                    // Apply enabled filters (plain loop - no LINQ allocation per frame)
                    for (var i = 0; i < _filters.Count; i++)
                    {
                        var filter = _filters[i];
                        if (filter.Enabled) filter.Apply(_mainCanvasBitmap);
                    }

                    // Apply global brightness adjustment
                    if (_brightness < 1.0f) ApplyBrightness(_mainCanvasBitmap, _brightness);

                    // Only fire event if not stopped.
                    // NOTE: kept inside the swap lock on purpose - the event hands the live
                    // _mainCanvasBitmap to the LED driver for synchronous DMA/SwapOnVsync, so the
                    // buffer must not be recomposited until the handler returns.
                    if (!_stopped) OnRenderCompleted(_mainCanvasBitmap);
                }

                // Drift-free frame pacing: schedule each frame against an absolute deadline instead of
                // sleeping a fixed amount after variable work. A hybrid coarse-sleep + short spin keeps
                // the interval steady despite OS scheduler jitter (the main source of the micro-stutter
                // that was visible on both the HDMI output and the web preview).
                var intervalTicks = (long)(Volatile.Read(ref _frameIntervalMicros) * ticksPerMicro);
                nextFrameTicks += intervalTicks;

                var nowTicks = stopwatch.ElapsedTicks;
                if (nowTicks >= nextFrameTicks)
                {
                    // Behind schedule (heavy frame, or FPS was raised): resync to now instead of
                    // trying to catch up in a burst, which would itself look like a stutter.
                    nextFrameTicks = nowTicks;
                    continue;
                }

                // Coarse-sleep until ~1.5 ms before the deadline, then spin the remainder so the
                // frame lands on time even though Thread.Sleep only has ~1 ms (Linux) granularity.
                var spinGuardTicks = (long)(1500 * ticksPerMicro);
                var sleepUntil = nextFrameTicks - spinGuardTicks;
                while (stopwatch.ElapsedTicks < sleepUntil && !ct.IsCancellationRequested && !_stopped)
                    Thread.Sleep(1);
                while (stopwatch.ElapsedTicks < nextFrameTicks && !_stopped)
                    Thread.SpinWait(64);
            }

            Console.WriteLine("Display refreshing stopped.");
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Current);
    }

    /// <summary>
    ///     Applies brightness adjustment to a bitmap in-place
    /// </summary>
    private unsafe void ApplyBrightness(SKBitmap bitmap, float brightness)
    {
        switch (brightness)
        {
            case >= 1.0f:
                return;
            case <= 0.0f:
                // Fast path: black out the entire bitmap
                bitmap.Erase(SKColors.Black);
                return;
        }

        // PERFORMANCE: Use a 256-entry lookup table instead of a float multiply per channel.
        // Rebuilt only when the brightness value actually changes.
        if (_brightnessLutValue != brightness)
        {
            for (var v = 0; v < 256; v++)
                _brightnessLut[v] = (byte)(v * brightness);
            _brightnessLutValue = brightness;
        }

        var lut = _brightnessLut;
        var pixels = (byte*)bitmap.GetPixels().ToPointer();
        var totalBytes = bitmap.Width * bitmap.Height * 4; // RGBA

        for (var i = 0; i < totalBytes; i += 4)
        {
            // Apply brightness to RGB channels via LUT, leave alpha unchanged
            pixels[i] = lut[pixels[i]]; // Red
            pixels[i + 1] = lut[pixels[i + 1]]; // Green
            pixels[i + 2] = lut[pixels[i + 2]]; // Blue
            // pixels[i + 3] is alpha - leave unchanged
        }
    }

    public void Stop()
    {
        // Prevent multiple stop calls
        if (_stopped) return;
        _stopped = true;

        // Signal cancellation
        _cancellationTokenSource?.Cancel();

        // Wait for the render loop task to complete
        if (_displayRefresh != null)
        {
            try
            {
                // Wait for the task to complete (it should exit due to cancellation)
                if (!_displayRefresh.Wait(TimeSpan.FromSeconds(5)))
                    Console.WriteLine("Warning: Render loop did not stop within timeout");
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // Expected - task was cancelled
            }
            catch (OperationCanceledException)
            {
                // Expected - task was cancelled
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error waiting for render loop to stop: {ex.Message}");
            }

            _displayRefresh = null;
        }

        // Dispose cancellation token source (safe to dispose, not used by hardware)
        try
        {
            _cancellationTokenSource?.Dispose();
        }
        catch
        {
        }

        _cancellationTokenSource = null;

        // IMPORTANT: Do NOT dispose _mainCanvasBitmap, _mainCanvas, or _compositePaint here!
        // The RenderCompleted event handler (LED matrix driver) may still be accessing
        // the bitmap's native memory for DMA transfer. Disposing would cause Bus Error/Segfault.
        // These resources will be cleaned up by GC when CanvasManager is no longer referenced,
        // or call Dispose() explicitly when completely shutting down.

        // Clear canvases list but don't dispose them yet - they may also be in use
        lock (_swapLock)
        {
            //GetCanvases.Clear();
        }

        Console.WriteLine("[CanvasManager] Stop completed - render loop stopped");
    }

    /// <summary>
    ///     Fully disposes all resources. Only call this when completely shutting down
    ///     and you're certain no hardware is accessing the bitmap memory.
    /// </summary>
    public void Dispose()
    {
        // Prevent double disposal
        if (_fullyDisposed) return;
        _fullyDisposed = true;

        // First stop the render loop if still running
        if (!_stopped) Stop();

        // Wait additional time for hardware to finish any pending DMA operations
        Thread.Sleep(100);

        // Dispose all canvases
        lock (_swapLock)
        {
            foreach (var canvasTuple in GetCanvases)
                try
                {
                    canvasTuple.Value?.Dispose();
                }
                catch
                {
                }

            GetCanvases.Clear();
        }

        // Dispose SkiaSharp resources in correct order (canvas before bitmap)
        try
        {
            _compositePaint?.Dispose();
            _opaqueCompositePaint?.Dispose();
            _alphaCompositePaint?.Dispose();
        }
        catch
        {
        }

        try
        {
            _mainCanvas?.Dispose();
        }
        catch
        {
        }

        try
        {
            _mainCanvasBitmap?.Dispose();
        }
        catch
        {
        }
    }

    #endregion
}