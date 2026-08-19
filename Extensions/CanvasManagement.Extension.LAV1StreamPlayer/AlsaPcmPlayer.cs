namespace CanvasManagement.Extension.LAV1StreamPlayer;

internal sealed class AlsaPcmPlayer(string deviceName, uint latencyUs) : IDisposable
{
    private readonly object _lock = new();
    private int _bytesPerFrame;

    private int _channels;
    private bool _isReady;

    private IntPtr _pcm;

    public string DeviceName { get; } = string.IsNullOrWhiteSpace(deviceName) ? "default" : deviceName;
    public uint LatencyUs { get; } = latencyUs;

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    public void Start(int sampleRate, int channels)
    {
        lock (_lock)
        {
            if (_isReady) return;

            // Validate parameters before calling ALSA.
            if (channels < 1 || channels > 8)
                throw new ArgumentOutOfRangeException(nameof(channels), channels,
                    "ALSA channels must be between 1 and 8.");

            if (sampleRate < 8000 || sampleRate > 192000)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                    "ALSA sample rate must be between 8000 and 192000 Hz.");

            var rc = AlsaNative.snd_pcm_open(out _pcm, DeviceName, AlsaNative.snd_pcm_stream_t.SND_PCM_STREAM_PLAYBACK,
                0);
            if (rc < 0)
                throw new InvalidOperationException(
                    $"ALSA snd_pcm_open('{DeviceName}') failed: {AlsaNative.GetError(rc)}");

            _channels = channels;
            _bytesPerFrame = checked(channels * 2); // S16_LE => 2 bytes per sample

            // Configure with latency target. ALSA will pick period/buffer sizes.
            rc = AlsaNative.snd_pcm_set_params(
                _pcm,
                AlsaNative.snd_pcm_format_t.SND_PCM_FORMAT_S16_LE,
                AlsaNative.snd_pcm_access_t.SND_PCM_ACCESS_RW_INTERLEAVED,
                (uint)channels,
                (uint)sampleRate,
                1,
                LatencyUs);

            if (rc < 0)
                throw new InvalidOperationException($"ALSA snd_pcm_set_params failed: {AlsaNative.GetError(rc)}");

            rc = AlsaNative.snd_pcm_prepare(_pcm);
            if (rc < 0)
                throw new InvalidOperationException($"ALSA snd_pcm_prepare failed: {AlsaNative.GetError(rc)}");

            _isReady = true;
        }
    }

    public unsafe void Write(ReadOnlySpan<byte> pcm)
    {
        if (!_isReady) return;

        lock (_lock)
        {
            if (!_isReady) return;

            if (_pcm == IntPtr.Zero) return;

            var totalFrames = (ulong)(pcm.Length / _bytesPerFrame);
            if (totalFrames == 0) return;

            fixed (byte* ptr = pcm)
            {
                var p = (IntPtr)ptr;
                var framesRemaining = totalFrames;

                while (framesRemaining > 0)
                {
                    var written = AlsaNative.snd_pcm_writei(_pcm, p, framesRemaining);
                    if (written >= 0)
                    {
                        var bytesWritten = written * _bytesPerFrame;
                        p = IntPtr.Add(p, (int)bytesWritten);
                        framesRemaining -= (ulong)written;
                        continue;
                    }

                    // Negative => error code.
                    var err = (int)written;

                    // Try to recover from XRUN, suspend, etc.
                    var recovered = AlsaNative.snd_pcm_recover(_pcm, err, 1);
                    if (recovered < 0)
                    {
                        // Hard failure: drop and attempt prepare.
                        try
                        {
                            AlsaNative.snd_pcm_drop(_pcm);
                        }
                        catch
                        {
                        }

                        AlsaNative.snd_pcm_prepare(_pcm);
                        return;
                    }

                    // After recovery, continue loop and retry write.
                }
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isReady) return;

            try
            {
                if (_pcm != IntPtr.Zero)
                {
                    try
                    {
                        AlsaNative.snd_pcm_drop(_pcm);
                    }
                    catch
                    {
                    }

                    AlsaNative.snd_pcm_close(_pcm);
                }
            }
            finally
            {
                _pcm = IntPtr.Zero;
                _isReady = false;
            }
        }
    }
}