using CanvasManagement.Interfaces;

namespace CanvasManagement;

/// <summary>
///     The voice pool behind <see cref="IExtensionAudio"/>, without a device. A host calls <see cref="Render"/>
///     on its audio thread and sends the block wherever it likes; a test renders to a file the same way.
///     Gains and pitches glide per sample, loops fade in and out, and the sum goes through a limiter,
///     so games can change levels every frame without clicks or hard clipping.
/// </summary>
public sealed class ExtensionVoiceMixer : IExtensionAudio
{
    public const int Slots = 8;
    private const int MaxTones = 16;
    private const int MaxNoise = 8;
    private const int MaxShots = 16;

    private readonly object _lock = new();
    private readonly LoopVoice[] _loops = new LoopVoice[Slots];
    private readonly List<ToneVoice> _tones = new();
    private readonly List<NoiseVoice> _noises = new();
    private readonly List<SampleVoice> _shots = new();
    private readonly List<SampleVoice> _tails = new();
    private uint _rng = 0x6d2b79f5;
    private float _limit = 1f;

    public ExtensionVoiceMixer(int sampleRate = 48000)
    {
        SampleRate = sampleRate;
        for (var i = 0; i < Slots; i++) _loops[i] = new LoopVoice();
    }

    public int SampleRate { get; }

    /// <summary>Master level before the limiter. 1 leaves extension gains as they are.</summary>
    public float Master { get; set; } = 0.9f;

    // ------------------------------------------------------------------ IExtensionAudio

    public void Tone(float hz, float seconds, float gain, float pan)
    {
        if (hz < 20 || seconds <= 0 || gain <= 0) return;
        var total = (int)(Math.Clamp(seconds, 0.02f, 4f) * SampleRate);
        lock (_lock)
        {
            if (_tones.Count >= MaxTones) _tones.RemoveAt(0);
            _tones.Add(new ToneVoice
            {
                Hz = Math.Min(hz, SampleRate * 0.45f),
                Gain = Math.Clamp(gain, 0, 1),
                Pan = Math.Clamp(pan, -1, 1),
                Total = total
            });
        }
    }

    public void Noise(float seconds, float gain, float pan)
    {
        if (seconds <= 0 || gain <= 0) return;
        var total = (int)(Math.Clamp(seconds, 0.02f, 2f) * SampleRate);
        lock (_lock)
        {
            if (_noises.Count >= MaxNoise) _noises.RemoveAt(0);
            _noises.Add(new NoiseVoice
            {
                Gain = Math.Clamp(gain, 0, 1),
                Pan = Math.Clamp(pan, -1, 1),
                Total = total
            });
        }
    }

    public void Loop(int slot, float hz, float gain)
    {
        if ((uint)slot >= Slots) return;
        lock (_lock)
        {
            var v = _loops[slot];
            if (v.Sample != null) Retire(v);
            v.Sample = null;
            v.TargetHz = Math.Clamp(hz, 20, 4000);
            if (!v.Active) v.Hz = v.TargetHz;
            v.Target = Math.Clamp(gain, 0, 1);
            v.Active |= v.Target > 0.0005f;
        }
    }

    public void StopLoop(int slot)
    {
        if ((uint)slot >= Slots) return;
        lock (_lock) _loops[slot].Target = 0;
    }

    public void Play(float[] mono, int sampleRate, float gain, float pan, float playbackRate)
    {
        if (mono.Length < 2 || gain <= 0 || sampleRate < 8000) return;
        var copy = new float[mono.Length];
        mono.AsSpan().CopyTo(copy);
        lock (_lock)
        {
            if (_shots.Count >= MaxShots)
            {
                var old = _shots[0];
                _shots.RemoveAt(0);
                old.Fade = 1f;
                _tails.Add(old);
            }
            _shots.Add(new SampleVoice
            {
                Data = copy,
                Step = Math.Clamp(playbackRate, 0.25f, 3f) * sampleRate / SampleRate,
                Gain = Math.Clamp(gain, 0, 1),
                Pan = Math.Clamp(pan, -1, 1)
            });
        }
    }

    public void LoopSample(int slot, float[] mono, int sampleRate, float gain, float playbackRate)
    {
        if ((uint)slot >= Slots) return;
        lock (_lock)
        {
            var v = _loops[slot];
            if (mono.Length < 2 || gain <= 0.0005f || sampleRate < 8000)
            {
                v.Target = 0;
                return;
            }
            if (!ReferenceEquals(v.Sample, mono))
            {
                Retire(v);
                v.Sample = mono;
                v.Cursor = 0;
                v.Gain = 0;
                v.Active = false;
            }
            v.TargetStep = Math.Clamp(playbackRate, 0.25f, 3f) * sampleRate / SampleRate;
            if (!v.Active) v.Step = v.TargetStep;
            v.Target = Math.Clamp(gain, 0, 1);
            v.Active = true;
        }
    }

    // ------------------------------------------------------------------ render

    /// <summary>True while any voice can still make sound.</summary>
    public bool HasWork
    {
        get
        {
            lock (_lock)
            {
                if (_tones.Count > 0 || _noises.Count > 0 || _shots.Count > 0 || _tails.Count > 0) return true;
                foreach (var loop in _loops)
                    if (loop.Active) return true;
                return false;
            }
        }
    }

    /// <summary>Drop every voice at once (the device went away).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _tones.Clear();
            _noises.Clear();
            _shots.Clear();
            _tails.Clear();
            foreach (var loop in _loops)
            {
                loop.Active = false;
                loop.Gain = loop.Target = 0;
            }
        }
    }

    /// <summary>
    ///     Mix one block into <paramref name="stereo"/> (interleaved, cleared by the caller).
    ///     <paramref name="extra"/> runs after the voices and before the limiter, for <see cref="ICanvasAudioSource"/>.
    /// </summary>
    public void Render(Span<float> stereo, Action<Span<float>>? extra = null)
    {
        var frames = stereo.Length / 2;
        lock (_lock)
        {
            foreach (var loop in _loops)
                if (loop.Active) RenderLoop(loop, stereo, frames);
            for (var i = _tails.Count - 1; i >= 0; i--)
                if (!RenderShot(_tails[i], stereo, frames, true)) _tails.RemoveAt(i);
            for (var i = _shots.Count - 1; i >= 0; i--)
                if (!RenderShot(_shots[i], stereo, frames, false)) _shots.RemoveAt(i);
            for (var i = _tones.Count - 1; i >= 0; i--)
                if (!RenderTone(_tones[i], stereo, frames)) _tones.RemoveAt(i);
            for (var i = _noises.Count - 1; i >= 0; i--)
                if (!RenderNoise(_noises[i], stereo, frames)) _noises.RemoveAt(i);
        }

        extra?.Invoke(stereo);
        Limit(stereo, frames);
    }

    // Loop gain glides over ~12 ms, pitch over ~40 ms. That is fast enough to follow a game frame and slow
    // enough that a 30 fps gain staircase does not buzz.
    private float GainGlide => 1f - MathF.Exp(-1f / (0.012f * SampleRate));
    private float PitchGlide => 1f - MathF.Exp(-1f / (0.040f * SampleRate));

    private void RenderLoop(LoopVoice v, Span<float> stereo, int frames)
    {
        var gk = GainGlide;
        var pk = PitchGlide;
        var data = v.Sample;
        for (var i = 0; i < frames; i++)
        {
            v.Gain += (v.Target - v.Gain) * gk;
            float s;
            if (data != null)
            {
                v.Step += (v.TargetStep - v.Step) * pk;
                var idx = (int)v.Cursor;
                var frac = (float)(v.Cursor - idx);
                var a = data[idx % data.Length];
                var b = data[(idx + 1) % data.Length];
                s = a + (b - a) * frac;
                v.Cursor += v.Step;
                if (v.Cursor >= data.Length) v.Cursor -= data.Length * Math.Floor(v.Cursor / data.Length);
            }
            else
            {
                v.Hz += (v.TargetHz - v.Hz) * pk;
                v.Phase += v.Hz / SampleRate;
                if (v.Phase >= 1f) v.Phase -= 1f;
                s = (1f - 4f * MathF.Abs(v.Phase - 0.5f)) * 0.8f;
            }
            Add(stereo, i, s * v.Gain, 0);
        }

        if (v.Target <= 0 && v.Gain < 0.0005f)
        {
            v.Active = false;
            v.Gain = 0;
        }
    }

    /// <summary>A loop that switches clips hands its old clip to a short fade so the swap does not click.</summary>
    private void Retire(LoopVoice v)
    {
        if (!v.Active || v.Sample == null || v.Gain < 0.002f) return;
        _tails.Add(new SampleVoice
        {
            Data = v.Sample,
            Cursor = v.Cursor,
            Step = v.Step,
            Gain = v.Gain,
            Loop = true,
            Fade = 1f
        });
    }

    private bool RenderShot(SampleVoice v, Span<float> stereo, int frames, bool fading)
    {
        var data = v.Data;
        var fadeStep = 1f / (0.010f * SampleRate);
        for (var i = 0; i < frames; i++)
        {
            var idx = (int)v.Cursor;
            if (idx >= data.Length - 1)
            {
                if (!v.Loop) return false;
                v.Cursor -= data.Length - 1;
                idx = (int)v.Cursor;
            }
            var frac = (float)(v.Cursor - idx);
            var s = (data[idx] + (data[idx + 1] - data[idx]) * frac) * v.Gain;
            if (fading)
            {
                v.Fade -= fadeStep;
                if (v.Fade <= 0) return false;
                s *= v.Fade;
            }
            Add(stereo, i, s, v.Pan);
            v.Cursor += v.Step;
        }
        return true;
    }

    /// <summary>Soft square (a few harmonics short of a hard edge), 3 ms attack, exponential decay, 12 ms release.</summary>
    private bool RenderTone(ToneVoice t, Span<float> stereo, int frames)
    {
        var step = t.Hz / SampleRate;
        var attack = 0.003f * SampleRate;
        var release = Math.Min(0.012f * SampleRate, t.Total * 0.4f);
        var decay = 1.2f / Math.Max(1, t.Total);
        for (var i = 0; i < frames; i++)
        {
            if (t.Pos >= t.Total) return false;
            t.Phase += step;
            if (t.Phase >= 1f) t.Phase -= 1f;
            var p = t.Phase * MathF.Tau;
            var s = MathF.Sin(p) + MathF.Sin(3 * p) * 0.30f + MathF.Sin(5 * p) * 0.12f;
            var env = MathF.Exp(-t.Pos * decay);
            if (t.Pos < attack) env *= t.Pos / attack;
            var left = t.Total - t.Pos;
            if (left < release) env *= left / release;
            Add(stereo, i, s * 0.6f * env * t.Gain, t.Pan);
            t.Pos++;
        }
        return t.Pos < t.Total;
    }

    private bool RenderNoise(NoiseVoice n, Span<float> stereo, int frames)
    {
        var release = Math.Min(0.010f * SampleRate, n.Total * 0.4f);
        var decay = 2.5f / Math.Max(1, n.Total);
        for (var i = 0; i < frames; i++)
        {
            if (n.Pos >= n.Total) return false;
            var white = NextRand() * 2f - 1f;
            n.Low += (white - n.Low) * 0.35f;
            var env = MathF.Exp(-n.Pos * decay);
            if (n.Pos < 48) env *= n.Pos / 48f;
            var left = n.Total - n.Pos;
            if (left < release) env *= left / release;
            Add(stereo, i, n.Low * 1.4f * n.Gain * env, n.Pan);
            n.Pos++;
        }
        return n.Pos < n.Total;
    }

    /// <summary>Peak limiter per block (gain interpolated across it), then a soft knee for whatever is left.</summary>
    private void Limit(Span<float> stereo, int frames)
    {
        var peak = 0f;
        for (var i = 0; i < stereo.Length; i++)
        {
            var a = MathF.Abs(stereo[i]) * Master;
            if (a > peak) peak = a;
        }

        const float ceiling = 0.85f;
        var want = peak > ceiling ? ceiling / peak : 1f;
        var from = _limit;
        var to = want < from ? want : from + (want - from) * 0.08f;
        _limit = to;

        for (var f = 0; f < frames; f++)
        {
            var g = Master * (from + (to - from) * (f + 1) / frames);
            for (var c = 0; c < 2; c++)
            {
                var x = stereo[f * 2 + c] * g;
                var ax = MathF.Abs(x);
                if (ax > 0.8f) x = MathF.Sign(x) * (0.8f + 0.2f * MathF.Tanh((ax - 0.8f) / 0.2f));
                stereo[f * 2 + c] = x;
            }
        }
    }

    private static void Add(Span<float> stereo, int frame, float sample, float pan)
    {
        // Constant power, centre at unity.
        var l = pan <= 0 ? 1f : MathF.Cos(pan * MathF.PI / 2);
        var r = pan >= 0 ? 1f : MathF.Cos(-pan * MathF.PI / 2);
        var i = frame * 2;
        stereo[i] += sample * l;
        stereo[i + 1] += sample * r;
    }

    private float NextRand()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) / 16777215f;
    }

    private sealed class LoopVoice
    {
        public bool Active;
        public float Gain, Target;
        public float Hz = 80, TargetHz = 80, Phase;
        public float[]? Sample;
        public double Cursor;
        public double Step = 1, TargetStep = 1;
    }

    private sealed class SampleVoice
    {
        public float[] Data = [];
        public double Cursor;
        public double Step = 1;
        public float Gain = 1, Pan, Fade = 1;
        public bool Loop;
    }

    private sealed class ToneVoice
    {
        public float Hz, Gain, Pan, Phase;
        public int Total, Pos;
    }

    private sealed class NoiseVoice
    {
        public float Gain, Pan, Low;
        public int Total, Pos;
    }
}
