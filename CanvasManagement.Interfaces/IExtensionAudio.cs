namespace CanvasManagement.Interfaces;

/// <summary>
///     One-shot and looping tones for an extension. The host mixes them into the single wall output.
///     Null on <see cref="ICanvas.Audio"/> means there is no device; callers should use <c>canvas.Audio?.Tone(...)</c>.
/// </summary>
public interface IExtensionAudio
{
    /// <param name="pan">-1 left, 0 center, 1 right.</param>
    void Tone(float hz, float seconds, float gain, float pan);

    /// <param name="pan">-1 left, 0 center, 1 right.</param>
    void Noise(float seconds, float gain, float pan);

    /// <summary>Up to four simultaneous loops. Slot 0 is the usual engine.</summary>
    void Loop(int slot, float hz, float gain);

    void StopLoop(int slot);

    /// <summary>
    ///     Play a mono clip once. The host copies the buffer. <paramref name="playbackRate"/> 1 keeps the clip's pitch.
    /// </summary>
    void Play(float[] mono, int sampleRate, float gain, float pan, float playbackRate);

    /// <summary>
    ///     Loop a mono clip. The buffer is not copied and must not change while the loop runs.
    ///     The same array keeps its playhead. Gain 0 or an empty buffer stops the slot.
    /// </summary>
    void LoopSample(int slot, float[] mono, int sampleRate, float gain, float playbackRate);
}

/// <summary>
///     Optional. An extension that wants to synthesize its own block implements this.
///     The host calls <see cref="Mix"/> on the audio thread, never under the compositor lock.
/// </summary>
public interface ICanvasAudioSource
{
    bool IsAudible { get; }

    /// <summary>Add into an interleaved stereo buffer that is already cleared. Samples are -1..1.</summary>
    void Mix(Span<float> stereo, int sampleRate);
}
