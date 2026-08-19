using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extensions.Default;

[ExtensionInfo("Matrix Rain",
    "Cascading digital rain effect inspired by The Matrix",
    "Visual Effects",
    IconResourceName = "matrix-rain.svg")]
public class MatrixExtension : IDisposable
{
    // Authentic Matrix characters including mirrored Latin, numbers, and katakana-style glyphs
    private static readonly char[] MatrixChars =
    {
        // Numbers
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        // Latin letters
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        // Katakana (authentic Matrix glyphs)
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?',
        // Special Matrix symbols
        ':', '�', '=', '*', '+', '-', '�', '|', '/', '\\', '[', ']',
        '?', '?', '?', '?', ';', '.', ',', '?', '?'
    };

    private readonly ICanvas _canvas;
    private readonly List<RainColumn> _columns = new();
    private readonly Random _random = new();
    private Task? _animationTask;
    private SKBitmap? _backBuffer;
    private CancellationTokenSource? _cancellationTokenSource;

    internal MatrixExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Animation Speed", "Speed in milliseconds (lower = faster)",
        DefaultValue = 90, MinValue = 20, MaxValue = 250)]
    public int AnimationSpeed { get; set; } = 90;

    [ExtensionParameter("Rain Density", "Density of falling rain (0.0 = sparse, 2.0 = heavy)",
        DefaultValue = 1.0, MinValue = 0.1, MaxValue = 2.0)]
    public double RainDensity { get; set; } = 1.0;

    [ExtensionParameter("Min Column Speed", "Minimum falling speed",
        DefaultValue = 1, MinValue = 1, MaxValue = 5)]
    public int MinSpeed { get; set; } = 1;

    [ExtensionParameter("Max Column Speed", "Maximum falling speed",
        DefaultValue = 8, MinValue = 2, MaxValue = 15)]
    public int MaxSpeed { get; set; } = 8;

    [ExtensionParameter("Min Trail Length", "Minimum character trail length",
        DefaultValue = 6, MinValue = 3, MaxValue = 15)]
    public int MinLength { get; set; } = 6;

    [ExtensionParameter("Max Trail Length", "Maximum character trail length",
        DefaultValue = 35, MinValue = 10, MaxValue = 50)]
    public int MaxLength { get; set; } = 35;

    [ExtensionParameter("Background Color", "Background color for matrix rain",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    public string Name => "Matrix Rain";

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
            const int charWidth = 8;
            const int charHeight = 10;
            var columnCount = _canvas.Width / charWidth;

            // Initialize rain columns with highly varied properties
            var initialColumns = (int)(columnCount * RainDensity);
            _columns.Clear();

            for (var i = 0; i < initialColumns; i++)
            {
                var initialY = _random.Next(-_canvas.Height * 4, -charHeight);

                _columns.Add(new RainColumn
                {
                    X = i % columnCount * charWidth,
                    Y = initialY,
                    Speed = _random.Next(MinSpeed, MaxSpeed),
                    Length = _random.Next(MinLength, MaxLength),
                    CharWidth = charWidth,
                    CharHeight = charHeight,
                    Brightness = (byte)_random.Next(150, 255),
                    GlowEffect = _random.Next(100) < 35,
                    Density = 0.5f + (float)_random.NextDouble() * 0.5f
                });
            }

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Apply sophisticated fade effect
                    ApplyAdvancedFade();

                    // Draw and update columns
                    foreach (var column in _columns)
                    {
                        DrawEnhancedColumn(column);
                        column.Update();

                        // Reset if off-screen
                        if (column.Y > _canvas.Height + column.Length * charHeight)
                        {
                            column.Y = -_random.Next(50, 800);
                            column.Speed = _random.Next(MinSpeed, MaxSpeed);
                            column.Length = _random.Next(MinLength, MaxLength);
                            column.Brightness = (byte)_random.Next(150, 255);
                            column.GlowEffect = _random.Next(100) < 35;
                            column.Density = 0.5f + (float)_random.NextDouble() * 0.5f;
                            column.RegenerateChars();
                        }
                    }

                    // Dynamic column management based on density
                    var targetColumns = Math.Max(1, (int)(columnCount * RainDensity));

                    if (_columns.Count > targetColumns)
                    {
                        var toRemove = _columns.Count - targetColumns;
                        for (var r = 0; r < toRemove && _columns.Count > targetColumns; r++)
                        {
                            var idx = _random.Next(_columns.Count);
                            _columns.RemoveAt(idx);
                        }
                    }
                    else if (_columns.Count < targetColumns)
                    {
                        var toAdd = Math.Min(5, targetColumns - _columns.Count);
                        for (var a = 0; a < toAdd; a++)
                        {
                            var x = _random.Next(columnCount) * charWidth;

                            _columns.Add(new RainColumn
                            {
                                X = x,
                                Y = -_random.Next(50, 800),
                                Speed = _random.Next(MinSpeed, MaxSpeed),
                                Length = _random.Next(MinLength, MaxLength),
                                CharWidth = charWidth,
                                CharHeight = charHeight,
                                Brightness = (byte)_random.Next(150, 255),
                                GlowEffect = _random.Next(100) < 35,
                                Density = 0.5f + (float)_random.NextDouble() * 0.5f
                            });
                        }
                    }

                    await Task.Delay(AnimationSpeed, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            finally
            {
                _canvas.Clear();
                _columns.Clear();
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
            Console.WriteLine($"Error stopping Matrix rain: {ex.Message}");
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

    private void ApplyAdvancedFade()
    {
        var maxY = _canvas.Height;
        var maxX = _canvas.Width;

        // Process every pixel, not every 2nd pixel, to avoid artifacts
        for (var y = 0; y < maxY; y++)
        for (var x = 0; x < maxX; x++)
            try
            {
                var pixel = _canvas.GetPixel(x, y);
                if (pixel.Green > 0)
                {
                    // Consistent fade based on brightness level
                    byte fadeAmount;
                    if (pixel.Green > 180)
                        fadeAmount = (byte)_random.Next(6, 10); // Very bright: slow fade
                    else if (pixel.Green > 100)
                        fadeAmount = (byte)_random.Next(10, 16); // Medium: moderate fade
                    else
                        fadeAmount = (byte)_random.Next(16, 24); // Dim: fast fade

                    if (pixel.Green > fadeAmount)
                    {
                        var newGreen = (byte)(pixel.Green - fadeAmount);
                        var newColor = new SKColor(0, newGreen, (byte)(newGreen * 0.12f));
                        _canvas.SetPixel(x, y, newColor);
                    }
                    else
                    {
                        _canvas.SetPixel(x, y, SKColors.Black);
                    }
                }
            }
            catch
            {
            }
    }

    private void DrawEnhancedColumn(RainColumn column)
    {
        for (var i = 0; i < column.Length; i++)
        {
            var y = column.Y - i * column.CharHeight;

            if (y + 7 < 0 || y >= _canvas.Height) continue;
            if (column.X < 0 || column.X + 5 >= _canvas.Width) continue;

            // Movie-authentic gap pattern: occasional missing characters in trail
            var allowGap = RainDensity < 0.8f && i > 2;
            if (allowGap && _random.NextDouble() > column.Density) continue;

            byte intensity;
            var isHead = i == 0;

            if (isHead)
            {
                // Head changes EVERY frame
                column.ChangeCharAt(i, true);

                // Bright white/cyan head with extra boost
                intensity = (byte)Math.Min(255, column.Brightness + 40);
                var headColor = new SKColor(
                    (byte)(intensity * 0.98), // Almost white
                    intensity,
                    (byte)(intensity * 0.98)
                );
                DrawDetailedChar(column.X, y, column.GetChar(i), headColor, true, true);
            }
            else if (i == 1)
            {
                // Second char: 70% change rate for high dynamism
                if (_random.Next(100) < 70) column.ChangeCharAt(i, true);

                intensity = (byte)(column.Brightness * 0.92f);
                var transColor = new SKColor((byte)(intensity * 0.25), intensity, (byte)(intensity * 0.35));
                DrawDetailedChar(column.X, y, column.GetChar(i), transColor, false, false);
            }
            else if (i < 4)
            {
                // Characters 2-3: 40% change rate
                if (_random.Next(100) < 40) column.ChangeCharAt(i, true);

                var fadeFactor = (float)Math.Pow(1.0f - i / (float)column.Length, 1.3);
                intensity = (byte)(column.Brightness * 0.85f * fadeFactor);
                intensity = (byte)(intensity * (0.75f + _random.NextDouble() * 0.3f));

                var color = new SKColor(0, intensity, (byte)(intensity * 0.2f));
                DrawDetailedChar(column.X, y, column.GetChar(i), color, false, false);
            }
            else
            {
                // Trail: 20% change rate with exponential fade
                if (_random.Next(100) < 20) column.ChangeCharAt(i, true);

                var fadePower = 1.15 + (1.0 - RainDensity) * 0.4;
                var fadeFactor = (float)Math.Pow(1.0f - i / (float)column.Length, fadePower);
                intensity = (byte)(column.Brightness * 0.78f * fadeFactor);

                // Add more randomness to trail brightness for organic look
                intensity = (byte)(intensity * (0.7f + _random.NextDouble() * 0.35f));

                var color = new SKColor(0, intensity, (byte)(intensity * 0.18f));
                DrawDetailedChar(column.X, y, column.GetChar(i), color, false, false);
            }
        }
    }

    private void DrawDetailedChar(int x, int y, char character, SKColor color, bool addGlow, bool isHead)
    {
        var pattern = GetEnhancedCharPattern(character);

        // Random bright flash effect (movie-authentic) - 3% chance, higher than before
        if (_random.Next(100) < 3)
        {
            color = new SKColor(
                (byte)Math.Min(255, color.Red * 1.6f),
                (byte)Math.Min(255, color.Green * 1.4f),
                (byte)Math.Min(255, color.Blue * 1.6f)
            );
            addGlow = true;
        }

        // Rare "glitch" effect - character appears dimmer/brighter (1% chance)
        if (_random.Next(100) < 1)
        {
            var glitchFactor = 0.4f + (float)_random.NextDouble() * 1.2f; // 40%-160%
            color = new SKColor(
                (byte)Math.Min(255, color.Red * glitchFactor),
                (byte)Math.Min(255, color.Green * glitchFactor),
                (byte)Math.Min(255, color.Blue * glitchFactor)
            );
        }

        // Enhanced glow with larger radius for heads
        if (addGlow && color.Green > 200)
        {
            // Outer glow (dimmer, larger radius)
            var outerGlowColor = new SKColor(0, (byte)(color.Green * 0.22), (byte)(color.Blue * 0.32));
            for (var gy = -2; gy <= 2; gy++)
            for (var gx = -2; gx <= 2; gx++)
                if (Math.Abs(gx) + Math.Abs(gy) <= 2)
                {
                    var pixelX = x + gx + 2;
                    var pixelY = y + gy + 3;

                    if (pixelX >= 0 && pixelX < _canvas.Width &&
                        pixelY >= 0 && pixelY < _canvas.Height)
                        _canvas.SetPixel(pixelX, pixelY, outerGlowColor);
                }

            // Inner glow (brighter)
            var innerGlowColor = new SKColor(0, (byte)(color.Green * 0.55), (byte)(color.Blue * 0.65));
            for (var gy = -1; gy <= 1; gy++)
            for (var gx = -1; gx <= 1; gx++)
            {
                if (gx == 0 && gy == 0) continue;

                var pixelX = x + gx + 2;
                var pixelY = y + gy + 3;

                if (pixelX >= 0 && pixelX < _canvas.Width &&
                    pixelY >= 0 && pixelY < _canvas.Height)
                    _canvas.SetPixel(pixelX, pixelY, innerGlowColor);
            }
        }

        // Draw the character with enhanced contrast
        for (var py = 0; py < 7; py++)
        for (var px = 0; px < 5; px++)
            if (pattern[py, px])
            {
                var pixelX = x + px;
                var pixelY = y + py;

                if (pixelX >= 0 && pixelX < _canvas.Width &&
                    pixelY >= 0 && pixelY < _canvas.Height)
                {
                    // Core pixel
                    _canvas.SetPixel(pixelX, pixelY, color);

                    // Increased bright spot chance for more shimmer
                    var brightChance = isHead ? 35 : 18;
                    if (_random.Next(100) < brightChance)
                    {
                        var sparkleColor = new SKColor(
                            (byte)Math.Min(255, color.Red * 1.2f),
                            (byte)Math.Min(255, color.Green * 1.2f),
                            (byte)Math.Min(255, color.Blue * 1.2f)
                        );
                        _canvas.SetPixel(pixelX, pixelY, sparkleColor);
                    }
                }
            }
    }

    private bool[,] GetEnhancedCharPattern(char character)
    {
        // Create a 7x5 pattern for authentic Matrix character rendering
        var pattern = new bool[7, 5];

        // Use character hash for deterministic patterns
        var hash = character.GetHashCode();
        var index = Math.Abs(hash % 26); // 26 different base patterns

        // Define authentic Matrix-style character patterns (inspired by actual Matrix font)
        // These create recognizable digital/katakana-style glyphs
        switch (index % 13)
        {
            case 0: // Vertical line with horizontal bars (like katakana "?")
                pattern[0, 2] = pattern[1, 2] =
                    pattern[2, 2] = pattern[3, 2] = pattern[4, 2] = pattern[5, 2] = pattern[6, 2] = true;
                pattern[1, 1] = pattern[1, 3] = true;
                pattern[4, 1] = pattern[4, 3] = true;
                break;

            case 1: // Box shape (like "?")
                pattern[1, 1] = pattern[1, 2] = pattern[1, 3] = true;
                pattern[2, 1] = pattern[2, 3] = true;
                pattern[3, 1] = pattern[3, 3] = true;
                pattern[4, 1] = pattern[4, 3] = true;
                pattern[5, 1] = pattern[5, 2] = pattern[5, 3] = true;
                break;

            case 2: // Diagonal slash
                pattern[0, 4] = pattern[1, 3] = pattern[2, 3] = true;
                pattern[3, 2] = pattern[4, 2] = pattern[5, 1] = pattern[6, 0] = true;
                break;

            case 3: // "T" shape (like katakana "?")
                pattern[1, 0] = pattern[1, 1] = pattern[1, 2] = pattern[1, 3] = pattern[1, 4] = true;
                pattern[2, 2] = pattern[3, 2] = pattern[4, 2] = pattern[5, 2] = true;
                break;

            case 4: // Cross pattern
                pattern[1, 2] = pattern[2, 2] = pattern[3, 2] = pattern[4, 2] = pattern[5, 2] = true;
                pattern[3, 0] = pattern[3, 1] = pattern[3, 3] = pattern[3, 4] = true;
                break;

            case 5: // "Z" zigzag
                pattern[1, 1] = pattern[1, 2] = pattern[1, 3] = true;
                pattern[2, 3] = pattern[3, 2] = pattern[4, 1] = true;
                pattern[5, 1] = pattern[5, 2] = pattern[5, 3] = true;
                break;

            case 6: // Number "0" style
                pattern[1, 1] = pattern[1, 2] = pattern[1, 3] = true;
                pattern[2, 1] = pattern[2, 3] = true;
                pattern[3, 1] = pattern[3, 3] = true;
                pattern[4, 1] = pattern[4, 3] = true;
                pattern[5, 1] = pattern[5, 2] = pattern[5, 3] = true;
                break;

            case 7: // Number "1" style  
                pattern[1, 2] = pattern[2, 2] = pattern[3, 2] = pattern[4, 2] = pattern[5, 2] = true;
                pattern[0, 1] = pattern[1, 1] = true;
                break;

            case 8: // "S" curve
                pattern[1, 2] = pattern[1, 3] = true;
                pattern[2, 1] = pattern[3, 2] = pattern[4, 3] = true;
                pattern[5, 1] = pattern[5, 2] = true;
                break;

            case 9: // Lightning bolt
                pattern[0, 3] = pattern[1, 3] = pattern[2, 2] = pattern[3, 2] = true;
                pattern[4, 1] = pattern[5, 1] = pattern[6, 0] = true;
                break;

            case 10: // "=" equals
                pattern[2, 0] = pattern[2, 1] = pattern[2, 2] = pattern[2, 3] = pattern[2, 4] = true;
                pattern[4, 0] = pattern[4, 1] = pattern[4, 2] = pattern[4, 3] = pattern[4, 4] = true;
                break;

            case 11: // Triangle
                pattern[0, 2] = true;
                pattern[1, 1] = pattern[1, 3] = true;
                pattern[2, 1] = pattern[2, 3] = true;
                pattern[3, 0] = pattern[3, 4] = true;
                pattern[4, 0] = pattern[4, 4] = true;
                pattern[5, 0] = pattern[5, 1] = pattern[5, 2] = pattern[5, 3] = pattern[5, 4] = true;
                break;

            case 12: // Scatter pattern (like digital noise)
                pattern[0, 1] = pattern[0, 3] = true;
                pattern[1, 0] = pattern[1, 4] = true;
                pattern[2, 2] = pattern[3, 0] = pattern[3, 4] = true;
                pattern[4, 1] = pattern[4, 3] = true;
                pattern[5, 2] = pattern[6, 1] = pattern[6, 3] = true;
                break;
        }

        // Add subtle variation based on second hash component
        var variation = Math.Abs((hash >> 8) % 5);
        if (variation > 0)
        {
            // Add or remove 1-2 pixels for variety while keeping character recognizable
            var modifications = Math.Min(variation, 2);
            for (var m = 0; m < modifications; m++)
            {
                var y = Math.Abs((hash >> (8 + m * 4)) % 7);
                var x = Math.Abs((hash >> (10 + m * 4)) % 5);
                pattern[y, x] = !pattern[y, x]; // Toggle pixel
            }
        }

        return pattern;
    }

    private class RainColumn
    {
        private const int MaxPatternLength = 35;
        private static readonly Random SeedRandom = new(0xDECADE);

        // Character storage for the column
        private readonly char[] _chars;
        private readonly char[] _originalChars;
        public byte Brightness;
        public int CharHeight;
        public int CharWidth;
        public float Density;
        public bool GlowEffect;
        public int Length;
        public int Speed;
        public int X;
        public int Y;

        public RainColumn()
        {
            // Use a static pattern seed for authentic appearance
            _originalChars = new char[MaxPatternLength];
            for (var i = 0; i < MaxPatternLength; i++)
                _originalChars[i] = MatrixChars[Math.Abs(SeedRandom.Next()) % MatrixChars.Length];

            // Initialize with a subset of the original characters
            _chars = new char[MaxPatternLength];
            Array.Copy(_originalChars, _chars, MaxPatternLength);
        }

        public char GetChar(int index)
        {
            // Return cached character
            return _chars[index % _chars.Length];
        }

        public void ChangeCharAt(int index, bool forceChange = false)
        {
            var actualIndex = index % _chars.Length;
            if (forceChange || _chars[actualIndex] == ' ')
            {
                // Use a seeded random index for authentic character selection
                var newChar = _originalChars[Math.Abs(SeedRandom.Next()) % _originalChars.Length];
                _chars[actualIndex] = newChar == ' ' ? '?' : newChar; // Avoid spaces, use '?' as fallback
            }
        }

        public void Update()
        {
            Y += Speed;
        }

        public void RegenerateChars()
        {
            // Only regenerate if density permits
            if (Density > 0.5f)
                for (var i = 0; i < Length && i < _chars.Length; i++)
                    ChangeCharAt(i, true);
        }
    }
}
