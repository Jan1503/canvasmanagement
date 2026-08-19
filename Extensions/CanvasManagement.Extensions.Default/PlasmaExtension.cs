using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extensions.Default;

[ExtensionInfo("Plasma",
    "Animated plasma effect with flowing colors and patterns",
    "Visual Effects",
    IconResourceName = "plasma.svg")]
public class PlasmaExtension : IDisposable
{
    private readonly ICanvas _canvas;
    private Task? _animationTask;
    private SKBitmap? _backBuffer;
    private SKColor _backgroundColor = SKColors.Black;
    private CancellationTokenSource? _cancellationTokenSource;

    internal PlasmaExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Resolution", "Pixel block size (higher = faster but less detail)",
        DefaultValue = 3, MinValue = 1, MaxValue = 10)]
    public int Resolution { get; set; } = 3;

    [ExtensionParameter("Speed", "Animation speed",
        DefaultValue = 0.03, MinValue = 0.01, MaxValue = 0.2)]
    public double Speed { get; set; } = 0.03;

    [ExtensionParameter("Color Shift", "Color range in degrees (360 = full rainbow)",
        DefaultValue = 180, MinValue = 60, MaxValue = 360)]
    public int ColorShift { get; set; } = 180;

    [ExtensionParameter("Background Color", "Background color for plasma",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }
    public string Name => "Plasma";

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        if (IsRunning) return;

        IsRunning = true;

        // Create back buffer
        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        _animationTask = Task.Run(async () =>
        {
            double time = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_backBuffer == null) break;

                    // Render to back buffer
                    using var canvas = new SKCanvas(_backBuffer);

                    // Clear with background color (supports transparency)
                    if (_backgroundColor.Alpha == 0)
                    {
                        canvas.Clear(SKColors.Transparent);
                    }
                    else if (_backgroundColor.Alpha == 255)
                    {
                        canvas.Clear(_backgroundColor);
                    }
                    else
                    {
                        canvas.Clear(SKColors.Transparent);
                        using var bgPaint = new SKPaint { Color = _backgroundColor, Style = SKPaintStyle.Fill };
                        canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, bgPaint);
                    }

                    // Draw plasma effect. Wave divisors scale with the panel so the pattern keeps a similar
                    // feature count on any display size (no change at the 384x192 reference). One reused
                    // paint instead of allocating one per block (was thousands of allocations per frame).
                    var sc = DisplayScale.GetScale(_canvas.Width, _canvas.Height);
                    var d16 = 16.0 * sc;
                    var d8 = 8.0 * sc;
                    using var paint = new SKPaint { Style = SKPaintStyle.Fill };
                    for (var y = 0; y < _canvas.Height; y += Resolution)
                    for (var x = 0; x < _canvas.Width; x += Resolution)
                    {
                        // Calculate plasma value using multiple sine waves
                        double value = 0;
                        value += Math.Sin(x / d16 + time);
                        value += Math.Sin(y / d8 - time);
                        value += Math.Sin((x + y) / d16);
                        value += Math.Sin(Math.Sqrt(x * x + y * y) / d8 + time);
                        value /= 4.0;

                        // Convert to color
                        var hue = (float)((value + 1) * ColorShift + time * 30) % 360;
                        paint.Color = SKColor.FromHsl(hue, 100, 50);
                        canvas.DrawRect(x, y, Resolution, Resolution, paint);
                    }

                    canvas.Flush();// Atomic submission
                    _canvas.SubmitCompletedFrame(_backBuffer);

                    time += Speed;
                    await Task.Delay(33, ct); // ~30 FPS
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            finally
            {
                _canvas.Clear();
                IsRunning = false;
            }
        }, ct);
    }

    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            _cancellationTokenSource?.Cancel();
            _animationTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"Error stopping plasma: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _animationTask = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            _canvas.Clear();
            IsRunning = false;
        }
    }
}