using Lav1.Protocol;

namespace CanvasManagement.Extension.LAV1StreamPlayer;

public enum AudioBackend
{
    Auto = 0,
    Alsa = 1,
    Pulse = 2
}

internal sealed class ByteStreamPlayer : IDisposable
{
    private readonly object _lock = new();

    private bool _isReady;
    private bool _disposed;

    private AudioBackend _backend;

    // Linux: direct ALSA pcm
    private AlsaPcmPlayer? _alsa;

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isReady) return;

            try
            {
                if (_backend is AudioBackend.Alsa or AudioBackend.Pulse)
                {
                    try
                    {
                        _alsa?.Stop();
                    }
                    catch
                    {
                    }

                    _alsa?.Dispose();
                    _alsa = null;
                }
            }
            finally
            {
                _isReady = false;
                _backend = AudioBackend.Auto;
            }
        }
    }

    public void Start(int frequency, int channels, int preBufferMargin)
    {
        Start(frequency, channels, Lav1AudioChunk.AudioFormatS16LE, preBufferMargin, AudioBackend.Auto);
    }

    public void Start(int frequency, int channels, byte format, int preBufferMargin, AudioBackend backend)
    {
        lock (_lock)
        {
            if (_isReady)
                return;

            // Validate audio parameters before attempting to initialize.
            if (channels < 1 || channels > 8)
                throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be between 1 and 8.");

            if (frequency < 8000 || frequency > 192000)
                throw new ArgumentOutOfRangeException(nameof(frequency), frequency,
                    "Sample rate must be between 8000 and 192000 Hz.");

            _backend = backend == AudioBackend.Auto
                ? AudioBackend.Alsa
                : backend;

            if (format != Lav1AudioChunk.AudioFormatS16LE)
                throw new NotSupportedException($"Audio format {format} not supported yet (only S16LE).");

            if (_backend is AudioBackend.Alsa or AudioBackend.Pulse)
            {
                // Option 1: ALSA device selection; "pulse" routes via PulseAudio ALSA plugin if installed.
                var device = _backend == AudioBackend.Pulse ? "pulse" : "default";

                // Translate prebuffer margin (ms) into target latency. Give it a healthy floor to reduce XRUN.
                var latencyUs = (uint)Math.Clamp(preBufferMargin, 10, 2000) * 1000u;

                _alsa = new AlsaPcmPlayer(device, latencyUs);
                _alsa.Start(frequency, channels);

                _isReady = true;
                return;
            }

            throw new PlatformNotSupportedException($"Audio backend '{_backend}' is not supported on this platform.");
        }
    }

    public void AddBytes(byte[] arrayByte)
    {
        AddBytes(arrayByte.AsSpan(), arrayByte.Length);
    }

    public void AddBytes(Span<byte> arraySpan, int length)
    {
        if (!_isReady || _disposed) return;

        lock (_lock)
        {
            if (!_isReady || _disposed) return;

            if (_backend is AudioBackend.Alsa or AudioBackend.Pulse) _alsa?.Write(arraySpan.Slice(0, length));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        GC.SuppressFinalize(this);
    }
}