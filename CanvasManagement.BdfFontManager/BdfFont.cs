using SkiaSharp;

namespace CanvasManagement.BdfFontManager;

/// <summary>
///     BDF (Glyph Bitmap Distribution Format) font parser and renderer
///     Optimized for LED matrices and low-resolution displays
/// </summary>
public class BdfFont
{
    private readonly Dictionary<int, BdfGlyph> _glyphs = new();
    private readonly object _lock = new();

    /// <summary>
    ///     Creates a new BDF font from a file
    /// </summary>
    /// <param name="fontFilePath">Path to .bdf font file</param>
    public BdfFont(string fontFilePath)
    {
        if (!File.Exists(fontFilePath))
            throw new FileNotFoundException($"BDF font file not found: {fontFilePath}");

        ParseBdfFile(fontFilePath);
    }

    /// <summary>
    ///     Font name
    /// </summary>
    public string FontName { get; private set; } = string.Empty;

    /// <summary>
    ///     Font size in points
    /// </summary>
    public int PointSize { get; private set; }

    /// <summary>
    ///     Font resolution X (DPI)
    /// </summary>
    public int ResolutionX { get; private set; } = 75;

    /// <summary>
    ///     Font resolution Y (DPI)
    /// </summary>
    public int ResolutionY { get; private set; } = 75;

    /// <summary>
    ///     Font bounding box width
    /// </summary>
    public int BoundingBoxWidth { get; private set; }

    /// <summary>
    ///     Font bounding box height
    /// </summary>
    public int BoundingBoxHeight { get; private set; }

    /// <summary>
    ///     Font bounding box X offset
    /// </summary>
    public int BoundingBoxOffsetX { get; private set; }

    /// <summary>
    ///     Font bounding box Y offset
    /// </summary>
    public int BoundingBoxOffsetY { get; private set; }

    /// <summary>
    ///     Number of glyphs in the font
    /// </summary>
    public int GlyphCount => _glyphs.Count;

    /// <summary>
    ///     Gets all available characters in this font
    /// </summary>
    public IEnumerable<char> AvailableCharacters
    {
        get
        {
            lock (_lock)
            {
                return _glyphs.Values
                    .Where(g => g.Encoding >= 0 && g.Encoding <= 0x10FFFF)
                    .Select(g => (char)g.Encoding)
                    .ToList();
            }
        }
    }

    /// <summary>
    ///     Parses a BDF font file
    /// </summary>
    private void ParseBdfFile(string fontFilePath)
    {
        var lines = File.ReadAllLines(fontFilePath);
        var lineIndex = 0;

        // Parse global font properties
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex].Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("COMMENT"))
            {
                lineIndex++;
                continue;
            }

            if (line.StartsWith("FONT "))
            {
                FontName = line.Substring(5).Trim();
            }
            else if (line.StartsWith("SIZE "))
            {
                var parts = line.Substring(5).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var pointSize))
                    PointSize = pointSize;
                if (parts.Length >= 2 && int.TryParse(parts[1], out var resX))
                    ResolutionX = resX;
                if (parts.Length >= 3 && int.TryParse(parts[2], out var resY))
                    ResolutionY = resY;
            }
            else if (line.StartsWith("FONTBOUNDINGBOX "))
            {
                var parts = line.Substring(16).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    if (int.TryParse(parts[0], out var width))
                        BoundingBoxWidth = width;
                    if (int.TryParse(parts[1], out var height))
                        BoundingBoxHeight = height;
                    if (int.TryParse(parts[2], out var offsetX))
                        BoundingBoxOffsetX = offsetX;
                    if (int.TryParse(parts[3], out var offsetY))
                        BoundingBoxOffsetY = offsetY;
                }
            }
            else if (line.StartsWith("CHARS "))
            {
                // Start of character definitions
                var charCountStr = line.Substring(6).Trim();
                if (int.TryParse(charCountStr, out var charCount))
                {
                    lineIndex++;

                    // Parse all characters
                    var parsedCount = 0;
                    var skippedCount = 0;

                    while (parsedCount + skippedCount < charCount && lineIndex < lines.Length)
                    {
                        var glyph = ParseGlyph(lines, ref lineIndex);
                        if (glyph != null && glyph.Encoding >= 0)
                        {
                            lock (_lock)
                            {
                                _glyphs[glyph.Encoding] = glyph;
                            }

                            parsedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }

                    if (skippedCount > 0)
                        Console.WriteLine($"[BDF] Parsed {parsedCount} glyphs, skipped {skippedCount} invalid glyphs");
                }
                else
                {
                    Console.WriteLine($"[BDF] Warning: Invalid CHARS count '{charCountStr}'");
                }

                break;
            }

            lineIndex++;
        }
    }

    /// <summary>
    ///     Parses a single glyph from the BDF file
    /// </summary>
    private BdfGlyph? ParseGlyph(string[] lines, ref int lineIndex)
    {
        var glyph = new BdfGlyph();

        // Parse glyph properties
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex].Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("COMMENT"))
            {
                lineIndex++;
                continue;
            }

            if (line.StartsWith("STARTCHAR "))
            {
                glyph.Name = line.Substring(10).Trim();
            }
            else if (line.StartsWith("ENCODING "))
            {
                var encodingStr = line.Substring(9).Trim();
                if (!string.IsNullOrWhiteSpace(encodingStr) && int.TryParse(encodingStr, out var encoding))
                {
                    glyph.Encoding = encoding;
                }
                else
                {
                    Console.WriteLine(
                        $"[BDF] Warning: Invalid ENCODING value '{encodingStr}' for glyph '{glyph.Name}', skipping glyph");
                    // Skip to ENDCHAR
                    while (lineIndex < lines.Length && !lines[lineIndex].Trim().Equals("ENDCHAR")) lineIndex++;
                    lineIndex++; // Skip ENDCHAR
                    return null;
                }
            }
            else if (line.StartsWith("DWIDTH "))
            {
                var parts = line.Substring(7).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var dwidth)) glyph.DeviceWidth = dwidth;
            }
            else if (line.StartsWith("BBX "))
            {
                var parts = line.Substring(4).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    if (int.TryParse(parts[0], out var width))
                        glyph.Width = width;
                    if (int.TryParse(parts[1], out var height))
                        glyph.Height = height;
                    if (int.TryParse(parts[2], out var offsetX))
                        glyph.OffsetX = offsetX;
                    if (int.TryParse(parts[3], out var offsetY))
                        glyph.OffsetY = offsetY;
                }
            }
            else if (line == "BITMAP")
            {
                // Parse bitmap data
                lineIndex++;
                glyph.Bitmap = ParseBitmap(lines, ref lineIndex, glyph.Width, glyph.Height);

                // Consume the trailing ENDCHAR if it is present and intact (the normal case).
                // If it is missing or corrupted (damaged font file), do NOT scan forward for it -
                // that would swallow the following glyph. Leaving the line lets the main parse
                // loop resync on the next STARTCHAR, so a corrupted glyph only loses itself,
                // not its neighbours (e.g. 'a'/'i'/'d').
                if (lineIndex < lines.Length && lines[lineIndex].Trim() == "ENDCHAR")
                    lineIndex++;

                return glyph;
            }
            else if (line == "ENDCHAR")
            {
                lineIndex++;
                return glyph;
            }

            lineIndex++;
        }

        return null;
    }

    /// <summary>
    ///     Parses bitmap data for a glyph
    /// </summary>
    private bool[,] ParseBitmap(string[] lines, ref int lineIndex, int width, int height)
    {
        var bitmap = new bool[width, height];

        for (var row = 0; row < height && lineIndex < lines.Length; row++)
        {
            var line = lines[lineIndex].Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                lineIndex++;
                row--; // Don't count this as a row
                continue;
            }

            // Stop at the glyph terminator, or the start of the next glyph. The latter is a
            // safety net for corrupted/short fonts so a bad glyph never consumes the next one's
            // header rows.
            if (line == "ENDCHAR" || line.StartsWith("STARTCHAR"))
                break;

            try
            {
                // BDF bitmap data can span multiple bytes for wide glyphs
                // Each byte is represented as 2 hex characters
                // For example, a 16-pixel wide glyph needs 4 hex chars (2 bytes)

                // Calculate how many bytes we need for this width
                var bytesNeeded = (width + 7) / 8; // Round up to nearest byte
                var hexCharsNeeded = bytesNeeded * 2;

                // Pad hex string if needed (some fonts omit leading zeros)
                var paddedLine = line.PadLeft(hexCharsNeeded, '0');

                // Parse bytes from hex string
                var bytes = new byte[bytesNeeded];
                for (var byteIndex = 0; byteIndex < bytesNeeded && byteIndex * 2 < paddedLine.Length; byteIndex++)
                {
                    var hexByte = paddedLine.Substring(byteIndex * 2, Math.Min(2, paddedLine.Length - byteIndex * 2));
                    if (hexByte.Length == 2) bytes[byteIndex] = Convert.ToByte(hexByte, 16);
                }

                // Extract bits from bytes (MSB first)
                for (var col = 0; col < width; col++)
                {
                    var byteIndex = col / 8;
                    var bitIndex = 7 - col % 8; // MSB first within each byte

                    if (byteIndex < bytes.Length) bitmap[col, row] = ((bytes[byteIndex] >> bitIndex) & 1) == 1;
                }
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"[BDF] Warning: Invalid bitmap hex value '{line}' at row {row}, skipping");
            }
            catch (OverflowException ex)
            {
                Console.WriteLine($"[BDF] Warning: Bitmap hex value '{line}' too large at row {row}, skipping");
            }

            lineIndex++;
        }

        return bitmap;
    }

    /// <summary>
    ///     Gets a glyph for a specific character
    /// </summary>
    /// <param name="ch">Character to get glyph for</param>
    /// <returns>Glyph or null if not found</returns>
    public BdfGlyph? GetGlyph(char ch)
    {
        lock (_lock)
        {
            return _glyphs.TryGetValue(ch, out var glyph) ? glyph : null;
        }
    }

    /// <summary>
    ///     Checks if a character is available in this font
    /// </summary>
    public bool HasGlyph(char ch)
    {
        lock (_lock)
        {
            return _glyphs.ContainsKey(ch);
        }
    }

    /// <summary>
    ///     Measures the size of a string when rendered
    /// </summary>
    /// <param name="text">Text to measure</param>
    /// <returns>Size in pixels (width, height)</returns>
    public SKSize MeasureText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new SKSize(0, 0);

        lock (_lock)
        {
            var totalWidth = 0;
            var maxHeight = 0;
            var minOffsetY = 0;
            var maxOffsetY = 0;

            foreach (var ch in text)
                if (_glyphs.TryGetValue(ch, out var glyph))
                {
                    totalWidth += glyph.DeviceWidth;

                    // Calculate actual vertical extent
                    var topExtent = glyph.Height + glyph.OffsetY;
                    var bottomExtent = glyph.OffsetY;

                    maxOffsetY = Math.Max(maxOffsetY, topExtent);
                    minOffsetY = Math.Min(minOffsetY, bottomExtent);
                }
                else
                {
                    // Use space width for unknown characters
                    if (_glyphs.TryGetValue(' ', out var spaceGlyph))
                        totalWidth += spaceGlyph.DeviceWidth;
                    else
                        totalWidth += BoundingBoxWidth;
                }

            // Total height includes descenders (characters below baseline)
            maxHeight = maxOffsetY - minOffsetY;

            // Use bounding box height if no glyphs rendered
            if (maxHeight == 0)
                maxHeight = BoundingBoxHeight;

            return new SKSize(totalWidth, maxHeight);
        }
    }

    /// <summary>
    ///     Gets a 2D boolean array representing the rendered text
    ///     Used for compatibility with existing TextRenderer
    /// </summary>
    /// <param name="text">Text to render</param>
    /// <returns>2D array where true = pixel on, false = pixel off</returns>
    public bool[,] GetMapOfString(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new bool[0, 0];

        var size = MeasureText(text);
        var width = (int)size.Width;
        var height = (int)size.Height;

        if (width == 0 || height == 0)
            return new bool[0, 0];

        var result = new bool[width, height];
        var xOffset = 0;

        lock (_lock)
        {
            // Find the minimum Y offset (descenders) to establish baseline
            var minOffsetY = 0;
            foreach (var ch in text)
                if (_glyphs.TryGetValue(ch, out var glyph))
                    minOffsetY = Math.Min(minOffsetY, glyph.OffsetY);

            foreach (var ch in text)
                if (_glyphs.TryGetValue(ch, out var glyph))
                {
                    // Copy glyph bitmap to result
                    // Adjust Y position to account for descenders
                    var glyphX = xOffset + glyph.OffsetX;
                    var glyphY = height - (glyph.Height + glyph.OffsetY - minOffsetY);

                    for (var y = 0; y < glyph.Height; y++)
                    for (var x = 0; x < glyph.Width; x++)
                    {
                        var targetX = glyphX + x;
                        var targetY = glyphY + y;

                        if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                            result[targetX, targetY] = glyph.Bitmap[x, y];
                    }

                    xOffset += glyph.DeviceWidth;
                }
                else
                {
                    // Skip unknown characters (use space width)
                    if (_glyphs.TryGetValue(' ', out var spaceGlyph))
                        xOffset += spaceGlyph.DeviceWidth;
                    else
                        xOffset += BoundingBoxWidth;
                }
        }

        return result;
    }

    /// <summary>
    ///     Renders text directly to an SKBitmap
    /// </summary>
    /// <param name="text">Text to render</param>
    /// <param name="color">Text color</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    /// <returns>Rendered bitmap</returns>
    public SKBitmap RenderText(string text, SKColor color, SKColor? backgroundColor = null)
    {
        var size = MeasureText(text);
        var width = (int)size.Width;
        var height = (int)size.Height;

        if (width == 0 || height == 0)
            return new SKBitmap(1, 1);

        var bitmap = new SKBitmap(width, height);
        var bgColor = backgroundColor ?? SKColors.Transparent;
        bitmap.Erase(bgColor);

        var xOffset = 0;

        lock (_lock)
        {
            // Find the minimum Y offset (descenders) to establish baseline
            var minOffsetY = 0;
            foreach (var ch in text)
                if (_glyphs.TryGetValue(ch, out var glyph))
                    minOffsetY = Math.Min(minOffsetY, glyph.OffsetY);

            foreach (var ch in text)
                if (_glyphs.TryGetValue(ch, out var glyph))
                {
                    // Render glyph to bitmap
                    // Adjust Y position to account for descenders
                    var glyphX = xOffset + glyph.OffsetX;
                    var glyphY = height - (glyph.Height + glyph.OffsetY - minOffsetY);

                    for (var y = 0; y < glyph.Height; y++)
                    for (var x = 0; x < glyph.Width; x++)
                        if (glyph.Bitmap[x, y])
                        {
                            var targetX = glyphX + x;
                            var targetY = glyphY + y;

                            if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                                bitmap.SetPixel(targetX, targetY, color);
                        }

                    xOffset += glyph.DeviceWidth;
                }
                else
                {
                    // Skip unknown characters
                    if (_glyphs.TryGetValue(' ', out var spaceGlyph))
                        xOffset += spaceGlyph.DeviceWidth;
                    else
                        xOffset += BoundingBoxWidth;
                }
        }

        return bitmap;
    }

    public override string ToString()
    {
        return $"BDF Font: {FontName}, {PointSize}pt, {GlyphCount} glyphs, {BoundingBoxWidth}x{BoundingBoxHeight}px";
    }
}