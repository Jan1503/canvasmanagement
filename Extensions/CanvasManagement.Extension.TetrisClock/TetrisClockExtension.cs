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

    private readonly DigitSlot[] _slots = [new(), new(), new(), new(), new(), new()];
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
    private SKPaint? _fillPaint;
    private SKCanvas? _draw;

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
    [ExtensionParameter("Animation Speed", "Delay between animation frames in milliseconds (hours/minutes)",
        MinValue = 1, MaxValue = 100, DefaultValue = 10)]
    public int AnimationDelay
    {
        get => _animationDelay;
        set => _animationDelay = Math.Clamp(value, 1, 100);
    }

    [ExtensionParameter("Seconds Fall Delay",
        "Delay per frame for the seconds digit in milliseconds. Lower so the drop finishes before the next second.",
        MinValue = 1, MaxValue = 50, DefaultValue = 1)]
    public int SecondsFallDelay { get; set; } = 1;

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
        foreach (var s in _slots) s.Reset();

        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        _fillPaint?.Dispose();
        _fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        _animationTask = Task.Run(async () =>
        {
            try
            {
                var lastTime = "";
                var lastTick = Environment.TickCount64;
                while (!ct.IsCancellationRequested)
                {
                    var currentTime = DateTime.Now.ToString("HHmmss");
                    if (currentTime != lastTime)
                    {
                        SyncDigits(currentTime);
                        lastTime = currentTime;
                    }

                    var now = Environment.TickCount64;
                    var dt = (int)Math.Clamp(now - lastTick, 1, 50);
                    lastTick = now;
                    StepDigits(dt);
                    RenderFrame();
                    await Task.Delay(16, ct);
                }
            }
            catch (OperationCanceledException)
            {
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
            _fillPaint?.Dispose();
            _fillPaint = null;
            _canvas.Clear();
            IsRunning = false;
        }
    }

    public void Refresh()
    {
        foreach (var s in _slots) s.Reset();
        Stop();
        Start();
    }

    private void SyncDigits(string hhmmss)
    {
        for (var i = 0; i < 6; i++)
        {
            var n = hhmmss[i] - '0';
            var delay = i == 5 ? SecondsFallDelay : AnimationDelay;
            if (_slots[i].Value == n && !_slots[i].Animating) continue;
            if (_slots[i].Value == n && _slots[i].Animating) continue;

            _slots[i].Begin(n, Math.Max(1, delay), PickColor(n, 0));
        }
    }

    private SKColor PickColor(int number, int blockIndex)
    {
        if (RandomColors) return _randomColorPalette[_random.Next(256)];
        var frag = TetrisNumber.GetAnimationFragment(number, blockIndex);
        return TetrisColors[Math.Clamp(frag.Color, 0, TetrisColors.Length - 1)];
    }

    private void StepDigits(int dtMs)
    {
        foreach (var slot in _slots)
        {
            if (!slot.Animating) continue;
            slot.AccruedMs += dtMs;
            var delay = Math.Max(1, slot.StepDelayMs);
            while (slot.Animating && slot.AccruedMs >= delay)
            {
                slot.AccruedMs -= delay;
                AdvanceSlot(slot);
            }
        }
    }

    private void RenderFrame()
    {
        var bb = _backBuffer;
        if (bb == null || _fillPaint == null) return;

        using var canvas = new SKCanvas(bb);
        if (_backgroundColor.Alpha > 0)
            canvas.Clear(_backgroundColor);
        else
            canvas.Clear(SKColors.Transparent);

        var x = ClockX;
        var y = ClockY;
        var spacing = DigitSpacing;
        for (var i = 0; i < 6; i++)
        {
            if (ShowColon && i is 2 or 4)
            {
                DrawColon(canvas, x, y, ColonColor);
                x += spacing / 2;
            }

            DrawDigit(canvas, _slots[i], x, y);
            x += spacing;
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawDigit(SKCanvas canvas, DigitSlot slot, int x, int yFinish)
    {
        if (slot.Value < 0) return;
        var dropTop = yFinish - TETRIS_Y_DROP_DEFAULT * _scale;
        foreach (var b in slot.Landed)
            DrawShape(canvas, _scale, b.BlockType, b.Color,
                x + b.XPos * _scale,
                RestY(dropTop, b.YStop),
                RestRot(b.NumRot, b.YStop - 1, b.YStop));

        if (!slot.Animating) return;
        var frag = TetrisNumber.GetAnimationFragment(slot.Value, slot.BlockIndex);
        var fallIndex = Math.Max(0, slot.FallIndex);
        var rot = RestRot(frag.NumRot, fallIndex, frag.YStop);
        DrawShape(canvas, _scale, frag.BlockType, slot.FallingColor,
            x + frag.XPos * _scale,
            dropTop + fallIndex * _scale - _scale,
            rot);
    }

    private int RestY(int dropTop, int yStop)
    {
        var fallIndex = Math.Max(0, yStop - 1);
        return dropTop + fallIndex * _scale - _scale;
    }

    private static int RestRot(int numRot, int fallIndex, int yStop)
    {
        var rotations = numRot;
        if (rotations == 1 && fallIndex < yStop / 2) rotations = 0;
        if (rotations == 2)
        {
            if (fallIndex < yStop / 3) rotations = 0;
            else if (fallIndex < yStop / 3 * 2) rotations = 1;
        }

        if (numRot == 3)
        {
            rotations = numRot;
            if (fallIndex < yStop / 4) rotations = 0;
            else if (fallIndex < yStop / 4 * 2) rotations = 1;
            else if (fallIndex < yStop / 4 * 3) rotations = 2;
        }

        return rotations;
    }

    private void AdvanceSlot(DigitSlot slot)
    {
        var frag = TetrisNumber.GetAnimationFragment(slot.Value, slot.BlockIndex);
        if (slot.FallIndex + 1 >= frag.YStop)
        {
            slot.Landed.Add(new LandedBlock(frag.BlockType, frag.XPos, frag.YStop, frag.NumRot, slot.FallingColor));
            slot.BlockIndex++;
            slot.FallIndex = 0;
            if (slot.BlockIndex >= TetrisNumber.BlocksPerNumber[slot.Value])
            {
                slot.Animating = false;
                return;
            }

            slot.FallingColor = PickColor(slot.Value, slot.BlockIndex);
            return;
        }

        slot.FallIndex++;
    }

    private void DrawColon(SKCanvas canvas, int x, int y, SKColor colonColor)
    {
        if (_fillPaint == null) return;
        _fillPaint.Color = colonColor;
        var colonSize = 2 * _scale;
        canvas.DrawRect(x, y - 9 * _scale, colonSize, colonSize, _fillPaint);
        canvas.DrawRect(x, y - 6 * _scale, colonSize, colonSize, _fillPaint);
    }

    private void DrawShape(SKCanvas canvas, int scale, int blockType, SKColor color, int xPos, int yPos,
        int numberOfRotations)
    {
        _draw = canvas;
        DrawShape(scale, blockType, color, xPos, yPos, numberOfRotations);
    }

    private void DrawShape(int xPos, int yPos, int scale, SKColor color)
    {
        if (_draw == null || _fillPaint == null || color.Alpha == 0) return;
        _fillPaint.Color = color;
        _draw.DrawRect(xPos, yPos, scale, scale, _fillPaint);
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

    private sealed class DigitSlot
    {
        public int Value = -1;
        public bool Animating;
        public int BlockIndex;
        public int FallIndex;
        public int AccruedMs;
        public int StepDelayMs = 1;
        public SKColor FallingColor;
        public readonly List<LandedBlock> Landed = new();

        public void Reset()
        {
            Value = -1;
            Animating = false;
            BlockIndex = 0;
            FallIndex = 0;
            AccruedMs = 0;
            Landed.Clear();
        }

        public void Begin(int value, int delayMs, SKColor color)
        {
            Value = value;
            Animating = true;
            BlockIndex = 0;
            FallIndex = 0;
            AccruedMs = 0;
            StepDelayMs = delayMs;
            FallingColor = color;
            Landed.Clear();
        }
    }

    private readonly record struct LandedBlock(int BlockType, int XPos, int YStop, int NumRot, SKColor Color);
}