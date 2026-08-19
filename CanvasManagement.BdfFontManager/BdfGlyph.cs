namespace CanvasManagement.BdfFontManager;

/// <summary>
///     Represents a single glyph (character) in a BDF font
/// </summary>
public class BdfGlyph
{
    /// <summary>
    ///     Character encoding (Unicode code point)
    /// </summary>
    public int Encoding { get; set; }

    /// <summary>
    ///     Character name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Glyph width in pixels
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    ///     Glyph height in pixels
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    ///     X offset for rendering
    /// </summary>
    public int OffsetX { get; set; }

    /// <summary>
    ///     Y offset for rendering
    /// </summary>
    public int OffsetY { get; set; }

    /// <summary>
    ///     Device width (horizontal advance)
    /// </summary>
    public int DeviceWidth { get; set; }

    /// <summary>
    ///     Bitmap data (row-major, true = pixel on)
    /// </summary>
    public bool[,] Bitmap { get; set; } = new bool[0, 0];

    /// <summary>
    ///     Gets the actual character this glyph represents
    /// </summary>
    public char Character => Encoding >= 0 && Encoding <= 0x10FFFF ? (char)Encoding : '?';

    public override string ToString()
    {
        return $"Glyph '{Character}' (U+{Encoding:X4}): {Width}x{Height}px";
    }
}