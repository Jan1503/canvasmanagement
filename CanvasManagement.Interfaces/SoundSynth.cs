namespace CanvasManagement.Interfaces;

/// <summary>Oscillator shapes for <see cref="SoundSynth"/>.</summary>
public enum Wave
{
    Sine,
    Triangle,

    /// <summary>Square with its edges softened, the 8-bit lead.</summary>
    Square,

    /// <summary>Narrow pulse (25 %), thinner and buzzier than the square.</summary>
    Pulse,
    Saw,
    Noise
}

/// <summary>
///     Builds short mono clips for <see cref="IExtensionAudio.Play"/> and <see cref="IExtensionAudio.LoopSample"/>
///     in code, so a game can have sound without shipping files. Every clip starts and ends at zero (no clicks).
///     Render clips once (at start) and keep the arrays.
/// </summary>
public static class SoundSynth
{
    public const int Rate = 32000;

    /// <summary>One note or sweep. Frequencies glide exponentially from <paramref name="hz"/> to <paramref name="toHz"/>.</summary>
    public static float[] Tone(Wave wave, float hz, float toHz, float seconds, float gain = 0.8f,
        float attack = 0.004f, float release = 0.03f, float vibratoHz = 0, float vibratoDepth = 0, float decay = 0)
    {
        var n = Math.Max(2, (int)(seconds * Rate));
        var buf = new float[n];
        Add(buf, 0, wave, hz, toHz, seconds, gain, attack, release, vibratoHz, vibratoDepth, decay);
        return buf;
    }

    /// <summary>Adds a note into <paramref name="buf"/> starting at <paramref name="at"/> seconds.</summary>
    public static void Add(float[] buf, float at, Wave wave, float hz, float toHz, float seconds, float gain = 0.8f,
        float attack = 0.004f, float release = 0.03f, float vibratoHz = 0, float vibratoDepth = 0, float decay = 0)
    {
        var start = (int)(at * Rate);
        var n = (int)(seconds * Rate);
        var phase = 0.0;
        var lp = 0f;
        uint rng = 0x9E3779B9u ^ (uint)(hz * 1000);
        var att = Math.Max(1, (int)(attack * Rate));
        var rel = Math.Max(1, Math.Min((int)(release * Rate), n / 2));
        var ratio = toHz / hz;
        for (var i = 0; i < n && start + i < buf.Length; i++)
        {
            var t = i / (float)n;
            var f = hz * MathF.Pow(ratio, t);
            if (vibratoHz > 0) f *= 1 + vibratoDepth * MathF.Sin(MathF.Tau * vibratoHz * i / Rate);
            phase += f / Rate;
            phase -= Math.Floor(phase);
            var p = (float)phase;
            float s;
            switch (wave)
            {
                case Wave.Sine: s = MathF.Sin(MathF.Tau * p); break;
                case Wave.Triangle: s = 1 - 4 * MathF.Abs(p - 0.5f); break;
                case Wave.Square: s = Soft(p < 0.5f ? 1f : -1f, ref lp, f); break;
                case Wave.Pulse: s = Soft(p < 0.25f ? 1f : -0.5f, ref lp, f); break;
                case Wave.Saw: s = Soft(2 * p - 1, ref lp, f); break;
                default:
                    rng ^= rng << 13;
                    rng ^= rng >> 17;
                    rng ^= rng << 5;
                    // Noise "pitch" sets a low-pass, so a falling noise sweep sounds like a whoosh.
                    var white = (rng & 0xFFFF) / 32767.5f - 1f;
                    var k = Math.Clamp(f / Rate * 6f, 0.02f, 1f);
                    lp += (white - lp) * k;
                    s = lp * 1.6f;
                    break;
            }

            var env = 1f;
            if (i < att) env = i / (float)att;
            if (n - i < rel) env *= (n - i) / (float)rel;
            if (decay > 0) env *= MathF.Exp(-decay * i / Rate);
            buf[start + i] += s * gain * env;
        }
    }

    /// <summary>A one-pole low-pass that rounds the edges of square and saw waves above a few hundred Hz.</summary>
    private static float Soft(float target, ref float state, float hz)
    {
        var k = Math.Clamp(9000f / Rate + hz / Rate * 4f, 0.05f, 1f);
        state += (target - state) * k;
        return state;
    }

    /// <summary>
    ///     A tune: notes as (MIDI note or 0 for a rest, length in beats). Returns the clip; <paramref name="into"/>
    ///     lets several voices share one buffer.
    /// </summary>
    public static float[] Melody(IReadOnlyList<(int Note, float Beats)> notes, float bpm, Wave wave, float gain = 0.5f,
        float[]? into = null, float legato = 0.85f, float decay = 2f)
    {
        var beat = 60f / bpm;
        var total = notes.Sum(n => n.Beats) * beat + 0.05f;
        var buf = into ?? new float[(int)(total * Rate)];
        var t = 0f;
        foreach (var (note, beats) in notes)
        {
            var len = beats * beat;
            if (note > 0)
            {
                var hz = Midi(note);
                Add(buf, t, wave, hz, hz, len * legato, gain, 0.004f, Math.Min(0.04f, len * 0.3f), decay: decay);
            }
            t += len;
        }
        return buf;
    }

    public static float Midi(int note) => 440f * MathF.Pow(2, (note - 69) / 12f);

    public static float[] Concat(params float[][] parts)
    {
        var buf = new float[parts.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in parts)
        {
            p.CopyTo(buf, o);
            o += p.Length;
        }
        return buf;
    }

    public static float[] Silence(float seconds) => new float[Math.Max(2, (int)(seconds * Rate))];

    /// <summary>
    ///     A seamless loop: <paramref name="seconds"/> is rounded so the carrier ends on a whole cycle, and the
    ///     frequency is modulated by <paramref name="lfoHz"/> (also a whole number of cycles), so the end meets
    ///     the start without a click.
    /// </summary>
    public static float[] Loop(Wave wave, float hz, float seconds, float lfoHz, float lfoDepth, float gain = 0.6f,
        bool lfoSaw = false)
    {
        var lfoCycles = Math.Max(1, (int)MathF.Round(seconds * lfoHz));
        var len = lfoCycles / lfoHz;
        var n = (int)MathF.Round(len * Rate);
        // Mean frequency so the carrier completes a whole number of cycles over the loop.
        var cycles = MathF.Round(hz * n / Rate);
        var mean = cycles * Rate / n;
        var buf = new float[n];
        var phase = 0.0;
        var lp = 0f;
        // Precompute the LFO's integral offset so the mean stays exact: sine and centred saw both average zero.
        for (var i = 0; i < n; i++)
        {
            var lt = (float)i / n * lfoCycles;
            lt -= MathF.Floor(lt);
            var lfo = lfoSaw ? 1 - 2 * lt : MathF.Sin(MathF.Tau * lt);
            var f = mean * (1 + lfoDepth * lfo);
            phase += f / Rate;
            phase -= Math.Floor(phase);
            var p = (float)phase;
            buf[i] = gain * wave switch
            {
                Wave.Sine => MathF.Sin(MathF.Tau * p),
                Wave.Triangle => 1 - 4 * MathF.Abs(p - 0.5f),
                Wave.Square => Soft(p < 0.5f ? 1f : -1f, ref lp, f),
                Wave.Pulse => Soft(p < 0.25f ? 1f : -0.5f, ref lp, f),
                Wave.Saw => Soft(2 * p - 1, ref lp, f),
                _ => MathF.Sin(MathF.Tau * p)
            };
        }
        return buf;
    }
}
