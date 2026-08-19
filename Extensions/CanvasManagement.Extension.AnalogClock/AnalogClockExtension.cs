using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.AnalogClock;

[ExtensionInfo(
    "Analog Clock",
    "Displays the current time as an analog clock with various styles",
    "Clocks",
    IconResourceName = "clock.svg")]
public sealed class AnalogClockExtension(ICanvas canvas) : ICanvasExtension, IDisposable
{
    private readonly object _bitmapLock = new();
    private readonly ICanvas _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    // Double buffering
    private SKBitmap? _backBuffer;
    private int _borderWidth = 2;

    // Extension parameters
    private ClockStyle _clockStyle = ClockStyle.Classic;
    private bool _disposed;

    // Rendering
    private CancellationTokenSource? _renderCts;
    private Task? _renderTask;

    [ExtensionParameter("Background Color", "Background color for the clock",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Clock Style", "Visual style of the clock",
        DefaultValue = ClockStyle.Classic)]
    public ClockStyle ClockStyle
    {
        get => _clockStyle;
        set
        {
            _clockStyle = value;
            Console.WriteLine($"[AnalogClock] Style changed to: {value}");
        }
    }

    [ExtensionParameter("Face Color", "Color of the clock face (hex)",
        DefaultValue = "#FFFFFF")]
    public SKColor FaceColor { get; set; } = SKColors.White;

    [ExtensionParameter("Hour Hand Color", "Color of the hour hand (hex)",
        DefaultValue = "#000000")]
    public SKColor HourHandColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Minute Hand Color", "Color of the minute hand (hex)",
        DefaultValue = "#000000")]
    public SKColor MinuteHandColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Second Hand Color", "Color of the second hand (hex)",
        DefaultValue = "#FF0000")]
    public SKColor SecondHandColor { get; set; } = SKColors.Red;

    [ExtensionParameter("Markings Color", "Color of numbers and tick marks (hex)",
        DefaultValue = "#000000")]
    public SKColor MarkingsColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Border Color", "Color of the clock border (hex)",
        DefaultValue = "#000000")]
    public SKColor BorderColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Show Seconds", "Display the second hand",
        DefaultValue = true)]
    public bool ShowSeconds { get; set; } = true;

    [ExtensionParameter("Show Numbers", "Display hour numbers",
        DefaultValue = true)]
    public bool ShowNumbers { get; set; } = true;

    [ExtensionParameter("Show Tick Marks", "Display minute/hour tick marks",
        DefaultValue = true)]
    public bool ShowTickMarks { get; set; } = true;

    [ExtensionParameter("Smooth Seconds", "Smooth second hand animation",
        DefaultValue = false)]
    public bool SmoothSeconds { get; set; }

    [ExtensionParameter("Border Width", "Width of the clock border (0-10)",
        MinValue = 0, MaxValue = 10, DefaultValue = 2)]
    public int BorderWidth
    {
        get => _borderWidth;
        set => _borderWidth = Math.Clamp(value, 0, 10);
    }

    [ExtensionParameter("Current Time", "Current time displayed (read-only)",
        ReadOnly = true)]
    public string CurrentTime => DateTime.Now.ToString("HH:mm:ss");

    public string Name => "Analog Clock";
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
            return;

        try
        {
            Console.WriteLine("[AnalogClock] Starting Analog Clock extension...");

            // Create back buffer
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul));

            IsRunning = true;
            Console.WriteLine("[AnalogClock] Extension started successfully");

            StartRendering();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnalogClock] ERROR during Start(): {ex.Message}");
            Cleanup();
            throw;
        }
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        Cleanup();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private void Cleanup()
    {
        _renderCts?.Cancel();
        _renderTask?.Wait(1000);

        _backBuffer?.Dispose();
        _backBuffer = null;
    }

    private void StartRendering()
    {
        _renderCts = new CancellationTokenSource();
        var token = _renderCts.Token;

        _renderTask = Task.Run(async () =>
        {
            Console.WriteLine("[AnalogClock] Rendering started");

            while (IsRunning && !token.IsCancellationRequested)
                try
                {
                    RenderClock();

                    // Update at 30 FPS for smooth seconds, 1 FPS for normal
                    var delay = SmoothSeconds && ShowSeconds ? 33 : 1000;
                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AnalogClock] Error in render loop: {ex.Message}");
                }

            Console.WriteLine("[AnalogClock] Rendering stopped");
        }, token);
    }

    private void RenderClock()
    {
        if (_backBuffer == null) return;

        var now = DateTime.Now;

        lock (_bitmapLock)
        {
            using var canvas = new SKCanvas(_backBuffer);

            // Clear with background color
            canvas.Clear(BackgroundColor);

            switch (_clockStyle)
            {
                case ClockStyle.Classic:
                    RenderClassicClock(canvas, now);
                    break;
                case ClockStyle.Minimalist:
                    RenderMinimalistClock(canvas, now);
                    break;
                case ClockStyle.Roman:
                    RenderRomanClock(canvas, now);
                    break;
                case ClockStyle.Modern:
                    RenderModernClock(canvas, now);
                    break;
                case ClockStyle.CounterClockwise:
                    RenderCounterClockwiseClock(canvas, now);
                    break;
                case ClockStyle.Railway:
                    RenderRailwayClock(canvas, now);
                    break;
                case ClockStyle.Skeleton:
                    RenderSkeletonClock(canvas, now);
                    break;
                case ClockStyle.Retro:
                    RenderRetroClock(canvas, now);
                    break;
                default:
                    RenderClassicClock(canvas, now);
                    break;
            }

            canvas.Flush();// Use SubmitCompletedFrame for atomic, flicker-free rendering
            _canvas.SubmitCompletedFrame(_backBuffer);
        }
    }

    private void RenderClassicClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // Draw face
        using (var facePaint = new SKPaint
               {
                   Color = FaceColor,
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, facePaint);
        }

        // Draw border
        if (_borderWidth > 0)
        {
            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = _borderWidth,
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, radius, borderPaint);
        }

        // Draw tick marks
        if (ShowTickMarks) DrawTickMarks(canvas, centerX, centerY, radius);

        // Draw numbers
        if (ShowNumbers) DrawNumbers(canvas, centerX, centerY, radius);

        // Draw hands
        DrawHands(canvas, centerX, centerY, radius, time);
    }

    private void RenderMinimalistClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // No face, just minimal marks at 12, 3, 6, 9
        if (ShowTickMarks)
        {
            using var markPaint = new SKPaint
            {
                Color = MarkingsColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            for (var i = 0; i < 12; i += 3)
            {
                var angle = (i * 30 - 90) * Math.PI / 180;
                var x1 = centerX + (float)(Math.Cos(angle) * (radius - 8));
                var y1 = centerY + (float)(Math.Sin(angle) * (radius - 8));
                canvas.DrawCircle(x1, y1, 3, markPaint);
            }
        }

        // Thin hands
        DrawHands(canvas, centerX, centerY, radius, time, false, 1.5f, 1f, 0.5f);
    }

    private void RenderRomanClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // Draw face
        using (var facePaint = new SKPaint
               {
                   Color = FaceColor,
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, facePaint);
        }

        // Draw border
        if (_borderWidth > 0)
        {
            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = _borderWidth,
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, radius, borderPaint);
        }

        // Draw tick marks
        if (ShowTickMarks) DrawTickMarks(canvas, centerX, centerY, radius);

        // Draw Roman numerals
        if (ShowNumbers) DrawRomanNumerals(canvas, centerX, centerY, radius);

        // Draw hands
        DrawHands(canvas, centerX, centerY, radius, time);
    }

    private void RenderModernClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // Gradient face
        using (var gradientPaint = new SKPaint
               {
                   Shader = SKShader.CreateRadialGradient(
                       new SKPoint(centerX, centerY),
                       radius,
                       new[] { FaceColor, SKColors.LightGray },
                       new[] { 0f, 1f },
                       SKShaderTileMode.Clamp),
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, gradientPaint);
        }

        // Modern tick marks (dots)
        if (ShowTickMarks)
        {
            using var markPaint = new SKPaint
            {
                Color = MarkingsColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            for (var i = 0; i < 60; i++)
            {
                var size = i % 5 == 0 ? 3f : 1.5f;
                var angle = (i * 6 - 90) * Math.PI / 180;
                var x = centerX + (float)(Math.Cos(angle) * (radius - 10));
                var y = centerY + (float)(Math.Sin(angle) * (radius - 10));
                canvas.DrawCircle(x, y, size, markPaint);
            }
        }

        // Draw hands with rounded caps
        DrawHands(canvas, centerX, centerY, radius, time, true);
    }

    private void RenderCounterClockwiseClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // Draw face
        using (var facePaint = new SKPaint
               {
                   Color = FaceColor,
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, facePaint);
        }

        // Draw border
        if (_borderWidth > 0)
        {
            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = _borderWidth,
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, radius, borderPaint);
        }

        // Draw tick marks (counter-clockwise)
        if (ShowTickMarks) DrawTickMarks(canvas, centerX, centerY, radius, true);

        // Draw numbers (counter-clockwise)
        if (ShowNumbers) DrawNumbers(canvas, centerX, centerY, radius, true);

        // Draw hands (counter-clockwise)
        DrawHands(canvas, centerX, centerY, radius, time, false, 3f, 2f, 1f, true);
    }

    private void RenderRailwayClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // White face with black border (Swiss railway style)
        using (var facePaint = new SKPaint
               {
                   Color = SKColors.White,
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, facePaint);
        }

        using (var borderPaint = new SKPaint
               {
                   Color = SKColors.Black,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 4,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, borderPaint);
        }

        // Railway-style tick marks (thick rectangles)
        if (ShowTickMarks)
        {
            using var markPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            for (var i = 0; i < 12; i++)
            {
                var angle = (i * 30 - 90) * Math.PI / 180;
                var x = centerX + (float)(Math.Cos(angle) * (radius - 15));
                var y = centerY + (float)(Math.Sin(angle) * (radius - 15));

                canvas.Save();
                canvas.Translate(x, y);
                canvas.RotateDegrees(i * 30);
                canvas.DrawRect(-2, -8, 4, 12, markPaint);
                canvas.Restore();
            }
        }

        // Black hands with white outline, red second hand with circular tip
        DrawRailwayHands(canvas, centerX, centerY, radius, time);
    }

    private void RenderSkeletonClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // Transparent face, only border
        if (_borderWidth > 0)
        {
            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = _borderWidth,
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, radius, borderPaint);
        }

        // Minimal marks at 12, 3, 6, 9 only
        if (ShowTickMarks)
        {
            using var markPaint = new SKPaint
            {
                Color = MarkingsColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };

            for (var i = 0; i < 12; i += 3)
            {
                var angle = (i * 30 - 90) * Math.PI / 180;
                var x1 = centerX + (float)(Math.Cos(angle) * (radius - 15));
                var y1 = centerY + (float)(Math.Sin(angle) * (radius - 15));
                var x2 = centerX + (float)(Math.Cos(angle) * radius);
                var y2 = centerY + (float)(Math.Sin(angle) * radius);
                canvas.DrawLine(x1, y1, x2, y2, markPaint);
            }
        }

        // Skeleton-style hands (outlined)
        DrawSkeletonHands(canvas, centerX, centerY, radius, time);
    }

    private void RenderRetroClock(SKCanvas canvas, DateTime time)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - 10;

        // Cream/beige face
        using (var facePaint = new SKPaint
               {
                   Color = new SKColor(245, 235, 220),
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, facePaint);
        }

        // Brown border
        using (var borderPaint = new SKPaint
               {
                   Color = new SKColor(139, 69, 19),
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 4,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, radius, borderPaint);
        }

        // Retro-style thick marks
        if (ShowTickMarks)
        {
            using var markPaint = new SKPaint
            {
                Color = new SKColor(139, 69, 19),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            for (var i = 0; i < 12; i++)
            {
                var angle = (i * 30 - 90) * Math.PI / 180;
                var x = centerX + (float)(Math.Cos(angle) * (radius - 12));
                var y = centerY + (float)(Math.Sin(angle) * (radius - 12));

                canvas.Save();
                canvas.Translate(x, y);
                canvas.RotateDegrees(i * 30);
                canvas.DrawRect(-3, -6, 6, 10, markPaint);
                canvas.Restore();
            }
        }

        // Vintage numbers
        if (ShowNumbers)
        {
            var fontSize = Math.Max(10, Math.Min(16, (int)(radius / 4)));
            using var font = new SKFont
            {
                Size = fontSize,
                Typeface = SKTypeface.FromFamilyName("Serif", SKFontStyle.Bold)
            };
            using var textPaint = new SKPaint
            {
                Color = new SKColor(139, 69, 19),
                IsAntialias = true
            };

            for (var i = 1; i <= 12; i++)
            {
                var angle = (i * 30 - 90) * Math.PI / 180;
                var x = centerX + (float)(Math.Cos(angle) * (radius - 25));
                var y = centerY + (float)(Math.Sin(angle) * (radius - 25)) + fontSize / 3;
                canvas.DrawText(i.ToString(), x, y, SKTextAlign.Center, font, textPaint);
            }
        }

        // Thick vintage hands
        DrawHands(canvas, centerX, centerY, radius, time, false, 5f, 3.5f, 2f);
    }

    private void DrawTickMarks(SKCanvas canvas, float centerX, float centerY, float radius,
        bool counterClockwise = false)
    {
        using var markPaint = new SKPaint
        {
            Color = MarkingsColor,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        for (var i = 0; i < 60; i++)
        {
            var multiplier = counterClockwise ? -1 : 1;
            var angle = (i * 6 * multiplier - 90) * Math.PI / 180;
            var isHourMark = i % 5 == 0;

            markPaint.StrokeWidth = isHourMark ? 2f : 1f;
            var innerRadius = radius - (isHourMark ? 12f : 8f);

            var x1 = centerX + (float)(Math.Cos(angle) * innerRadius);
            var y1 = centerY + (float)(Math.Sin(angle) * innerRadius);
            var x2 = centerX + (float)(Math.Cos(angle) * radius);
            var y2 = centerY + (float)(Math.Sin(angle) * radius);

            canvas.DrawLine(x1, y1, x2, y2, markPaint);
        }
    }

    private void DrawNumbers(SKCanvas canvas, float centerX, float centerY, float radius, bool counterClockwise = false)
    {
        var fontSize = Math.Max(8, Math.Min(16, (int)(radius / 4)));
        using var font = new SKFont
        {
            Size = fontSize
        };
        using var textPaint = new SKPaint
        {
            Color = MarkingsColor,
            IsAntialias = true
        };

        for (var i = 1; i <= 12; i++)
        {
            var displayNum = counterClockwise ? (12 - i + 12) % 12 : i;
            if (displayNum == 0) displayNum = 12;

            var multiplier = counterClockwise ? -1 : 1;
            var angle = (i * 30 * multiplier - 90) * Math.PI / 180;
            var x = centerX + (float)(Math.Cos(angle) * (radius - 20));
            var y = centerY + (float)(Math.Sin(angle) * (radius - 20)) + fontSize / 3;

            canvas.DrawText(displayNum.ToString(), x, y, SKTextAlign.Center, font, textPaint);
        }
    }

    private void DrawRomanNumerals(SKCanvas canvas, float centerX, float centerY, float radius)
    {
        var numerals = new[] { "XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI" };
        var fontSize = Math.Max(8, Math.Min(16, (int)(radius / 4)));

        using var font = new SKFont
        {
            Size = fontSize,
            Typeface = SKTypeface.FromFamilyName("Serif")
        };
        using var textPaint = new SKPaint
        {
            Color = MarkingsColor,
            IsAntialias = true
        };

        for (var i = 0; i < 12; i++)
        {
            var angle = (i * 30 - 90) * Math.PI / 180;
            var x = centerX + (float)(Math.Cos(angle) * (radius - 22));
            var y = centerY + (float)(Math.Sin(angle) * (radius - 22)) + fontSize / 3;

            canvas.DrawText(numerals[i], x, y, SKTextAlign.Center, font, textPaint);
        }
    }

    private void DrawHands(SKCanvas canvas, float centerX, float centerY, float radius, DateTime time,
        bool roundCap = false, float hourWidth = 3f, float minuteWidth = 2f, float secondWidth = 1f,
        bool counterClockwise = false)
    {
        var multiplier = counterClockwise ? -1 : 1;

        // Calculate angles
        var seconds = time.Second + time.Millisecond / 1000.0;
        var minutes = time.Minute + seconds / 60.0;
        var hours = time.Hour % 12 + minutes / 60.0;

        if (!SmoothSeconds) seconds = time.Second;

        var hourAngle = (hours * 30 * multiplier - 90) * Math.PI / 180;
        var minuteAngle = (minutes * 6 * multiplier - 90) * Math.PI / 180;
        var secondAngle = (seconds * 6 * multiplier - 90) * Math.PI / 180;

        var strokeCap = roundCap ? SKStrokeCap.Round : SKStrokeCap.Square;

        // Hour hand
        using (var hourPaint = new SKPaint
               {
                   Color = HourHandColor,
                   StrokeWidth = hourWidth,
                   StrokeCap = strokeCap,
                   IsAntialias = true
               })
        {
            var hourLength = radius * 0.5f;
            var hourX = centerX + (float)(Math.Cos(hourAngle) * hourLength);
            var hourY = centerY + (float)(Math.Sin(hourAngle) * hourLength);
            canvas.DrawLine(centerX, centerY, hourX, hourY, hourPaint);
        }

        // Minute hand
        using (var minutePaint = new SKPaint
               {
                   Color = MinuteHandColor,
                   StrokeWidth = minuteWidth,
                   StrokeCap = strokeCap,
                   IsAntialias = true
               })
        {
            var minuteLength = radius * 0.75f;
            var minuteX = centerX + (float)(Math.Cos(minuteAngle) * minuteLength);
            var minuteY = centerY + (float)(Math.Sin(minuteAngle) * minuteLength);
            canvas.DrawLine(centerX, centerY, minuteX, minuteY, minutePaint);
        }

        // Second hand
        if (ShowSeconds)
        {
            using var secondPaint = new SKPaint
            {
                Color = SecondHandColor,
                StrokeWidth = secondWidth,
                StrokeCap = strokeCap,
                IsAntialias = true
            };

            var secondLength = radius * 0.85f;
            var secondX = centerX + (float)(Math.Cos(secondAngle) * secondLength);
            var secondY = centerY + (float)(Math.Sin(secondAngle) * secondLength);
            canvas.DrawLine(centerX, centerY, secondX, secondY, secondPaint);
        }

        // Center dot
        using (var centerPaint = new SKPaint
               {
                   Color = MarkingsColor,
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, 4, centerPaint);
        }
    }

    private void DrawRailwayHands(SKCanvas canvas, float centerX, float centerY, float radius, DateTime time)
    {
        var seconds = time.Second + time.Millisecond / 1000.0;
        var minutes = time.Minute + seconds / 60.0;
        var hours = time.Hour % 12 + minutes / 60.0;

        if (!SmoothSeconds) seconds = time.Second;

        var hourAngle = (hours * 30 - 90) * Math.PI / 180;
        var minuteAngle = (minutes * 6 - 90) * Math.PI / 180;
        var secondAngle = (seconds * 6 - 90) * Math.PI / 180;

        // Hour hand (black with white outline)
        using (var outlinePaint = new SKPaint
               {
                   Color = SKColors.White,
                   StrokeWidth = 7,
                   StrokeCap = SKStrokeCap.Round,
                   IsAntialias = true
               })
        using (var hourPaint = new SKPaint
               {
                   Color = SKColors.Black,
                   StrokeWidth = 5,
                   StrokeCap = SKStrokeCap.Round,
                   IsAntialias = true
               })
        {
            var hourLength = radius * 0.5f;
            var hourX = centerX + (float)(Math.Cos(hourAngle) * hourLength);
            var hourY = centerY + (float)(Math.Sin(hourAngle) * hourLength);
            canvas.DrawLine(centerX, centerY, hourX, hourY, outlinePaint);
            canvas.DrawLine(centerX, centerY, hourX, hourY, hourPaint);
        }

        // Minute hand (black with white outline)
        using (var outlinePaint = new SKPaint
               {
                   Color = SKColors.White,
                   StrokeWidth = 6,
                   StrokeCap = SKStrokeCap.Round,
                   IsAntialias = true
               })
        using (var minutePaint = new SKPaint
               {
                   Color = SKColors.Black,
                   StrokeWidth = 4,
                   StrokeCap = SKStrokeCap.Round,
                   IsAntialias = true
               })
        {
            var minuteLength = radius * 0.75f;
            var minuteX = centerX + (float)(Math.Cos(minuteAngle) * minuteLength);
            var minuteY = centerY + (float)(Math.Sin(minuteAngle) * minuteLength);
            canvas.DrawLine(centerX, centerY, minuteX, minuteY, outlinePaint);
            canvas.DrawLine(centerX, centerY, minuteX, minuteY, minutePaint);
        }

        // Second hand (red with circular tip - Swiss railway style)
        if (ShowSeconds)
        {
            using var secondPaint = new SKPaint
            {
                Color = SKColors.Red,
                StrokeWidth = 2,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };

            var secondLength = radius * 0.85f;
            var secondX = centerX + (float)(Math.Cos(secondAngle) * secondLength);
            var secondY = centerY + (float)(Math.Sin(secondAngle) * secondLength);
            canvas.DrawLine(centerX, centerY, secondX, secondY, secondPaint);

            // Red circle at tip
            canvas.DrawCircle(secondX, secondY, 4, secondPaint);
        }

        // Center dot
        using (var centerPaint = new SKPaint
               {
                   Color = SKColors.Black,
                   Style = SKPaintStyle.Fill,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, 5, centerPaint);
        }
    }

    private void DrawSkeletonHands(SKCanvas canvas, float centerX, float centerY, float radius, DateTime time)
    {
        var seconds = time.Second + time.Millisecond / 1000.0;
        var minutes = time.Minute + seconds / 60.0;
        var hours = time.Hour % 12 + minutes / 60.0;

        if (!SmoothSeconds) seconds = time.Second;

        var hourAngle = (hours * 30 - 90) * Math.PI / 180;
        var minuteAngle = (minutes * 6 - 90) * Math.PI / 180;
        var secondAngle = (seconds * 6 - 90) * Math.PI / 180;

        // Hour hand (outlined)
        using (var hourPaint = new SKPaint
               {
                   Color = HourHandColor,
                   StrokeWidth = 4,
                   Style = SKPaintStyle.Stroke,
                   IsAntialias = true
               })
        {
            var hourLength = radius * 0.5f;
            var hourX = centerX + (float)(Math.Cos(hourAngle) * hourLength);
            var hourY = centerY + (float)(Math.Sin(hourAngle) * hourLength);
            canvas.DrawLine(centerX, centerY, hourX, hourY, hourPaint);
        }

        // Minute hand (outlined)
        using (var minutePaint = new SKPaint
               {
                   Color = MinuteHandColor,
                   StrokeWidth = 3,
                   Style = SKPaintStyle.Stroke,
                   IsAntialias = true
               })
        {
            var minuteLength = radius * 0.75f;
            var minuteX = centerX + (float)(Math.Cos(minuteAngle) * minuteLength);
            var minuteY = centerY + (float)(Math.Sin(minuteAngle) * minuteLength);
            canvas.DrawLine(centerX, centerY, minuteX, minuteY, minutePaint);
        }

        // Second hand (thin outlined)
        if (ShowSeconds)
        {
            using var secondPaint = new SKPaint
            {
                Color = SecondHandColor,
                StrokeWidth = 2,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            var secondLength = radius * 0.85f;
            var secondX = centerX + (float)(Math.Cos(secondAngle) * secondLength);
            var secondY = centerY + (float)(Math.Sin(secondAngle) * secondLength);
            canvas.DrawLine(centerX, centerY, secondX, secondY, secondPaint);
        }

        // Center circle (outlined)
        using (var centerPaint = new SKPaint
               {
                   Color = MarkingsColor,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 2,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centerX, centerY, 5, centerPaint);
        }
    }

    private SKColor ParseColor(string hexColor)
    {
        try
        {
            return SKColor.Parse(hexColor);
        }
        catch
        {
            return SKColors.Black;
        }
    }
}