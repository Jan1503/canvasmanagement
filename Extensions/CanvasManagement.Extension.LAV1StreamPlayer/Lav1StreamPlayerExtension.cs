using System.Buffers;
using System.Net;
using System.Net.Sockets;
using CanvasManagement.Interfaces;
using Lav1.Protocol;
using SkiaSharp;

namespace CanvasManagement.Extension.LAV1StreamPlayer;

[ExtensionInfo(
    "LAV1 Stream Player",
    "Receives LAV1 UDP audio/video stream and renders frames to the canvas.",
    "Media Players",
    IconResourceName = "lav1.svg")]
public sealed class Lav1StreamPlayerExtension(ICanvas canvas) : ICanvasExtension, IDisposable
{
    // FIX: Add max frame limit to prevent unbounded growth
    private const int MaxPendingFrames = 100;

    private readonly object _audioLock = new();

    // Audio scheduling
    private readonly PriorityQueue<ScheduledAudio, long> _audioQ = new();
    private readonly object _bitmapLock = new();
    private readonly ICanvas _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    // Video assembly + scheduling
    private readonly Dictionary<uint, FrameAssembly> _frames = new();

    // Config/state
    private readonly object _sync = new();
    private readonly PriorityQueue<ScheduledFrame, long> _videoQ = new();
    private byte _audioChannels;

    // Drop protection: drop packets that are far too late (otherwise they tend to sound like pops)
    private int _audioDropLateMs = 150;
    private byte _audioFormat;
    private int _audioHoldMs = 140;
    private ByteStreamPlayer? _audioPlayer;
    private ushort _audioSampleRate;
    private Task? _audioSchedulerTask;
    private SKBitmap? _backBuffer; // Back buffer for atomic frame submission

    private long _basePtsUs = long.MinValue;
    private long _baseUtcTicks;

    private SKBitmap? _bitmap;
    private Lav1StreamConfig _config;

    private CancellationTokenSource? _cts;
    private bool _haveConfig;

    // Pre-allocated receive buffer
    private byte[]? _rxBuf;
    private uint _streamId;
    private Task? _udpTask;

    // Separate holds for better A/V alignment on slower receivers
    private int _videoHoldMs = 200;
    private Task? _videoSchedulerTask;

    [ExtensionParameter("Background Color", "Background color for the player",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Desired Port", "Preferred UDP port to listen on (auto-increment if busy)",
        MinValue = 1024, MaxValue = 65535, DefaultValue = 55100)]
    public int DesiredPort { get; set; } = 55100;

    [ExtensionParameter("Actual Port", "Currently bound UDP port (read-only)", ReadOnly = true)]
    public int ActualPort { get; private set; }

    [ExtensionParameter("Max Port Search", "Maximum number of ports to try if desired port is busy",
        MinValue = 1, MaxValue = 100, DefaultValue = 50)]
    public int MaxPortSearchAttempts { get; set; } = 50;

    [ExtensionParameter("UDP Receive Buffer", "Socket receive buffer size",
        MinValue = 65536, MaxValue = 8_388_608, DefaultValue = 4_194_304, Unit = "bytes")]
    public int ReceiveBufferBytes { get; set; } = 4 * 1024 * 1024;

    [ExtensionParameter("Video Hold", "Additional receiver-side video hold",
        MinValue = 0, MaxValue = 2000, DefaultValue = 200, Unit = "ms")]
    public int VideoHoldMs
    {
        get
        {
            lock (_sync)
            {
                return _videoHoldMs;
            }
        }
        set
        {
            lock (_sync)
            {
                _videoHoldMs = Math.Clamp(value, 0, 2000);
            }
        }
    }

    [ExtensionParameter("Audio Hold", "Additional receiver-side audio hold",
        MinValue = 0, MaxValue = 2000, DefaultValue = 140, Unit = "ms")]
    public int AudioHoldMs
    {
        get
        {
            lock (_sync)
            {
                return _audioHoldMs;
            }
        }
        set
        {
            lock (_sync)
            {
                _audioHoldMs = Math.Clamp(value, 0, 2000);
            }
        }
    }

    [ExtensionParameter("Audio Drop Late", "Drop audio packets later than this (helps pops)",
        MinValue = 0, MaxValue = 2000, DefaultValue = 150, Unit = "ms")]
    public int AudioDropLateMs
    {
        get
        {
            lock (_sync)
            {
                return _audioDropLateMs;
            }
        }
        set
        {
            lock (_sync)
            {
                _audioDropLateMs = Math.Clamp(value, 0, 2000);
            }
        }
    }

    [ExtensionParameter("Audio Backend", "Audio output backend (Auto=Windows:BASS, Linux:ALSA)",
        DefaultValue = 0)]
    public AudioBackend AudioBackend { get; set; } = AudioBackend.Auto;

    [ExtensionParameter("Stream Active", "Whether a config was received and stream is active", ReadOnly = true)]
    public bool StreamActive
    {
        get
        {
            lock (_sync)
            {
                return _haveConfig;
            }
        }
    }

    public string Name => "LAV1 Stream Player";

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
            return;

        Stop();

        _cts = new CancellationTokenSource();

        _bitmap?.Dispose();
        _bitmap = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888,
            SKAlphaType.Premul));

        _backBuffer?.Dispose();
        _backBuffer =
            new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        _udpTask = Task.Run(() => UdpLoop(_cts.Token));
        _videoSchedulerTask = Task.Run(() => VideoSchedulerLoop(_cts.Token));
        _audioSchedulerTask = Task.Run(() => AudioSchedulerLoop(_cts.Token));

        IsRunning = true;
    }

    public void Stop()
    {
        // Signal cancellation FIRST
        _cts?.Cancel();

        // Wait for tasks to complete
        try
        {
            _udpTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        try
        {
            _videoSchedulerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        try
        {
            _audioSchedulerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        // Now safely cleanup resources
        _cts?.Dispose();
        _cts = null;
        _udpTask = null;
        _videoSchedulerTask = null;
        _audioSchedulerTask = null;

        lock (_audioLock)
        {
            try
            {
                _audioPlayer?.Stop();
            }
            catch
            {
            }

            _audioPlayer?.Dispose(); // FIX: Explicitly dispose audio player
            _audioPlayer = null;
            _audioSampleRate = 0;
            _audioChannels = 0;
            _audioFormat = 0;
        }

        // FIX: Set _isRunning to false BEFORE cleanup to prevent new items being added
        IsRunning = false;

        ResetStream(0);
    }

    public void Dispose()
    {
        Stop();

        // FIX: Dispose the pinned receive buffer
        _rxBuf = null; // Allow GC to collect the pinned array

        _bitmap?.Dispose();
        _bitmap = null;

        _backBuffer?.Dispose();
        _backBuffer = null;

        GC.SuppressFinalize(this);
    }

    private void ResetStream(uint streamId)
    {
        lock (_sync)
        {
            _streamId = streamId;
            _haveConfig = false;
            _basePtsUs = long.MinValue;
            _baseUtcTicks = 0;
        }

        lock (_frames)
        {
            foreach (var kv in _frames)
                kv.Value.Dispose();
            _frames.Clear();
        }

        lock (_videoQ)
        {
            while (_videoQ.TryDequeue(out var sf, out _))
                ArrayPool<byte>.Shared.Return(sf.FrameBuffer, true); // FIX: Clear arrays
        }

        lock (_audioQ)
        {
            while (_audioQ.TryDequeue(out var sa, out _))
                ArrayPool<byte>.Shared.Return(sa.Pcm, true); // FIX: Clear arrays
        }
    }

    private void EnsureBaseClock(long ptsUs)
    {
        lock (_sync)
        {
            if (_basePtsUs != long.MinValue)
                return;

            _basePtsUs = ptsUs;
            _baseUtcTicks = DateTime.UtcNow.Ticks;
        }
    }

    private long PtsToDueUtcTicks(long ptsUs, int holdMs)
    {
        lock (_sync)
        {
            var deltaUs = ptsUs - _basePtsUs;
            return _baseUtcTicks + TimeSpan.FromMilliseconds(deltaUs / 1000.0 + holdMs).Ticks;
        }
    }

    private static int ExpectedFrameBytes(in Lav1StreamConfig cfg)
    {
        return cfg.PixelFormat switch
        {
            Lav1PixelFormat.Bgra8888 => cfg.Width * cfg.Height * 4,
            _ => cfg.Width * cfg.Height * 3
        };
    }

    private Socket? TryBindToPort(int startPort, int maxAttempts, out int boundPort)
    {
        boundPort = 0;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var port = startPort + attempt;
            if (port > 65535)
                break;

            try
            {
                // Bind IPv4-only to avoid per-packet IPv4->IPv6 normalization allocations (MapToIPv6).
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                boundPort = port;
                return socket;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
            }
        }

        return null;
    }

    private void UdpLoop(CancellationToken ct)
    {
        Socket? udp = null;
        try
        {
            udp = TryBindToPort(DesiredPort, MaxPortSearchAttempts, out var boundPort);
            if (udp == null || boundPort == 0)
            {
                ActualPort = 0;
                return;
            }

            ActualPort = boundPort;
            udp.ReceiveBufferSize = ReceiveBufferBytes;

            _rxBuf ??= GC.AllocateArray<byte>(65527, true);
            var mem = _rxBuf.AsMemory();

            // Reuse a single EndPoint instance; sender address is not used.
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (!ct.IsCancellationRequested)
            {
                int received;
                try
                {
                    // On Linux this avoids some per-call overhead vs sync ReceiveFrom in tight loops.
                    var rr = udp.ReceiveFromAsync(mem, SocketFlags.None, remote, ct).AsTask().GetAwaiter().GetResult();
                    received = rr.ReceivedBytes;
                    // NOTE: sender endpoint is unused; avoid assigning rr.RemoteEndPoint to prevent per-packet endpoint allocations.
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    if (ct.IsCancellationRequested) break;
                    continue;
                }

                if (received < Lav1Constants.HeaderSize)
                    continue;

                var packet = _rxBuf.AsSpan(0, received);
                if (!Lav1HeaderCodec.TryReadHeader(packet, out var h))
                    continue;

                var payloadLen = h.PayloadLen;
                if (Lav1Constants.HeaderSize + payloadLen > packet.Length)
                    continue;

                var payload = packet.Slice(Lav1Constants.HeaderSize, payloadLen);

                if (_streamId != 0 && h.StreamId != _streamId)
                    ResetStream(h.StreamId);

                if (_streamId == 0)
                    _streamId = h.StreamId;

                switch (h.MessageType)
                {
                    case Lav1MessageType.StreamConfig:
                        if (Lav1StreamConfigCodec.TryReadPayload(payload, out var cfg))
                        {
                            lock (_sync)
                            {
                                _config = cfg;
                                _haveConfig = true;

                                // suggested hold with mild bias
                                _audioHoldMs = Math.Clamp(cfg.SuggestedHoldMs - 40, 0, 2000);
                                _videoHoldMs = Math.Clamp(cfg.SuggestedHoldMs + 40, 0, 2000);
                            }

                            EnsureAudioOutput(cfg);
                        }

                        break;

                    case Lav1MessageType.VideoFragment:
                        HandleVideoFragment(h.PtsUs, payload);
                        break;

                    case Lav1MessageType.AudioChunk:
                        HandleAudioChunk(h.PtsUs, payload);
                        break;
                }
            }
        }
        finally
        {
            try
            {
                udp?.Close();
            }
            catch
            {
            }

            udp?.Dispose();
            ActualPort = 0;
        }
    }

    private void EnsureAudioOutput(in Lav1StreamConfig cfg)
    {
        // Start (or restart) audio output when stream config arrives/changes.
        if (cfg.AudioSampleRate == 0 || cfg.AudioChannels == 0)
            return;

        lock (_audioLock)
        {
            if (_audioPlayer != null && _audioSampleRate == cfg.AudioSampleRate &&
                _audioChannels == cfg.AudioChannels && _audioFormat == cfg.AudioFormat)
                return;

            try
            {
                _audioPlayer?.Stop();
            }
            catch
            {
            }

            _audioSampleRate = cfg.AudioSampleRate;
            _audioChannels = cfg.AudioChannels;
            _audioFormat = cfg.AudioFormat;

            _audioPlayer = new ByteStreamPlayer();
            _audioPlayer.Start(cfg.AudioSampleRate, cfg.AudioChannels, cfg.AudioFormat, 90, AudioBackend);
        }
    }

    private void HandleVideoFragment(long ptsUs, ReadOnlySpan<byte> payload)
    {
        Lav1StreamConfig cfg;
        int holdMs;

        lock (_sync)
        {
            if (!_haveConfig)
                return;
            cfg = _config;
            holdMs = _videoHoldMs;
        }

        if (!Lav1VideoFragment.TryReadHeader(payload, out var frameId, out var frameBytesU, out var fragIndex,
                out var fragCount, out var fragOffset))
            return;

        var frameBytes = (int)frameBytesU;
        if (frameBytes <= 0) return;
        if (frameBytes != ExpectedFrameBytes(cfg)) return;

        EnsureBaseClock(ptsUs);

        var fragPayload = payload.Slice(Lav1VideoFragment.PayloadHeaderSize);

        FrameAssembly asm;
        lock (_frames)
        {
            if (!_frames.TryGetValue(frameId, out asm!))
            {
                // FIX: Limit frame dictionary size to prevent memory exhaustion
                if (_frames.Count >= MaxPendingFrames)
                {
                    // Find and remove oldest frame
                    var oldestKey = _frames.Keys.OrderBy(k => _frames[k].LastUpdateUtcTicks).FirstOrDefault();
                    if (oldestKey != 0 && _frames.Remove(oldestKey, out var oldest)) oldest.Dispose();
                }

                asm = new FrameAssembly(frameId, frameBytes, fragCount, ptsUs);
                asm.InitBuffers();
                _frames[frameId] = asm;
            }
        }

        if (asm.FrameBytes != frameBytes || asm.FragCount != fragCount) return;
        if (fragIndex >= asm.FragCount) return;

        var nowTicks = DateTime.UtcNow.Ticks;

        if (!asm.IsReceived(fragIndex))
        {
            var off = (int)fragOffset;
            if (off >= 0 && off + fragPayload.Length <= asm.FrameBytes)
            {
                fragPayload.CopyTo(asm.Buffer.AsSpan(off, fragPayload.Length));
                asm.MarkReceived(fragIndex);
                asm.Remaining--;
            }
        }

        if (asm.Remaining == 0)
        {
            lock (_frames)
            {
                _frames.Remove(frameId);
            }

            var dueTicks = PtsToDueUtcTicks(asm.PtsUs, holdMs);
            lock (_videoQ)
            {
                _videoQ.Enqueue(
                    new ScheduledFrame(dueTicks, asm.Buffer, asm.FrameBytes, cfg.Width, cfg.Height, cfg.PixelFormat),
                    dueTicks);
            }

            asm.DetachBuffer();
            asm.DisposeBitset();
        }
        else
        {
            // Drop stale incomplete frames. IMPORTANT: compare against the last update *before* we refresh it.
            if (nowTicks - asm.LastUpdateUtcTicks > TimeSpan.FromMilliseconds(200).Ticks)
            {
                // FIX: Remove frame first, then dispose outside lock to reduce contention
                FrameAssembly? stale = null;
                lock (_frames)
                {
                    if (_frames.Remove(frameId, out stale))
                    {
                        // Removed successfully
                    }
                }

                // Dispose outside the lock
                stale?.Dispose();
                return;
            }

            asm.LastUpdateUtcTicks = nowTicks;
        }
    }

    private void HandleAudioChunk(long ptsUs, ReadOnlySpan<byte> payload)
    {
        if (!Lav1AudioChunk.TryReadHeader(payload, out _, out var sampleRate, out var channels, out var format, out _))
            return;

        // Validate audio parameters before using them.
        if (channels < 1 || channels > 8)
            // Invalid channel count (likely corrupt packet) - drop it.
            return;

        if (sampleRate < 8000 || sampleRate > 192000)
            // Invalid sample rate - drop it.
            return;

        // For now we only support S16LE end-to-end.
        if (format != Lav1AudioChunk.AudioFormatS16LE) return;

        // If the stream changes audio parameters mid-flight, restart output.
        lock (_audioLock)
        {
            if (_audioPlayer == null || _audioSampleRate != sampleRate || _audioChannels != channels ||
                _audioFormat != format)
            {
                var cfg = _config;
                cfg = cfg with { AudioSampleRate = sampleRate, AudioChannels = channels, AudioFormat = format };

                try
                {
                    EnsureAudioOutput(cfg);
                }
                catch (Exception ex)
                {
                    // If audio output fails to initialize with these parameters, log and drop packets.
                    Console.WriteLine($"[LAV1] Audio output failed: {ex.Message}. Dropping audio.");
                    _audioPlayer = null;
                    _audioSampleRate = 0;
                    _audioChannels = 0;
                    _audioFormat = 0;
                    return;
                }
            }
        }

        EnsureBaseClock(ptsUs);

        var pcmPayload = payload.Slice(Lav1AudioChunk.PayloadHeaderSize);
        if (pcmPayload.Length <= 0) return;

        var buf = ArrayPool<byte>.Shared.Rent(pcmPayload.Length);
        pcmPayload.CopyTo(buf);

        int holdMs;
        lock (_sync)
        {
            holdMs = _audioHoldMs;
        }

        var dueTicks = PtsToDueUtcTicks(ptsUs, holdMs);
        lock (_audioQ)
        {
            _audioQ.Enqueue(new ScheduledAudio(dueTicks, buf, pcmPayload.Length), dueTicks);
        }
    }

    private async Task VideoSchedulerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScheduledFrame? sf = null;
            long nextDue = 0;
            var nowTicks = DateTime.UtcNow.Ticks;

            lock (_videoQ)
            {
                if (_videoQ.TryPeek(out var next, out var due))
                {
                    nextDue = due;
                    if (due <= nowTicks)
                    {
                        _videoQ.Dequeue();
                        sf = next;
                    }
                }
            }

            if (sf.HasValue)
            {
                try
                {
                    RenderFrame(sf.Value);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(sf.Value.FrameBuffer, true); // FIX: Clear array
                }

                continue;
            }

            if (nextDue == 0)
            {
                await Task.Delay(5, ct);
                continue;
            }

            var ms = (nextDue - nowTicks) / (double)TimeSpan.TicksPerMillisecond;
            if (ms > 10)
                await Task.Delay(5, ct);
            else if (ms > 2)
                await Task.Delay(1, ct);
            else
                await Task.Yield();
        }
    }

    private void RenderFrame(in ScheduledFrame sf)
    {
        if (_bitmap == null || _backBuffer == null) return;

        lock (_bitmapLock)
        {
            // Copy video frame to _bitmap
            if (sf.PixelFormat == Lav1PixelFormat.Bgra8888)
                unsafe
                {
                    var dst = new Span<byte>((void*)_bitmap.GetPixels(), sf.Width * sf.Height * 4);
                    sf.FrameBuffer.AsSpan(0, sf.Width * sf.Height * 4).CopyTo(dst);
                }
            else
                unsafe
                {
                    var dst = (byte*)_bitmap.GetPixels().ToPointer();
                    var si = 0;
                    for (var i = 0; i < sf.Width * sf.Height; i++)
                    {
                        dst[2] = sf.FrameBuffer[si++];
                        dst[1] = sf.FrameBuffer[si++];
                        dst[0] = sf.FrameBuffer[si++];
                        dst[3] = 255;
                        dst += 4;
                    }
                }

            // Now compose to back buffer with background
            using var canvas = new SKCanvas(_backBuffer);

            // Clear with background color
            canvas.Clear(BackgroundColor);

            // Draw video frame on top of background
            canvas.DrawBitmap(_bitmap, 0, 0);
            canvas.Flush();_canvas.SubmitCompletedFrame(_backBuffer);
        }
    }

    private async Task AudioSchedulerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScheduledAudio? sa = null;
            long nextDue = 0;
            var nowTicks = DateTime.UtcNow.Ticks;

            lock (_audioQ)
            {
                if (_audioQ.TryPeek(out var next, out var due))
                {
                    nextDue = due;
                    if (due <= nowTicks)
                    {
                        _audioQ.Dequeue();
                        sa = next;
                    }
                }
            }

            if (sa.HasValue)
            {
                int dropLateMs;
                lock (_sync)
                {
                    dropLateMs = _audioDropLateMs;
                }

                if (dropLateMs > 0)
                {
                    var lateMs = (nowTicks - sa.Value.DueUtcTicks) / (double)TimeSpan.TicksPerMillisecond;
                    if (lateMs > dropLateMs)
                    {
                        ArrayPool<byte>.Shared.Return(sa.Value.Pcm);
                        continue;
                    }
                }

                ByteStreamPlayer? player;
                lock (_audioLock)
                {
                    player = _audioPlayer;
                }

                player?.AddBytes(sa.Value.Pcm.AsSpan(0, sa.Value.Length), sa.Value.Length);
                ArrayPool<byte>.Shared.Return(sa.Value.Pcm, true); // FIX: Clear array
                continue;
            }

            if (nextDue == 0)
            {
                await Task.Delay(5, ct);
                continue;
            }

            var ms = (nextDue - nowTicks) / (double)TimeSpan.TicksPerMillisecond;
            if (ms > 10)
                await Task.Delay(5, ct);
            else if (ms > 2)
                await Task.Delay(1, ct);
            else
                await Task.Yield();
        }
    }

    private sealed class FrameAssembly
    {
        public FrameAssembly(uint frameId, int frameBytes, ushort fragCount, long ptsUs)
        {
            FrameId = frameId;
            FrameBytes = frameBytes;
            FragCount = fragCount;
            PtsUs = ptsUs;
        }

        public uint FrameId { get; }
        public int FrameBytes { get; }
        public ushort FragCount { get; }
        public long PtsUs { get; }

        public byte[] Buffer { get; private set; } = Array.Empty<byte>();
        public ulong[] ReceivedBits { get; private set; } = Array.Empty<ulong>();
        public int Remaining { get; set; }
        public long LastUpdateUtcTicks { get; set; }

        public void InitBuffers()
        {
            Buffer = ArrayPool<byte>.Shared.Rent(FrameBytes);
            ReceivedBits = ArrayPool<ulong>.Shared.Rent((FragCount + 63) / 64);
            Array.Clear(ReceivedBits, 0, (FragCount + 63) / 64);
            Remaining = FragCount;
            LastUpdateUtcTicks = DateTime.UtcNow.Ticks;
        }

        public bool IsReceived(ushort fragIndex)
        {
            var w = fragIndex >> 6;
            var b = fragIndex & 63;
            return (ReceivedBits[w] & (1UL << b)) != 0;
        }

        public void MarkReceived(ushort fragIndex)
        {
            var w = fragIndex >> 6;
            var b = fragIndex & 63;
            ReceivedBits[w] |= 1UL << b;
        }

        public void DetachBuffer()
        {
            Buffer = Array.Empty<byte>();
        }

        public void DisposeBitset()
        {
            if (ReceivedBits.Length != 0)
                ArrayPool<ulong>.Shared.Return(ReceivedBits, true);
            ReceivedBits = Array.Empty<ulong>();
        }

        public void Dispose()
        {
            if (Buffer.Length != 0)
                ArrayPool<byte>.Shared.Return(Buffer);
            Buffer = Array.Empty<byte>();

            DisposeBitset();
        }
    }

    private readonly record struct ScheduledFrame(
        long DueUtcTicks,
        byte[] FrameBuffer,
        int Length,
        ushort Width,
        ushort Height,
        Lav1PixelFormat PixelFormat);

    private readonly record struct ScheduledAudio(long DueUtcTicks, byte[] Pcm, int Length);
}