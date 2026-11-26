using System.Diagnostics;
using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     Base class for an extension that draws frames: it owns the loop, the back buffer (recreated when the
///     canvas changes size), frame timing, and a Start/Stop that really stops — after <see cref="Stop"/>
///     returns no frame is drawn or submitted any more.
///     <para>
///         Derived classes implement <see cref="Render"/> and, as needed, <see cref="OnStart"/>,
///         <see cref="OnStop"/> and <see cref="OnResize"/>. All of these run under <see cref="Sync"/>; take the
///         same lock in methods that change state from outside (key input, <c>[ExtensionMethod]</c>s).
///     </para>
/// </summary>
public abstract class FrameExtension : ICanvasExtension, IDisposable
{
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _lastError = new();
    private ExtensionLoop? _loop;
    private SKBitmap? _backBuffer;
    private SKCanvas? _backCanvas;
    private double _lastFrame;
    private int _generation;

    protected FrameExtension(ICanvas canvas) => Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    /// <summary>The canvas frames are submitted to. Read its size every frame; it can change.</summary>
    protected ICanvas Canvas { get; }

    /// <summary>Guards the extension's state; every hook of this class runs under it.</summary>
    protected object Sync { get; } = new();

    public abstract string Name { get; }

    public bool IsRunning { get; private set; }

    /// <summary>Milliseconds between frames; read before every frame, so a parameter may change it.</summary>
    protected virtual double FrameIntervalMs => 1000.0 / 30;

    /// <summary>
    ///     The bitmap <see cref="Render"/> draws into, for extensions that write pixels directly. Only valid
    ///     during <see cref="Render"/> and <see cref="OnResize"/>; null while stopped.
    /// </summary>
    protected SKBitmap? BackBuffer => _backBuffer;

    /// <summary>Seconds since <see cref="Start"/>.</summary>
    protected double Time => _clock.Elapsed.TotalSeconds;

    /// <summary>Whether the canvas is cleared to black when the extension stops.</summary>
    protected virtual bool ClearOnStop => true;

    /// <summary>Reset state for a new run. The canvas size is known (<see cref="Canvas"/>).</summary>
    protected virtual void OnStart() { }

    /// <summary>Release what the run used (sounds, connections). The loop has already ended.</summary>
    protected virtual void OnStop() { }

    /// <summary>Before the first frame and whenever the canvas changed size: lay out for the new size.</summary>
    protected virtual void OnResize(int width, int height) { }

    /// <summary>
    ///     Draw one complete frame into <paramref name="canvas"/> (the back buffer, not cleared).
    ///     <paramref name="dt"/> is the time since the previous frame in seconds, at most 0.1.
    /// </summary>
    protected abstract void Render(SKCanvas canvas, int width, int height, float dt);

    public void Start()
    {
        lock (Sync)
        {
            if (IsRunning) return;
            IsRunning = true;
            _generation++;
            _clock.Restart();
            _lastFrame = 0;
            OnStart();
            _loop = new ExtensionLoop(() => FrameIntervalMs, Tick, Canvas.Stats);
            _loop.Start();
        }
    }

    public void Stop()
    {
        ExtensionLoop? loop;
        int generation;
        lock (Sync)
        {
            if (!IsRunning) return;
            IsRunning = false;
            loop = _loop;
            _loop = null;
            generation = _generation;
        }

        // Outside the lock: the loop waits for a running frame, and that frame holds the lock.
        loop?.Dispose();

        lock (Sync)
        {
            if (generation != _generation) return; // started again meanwhile; that run owns the state now
            OnStop();
            FreeBackBuffer();
            if (ClearOnStop)
                try { Canvas.Clear(SKColors.Black); } catch { /* canvas already gone */ }
        }
    }

    private Task Tick(CancellationToken ct)
    {
        lock (Sync)
        {
            if (!IsRunning || ct.IsCancellationRequested) return Task.CompletedTask;
            var w = Canvas.Width;
            var h = Canvas.Height;
            if (w <= 0 || h <= 0) return Task.CompletedTask;
            if (_backBuffer == null || _backBuffer.Width != w || _backBuffer.Height != h)
            {
                FreeBackBuffer();
                _backBuffer = new SKBitmap(w, h);
                _backCanvas = new SKCanvas(_backBuffer);
                OnResize(w, h);
            }

            var now = _clock.Elapsed.TotalSeconds;
            var dt = (float)Math.Clamp(now - _lastFrame, 0, 0.1);
            _lastFrame = now;

            // Whatever transform or clip a frame leaves behind does not leak into the next one.
            var saved = _backCanvas!.Save();
            try
            {
                Render(_backCanvas, w, h, dt);
            }
            catch (Exception ex)
            {
                // One bad frame is skipped; the loop keeps going. Throttled so a broken extension cannot flood the log.
                if (_lastError.Elapsed.TotalSeconds > 10 || !_lastError.IsRunning)
                {
                    Console.WriteLine($"[{Name}] Render error: {ex.GetType().Name}: {ex.Message}");
                    _lastError.Restart();
                }
                return Task.CompletedTask;
            }
            finally
            {
                _backCanvas.RestoreToCount(saved);
            }
            _backCanvas.Flush();
            Canvas.SubmitCompletedFrame(_backBuffer);
        }
        return Task.CompletedTask;
    }

    private void FreeBackBuffer()
    {
        _backCanvas?.Dispose();
        _backCanvas = null;
        _backBuffer?.Dispose();
        _backBuffer = null;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) Stop();
    }
}
