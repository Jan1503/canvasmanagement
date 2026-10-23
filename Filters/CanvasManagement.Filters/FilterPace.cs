using System.Diagnostics;

namespace CanvasManagement.Filters;

/// <summary>
///     Converts per-frame motion into wall-clock motion.
///     Speed 1 matches about 20 fps, so a 60 fps host does not play the effect three times as fast.
/// </summary>
internal struct FilterPace
{
    private const float ReferenceFps = 20f;
    private long _last;
    private float _fraction;

    /// <summary>
    ///     Scale for a value that used to change once per compositor frame.
    ///     Call <see cref="ConsumeFrames"/> once after each call.
    /// </summary>
    public float Step(float speed)
    {
        var now = Stopwatch.GetTimestamp();
        float dt;
        if (_last == 0)
            dt = 1f / ReferenceFps;
        else
            dt = (float)((now - _last) / (double)Stopwatch.Frequency);
        _last = now;

        if (dt < 0f) dt = 0f;
        else if (dt > 0.1f) dt = 0.1f;

        var scale = Math.Clamp(speed, 0.2f, 4f) * dt * ReferenceFps;
        _fraction += scale;
        return scale;
    }

    public int ConsumeFrames()
    {
        var whole = (int)_fraction;
        if (whole > 0)
            _fraction -= whole;
        return whole;
    }
}
