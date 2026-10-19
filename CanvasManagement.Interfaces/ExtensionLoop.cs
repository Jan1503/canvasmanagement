namespace CanvasManagement.Interfaces;

/// <summary>
///     Shared animation loop for extensions. Token + target fps + Stop/Dispose.
///     Read <c>canvas.Width</c> / <c>Height</c> inside <paramref name="tick"/> — do not cache 256×128.
/// </summary>
public sealed class ExtensionLoop : IDisposable
{
    private readonly Func<CancellationToken, Task> _tick;
    private readonly int _targetFps;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public ExtensionLoop(int targetFps, Func<CancellationToken, Task> tick)
    {
        _targetFps = Math.Clamp(targetFps, 1, 60);
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
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
        var interval = TimeSpan.FromMilliseconds(1000.0 / _targetFps);
        while (!ct.IsCancellationRequested)
        {
            var start = DateTime.UtcNow;
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

            var delay = interval - (DateTime.UtcNow - start);
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose() => Stop();
}
