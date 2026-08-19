using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.WordClock;

[ExtensionInfo("Word Clock (German)",
    "Display time in German words - \"ES IST ZEHN UHR\"",
    "Clocks",
    IconResourceName = "wordclock.svg")]
public class WordClockExtension : IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _renderLock = new();

    // German word clock matrix (11x10)
    private readonly string[] _wordGrid = new[]
    {
        // \u00DC = U-umlaut, \u00D6 = O-umlaut (escapes used so the file stays pure ASCII and can't be
        // mangled by an encoding round-trip again - that corruption is what produced the tofu glyphs).
        "ESKISTAF\u00DCNF", // FUENF
        "ZEHNZWANZIG",
        "DREIVIERTEL",
        "TGNACHVORJM",
        "HALBQZW\u00D6LFP", // ZWOELF
        "ZWEINSIEBEN",
        "KDREIRHF\u00DCNF", // FUENF
        "ELFNEUNVIER",
        "WACHTZEHNRS",
        "BSECHSFMUHR"
    };

    // Double buffering to prevent flicker
    private SKBitmap? _backBuffer;
    private bool _disposed;
    private Timer? _updateTimer;

    internal WordClockExtension(ICanvas canvas)
    {
        _canvas = canvas;
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

        // Create back buffer
        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

        _updateTimer = new Timer(1000); // Update every second
        _updateTimer.Elapsed += OnUpdate;
        _updateTimer.AutoReset = true;
        _updateTimer.Start();

        IsRunning = true;
        Console.WriteLine("Word Clock started");
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
        Console.WriteLine("Word Clock stopped");
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
            Console.WriteLine($"Word Clock update error: {ex.Message}");
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
        lock (_renderLock)
        {
            try
            {
                if (_backBuffer == null) return;

                // Render to back buffer
                using var canvas = new SKCanvas(_backBuffer);

                // Clear with background color (supports transparency)
                if (BackgroundColor.Alpha == 0)
                {
                    canvas.Clear(SKColors.Transparent);
                }
                else if (BackgroundColor.Alpha == 255)
                {
                    canvas.Clear(BackgroundColor);
                }
                else
                {
                    canvas.Clear(SKColors.Transparent);
                    using var bgPaint = new SKPaint { Color = BackgroundColor, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, bgPaint);
                }

                var now = DateTime.Now;
                var activeWords = GetActiveWords(now);

                // Determine layout
                int cols, rows;
                var isLandscape = DetermineLayout(out cols, out rows);

                // Calculate optimal letter size if auto
                var actualLetterSize = LetterSize;
                var spacing = LetterSpacing;
                if (actualLetterSize == 0)
                {
                    // Auto-fit to canvas. Padding and inter-letter spacing shrink on small panels
                    // so the entire word grid fits at native resolution.
                    var pad = Math.Min(_canvas.Width, _canvas.Height) / 10;
                    spacing = Math.Max(0, Math.Min(LetterSpacing,
                        Math.Min(_canvas.Width / cols, _canvas.Height / rows) / 4));

                    var maxWidth = _canvas.Width - pad;
                    var maxHeight = _canvas.Height - pad;

                    var sizeByWidth = maxWidth / cols - spacing;
                    var sizeByHeight = maxHeight / rows - spacing;

                    actualLetterSize = Math.Min(sizeByWidth, sizeByHeight);
                    actualLetterSize = Math.Max(3, Math.Min(100, actualLetterSize)); // Clamp 3-100
                }

                // Calculate grid dimensions
                var cellSize = actualLetterSize + spacing;
                var gridWidth = cols * cellSize - spacing;
                var gridHeight = rows * cellSize - spacing;
                var startX = (_canvas.Width - gridWidth) / 2;
                var startY = (_canvas.Height - gridHeight) / 2;

                // Draw grid background
                if (ShowGrid)
                {
                    using var gridPaint = new SKPaint
                    {
                        Color = new SKColor(20, 20, 20),
                        Style = SKPaintStyle.Fill
                    };
                    var inset = _canvas.ScaleSize(10);
                    canvas.DrawRect(startX - inset, startY - inset, gridWidth + inset * 2, gridHeight + inset * 2,
                        gridPaint);
                }

                // Draw letters
                using var font = new SKFont
                {
                    Size = actualLetterSize,
                    Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
                };
                // Anti-aliasing makes tiny LED letters mushy/invisible; only enable it once
                // letters are reasonably large.
                var crisp = actualLetterSize < 12;
                using var textPaint = new SKPaint
                {
                    IsAntialias = !crisp
                };

                for (var row = 0; row < rows; row++)
                for (var col = 0; col < cols; col++)
                {
                    var letter = GetLetterAt(row, col, isLandscape);
                    var isActive = IsLetterActive(row, col, activeWords, isLandscape);

                    var x = startX + col * cellSize + actualLetterSize / 2;
                    var y = startY + row * cellSize + actualLetterSize;

                    // Draw glow effect for active letters (blur scaled to letter size so it does
                    // not smear small letters into invisibility on low-res panels).
                    if (isActive && GlowEffect && actualLetterSize >= 8)
                    {
                        using var glowPaint = new SKPaint
                        {
                            Color = new SKColor(
                                ActiveColor.Red,
                                ActiveColor.Green,
                                ActiveColor.Blue,
                                80),
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1f, actualLetterSize / 4f))
                        };
                        canvas.DrawText(letter.ToString(), x, y, SKTextAlign.Center, font, glowPaint);
                    }

                    // Draw letter
                    textPaint.Color = isActive ? ActiveColor : InactiveColor;
                    canvas.DrawText(letter.ToString(), x, y, SKTextAlign.Center, font, textPaint);
                }

                canvas.Flush();// Atomic submission
                _canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render error: {ex.Message}");
            }
        }
    }

    private bool DetermineLayout(out int cols, out int rows)
    {
        var isLandscape = false;

        switch (LayoutMode)
        {
            case 0: // Auto
                isLandscape = _canvas.Width > _canvas.Height;
                break;
            case 1: // Portrait
                isLandscape = false;
                break;
            case 2: // Landscape
                isLandscape = true;
                break;
        }

        if (isLandscape)
        {
            cols = 11;
            rows = 10;
        }
        else
        {
            cols = 11;
            rows = 10;
        }

        return isLandscape;
    }

    private char GetLetterAt(int row, int col, bool isLandscape)
    {
        // For now, both layouts use the same grid
        // In landscape mode, we could rotate or use different grid in future
        return _wordGrid[row][col];
    }

    private List<WordPosition> GetActiveWords(DateTime time)
    {
        var words = new List<WordPosition>();

        // Always add "ES IST"
        words.Add(new WordPosition(0, 0, 2)); // ES
        words.Add(new WordPosition(0, 3, 3)); // IST

        var hour = time.Hour % 12;
        if (hour == 0) hour = 12;
        var minute = time.Minute;

        // Determine minute words
        if (minute >= 5 && minute < 10)
        {
            words.Add(new WordPosition(0, 7, 4)); // F?NF
            words.Add(new WordPosition(3, 2, 4)); // NACH
        }
        else if (minute >= 10 && minute < 15)
        {
            words.Add(new WordPosition(1, 0, 4)); // ZEHN
            words.Add(new WordPosition(3, 2, 4)); // NACH
        }
        else if (minute >= 15 && minute < 20)
        {
            words.Add(new WordPosition(2, 4, 7)); // VIERTEL
            words.Add(new WordPosition(3, 2, 4)); // NACH
        }
        else if (minute >= 20 && minute < 25)
        {
            words.Add(new WordPosition(1, 4, 7)); // ZWANZIG
            words.Add(new WordPosition(3, 2, 4)); // NACH
        }
        else if (minute >= 25 && minute < 30)
        {
            words.Add(new WordPosition(0, 7, 4)); // F?NF
            words.Add(new WordPosition(3, 6, 3)); // VOR
            words.Add(new WordPosition(4, 0, 4)); // HALB
            hour++; // Next hour for "vor halb"
        }
        else if (minute >= 30 && minute < 35)
        {
            words.Add(new WordPosition(4, 0, 4)); // HALB
            hour++; // Next hour
        }
        else if (minute >= 35 && minute < 40)
        {
            words.Add(new WordPosition(0, 7, 4)); // F?NF
            words.Add(new WordPosition(3, 2, 4)); // NACH
            words.Add(new WordPosition(4, 0, 4)); // HALB
            hour++; // Next hour
        }
        else if (minute >= 40 && minute < 45)
        {
            words.Add(new WordPosition(1, 4, 7)); // ZWANZIG
            words.Add(new WordPosition(3, 6, 3)); // VOR
            hour++; // Next hour
        }
        else if (minute >= 45 && minute < 50)
        {
            words.Add(new WordPosition(2, 4, 7)); // VIERTEL
            words.Add(new WordPosition(3, 6, 3)); // VOR
            hour++; // Next hour
        }
        else if (minute >= 50 && minute < 55)
        {
            words.Add(new WordPosition(1, 0, 4)); // ZEHN
            words.Add(new WordPosition(3, 6, 3)); // VOR
            hour++; // Next hour
        }
        else if (minute >= 55)
        {
            words.Add(new WordPosition(0, 7, 4)); // F?NF
            words.Add(new WordPosition(3, 6, 3)); // VOR
            hour++; // Next hour
        }

        // Wrap hour
        if (hour > 12) hour = 1;

        // Add hour word
        switch (hour)
        {
            case 1:
                if (minute >= 0 && minute < 5)
                    words.Add(new WordPosition(5, 2, 3)); // EIN (special case)
                else
                    words.Add(new WordPosition(5, 2, 4)); // EINS
                break;
            case 2:
                words.Add(new WordPosition(5, 0, 4)); // ZWEI
                break;
            case 3:
                words.Add(new WordPosition(6, 1, 4)); // DREI
                break;
            case 4:
                words.Add(new WordPosition(7, 7, 4)); // VIER
                break;
            case 5:
                words.Add(new WordPosition(6, 7, 4)); // F?NF
                break;
            case 6:
                words.Add(new WordPosition(9, 1, 5)); // SECHS
                break;
            case 7:
                words.Add(new WordPosition(5, 5, 6)); // SIEBEN
                break;
            case 8:
                words.Add(new WordPosition(8, 1, 4)); // ACHT
                break;
            case 9:
                words.Add(new WordPosition(7, 3, 4)); // NEUN
                break;
            case 10:
                words.Add(new WordPosition(8, 5, 4)); // ZEHN
                break;
            case 11:
                words.Add(new WordPosition(7, 0, 3)); // ELF
                break;
            case 12:
                words.Add(new WordPosition(4, 5, 5)); // ZW?LF
                break;
        }

        // Add "UHR"
        if (minute >= 0 && minute < 5)
            words.Add(new WordPosition(9, 8, 3)); // UHR

        return words;
    }

    private bool IsLetterActive(int row, int col, List<WordPosition> activeWords, bool isLandscape = false)
    {
        foreach (var word in activeWords)
            if (word.Row == row && col >= word.Col && col < word.Col + word.Length)
                return true;

        return false;
    }

    #region Parameters

    [ExtensionParameter("Active Color", "Color for active words",
        DefaultValue = "#00FF00")]
    public SKColor ActiveColor { get; set; } = SKColors.LimeGreen;

    [ExtensionParameter("Inactive Color", "Color for inactive letters",
        DefaultValue = "#1a1a1a")]
    public SKColor InactiveColor { get; set; } = new(26, 26, 26);

    [ExtensionParameter("Background Color", "Background color for the clock",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Letter Size", "Size of each letter",
        DefaultValue = 0, MinValue = 0, MaxValue = 100)]
    public int LetterSize { get; set; } = 0; // 0 = auto-fit

    [ExtensionParameter("Letter Spacing", "Space between letters",
        DefaultValue = 5, MinValue = 0, MaxValue = 20)]
    public int LetterSpacing { get; set; } = 5;

    [ExtensionParameter("Glow Effect", "Add glow to active words",
        DefaultValue = true)]
    public bool GlowEffect { get; set; } = true;

    [ExtensionParameter("Show Grid", "Show letter grid background",
        DefaultValue = false)]
    public bool ShowGrid { get; set; } = false;

    [ExtensionParameter("Layout Mode", "Grid layout (0=Auto, 1=Portrait, 2=Landscape)",
        DefaultValue = 0, MinValue = 0, MaxValue = 2)]
    public int LayoutMode { get; set; } = 0;

    #endregion
}

public record WordPosition(int Row, int Col, int Length);