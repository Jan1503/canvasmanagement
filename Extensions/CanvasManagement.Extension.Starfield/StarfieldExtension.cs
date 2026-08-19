using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.Starfield;

[ExtensionInfo("Starfield",
    "Classic 3D starfield effect with parallax scrolling stars",
    "Visual Effects",
    IconResourceName = "starfield.svg")]
public class StarfieldExtension : IDisposable
{
    private readonly ICanvas _canvas;
    private readonly Random _random = new();
    private readonly List<Star> _stars = new();
    private Task? _animationTask;
    private SKBitmap? _backBuffer;
    private CancellationTokenSource? _cancellationTokenSource;

    internal StarfieldExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Background Color", "Background color for the starfield",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Star Count", "Number of stars in the field",
        DefaultValue = 200, MinValue = 50, MaxValue = 500)]
    public int StarCount { get; set; } = 200;

    [ExtensionParameter("Min Speed", "Minimum star speed",
        DefaultValue = 1, MinValue = 1, MaxValue = 5)]
    public int MinSpeed { get; set; } = 1;

    [ExtensionParameter("Max Speed", "Maximum star speed",
        DefaultValue = 3, MinValue = 2, MaxValue = 10)]
    public int MaxSpeed { get; set; } = 3;

    [ExtensionParameter("Colored Stars", "Use rainbow colors for stars",
        DefaultValue = false)]
    public bool ColoredStars { get; set; } = false;

    public string Name => "Starfield";

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
            // Initialize stars
            _stars.Clear();
            var centerX = _canvas.Width / 2f;
            var centerY = _canvas.Height / 2f;

            for (var i = 0; i < StarCount; i++)
                _stars.Add(new Star
                {
                    X = _random.Next(_canvas.Width),
                    Y = _random.Next(_canvas.Height),
                    Z = _random.Next(1, 20),
                    Speed = SpeedRange(),
                    Hue = ColoredStars ? _random.Next(360) : 0
                });

            // One reused paint instead of allocating one per star per frame.
            using var starPaint = new SKPaint { Style = SKPaintStyle.Fill };

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_backBuffer == null) break;

                    // Apply Star Count changes live.
                    while (_stars.Count < StarCount)
                        _stars.Add(new Star
                        {
                            X = _random.Next(_canvas.Width),
                            Y = _random.Next(_canvas.Height),
                            Z = _random.Next(1, 20),
                            Speed = SpeedRange(),
                            Hue = ColoredStars ? _random.Next(360) : 0
                        });
                    if (_stars.Count > StarCount)
                        _stars.RemoveRange(StarCount, _stars.Count - StarCount);

                    // Render to back buffer
                    using var canvas = new SKCanvas(_backBuffer);

                    // Clear with background color
                    canvas.Clear(BackgroundColor);

                    foreach (var star in _stars)
                    {
                        // Update star position (moving outward from center)
                        var dx = star.X - centerX;
                        var dy = star.Y - centerY;

                        star.X += dx / star.Z * star.Speed;
                        star.Y += dy / star.Z * star.Speed;
                        star.Z -= 0.1f;

                        // Reset star when it goes off screen or too close
                        if (star.X < 0 || star.X > _canvas.Width ||
                            star.Y < 0 || star.Y > _canvas.Height ||
                            star.Z < 1)
                        {
                            star.X = centerX + _random.Next(-10, 10);
                            star.Y = centerY + _random.Next(-10, 10);
                            star.Z = 20;
                            star.Speed = SpeedRange();
                            if (ColoredStars)
                                star.Hue = _random.Next(360);
                        }

                        // Draw star (size based on depth)
                        var size = (int)(5 - star.Z / 4);
                        if (size < 1) size = 1;

                        SKColor color;
                        if (ColoredStars)
                        {
                            var brightness = (byte)Math.Max(0, Math.Min(255, 255 - star.Z * 10));
                            color = SKColor.FromHsl(star.Hue, 100, brightness / 255f * 50);
                        }
                        else
                        {
                            var brightness = (byte)Math.Max(0, Math.Min(255, 255 - star.Z * 10));
                            color = new SKColor(brightness, brightness, brightness);
                        }

                        // Apply canvas opacity to star color
                        color = new SKColor(color.Red, color.Green, color.Blue,
                            (byte)(_canvas.Opacity * 255));

                        starPaint.Color = color;
                        canvas.DrawRect((int)star.X, (int)star.Y, size, size, starPaint);
                    }

                    canvas.Flush();// Atomic submission
                    _canvas.SubmitCompletedFrame(_backBuffer);

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
                _stars.Clear();
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
            Console.WriteLine($"Error stopping starfield: {ex.Message}");
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

    /// <summary>Random speed in [MinSpeed, MaxSpeed] (inclusive) that is safe when the two are equal.</summary>
    private int SpeedRange()
    {
        var lo = Math.Max(1, MinSpeed);
        var hi = Math.Max(lo + 1, MaxSpeed + 1);
        return _random.Next(lo, hi);
    }

    private class Star
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int Speed { get; set; }
        public int Hue { get; set; }
    }
}