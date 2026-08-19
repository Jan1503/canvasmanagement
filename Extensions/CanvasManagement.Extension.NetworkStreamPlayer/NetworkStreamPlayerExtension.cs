using System.Net;
using System.Net.Sockets;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.NetworkStreamPlayer;

/// <summary>
///     High-performance UDP network stream player extension for TPM2.NET and TPM2MAX.NET protocols
///     Automatically finds available UDP port if specified port is in use
///     Optimized for Raspberry Pi and LED matrix displays
/// </summary>
[ExtensionInfo("Network Stream Player",
    "Receives UDP network streams using TPM2.NET/TPM2MAX.NET protocols for LED displays",
    "Media Players",
    IconResourceName = "network.svg")]
public class NetworkStreamPlayerExtension : ICanvasExtension, IDisposable
{
    // Protocol constants
    private const byte PROTOCOL_TPM2_NET = 0x9C;
    private const byte PROTOCOL_TPM2MAX_NET = 0x9D;
    private const byte PACKET_TYPE_DATA = 0xDA;
    private const byte PACKET_TYPE_COMMAND = 0xC0;
    private const byte COMMAND_READ_WITH_ANSWER = 0x40;
    private const byte COMMAND_TYPE_PIXEL_CONFIG = 0x10;
    private const byte PACKET_END_MARKER = 0x36;
    private const byte RESPONSE_ACK = 0xAD;
    private const byte TPM2_HEADER_SIZE = 7;
    private const byte TPM2MAX_HEADER_SIZE = 9;

    private const int DEFAULT_TIMEOUT_SECONDS = 10;
    private const int DEFAULT_BUFFER_SIZE = 48000;
    private const int DEFAULT_PORT = 65506;
    private const int MAX_PORT_SEARCH_ATTEMPTS = 50;
    private readonly ICanvas _parentCanvas;
    private readonly Timer _timeoutWatchdog;
    private SKBitmap? _backBuffer; // Back buffer for atomic frame submission
    private CancellationTokenSource? _cts;
    private bool _firstStart = true;
    private SKBitmap? _frameBitmap;
    private byte[]? _imageBuffer;
    private Task? _listenerTask;

    // Pre-allocated buffers to reduce GC pressure
    private byte[]? _receiveBuffer;

    internal NetworkStreamPlayerExtension(ICanvas canvas)
    {
        _parentCanvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _timeoutWatchdog = new Timer(TimeSpan.FromSeconds(DEFAULT_TIMEOUT_SECONDS));
        _timeoutWatchdog.Elapsed += OnTimeoutElapsed;
    }

    [ExtensionParameter("Background Color", "Background color for the player",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Desired Port", "Preferred UDP port to listen on (will auto-increment if busy)",
        MinValue = 1024, MaxValue = 65535, DefaultValue = DEFAULT_PORT)]
    public int DesiredPort { get; set; } = DEFAULT_PORT;

    [ExtensionParameter("Actual Port", "Currently bound UDP port (read-only)",
        ReadOnly = true)]
    public int ActualPort { get; private set; }

    [ExtensionParameter("Buffer Size", "UDP receive buffer size in bytes",
        MinValue = 4096, MaxValue = 524288, DefaultValue = DEFAULT_BUFFER_SIZE, Unit = "bytes")]
    public int BufferSize { get; set; } = DEFAULT_BUFFER_SIZE;

    [ExtensionParameter("Timeout", "Stream timeout in seconds (auto-stop if no data)",
        MinValue = 1, MaxValue = 300, DefaultValue = DEFAULT_TIMEOUT_SECONDS, Unit = "seconds")]
    public int TimeoutSeconds { get; set; } = DEFAULT_TIMEOUT_SECONDS;

    [ExtensionParameter("Max Port Search", "Maximum number of ports to try if desired port is busy",
        MinValue = 1, MaxValue = 100, DefaultValue = MAX_PORT_SEARCH_ATTEMPTS)]
    public int MaxPortSearchAttempts { get; set; } = MAX_PORT_SEARCH_ATTEMPTS;

    [ExtensionParameter("Frames Received", "Total number of frames received since start",
        ReadOnly = true)]
    public long FramesReceived { get; private set; }

    [ExtensionParameter("Packets Received", "Total number of packets received since start",
        ReadOnly = true)]
    public long PacketsReceived { get; private set; }

    [ExtensionParameter("Stream Active", "Whether stream data is currently being received",
        ReadOnly = true)]
    public bool StreamActive => IsRunning && !_firstStart;

    public string Name => "Network Stream Player";

    public bool IsRunning { get; private set; }

    /// <summary>
    ///     Start listening for UDP network streams
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            Console.WriteLine("NetworkStreamPlayer is already running");
            return;
        }

        Stop(); // Clean up any previous state

        // Update timeout watchdog interval
        _timeoutWatchdog.Interval = TimeSpan.FromSeconds(TimeoutSeconds).TotalMilliseconds;

        _cts = new CancellationTokenSource();
        _listenerTask = StartListenerAsync(_cts.Token);
        IsRunning = true;
    }

    /// <summary>
    ///     Stop listening and clean up resources
    /// </summary>
    public void Stop()
    {
        if (!IsRunning && _cts == null) return;

        _timeoutWatchdog.Stop();
        _cts?.Cancel();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is AggregateException or TaskCanceledException)
        {
            // Expected during cancellation
        }

        _cts?.Dispose();
        _cts = null;
        _listenerTask = null;
        IsRunning = false;
        ActualPort = 0;

        Console.WriteLine("NetworkStreamPlayer stopped");
    }

    public void Dispose()
    {
        Stop();
        _timeoutWatchdog?.Dispose();
        GC.SuppressFinalize(this);
    }

    public event EventHandler? StreamStopped;
    public event EventHandler? StreamStarted;

    /// <summary>
    ///     Try to bind to a UDP port, with automatic retry on higher ports if busy
    /// </summary>
    private Socket? TryBindToPort(int startPort, int maxAttempts, out int boundPort)
    {
        boundPort = 0;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var port = startPort + attempt;

            // Validate port range
            if (port > 65535)
            {
                Console.WriteLine($"Port {port} exceeds maximum (65535), stopping search");
                break;
            }

            try
            {
                var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                var endpoint = new IPEndPoint(IPAddress.Any, port);
                socket.Bind(endpoint);

                boundPort = port;

                if (port != startPort)
                    Console.WriteLine($"Port {startPort} was busy, successfully bound to port {port}");
                else
                    Console.WriteLine($"Successfully bound to desired port {port}");

                return socket;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                if (attempt == 0) Console.WriteLine($"Port {port} is already in use, trying next port...");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket error on port {port}: {ex.Message}");
                break;
            }
        }

        Console.WriteLine($"Failed to bind to any port in range {startPort}-{startPort + maxAttempts - 1}");
        return null;
    }

    /// <summary>
    ///     Calculates optimal frame length for UDP packet size
    /// </summary>
    private static int CalculateMaxFrameLength(int pixelCount, int maxFrameLength)
    {
        for (var i = maxFrameLength; i >= 1; i--)
            if (pixelCount % i == 0)
                return i;
        return 1;
    }

    private async Task StartListenerAsync(CancellationToken ct)
    {
        Socket? udpSocket = null;

        try
        {
            // Try to bind to port with automatic retry
            udpSocket = TryBindToPort(DesiredPort, MaxPortSearchAttempts, out var boundPort);

            if (udpSocket == null || boundPort == 0)
            {
                Console.WriteLine("Failed to bind to any UDP port. NetworkStreamPlayer cannot start.");
                IsRunning = false;
                return;
            }

            ActualPort = boundPort;
            udpSocket.ReceiveBufferSize = BufferSize;

            // Pre-allocate buffers (pinned to reduce GC pressure)
            _receiveBuffer = GC.AllocateArray<byte>(BufferSize, true);

            // Create frame bitmap
            _frameBitmap = new SKBitmap(_parentCanvas.Width, _parentCanvas.Height);
            _backBuffer = new SKBitmap(_parentCanvas.Width, _parentCanvas.Height);
            var pixelCount = _frameBitmap.Width * _frameBitmap.Height;
            // Keep every datagram inside one Ethernet frame (no IP fragmentation). Huge (e.g. 32 KB)
            // datagrams get fragmented into ~20+ IP fragments; losing any one drops the whole packet, and a
            // large canvas then loses its last packet almost every frame -> the stream crawls. The RX socket
            // buffer (BufferSize) still absorbs many of these small packets. Senders auto-negotiate this via
            // the config reply, so the packet size stays in lock-step.
            const int MAX_UDP_PAYLOAD = 1400; // < 1472 (1500 MTU - IP/UDP headers), minus TPM2 header
            var maxPayload = Math.Min(BufferSize - TPM2MAX_HEADER_SIZE, MAX_UDP_PAYLOAD);
            var frameSize = CalculateMaxFrameLength(pixelCount * 3, maxPayload);

            _imageBuffer = GC.AllocateArray<byte>(pixelCount * 3, true);

            // Reset counters
            FramesReceived = 0;
            PacketsReceived = 0;
            _firstStart = true;

            Console.WriteLine($"NetworkStreamPlayer started on port {ActualPort} (buffer: {BufferSize} bytes)");

            var receiveMemory = _receiveBuffer.AsMemory();
            var endpoint = new IPEndPoint(IPAddress.Any, ActualPort);

            while (!ct.IsCancellationRequested)
                try
                {
                    var result = await udpSocket.ReceiveFromAsync(receiveMemory, SocketFlags.None, endpoint, ct);

                    if (result.ReceivedBytes > 0)
                    {
                        PacketsReceived++;
                        ResetTimeoutWatchdog();
                        await ProcessPacketAsync(receiveMemory[..result.ReceivedBytes], result.RemoteEndPoint,
                            udpSocket, frameSize, ct);
                    }
                }
                catch (SocketException ex) when (!ct.IsCancellationRequested)
                {
                    Console.WriteLine($"Socket error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NetworkStreamPlayer error: {ex.Message}");
        }
        finally
        {
            udpSocket?.Close();
            udpSocket?.Dispose();
            _frameBitmap?.Dispose();
            _frameBitmap = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            _receiveBuffer = null;
            _imageBuffer = null;

            if (ActualPort != 0) Console.WriteLine($"Released port {ActualPort}");
        }
    }

    private async Task ProcessPacketAsync(Memory<byte> packet, EndPoint remoteEndPoint,
        Socket socket, int frameSize, CancellationToken ct)
    {
        if (packet.Length < 2) return;

        var protocolType = packet.Span[0];
        var packetType = packet.Span[1];

        switch (packetType)
        {
            case PACKET_TYPE_DATA:
                ProcessDataPacket(packet, protocolType, frameSize);
                break;

            case PACKET_TYPE_COMMAND:
                await ProcessCommandPacketAsync(packet, remoteEndPoint, socket, frameSize, ct);
                break;
        }
    }

    private void ProcessDataPacket(Memory<byte> packet, byte protocolType, int frameSize)
    {
        if (packet.Length < 4) return;

        var frameLength = (packet.Span[2] << 8) | packet.Span[3];
        var (packetNum, numPackets, headerSize) = ParsePacketInfo(packet, protocolType);

        if (packetNum == 0 || numPackets == 0) return;

        // Validate packet structure
        if (packet.Length < headerSize + frameLength) return;

        var endMarkerPos = headerSize - 1 + frameLength;
        if (endMarkerPos >= packet.Length) return;
        if (packet.Span[endMarkerPos] != PACKET_END_MARKER) return;

        // Copy RGB data to image buffer
        var rgbData = packet.Slice(headerSize - 1, frameLength);
        var offset = (packetNum - 1) * frameSize;

        if (offset + frameLength > _imageBuffer!.Length) return;

        rgbData.CopyTo(_imageBuffer.AsMemory(offset, frameLength));

        // Render on last packet
        if (packetNum == numPackets && _frameBitmap != null && _backBuffer != null)
        {
            if (_firstStart)
            {
                OnStreamStarted();
                _firstStart = false;
            }

            DrawBitmapFromRgb(_imageBuffer, _frameBitmap);

            // Compose to back buffer with background
            using var canvas = new SKCanvas(_backBuffer);

            // Clear with background color
            canvas.Clear(BackgroundColor);

            // Draw frame on top
            canvas.DrawBitmap(_frameBitmap, 0, 0);
            canvas.Flush();// Atomic submission
            _parentCanvas.SubmitCompletedFrame(_backBuffer);

            FramesReceived++;
        }
    }

    private async Task ProcessCommandPacketAsync(Memory<byte> packet, EndPoint remoteEndPoint,
        Socket socket, int frameSize, CancellationToken ct)
    {
        if (packet.Length < 10) return;

        var command = packet.Span[8];
        var commandType = packet.Span[9];

        if (command == COMMAND_READ_WITH_ANSWER && commandType == COMMAND_TYPE_PIXEL_CONFIG)
            await SendPixelConfigurationAsync(remoteEndPoint, socket, frameSize, ct);
    }

    private async Task SendPixelConfigurationAsync(EndPoint remoteEndPoint, Socket socket,
        int frameSize, CancellationToken ct)
    {
        if (_frameBitmap == null) return;

        var totalPixels = _frameBitmap.Width * _frameBitmap.Height * 3;
        var packetCount = (totalPixels + frameSize - 1) / frameSize;
        var protocol = packetCount > 255 ? PROTOCOL_TPM2MAX_NET : PROTOCOL_TPM2_NET;

        var response = new byte[12];
        response[0] = protocol;
        response[1] = RESPONSE_ACK;
        response[2] = (byte)((frameSize >> 8) & 0xFF);
        response[3] = (byte)(frameSize & 0xFF);
        response[4] = 1;
        response[5] = 1;
        response[6] = 0;
        response[7] = (byte)((_frameBitmap.Width >> 8) & 0xFF);
        response[8] = (byte)(_frameBitmap.Width & 0xFF);
        response[9] = (byte)((_frameBitmap.Height >> 8) & 0xFF);
        response[10] = (byte)(_frameBitmap.Height & 0xFF);
        response[11] = PACKET_END_MARKER;

        try
        {
            await socket.SendToAsync(response, SocketFlags.None, remoteEndPoint, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send configuration: {ex.Message}");
        }
    }

    private static (int packetNum, int numPackets, int headerSize) ParsePacketInfo(
        Memory<byte> packet, byte protocolType)
    {
        return protocolType switch
        {
            PROTOCOL_TPM2_NET => (
                packet.Span[4],
                packet.Span[5],
                TPM2_HEADER_SIZE
            ),
            PROTOCOL_TPM2MAX_NET => (
                (packet.Span[4] << 8) | packet.Span[5],
                (packet.Span[6] << 8) | packet.Span[7],
                TPM2MAX_HEADER_SIZE
            ),
            _ => (0, 0, 0)
        };
    }

    private unsafe void DrawBitmapFromRgb(byte[] rgbData, SKBitmap bitmap)
    {
        var pixelsPtr = (byte*)bitmap.GetPixels().ToPointer();
        var width = bitmap.Width;
        var height = bitmap.Height;

        // Optimized pixel copy - convert RGB to RGBA
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var srcIdx = (x + y * width) * 3;

            *pixelsPtr++ = rgbData[srcIdx]; // R
            *pixelsPtr++ = rgbData[srcIdx + 1]; // G
            *pixelsPtr++ = rgbData[srcIdx + 2]; // B
            *pixelsPtr++ = 0xFF; // A
        }
    }

    private void ResetTimeoutWatchdog()
    {
        _timeoutWatchdog.Stop();
        _timeoutWatchdog.Start();
    }

    private void OnTimeoutElapsed(object? sender, ElapsedEventArgs e)
    {
        _timeoutWatchdog.Stop();
        _firstStart = true;
        OnStreamStopped();
    }

    protected virtual void OnStreamStopped()
    {
        StreamStopped?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnStreamStarted()
    {
        StreamStarted?.Invoke(this, EventArgs.Empty);
    }
}