using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.BinaryClock;

[ExtensionInfo("Binary Clock",
    "Display time in binary format with customizable LED style",
    "Clocks",
    IconResourceName = "binaryclock.svg")]
public class BinaryClockExtension : IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _renderLock = new();
    private SKBitmap? _backBuffer;
    private bool _disposed;
    private Timer? _updateTimer;

    internal BinaryClockExtension(ICanvas canvas)
    {
        _canvas = canvas;

        // Auto-fit the LED grid to the panel (defaults are sized for 384x192).
        var s = DisplayScale.GetScale(canvas.Width, canvas.Height);
        LEDSize = Math.Max(3, (int)Math.Round(20 * s));
        LEDSpacing = Math.Max(1, (int)Math.Round(5 * s));
    }

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _backBuffer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        if (IsRunning) return;

        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

        _updateTimer = new Timer(100); // Update 10 times per second
        _updateTimer.Elapsed += OnUpdate;
        _updateTimer.AutoReset = true;
        _updateTimer.Start();

        IsRunning = true;
        Console.WriteLine("Binary Clock started");
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _updateTimer?.Stop();
        _updateTimer?.Dispose();
        _updateTimer = null;

        _backBuffer?.Dispose();
        _backBuffer = null;

        try
        {
            _canvas.Clear(BackgroundColor);
        }
        catch
        {
        }

        IsRunning = false;
        Console.WriteLine("Binary Clock stopped");
    }

    private void OnUpdate(object? sender, ElapsedEventArgs e)
    {
        if (!IsRunning) return;

        try
        {
            _updateTimer?.Stop();
            Render();
            if (IsRunning && _updateTimer != null) _updateTimer.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Binary Clock update error: {ex.Message}");
            try
            {
                _updateTimer?.Start();
            }
            catch
            {
                Stop();
            }
        }
    }

    private void Render()
    {
        if (_backBuffer == null) return;

        lock (_renderLock)
        {
            try
            {
                using var canvas = new SKCanvas(_backBuffer);

                // Clear with background color
                canvas.Clear(BackgroundColor);

                var now = DateTime.Now;
                var hours = TwentyFourHour ? now.Hour : now.Hour % 12 == 0 ? 12 : now.Hour % 12;
                var minutes = now.Minute;
                var seconds = now.Second;

                // Calculate dimensions
                var columns = ShowSeconds ? 6 : 4;
                var rows = 4;

                var totalWidth = columns * (LEDSize + LEDSpacing) - LEDSpacing;
                var totalHeight = rows * (LEDSize + LEDSpacing) - LEDSpacing;

                var startX = (_canvas.Width - totalWidth) / 2;
                var startY = (_canvas.Height - totalHeight) / 2;

                var labelOffset = 0;
                if (ShowLabels)
                {
                    labelOffset = _canvas.ScaleSize(25);
                    startY += labelOffset / 2;
                }

                // Draw labels - properly centered above each column
                if (ShowLabels)
                {
                    using var labelFont = new SKFont
                    {
                        Size = _canvas.ScaleSizeF(12),
                        Typeface = SKTypeface.FromFamilyName("Arial")
                    };
                    using var labelPaint = new SKPaint
                    {
                        Color = SKColors.Gray,
                        IsAntialias = true
                    };

                    var labelY = startY - _canvas.ScaleSize(10);
                    var colWidth = LEDSize + LEDSpacing;

                    canvas.DrawText("H", startX + LEDSize / 2, labelY, SKTextAlign.Center, labelFont, labelPaint);
                    canvas.DrawText("H", startX + colWidth + LEDSize / 2, labelY, SKTextAlign.Center, labelFont,
                        labelPaint);
                    canvas.DrawText("M", startX + colWidth * 2 + LEDSize / 2, labelY, SKTextAlign.Center, labelFont,
                        labelPaint);
                    canvas.DrawText("M", startX + colWidth * 3 + LEDSize / 2, labelY, SKTextAlign.Center, labelFont,
                        labelPaint);
                    if (ShowSeconds)
                    {
                        canvas.DrawText("S", startX + colWidth * 4 + LEDSize / 2, labelY, SKTextAlign.Center, labelFont,
                            labelPaint);
                        canvas.DrawText("S", startX + colWidth * 5 + LEDSize / 2, labelY, SKTextAlign.Center, labelFont,
                            labelPaint);
                    }
                }

                // Draw binary time
                DrawBinaryDigit(canvas, hours / 10, startX, startY, 0);
                DrawBinaryDigit(canvas, hours % 10, startX, startY, 1);
                DrawBinaryDigit(canvas, minutes / 10, startX, startY, 2);
                DrawBinaryDigit(canvas, minutes % 10, startX, startY, 3);

                if (ShowSeconds)
                {
                    DrawBinaryDigit(canvas, seconds / 10, startX, startY, 4);
                    DrawBinaryDigit(canvas, seconds % 10, startX, startY, 5);
                }

                // Draw colon separators
                if (ShowColons)
                {
                    var colonY = startY + totalHeight / 2;
                    var colWidth = LEDSize + LEDSpacing;

                    using var colonPaint = new SKPaint
                    {
                        Color = LEDOnColor,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };

                    var dotR = Math.Max(1, _canvas.ScaleSize(3));

                    // First colon (between HH and MM)
                    var colon1X = startX + colWidth * 2 - LEDSpacing / 2;
                    canvas.DrawCircle(colon1X, colonY - LEDSize / 2, dotR, colonPaint);
                    canvas.DrawCircle(colon1X, colonY + LEDSize / 2, dotR, colonPaint);

                    // Second colon (between MM and SS)
                    if (ShowSeconds)
                    {
                        var colon2X = startX + colWidth * 4 - LEDSpacing / 2;
                        canvas.DrawCircle(colon2X, colonY - LEDSize / 2, dotR, colonPaint);
                        canvas.DrawCircle(colon2X, colonY + LEDSize / 2, dotR, colonPaint);
                    }
                }

                // Draw digital time text - properly centered
                if (ShowTimeText)
                {
                    var timeText = ShowSeconds
                        ? $"{hours:D2}:{minutes:D2}:{seconds:D2}"
                        : $"{hours:D2}:{minutes:D2}";

                    if (!TwentyFourHour) timeText += now.Hour >= 12 ? " PM" : " AM";

                    using var timeFont = new SKFont
                    {
                        Size = _canvas.ScaleSizeF(20),
                        Typeface = SKTypeface.FromFamilyName("Arial")
                    };
                    using var timePaint = new SKPaint
                    {
                        Color = SKColors.White,
                        IsAntialias = true
                    };

                    // Center the text below the LEDs
                    var textY = startY + totalHeight + _canvas.ScaleSize(35);
                    canvas.DrawText(timeText, _canvas.Width / 2, textY, SKTextAlign.Center, timeFont, timePaint);
                }

                canvas.Flush();// Blit to canvas
                _canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render error: {ex.Message}");
            }
        }
    }

    private void DrawBinaryDigit(SKCanvas canvas, int digit, int baseX, int baseY, int column)
    {
        var x = baseX + column * (LEDSize + LEDSpacing);

        for (var bit = 0; bit < 4; bit++)
        {
            var y = baseY + bit * (LEDSize + LEDSpacing);
            var isOn = ((digit >> (3 - bit)) & 1) == 1;
            var color = isOn ? LEDOnColor : LEDOffColor;

            using var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            switch ((LEDStyleType)LEDStyle)
            {
                case LEDStyleType.Circle:
                    canvas.DrawCircle(x + LEDSize / 2f, y + LEDSize / 2f, LEDSize / 2f, paint);
                    if (isOn && GlowEffect)
                    {
                        var glowColor = new SKColor(color.Red, color.Green, color.Blue, 80);
                        paint.Color = glowColor;
                        canvas.DrawCircle(x + LEDSize / 2f, y + LEDSize / 2f, LEDSize / 2f + 3, paint);
                    }

                    break;

                case LEDStyleType.Square:
                    canvas.DrawRect(x, y, LEDSize, LEDSize, paint);
                    if (isOn && GlowEffect)
                    {
                        var glowColor = new SKColor(color.Red, color.Green, color.Blue, 80);
                        paint.Color = glowColor;
                        canvas.DrawRect(x - 2, y - 2, LEDSize + 4, LEDSize + 4, paint);
                    }

                    break;

                case LEDStyleType.Diamond:
                    var path = new SKPath();
                    path.MoveTo(x + LEDSize / 2f, y);
                    path.LineTo(x + LEDSize, y + LEDSize / 2f);
                    path.LineTo(x + LEDSize / 2f, y + LEDSize);
                    path.LineTo(x, y + LEDSize / 2f);
                    path.Close();
                    canvas.DrawPath(path, paint);

                    if (isOn && GlowEffect)
                    {
                        var glowColor = new SKColor(color.Red, color.Green, color.Blue, 80);
                        paint.Color = glowColor;
                        var glowPath = new SKPath();
                        glowPath.MoveTo(x + LEDSize / 2f, y - 2);
                        glowPath.LineTo(x + LEDSize + 2, y + LEDSize / 2f);
                        glowPath.LineTo(x + LEDSize / 2f, y + LEDSize + 2);
                        glowPath.LineTo(x - 2, y + LEDSize / 2f);
                        glowPath.Close();
                        canvas.DrawPath(glowPath, paint);
                    }

                    break;
            }
        }
    }

    #region Parameters

    [ExtensionParameter("LED Size", "Size of each LED dot",
        DefaultValue = 20, MinValue = 3, MaxValue = 50)]
    public int LEDSize { get; set; } = 20;

    [ExtensionParameter("LED Spacing", "Space between LEDs",
        DefaultValue = 5, MinValue = 1, MaxValue = 20)]
    public int LEDSpacing { get; set; } = 5;

    [ExtensionParameter("LED On Color", "Color for active LEDs",
        DefaultValue = "#00FF00")]
    public SKColor LEDOnColor { get; set; } = SKColors.LimeGreen;

    [ExtensionParameter("LED Off Color", "Color for inactive LEDs",
        DefaultValue = "#003300")]
    public SKColor LEDOffColor { get; set; } = new(0, 51, 0);

    [ExtensionParameter("Background Color", "Background color for the clock",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Show Labels", "Show hour/minute/second labels",
        DefaultValue = true)]
    public bool ShowLabels { get; set; } = true;

    [ExtensionParameter("Show Time Text", "Show digital time below binary",
        DefaultValue = true)]
    public bool ShowTimeText { get; set; } = true;

    [ExtensionParameter("Show Colons", "Show colon separators",
        DefaultValue = true)]
    public bool ShowColons { get; set; } = true;

    [ExtensionParameter("LED Style", "LED appearance (0=Circle, 1=Square, 2=Diamond)",
        DefaultValue = 0, MinValue = 0, MaxValue = 2)]
    public int LEDStyle { get; set; } = 0;

    [ExtensionParameter("Glow Effect", "Add glow effect to active LEDs",
        DefaultValue = true)]
    public bool GlowEffect { get; set; } = true;

    [ExtensionParameter("24 Hour Format", "Use 24-hour time format",
        DefaultValue = true)]
    public bool TwentyFourHour { get; set; } = true;

    [ExtensionParameter("Show Seconds", "Display seconds column",
        DefaultValue = true)]
    public bool ShowSeconds { get; set; } = true;

    #endregion
}

public enum LEDStyleType
{
    Circle = 0,
    Square = 1,
    Diamond = 2
}