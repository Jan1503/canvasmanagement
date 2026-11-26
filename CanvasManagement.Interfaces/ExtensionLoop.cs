namespace CanvasManagement.Interfaces;

/// <summary>
///     Shared animation loop for extensions. Token + target fps + Stop/Dispose.
///     Read <c>canvas.Width</c> / <c>Height</c> inside <paramref name="tick"/> — do not cache 256×128.
/// </summary>
public sealed class ExtensionLoop : IDisposable
{
    private readonly Func<CancellationToken, Task> _tick;
    private readonly Func<double> _intervalMs;
    private readonly FrameStats? _stats;
    private CancellationTokenSource? _cts;
    private Task? _task;

    /// <param name="stats">Optional: where to report how long each tick took (pass <c>canvas.Stats</c>).</param>
    public ExtensionLoop(int targetFps, Func<CancellationToken, Task> tick, FrameStats? stats = null)
    {
        var interval = 1000.0 / Math.Clamp(targetFps, 1, 60);
        _intervalMs = () => interval;
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _stats = stats;
    }

    /// <summary>
    ///     A loop whose frame interval may change while it runs (a "Game Speed" parameter): read before every
    ///     frame, clamped to 8 ms – 10 s.
    /// </summary>
    public ExtensionLoop(Func<double> intervalMs, Func<CancellationToken, Task> tick, FrameStats? stats = null)
    {
        _intervalMs = intervalMs ?? throw new ArgumentNullException(nameof(intervalMs));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _stats = stats;
    }

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Run(() => RunAsync(token), token);
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts == null) return;
        try { cts.Cancel(); } catch { /* ignore */ }
        try { _task?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        cts.Dispose();
        _task = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Fixed schedule on the stopwatch: a slow tick does not push every later frame back.
        var next = (double)System.Diagnostics.Stopwatch.GetTimestamp();
        while (!ct.IsCancellationRequested)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                await _tick(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOOP] {ex.Message}");
            }
            _stats?.RecordRender(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            double ms;
            try { ms = Math.Clamp(_intervalMs(), 8, 10_000); }
            catch { ms = 1000.0 / 30; }
            var interval = System.Diagnostics.Stopwatch.Frequency * ms / 1000.0;
            next += interval;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now > next + interval) next = now; // fell behind: do not try to catch up with a burst
            var wait = TimeSpan.FromSeconds((next - now) / System.Diagnostics.Stopwatch.Frequency);
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose() => Stop();
}
