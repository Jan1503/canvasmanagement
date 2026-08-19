using System.Net;
using System.Net.Sockets;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.NetworkStreamPlayer;

/// <summary>
///     High-performance UDP network stream player for TPM2.NET and TPM2MAX.NET protocols
///     Optimized for Raspberry Pi and LED matrix displays
///     NOTE: This class is maintained for backward compatibility. Use NetworkStreamPlayerExtension instead.
/// </summary>
[Obsolete("Use NetworkStreamPlayerExtension instead. This class is maintained for backward compatibility only.")]
public class NetworkStreamPlayer : IDisposable
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
    private readonly ICanvas _parentCanvas;
    private readonly Timer _timeoutWatchdog;
    private CancellationTokenSource? _cts;
    private bool _firstStart = true;
    private SKBitmap? _frameBitmap;
    private byte[]? _imageBuffer;
    private Task? _listenerTask;

    // Pre-allocated buffers to reduce GC pressure
    private byte[]? _receiveBuffer;

    internal NetworkStreamPlayer(ICanvas canvas)
    {
        _parentCanvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _timeoutWatchdog = new Timer(TimeSpan.FromSeconds(DEFAULT_TIMEOUT_SECONDS));
        _timeoutWatchdog.Elapsed += OnTimeoutElapsed;
    }

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _timeoutWatchdog?.Dispose();
        GC.SuppressFinalize(this);
    }

    public event EventHandler? StreamStopped;
    public event EventHandler? StreamStarted;

    /// <summary>
    ///     Starts the UDP listener on the specified port
    /// </summary>
    public void StartListener(int port, int bufferSize = DEFAULT_BUFFER_SIZE)
    {
        if (IsRunning)
        {
            Console.WriteLine("NetworkStreamPlayer is already running");
            return;
        }

        Stop(); // Clean up any previous state

        _cts = new CancellationTokenSource();
        _listenerTask = StartListenerAsync(port, bufferSize, _cts.Token);
        IsRunning = true;

        Console.WriteLine($"NetworkStreamPlayer started on port {port}");
    }

    /// <summary>
    ///     Stops the UDP listener and cleans up resources
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

    private async Task StartListenerAsync(int port, int bufferSize, CancellationToken ct)
    {
        Socket? udpSocket = null;

        try
        {
            // Initialize socket
            var endpoint = new IPEndPoint(IPAddress.Any, port);
            udpSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(endpoint);
            udpSocket.ReceiveBufferSize = bufferSize;

            // Pre-allocate buffers (pinned to reduce GC pressure)
            _receiveBuffer = GC.AllocateArray<byte>(bufferSize, true);

            // Create frame bitmap
            _frameBitmap = new SKBitmap(_parentCanvas.Width, _parentCanvas.Height);
            var pixelCount = _frameBitmap.Width * _frameBitmap.Height;
            var frameSize = CalculateMaxFrameLength(pixelCount * 3, bufferSize - TPM2MAX_HEADER_SIZE);

            _imageBuffer = GC.AllocateArray<byte>(pixelCount * 3, true);

            var receiveMemory = _receiveBuffer.AsMemory();

            while (!ct.IsCancellationRequested)
                try
                {
                    var result = await udpSocket.ReceiveFromAsync(receiveMemory, SocketFlags.None, endpoint, ct);

                    if (result.ReceivedBytes > 0)
                    {
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
            _receiveBuffer = null;
            _imageBuffer = null;
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
        if (packetNum == numPackets && _frameBitmap != null)
        {
            if (_firstStart)
            {
                OnStreamStarted();
                _firstStart = false;
            }

            DrawBitmapFromRgb(_imageBuffer, _frameBitmap);
            _parentCanvas.DrawBitmap(_frameBitmap, 0, 0);
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

        // Optimized pixel copy
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var srcIdx = (x + y * width) * 3;

            *pixelsPtr++ = rgbData[srcIdx];
            *pixelsPtr++ = rgbData[srcIdx + 1];
            *pixelsPtr++ = rgbData[srcIdx + 2];
            *pixelsPtr++ = 0xFF;
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