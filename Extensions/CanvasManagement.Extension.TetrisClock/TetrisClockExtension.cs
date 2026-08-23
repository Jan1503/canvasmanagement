using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.TetrisClock;

/// <summary>
///     Animated falling Tetris blocks that form numbers for clock displays and counters
///     Optimized for Raspberry Pi performance
/// </summary>
[ExtensionInfo("Tetris Clock",
    "Animated falling Tetris blocks that form numbers - perfect for a retro-style clock",
    "Clocks",
    IconResourceName = "tetris.svg")]
public class TetrisClockExtension : IDisposable
{
    private const int TETRIS_Y_DROP_DEFAULT = 16;

    private static readonly SKColor TetrisRed = SKColors.Red;
    private static readonly SKColor TetrisGreen = SKColors.Green;
    private static readonly SKColor TetrisBlue = SKColors.Blue;
    private static readonly SKColor TetrisWhite = SKColors.White;
    private static readonly SKColor TetrisYellow = SKColors.Yellow;
    private static readonly SKColor TetrisCyan = SKColors.Cyan;
    private static readonly SKColor TetrisMagenta = SKColors.Magenta;
    private static readonly SKColor TetrisOrange = SKColors.Orange;
    private static readonly SKColor TetrisBlack = SKColors.Black;

    private static readonly SKColor[] TetrisColors =
    {
        TetrisRed, TetrisGreen, TetrisBlue, TetrisWhite, TetrisYellow, TetrisCyan, TetrisMagenta, TetrisOrange,
        TetrisBlack
    };

    private readonly ICanvas _canvas;

    private readonly Dictionary<int, int> _currentNumbers = new();

    private readonly bool _drawOutline = false;
    private readonly SKColor _outlineColor = SKColors.Lime;
    private readonly Random _random = new();

    // Pre-generated random color palette (eliminates SKColor allocations)
    private readonly SKColor[] _randomColorPalette = new SKColor[256];
    private int _animationDelay = 10;
    private Task? _animationTask;
    private SKBitmap? _backBuffer;
    private SKColor _backgroundColor = SKColors.Black;

    // Cached offset calculations
    private int _cachedOffset1, _cachedOffset2, _cachedOffset3;

    private CancellationTokenSource? _cancellationTokenSource;
    private int _clockX = 32;
    private int _clockY = 120;
    private SKColor _colonColor = TetrisCyan;
    private int _digitSpacing = 45;
    private int _lastCachedScale = -1;
    private bool _randomColors = true;

    private int _scale = 5;
    private bool _showColon = true;

    internal TetrisClockExtension(ICanvas canvas)
    {
        _canvas = canvas;

        // Pre-generate random color palette
        for (var i = 0; i < _randomColorPalette.Length; i++)
            _randomColorPalette[i] = new SKColor(
                (byte)_random.Next(256),
                (byte)_random.Next(256),
                (byte)_random.Next(256));

        // Auto-fit the clock layout to the actual panel size. The defaults above
        // (scale 5, spacing 45, position 32/120) were authored for a 384x192 panel;
        // scale them proportionally so the clock fits any resolution at native size.
        var s = DisplayScale.GetScale(canvas.Width, canvas.Height);
        _scale = Math.Clamp((int)Math.Round(5 * s), 1, 10);
        _digitSpacing = Math.Max(5, (int)Math.Round(45 * s));
        _clockX = Math.Max(0, (int)Math.Round(32 * s));
        _clockY = Math.Max(0, (int)Math.Round(120 * s));
    }

    /// <summary>
    ///     Block scale/size (pixels per block unit)
    /// </summary>
    [ExtensionParameter("Block Scale", "Size of each Tetris block in pixels",
        MinValue = 1, MaxValue = 10, DefaultValue = 5)]
    public int BlockScale
    {
        get => _scale;
        set
        {
            var newValue = Math.Clamp(value, 1, 10);
            if (_scale != newValue)
            {
                _scale = newValue;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Spacing between digits
    /// </summary>
    [ExtensionParameter("Digit Spacing", "Horizontal spacing between digits",
        MinValue = 5, MaxValue = 100, DefaultValue = 45)]
    public int DigitSpacing
    {
        get => _digitSpacing;
        set
        {
            if (_digitSpacing != value)
            {
                _digitSpacing = value;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Animation delay (ms per frame)
    /// </summary>
    [ExtensionParameter("Animation Speed", "Delay between animation frames in milliseconds",
        MinValue = 1, MaxValue = 100, DefaultValue = 10)]
    public int AnimationDelay
    {
        get => _animationDelay;
        set
        {
            if (_animationDelay != value)
            {
                _animationDelay = value;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Use random colors for blocks
    /// </summary>
    [ExtensionParameter("Random Colors", "Use random colors instead of classic Tetris colors", DefaultValue = true)]
    public bool RandomColors
    {
        get => _randomColors;
        set
        {
            if (_randomColors != value)
            {
                _randomColors = value;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Show colon separator in clock mode
    /// </summary>
    [ExtensionParameter("Show Colon", "Display colon separator between hours/minutes/seconds", DefaultValue = true)]
    public bool ShowColon
    {
        get => _showColon;
        set
        {
            if (_showColon != value)
            {
                _showColon = value;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Colon color
    /// </summary>
    [ExtensionParameter("Colon Color", "Color of the colon separator", DefaultValue = "#00FFFF")]
    public SKColor ColonColor
    {
        get => _colonColor;
        set
        {
            if (_colonColor != value)
            {
                _colonColor = value;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Clock X position
    /// </summary>
    [ExtensionParameter("Clock X Position", "Horizontal position of the clock",
        MinValue = 0, MaxValue = 1000, DefaultValue = 32)]
    public int ClockX
    {
        get => _clockX;
        set
        {
            if (_clockX != value)
            {
                _clockX = value;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Clock Y position
    /// </summary>
    [ExtensionParameter("Clock Y Position", "Vertical position of the clock",
        MinValue = 0, MaxValue = 1000, DefaultValue = 120)]
    public int ClockY
    {
        get => _clockY;
        set
        {
            if (_clockY != value)
            {
                _clockY = value;
                Refresh();
            }
        }
    }

    /// <summary>
    ///     Gets whether the clock animation is currently running
    /// </summary>
    public bool IsRunning { get; private set; }

    public string Name => "Tetris Clock";

    [ExtensionParameter("Background Color", "Background color for the clock",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }
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
            try
            {
                var lastTime = "";

                // Initial clear with background
                if (_backgroundColor.Alpha > 0)
                    _canvas.Clear(_backgroundColor);
                else
                    _canvas.Clear(SKColors.Transparent);

                while (!ct.IsCancellationRequested)
                {
                    // Only update when time actually changes (optimization)
                    var currentTime = DateTime.Now.ToString("HHmmss");

                    if (currentTime != lastTime)
                    {
                        SetNumbers(null, DigitSpacing, ClockX, ClockY,
                            BlockScale,
                            AnimationDelay,
                            RandomColors,
                            false,
                            true,
                            ct);

                        lastTime = currentTime;
                    }

                    // Check every 100ms for responsive updates
                    await Task.Delay(100, ct);
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
            Console.WriteLine($"Error stopping extension: {ex.Message}");
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

    /// <summary>
    ///     Clear all cached number states to force refresh
    /// </summary>
    public void Refresh()
    {
        _currentNumbers.Clear();
        //_canvas.Clear();
        Stop();
        Start();
    }

    /// <summary>
    ///     Draw numbers with Tetris animation
    /// </summary>
    /// <param name="value">Number to display (null for clock mode)</param>
    /// <param name="spacing">Spacing between digits</param>
    /// <param name="x">X position</param>
    /// <param name="y">Y position</param>
    /// <param name="scale">Block scale</param>
    /// <param name="delay">Animation delay (0 for random)</param>
    /// <param name="randomColors">Use random colors</param>
    /// <param name="forceRefresh">Force redraw all digits</param>
    /// <param name="clockMode">Display current time in HH:MM:SS format</param>
    public void SetNumbers(int? value, int spacing, int x, int y, int scale = 5, int delay = 0,
        bool randomColors = true, bool forceRefresh = false, bool clockMode = false, CancellationToken ct = default)
    {
        if (!clockMode && value is null)
            throw new ArgumentNullException(nameof(value), "You need to provide a value if 'clockMode' is false.");

        // Avoid string allocations for clock mode
        Span<char> valueChars = stackalloc char[10];
        int valueLength;

        if (clockMode)
        {
            var now = DateTime.Now;
            valueChars[0] = (char)('0' + now.Hour / 10);
            valueChars[1] = (char)('0' + now.Hour % 10);
            valueChars[2] = (char)('0' + now.Minute / 10);
            valueChars[3] = (char)('0' + now.Minute % 10);
            valueChars[4] = (char)('0' + now.Second / 10);
            valueChars[5] = (char)('0' + now.Second % 10);
            valueLength = 6;
        }
        else
        {
            var valueStr = value!.Value.ToString();
            valueStr.AsSpan().CopyTo(valueChars);
            valueLength = valueStr.Length;
        }

        var xOffset = 0;
        _scale = scale;

        for (var i = 0; i < valueLength; i++)
        {
            var number = valueChars[i] - '0';
            var fallingDelay = delay == 0 ? _random.Next(1, 50) : delay;

            if (clockMode && ShowColon && i is 2 or 4)
            {
                DrawColon(x + xOffset, y, ColonColor);
                xOffset += spacing / 2;
            }

            if (forceRefresh || !_currentNumbers.ContainsKey(i) || _currentNumbers[i] != number)
            {
                if (_currentNumbers.ContainsKey(i)) _currentNumbers.Remove(i);

                _currentNumbers.Add(i, number);
                _ = DrawNumberAsync(number, x + xOffset, y, clockMode && i is 5 ? 1 : fallingDelay, randomColors, ct);
            }

            xOffset += spacing;
        }
    }

    /// <summary>
    ///     Set numbers without animation (backward compatibility)
    /// </summary>
    [Obsolete("Use SetNumbers with all parameters for better control")]
    public void SetNumbers(int value, bool forceRefresh)
    {
        SetNumbers(value, DigitSpacing, 0, 0, BlockScale, AnimationDelay, RandomColors, forceRefresh);
    }

    /// <summary>
    ///     Draw numbers async (backward compatibility)
    /// </summary>
    [Obsolete("Use DrawNumberAsync instead")]
    public async Task<bool> DrawNumbersAsync(int x, int yFinish, bool displayColon)
    {
        // Simple sync version for backward compatibility
        return true;
    }

    /// <summary>
    ///     Draw a single animated number
    /// </summary>
    public async Task DrawNumberAsync(int number, int x, int yFinish, int delay, bool randomColor = true,
        CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return;

        var scaledYOffset = _scale > 1 ? _scale : 1;
        var y = yFinish - TETRIS_Y_DROP_DEFAULT * _scale;
        var transparent = _backgroundColor.Alpha == 0;

        if (number is >= 0 and < 10)
        {
            // Clear the digit area (to transparent when the background is transparent, so layers show through).
            if (transparent)
                _canvas.ClearRect(x, y, 6 * _scale, TETRIS_Y_DROP_DEFAULT * _scale);
            else
                _canvas.DrawRect(x, y, 6 * _scale, TETRIS_Y_DROP_DEFAULT * _scale, _backgroundColor, SKPaintStyle.Fill);

            for (var blockIndex = 0; blockIndex < TetrisNumber.BlocksPerNumber[number]; blockIndex++)
            {
                var currentState = TetrisNumber.GetAnimationFragment(number, blockIndex);

                var blockColor = randomColor
                    ? _randomColorPalette[_random.Next(256)]
                    : TetrisColors[currentState.Color];

                int prevX = -1, prevY = -1, prevRot = -1;

                for (var fallIndex = 0; fallIndex < currentState.YStop; fallIndex++)
                {
                    if (ct.IsCancellationRequested) return;
                    var rotations = currentState.NumRot;
                    if (rotations == 1)
                        if (fallIndex < currentState.YStop / 2)
                            rotations = 0;

                    if (rotations == 2)
                    {
                        if (fallIndex < currentState.YStop / 3) rotations = 0;
                        if (fallIndex < currentState.YStop / 3 * 2) rotations = 1;
                    }

                    if (rotations == 3)
                    {
                        if (fallIndex < currentState.YStop / 4) rotations = 0;
                        if (fallIndex < currentState.YStop / 4 * 2) rotations = 1;
                        if (fallIndex < currentState.YStop / 4 * 3) rotations = 2;
                    }

                    if (prevX >= 0 && fallIndex != 0)
                        // Erase the previous block precisely: transparent (alpha 0) erases each cell to
                        // transparent (see the cell-level DrawShape); otherwise paint it black.
                        DrawShape(_scale, currentState.BlockType,
                            transparent ? SKColors.Transparent : SKColors.Black, prevX, prevY, prevRot);

                    var currentX = x + currentState.XPos * _scale;
                    var currentY = y + fallIndex * scaledYOffset - scaledYOffset;

                    DrawShape(_scale, currentState.BlockType, blockColor,
                        currentX, currentY, rotations);

                    prevX = currentX;
                    prevY = currentY;
                    prevRot = rotations;

                    if (delay != -1)
                        await Task.Delay(delay);
                }
            }
        }
    }

    private void DrawColon(int x, int y, SKColor colonColor)
    {
        var colonSize = 2 * _scale;
        _canvas.DrawRect(x, y - 9 * _scale, colonSize, colonSize, colonColor, SKPaintStyle.Fill);
        _canvas.DrawRect(x, y - 6 * _scale, colonSize, colonSize, colonColor, SKPaintStyle.Fill);
    }

    private void DrawShape(int xPos, int yPos, int scale, SKColor color)
    {
        // A transparent (alpha 0) colour means "erase this cell" — clear it to transparent so a transparent
        // background reveals the layer beneath (a plain fill with a transparent colour would be a no-op).
        if (color.Alpha == 0)
        {
            _canvas.ClearRect(xPos, yPos, scale, scale);
            return;
        }

        _canvas.DrawRect(xPos, yPos, scale, scale, color, SKPaintStyle.Fill);
        if (_drawOutline) _canvas.DrawRect(xPos, yPos, scale, scale, _outlineColor, SKPaintStyle.Stroke);
    }

    public void DrawShape(int scale, int blockType, SKColor color, int xPos, int yPos, int numberOfRotations)
    {
        // Cache offset calculations per scale value
        if (scale != _lastCachedScale)
        {
            _cachedOffset1 = scale;
            _cachedOffset2 = 2 * scale;
            _cachedOffset3 = 3 * scale;
            _lastCachedScale = scale;
        }

        var offset1 = _cachedOffset1;
        var offset2 = _cachedOffset2;
        var offset3 = _cachedOffset3;

        switch (blockType)
        {
            case 0: // Square
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                return;
            case 1 when numberOfRotations == 0: // L-Shape
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos, yPos - offset2, scale, color);
                return;
            case 1 when numberOfRotations == 1:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset2, yPos - offset1, scale, color);
                return;
            case 1 when numberOfRotations == 2:
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset2, scale, color);
                DrawShape(xPos, yPos - offset2, scale, color);
                return;
            case 1 when numberOfRotations == 3:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset2, yPos, scale, color);
                DrawShape(xPos + offset2, yPos - offset1, scale, color);
                return;
            case 2 when numberOfRotations == 0: // L-Shape (reverse)
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset2, scale, color);
                return;
            case 2 when numberOfRotations == 1:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset2, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                return;
            case 2 when numberOfRotations == 2:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos, yPos - offset2, scale, color);
                DrawShape(xPos + offset1, yPos - offset2, scale, color);
                return;
            case 2 when numberOfRotations == 3:
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset2, yPos - offset1, scale, color);
                DrawShape(xPos + offset2, yPos, scale, color);
                return;
            case 3 when numberOfRotations is 0 or 2: // I-Shape horizontal
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset2, yPos, scale, color);
                DrawShape(xPos + offset3, yPos, scale, color);
                return;
            case 3: // I-Shape vertical
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos, yPos - offset2, scale, color);
                DrawShape(xPos, yPos - offset3, scale, color);
                return;
            case 4 when numberOfRotations is 0 or 2: // S-Shape
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos, yPos - offset2, scale, color);
                return;
            case 4:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset2, yPos - offset1, scale, color);
                return;
            case 5 when numberOfRotations is 0 or 2: // S-Shape (reversed)
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset2, scale, color);
                return;
            case 5:
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset2, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                return;
            case 6 when numberOfRotations == 0: // Half cross
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset2, yPos, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                return;
            case 6 when numberOfRotations == 1:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos, yPos - offset2, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                return;
            case 6 when numberOfRotations == 2:
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset2, yPos - offset1, scale, color);
                return;
            case 6:
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset2, scale, color);
                break;
            case 7 when numberOfRotations == 0: // Corner-Shape
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                return;
            case 7 when numberOfRotations == 1:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                return;
            case 7 when numberOfRotations == 2:
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                DrawShape(xPos, yPos - offset1, scale, color);
                return;
            case 7:
                DrawShape(xPos, yPos, scale, color);
                DrawShape(xPos + offset1, yPos, scale, color);
                DrawShape(xPos + offset1, yPos - offset1, scale, color);
                break;
        }
    }
}