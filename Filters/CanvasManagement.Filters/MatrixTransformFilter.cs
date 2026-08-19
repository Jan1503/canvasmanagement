using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Matrix digital rain transformation - converts any content into falling code/characters
/// </summary>
[FilterInfo("Matrix Transform",
    "Convert your content into cascading Matrix digital rain with falling code columns",
    "Matrix Effects",
    IconResourceName = "matrixtransform.svg")]
public class MatrixTransformFilter : ICanvasFilter
{
    // Authentic Matrix characters
    private static readonly char[] MatrixChars =
    {
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        '?', '?', '?', '?', '?', '?', '?', '?', '?', '?',
        ':', '=', '*', '+', '-', '|', '/', '\\'
    };

    private readonly List<DigitalColumn> _columns = new();
    private readonly Random _random = new();
    private int _frameCount;
    private bool _initialized;

    /// <summary>
    ///     How much of the original image shows through (0.0 = all matrix, 1.0 = original visible)
    /// </summary>
    [FilterParameter("Source Blend", "How much of the original image shows through", MinValue = 0.0f, MaxValue = 1.0f,
        DefaultValue = 0.2f)]
    public float SourceBlend { get; set; } = 0.2f;

    /// <summary>
    ///     Column density (higher = more columns)
    /// </summary>
    [FilterParameter("Column Density", "Density of falling code columns", MinValue = 0.5f, MaxValue = 2.0f,
        DefaultValue = 1.0f)]
    public float ColumnDensity { get; set; } = 1.0f;

    public string Name => "Matrix Transform";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        // Initialize columns on first run
        if (!_initialized)
        {
            InitializeColumns(bitmap.Width, bitmap.Height);
            _initialized = true;
        }

        // Convert image to Matrix digital rain
        TransformToMatrix(bitmap, source);

        // Update animation
        _frameCount++;
        if (_frameCount % 2 == 0) // Update every 2 frames for performance
            UpdateColumns(bitmap.Height);

        return bitmap;
    }

    private void InitializeColumns(int width, int height)
    {
        var charWidth = 8;
        var columnCount = width / charWidth;
        var targetColumns = (int)(columnCount * ColumnDensity);

        for (var i = 0; i < targetColumns; i++)
            _columns.Add(new DigitalColumn
            {
                X = i % columnCount * charWidth,
                Y = _random.Next(-height * 2, 0),
                Speed = _random.Next(2, 6),
                Length = _random.Next(6, 20),
                Brightness = (byte)_random.Next(180, 255),
                CharWidth = charWidth,
                CharHeight = 10
            });
    }

    private void TransformToMatrix(SKBitmap target, SKBitmap source)
    {
        var width = target.Width;
        var height = target.Height;

        unsafe
        {
            var srcPixels = (uint*)source.GetPixels().ToPointer();
            var dstPixels = (uint*)target.GetPixels().ToPointer();

            // Apply green tint and darken to entire image
            for (var i = 0; i < width * height; i++)
            {
                var pixel = srcPixels[i];
                var a = (byte)((pixel >> 24) & 0xFF);
                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Get brightness
                var brightness = (int)(r * 0.299 + g * 0.587 + b * 0.114);

                // Convert to green scale with darkening
                var blendFactor = SourceBlend * (1.0f - Intensity);
                var greenIntensity = (byte)(brightness * (0.3f + blendFactor * 0.7f) * Intensity);

                r = (byte)Math.Min(255, greenIntensity * 0.1f);
                g = greenIntensity;
                b = (byte)Math.Min(255, greenIntensity * 0.1f);

                dstPixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }

        // Draw Matrix characters based on source brightness
        DrawMatrixOverContent(target, source);

        // Draw animated columns on top
        DrawAnimatedColumns(target);
    }

    private void DrawMatrixOverContent(SKBitmap target, SKBitmap source)
    {
        var width = target.Width;
        var height = target.Height;
        var charWidth = 8;
        var charHeight = 10;

        unsafe
        {
            var srcPixels = (uint*)source.GetPixels().ToPointer();
            var dstPixels = (uint*)target.GetPixels().ToPointer();

            // Draw characters based on source content brightness
            for (var y = 0; y < height; y += charHeight)
            for (var x = 0; x < width; x += charWidth)
            {
                // Sample source brightness
                var sampleX = Math.Min(x + charWidth / 2, width - 1);
                var sampleY = Math.Min(y + charHeight / 2, height - 1);
                var idx = sampleY * width + sampleX;

                var pixel = srcPixels[idx];
                var brightness = GetBrightness(pixel);

                // Only draw character if area is bright enough
                if (brightness > 30 && _random.Next(100) < brightness / 3)
                {
                    var charIntensity = (byte)Math.Min(255, brightness * 1.2f);
                    var color = new SKColor(0, charIntensity, (byte)(charIntensity * 0.2f));

                    DrawMatrixChar(dstPixels, width, height, x, y,
                        MatrixChars[_random.Next(MatrixChars.Length)], color);
                }
            }
        }
    }

    private void DrawAnimatedColumns(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var column in _columns)
                for (var i = 0; i < column.Length; i++)
                {
                    var y = column.Y - i * column.CharHeight;

                    if (y < -column.CharHeight || y >= height) continue;
                    if (column.X < 0 || column.X >= width) continue;

                    byte intensity;
                    var isHead = i == 0;

                    if (isHead)
                    {
                        // Bright white head
                        intensity = (byte)Math.Min(255, column.Brightness + 40);
                        var headColor = new SKColor(
                            (byte)(intensity * 0.95f),
                            intensity,
                            (byte)(intensity * 0.95f)
                        );
                        DrawMatrixChar(pixels, width, height, column.X, y,
                            MatrixChars[_random.Next(MatrixChars.Length)], headColor);
                    }
                    else
                    {
                        // Fading trail
                        var fadeFactor = 1.0f - i / (float)column.Length;
                        intensity = (byte)(column.Brightness * 0.7f * fadeFactor);
                        var color = new SKColor(0, intensity, (byte)(intensity * 0.15f));

                        DrawMatrixChar(pixels, width, height, column.X, y,
                            MatrixChars[_random.Next(MatrixChars.Length)], color);
                    }
                }
        }
    }

    private unsafe void DrawMatrixChar(uint* pixels, int width, int height,
        int x, int y, char c, SKColor color)
    {
        // Simple 5x7 character rendering
        var pattern = GetSimpleCharPattern(c);

        for (var py = 0; py < 7 && y + py < height; py++)
        for (var px = 0; px < 5 && x + px < width; px++)
            if (pattern[py, px])
            {
                var idx = (y + py) * width + x + px;
                if (idx >= 0 && idx < width * height)
                {
                    // Blend with existing pixel
                    var existing = pixels[idx];
                    var er = (byte)((existing >> 16) & 0xFF);
                    var eg = (byte)((existing >> 8) & 0xFF);
                    var eb = (byte)(existing & 0xFF);

                    var nr = (byte)Math.Min(255, er + color.Red);
                    var ng = (byte)Math.Min(255, eg + color.Green);
                    var nb = (byte)Math.Min(255, eb + color.Blue);

                    pixels[idx] = 0xFF000000u | ((uint)nr << 16) | ((uint)ng << 8) | nb;
                }
            }
    }

    private bool[,] GetSimpleCharPattern(char c)
    {
        // Simplified patterns for performance
        if (c >= '0' && c <= '9')
            return new[,]
            {
                { false, true, true, true, false },
                { true, false, false, false, true },
                { true, false, false, false, true },
                { true, false, false, false, true },
                { true, false, false, false, true },
                { true, false, false, false, true },
                { false, true, true, true, false }
            };

        // Japanese-style pattern
        return new[,]
        {
            { false, false, true, false, false },
            { false, false, true, false, false },
            { true, true, true, true, true },
            { false, false, true, false, false },
            { false, true, false, true, false },
            { true, false, false, false, true },
            { false, false, false, false, false }
        };
    }

    private void UpdateColumns(int height)
    {
        foreach (var column in _columns)
        {
            column.Y += column.Speed;

            // Reset if off screen
            if (column.Y > height + column.Length * column.CharHeight)
            {
                column.Y = -_random.Next(100, 400);
                column.Speed = _random.Next(2, 6);
                column.Length = _random.Next(6, 20);
                column.Brightness = (byte)_random.Next(180, 255);
            }
        }
    }

    private int GetBrightness(uint pixel)
    {
        var r = (pixel >> 16) & 0xFF;
        var g = (pixel >> 8) & 0xFF;
        var b = pixel & 0xFF;
        return (int)(r * 0.299 + g * 0.587 + b * 0.114);
    }

    private class DigitalColumn
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Speed { get; set; }
        public int Length { get; set; }
        public int CharWidth { get; set; }
        public int CharHeight { get; set; }
        public byte Brightness { get; set; }
    }
}