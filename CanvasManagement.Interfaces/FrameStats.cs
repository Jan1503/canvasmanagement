using System.Diagnostics;

namespace CanvasManagement.Interfaces;

/// <summary>
///     Frame timing for one canvas: how often frames arrive (fps) and how long the extension needed to render
///     them. <see cref="ExtensionLoop"/> reports render times; the canvas counts submitted frames. Thread-safe,
///     allocation-free on the hot path.
/// </summary>
public sealed class FrameStats
{
    private readonly object _gate = new();
    private readonly long _start = Stopwatch.GetTimestamp();
    private long _frames;
    private long _windowStart = Stopwatch.GetTimestamp();
    private int _windowFrames;
    private double _fps;
    private double _avgMs = double.NaN;
    private double _maxMs, _maxWindow;
    private long _maxWindowStart = Stopwatch.GetTimestamp();
    private long _renders;
    private long _lastSubmit;

    /// <summary>A frame reached the canvas.</summary>
    public void RecordSubmit()
    {
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _frames++;
            _windowFrames++;
            _lastSubmit = now;
            var span = Stopwatch.GetElapsedTime(_windowStart, now).TotalSeconds;
            if (span >= 1)
            {
                _fps = _windowFrames / span;
                _windowFrames = 0;
                _windowStart = now;
            }
        }
    }

    /// <summary>The extension spent <paramref name="ms"/> rendering one frame.</summary>
    public void RecordRender(double ms)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _renders++;
            _avgMs = double.IsNaN(_avgMs) ? ms : _avgMs + (ms - _avgMs) * 0.05;
            // Max over the last ~5 s, so one slow frame at start-up does not stick forever.
            if (Stopwatch.GetElapsedTime(_maxWindowStart, now).TotalSeconds > 5)
            {
                _maxMs = _maxWindow;
                _maxWindow = 0;
                _maxWindowStart = now;
            }
            _maxWindow = Math.Max(_maxWindow, ms);
        }
    }

    public FrameStatsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var idle = _lastSubmit == 0 ? double.PositiveInfinity : Stopwatch.GetElapsedTime(_lastSubmit, now).TotalSeconds;
            return new FrameStatsSnapshot(
                Frames: _frames,
                Fps: idle > 2 ? 0 : _fps,
                AvgRenderMs: _renders == 0 ? null : _avgMs,
                MaxRenderMs: _renders == 0 ? null : Math.Max(_maxMs, _maxWindow),
                Uptime: Stopwatch.GetElapsedTime(_start, now));
        }
    }
}

/// <param name="AvgRenderMs">Null when the extension does not run on <see cref="ExtensionLoop"/> (not measured).</param>
public readonly record struct FrameStatsSnapshot(
    long Frames,
    double Fps,
    double? AvgRenderMs,
    double? MaxRenderMs,
    TimeSpan Uptime);
