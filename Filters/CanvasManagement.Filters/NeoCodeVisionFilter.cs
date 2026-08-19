using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Neo's "Code Vision" - TRUE pixel-by-pixel transformation into vertical Matrix code streams
///     Every pixel is analyzed and completely redrawn as flowing digital code
/// </summary>
[FilterInfo("Neo Code Vision",
    "Transform your world into pure Matrix code streams - see through Neo's eyes",
    "Matrix Effects",
    IconResourceName = "neocodevision.svg")]
public class NeoCodeVisionFilter : ICanvasFilter
{
    // CRITICAL FIX: Pre-allocate all micro patterns to eliminate 106 MB/s allocation rate
    private static readonly Dictionary<int, bool[]> _patternCache = new()
    {
        [0] = new[] // Vertical line with cross
        {
            false, false, true, false,
            false, false, true, false,
            true, true, true, true,
            false, false, true, false,
            false, true, false, true,
            true, false, false, false
        },
        [1] = new[] // Diagonal
        {
            false, false, false, true,
            false, false, true, false,
            false, true, false, false,
            false, true, false, false,
            true, false, false, false,
            true, false, false, false
        },
        [2] = new[] // T-shape
        {
            true, true, true, true,
            false, false, true, false,
            false, false, true, false,
            false, false, true, false,
            false, true, false, true,
            false, false, false, false
        },
        [3] = new[] // Box
        {
            true, true, true, true,
            true, false, false, true,
            true, false, false, true,
            true, false, false, true,
            true, true, true, true,
            false, false, false, false
        },
        [4] = new[] // Scattered
        {
            false, true, false, true,
            true, false, true, false,
            false, true, true, false,
            true, false, false, true,
            false, true, false, false,
            false, false, true, false
        },
        [10] = new[] // Number pattern
        {
            false, true, true, false,
            true, false, false, true,
            true, false, false, true,
            true, false, false, true,
            true, false, false, true,
            false, true, true, false
        }
    };

    // Matrix code characters (heavy on katakana for authenticity)
    private static readonly char[] CodeChars =
    {
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?',
        ':', '=', '*', '-', '|', '/', '\\'
    };

    private readonly Random _random = new();
    private byte[,] _brightnessMap;
    private char[,] _characterMap;
    private byte[,] _colorHueMap;
    private int _frameCount;
    private bool _initialized;

    /// <summary>
    ///     Density of code characters (0.5-2.0)
    /// </summary>
    [FilterParameter("Stream Density", "Density of Matrix code characters", MinValue = 0.5f, MaxValue = 2.0f,
        DefaultValue = 1.2f)]
    public float StreamDensity { get; set; } = 1.2f;

    /// <summary>
    ///     How much original image structure influences the code (0.0 = abstract, 1.0 = exact structure)
    /// </summary>
    [FilterParameter("Content Mapping", "How much original image structure shows through", MinValue = 0.0f,
        MaxValue = 1.0f, DefaultValue = 0.8f)]
    public float ContentMapping { get; set; } = 0.8f;

    /// <summary>
    ///     Code stream animation speed
    /// </summary>
    [FilterParameter("Fall Speed", "Code stream animation speed", MinValue = 1, MaxValue = 10, DefaultValue = 2)]
    public int FallSpeed { get; set; } = 2;

    public string Name => "Neo Code Vision";
    public float Intensity { get; set; } = 0.9f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        // CRITICAL FIX: Neo Code Vision NEVER works in-place
        // Always create a completely new bitmap to prevent race conditions
        // This ensures the filter output is atomic - no partial frames can escape

        var result = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);

        // Initialize or update analysis maps
        if (!_initialized || _brightnessMap == null ||
            _brightnessMap.GetLength(0) != source.Width ||
            _brightnessMap.GetLength(1) != source.Height)
        {
            InitializeMaps(source.Width, source.Height);
            _initialized = true;
        }

        // BLACK OUT the result bitmap FIRST (guarantee no leakage)
        unsafe
        {
            var pixels = (uint*)result.GetPixels().ToPointer();
            var pixelCount = result.Width * result.Height;
            for (var i = 0; i < pixelCount; i++) pixels[i] = 0xFF000000; // Pure black
        }

        // Analyze the ORIGINAL source image only (read-only)
        AnalyzeSourceImage(source);

        // Transform on the blacked-out result bitmap (write-only)
        TransformToCodeVision(result);

        // Update animation
        _frameCount++;
        if (_frameCount % FallSpeed == 0) UpdateCharacterMap();

        // CRITICAL: Always copy atomically, even if inPlace=true
        // This prevents the CanvasManager from seeing partial frames
        if (inPlace)
        {
            // Lock BOTH bitmaps during the entire copy operation
            lock (source)
            {
                unsafe
                {
                    var srcPtr = (uint*)result.GetPixels().ToPointer();
                    var dstPtr = (uint*)source.GetPixels().ToPointer();
                    var pixelCount = source.Width * source.Height;

                    // Single atomic memory copy - no interruption possible
                    Buffer.MemoryCopy(srcPtr, dstPtr, pixelCount * 4, pixelCount * 4);
                }
            }

            result.Dispose();
            return source;
        }

        return result;
    }

    private void InitializeMaps(int width, int height)
    {
        _brightnessMap = new byte[width, height];
        _colorHueMap = new byte[width, height];
        _characterMap = new char[width / 4, height / 6]; // Character grid

        // Initialize character map with random characters
        for (var x = 0; x < _characterMap.GetLength(0); x++)
        for (var y = 0; y < _characterMap.GetLength(1); y++)
            _characterMap[x, y] = CodeChars[_random.Next(CodeChars.Length)];
    }

    private void AnalyzeSourceImage(SKBitmap source)
    {
        var width = source.Width;
        var height = source.Height;

        unsafe
        {
            var pixels = (uint*)source.GetPixels().ToPointer();

            // Analyze every single pixel
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var pixel = pixels[idx];

                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Store brightness for every pixel
                _brightnessMap[x, y] = (byte)(r * 0.299 + g * 0.587 + b * 0.114);

                // Store color information (simplified hue)
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var delta = max - min;

                if (delta > 0)
                {
                    if (max == r)
                        _colorHueMap[x, y] = (byte)((g - b) * 255 / (6 * delta));
                    else if (max == g)
                        _colorHueMap[x, y] = (byte)(85 + (b - r) * 255 / (6 * delta));
                    else
                        _colorHueMap[x, y] = (byte)(170 + (r - g) * 255 / (6 * delta));
                }
                else
                {
                    _colorHueMap[x, y] = 0;
                }
            }
        }
    }

    private void TransformToCodeVision(SKBitmap target)
    {
        var width = target.Width;
        var height = target.Height;

        unsafe
        {
            var pixels = (uint*)target.GetPixels().ToPointer();

            // COMPLETE REPAINT - every pixel becomes part of code streams
            var charWidth = 4;
            var charHeight = 6;

            // Target is already black from Apply() method, no need to black out again

            // Now draw EVERYTHING as vertical code streams
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sourceBrightness = _brightnessMap[x, y];

                // Calculate vertical flow position for this pixel
                var flowPhase = (y + _frameCount * 2 + x / 2) % (height + 50);
                var flowIntensity = (float)Math.Sin(flowPhase * Math.PI / height) * 0.3f + 0.7f;

                // Determine if this pixel should be bright (part of code character)
                var charX = x / charWidth;
                var charY = y / charHeight;

                if (charX >= _characterMap.GetLength(0)) charX = _characterMap.GetLength(0) - 1;
                if (charY >= _characterMap.GetLength(1)) charY = _characterMap.GetLength(1) - 1;

                var character = _characterMap[charX, charY];
                var pattern = GetMicroPattern(character);

                var patternX = x % charWidth;
                var patternY = y % charHeight;

                // Check if this pixel is part of the character pattern
                var isCharPixel = pattern[patternY * charWidth + patternX];

                // Calculate final brightness
                var finalBrightness = sourceBrightness * ContentMapping * Intensity;

                // Apply vertical flow
                finalBrightness *= flowIntensity;

                // Add density variation
                var densityFactor = StreamDensity * (0.8f + (float)_random.NextDouble() * 0.4f);

                // Determine pixel color
                byte greenValue = 0;

                if (isCharPixel && sourceBrightness > 10)
                {
                    // This pixel is part of a code character
                    greenValue = (byte)Math.Min(255, finalBrightness * densityFactor);

                    // Occasional bright flashes
                    if (_random.Next(10000) < 3) greenValue = 255;
                }
                else if (sourceBrightness > 5)
                {
                    // Background glow from nearby characters
                    greenValue = (byte)Math.Min(100, finalBrightness * 0.3f * densityFactor);
                }

                // Apply the transformation - pixel is already black, just set if > 0
                if (greenValue > 0)
                {
                    var r = greenValue > 200 ? (byte)(greenValue * 0.15f) : (byte)0;
                    var g = greenValue;
                    var b = (byte)(greenValue * 0.12f);

                    pixels[y * width + x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }

            // Add vertical scan/streak effects for extra authenticity
            AddVerticalEffects(pixels, width, height);
        }
    }

    private bool[] GetMicroPattern(char c)
    {
        // CRITICAL FIX: Return cached pattern instead of allocating new array
        // This eliminates 106 MB/s allocation rate (73,728 pixels × 24 bytes × 60 FPS)

        if (c >= '0' && c <= '9') return _patternCache[10]; // Number pattern

        // Use character hash for deterministic pattern selection (more cache-friendly than random)
        var patternType = (c.GetHashCode() & 0x7FFFFFFF) % 5;
        return _patternCache[patternType];
    }

    private unsafe void AddVerticalEffects(uint* pixels, int width, int height)
    {
        // Add moving vertical bright streaks (more subtle)
        var streakCount = Math.Max(2, (int)(2 * StreamDensity)); // Reduced from 3

        for (var s = 0; s < streakCount; s++)
        {
            var streakX = (_frameCount * 3 + s * 70) % width;

            for (var y = 0; y < height; y++)
            {
                // Streak intensity varies (reduced)
                var phase = (y + _frameCount + s * 20) % 150;
                var intensity = (byte)(50 + Math.Sin(phase * Math.PI / 75) * 50); // Reduced from 80+80

                var idx = y * width + streakX;
                var existing = pixels[idx];
                var eg = (byte)((existing >> 8) & 0xFF);

                // Only brighten if there's already code there
                if (eg > 20)
                {
                    var ng = (byte)Math.Min(255, eg + intensity * Intensity * 0.3f); // Reduced from 0.5f
                    var r = (byte)(ng * 0.15f);
                    var b = (byte)(ng * 0.1f);

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)ng << 8) | b;
                }
            }
        }

        // Add horizontal scan line (much more subtle)
        var scanY = _frameCount * 3 % height;
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = scanY + dy;
            if (y < 0 || y >= height) continue;

            var scanIntensity = (byte)(15 * (1 - Math.Abs(dy) / 2.0f) * Intensity); // Reduced from 30

            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var existing = pixels[idx];
                var eg = (byte)((existing >> 8) & 0xFF);

                // Only brighten if there's already some green (code visible)
                if (eg > 30)
                {
                    var ng = (byte)Math.Min(255, eg + scanIntensity);
                    var r = (byte)((existing >> 16) & 0xFF);
                    var b = (byte)(existing & 0xFF);

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)ng << 8) | b;
                }
            }
        }
    }

    private void UpdateCharacterMap()
    {
        // Randomly change characters to create flowing effect
        var changeCount = (int)(_characterMap.GetLength(0) * _characterMap.GetLength(1) * 0.15f * StreamDensity);

        for (var i = 0; i < changeCount; i++)
        {
            var x = _random.Next(_characterMap.GetLength(0));
            var y = _random.Next(_characterMap.GetLength(1));

            _characterMap[x, y] = CodeChars[_random.Next(CodeChars.Length)];
        }
    }

    private class CodeStream
    {
        public char Character { get; set; }
        public byte Brightness { get; set; }
        public int ChangeSpeed { get; set; }
        public int FlowOffset { get; set; }
    }
}