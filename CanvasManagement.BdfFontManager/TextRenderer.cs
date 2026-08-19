using SkiaSharp;

namespace CanvasManagement.BdfFontManager;

public class TextRenderer
{
    /// <summary>
    ///     Renders text using a BDF font with specified color
    /// </summary>
    /// <param name="bdfFont">BDF font to use</param>
    /// <param name="text">Text to render</param>
    /// <param name="color">Color for the text</param>
    /// <param name="backgroundColor">Background color (default: transparent)</param>
    /// <returns>Rendered bitmap with text</returns>
    internal static SKBitmap? RenderText(BdfFont bdfFont, string text, SKColor color, SKColor? backgroundColor = null)
    {
        return bdfFont.RenderText(text, color, backgroundColor);
    }

    /// <summary>
    ///     Gets the size of rendered text
    /// </summary>
    internal static SKSize GetTextSize(BdfFont bdfFont, string text)
    {
        return bdfFont.MeasureText(text);
    }
}