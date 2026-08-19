using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.BdfFontManager;

/// <summary>
///     Extension methods for Canvas to support BDF font rendering
///     Provides pixel-perfect text rendering for LED matrices and low-resolution displays
/// </summary>
public static class CanvasBdfExtensions
{
    /// <summary>
    ///     Draws text using a BDF font at the specified position
    /// </summary>
    /// <param name="canvas">Target canvas</param>
    /// <param name="text">Text to render</param>
    /// <param name="x">X position</param>
    /// <param name="y">Y position</param>
    /// <param name="color">Text color</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    public static void DrawBdfText(this ICanvas canvas, string text, int x, int y, SKColor color,
        string? fontName = null, SKColor? backgroundColor = null)
    {
        var font = BdfFontRegistry.GetFont(fontName);
        using var textBitmap = TextRenderer.RenderText(font, text, color, backgroundColor);

        if (textBitmap != null) canvas.DrawBitmap(textBitmap, x, y, fitToCanvas: false);
    }

    /// <summary>
    ///     Draws centered text using a BDF font
    /// </summary>
    /// <param name="canvas">Target canvas</param>
    /// <param name="text">Text to render</param>
    /// <param name="y">Y position (text will be centered horizontally)</param>
    /// <param name="color">Text color</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    public static void DrawBdfTextCentered(this ICanvas canvas, string text, int y, SKColor color,
        string? fontName = null, SKColor? backgroundColor = null)
    {
        var size = canvas.MeasureBdfText(text, fontName);
        var x = (canvas.Width - (int)size.Width) / 2;

        canvas.DrawBdfText(text, x, y, color, fontName, backgroundColor);
    }

    /// <summary>
    ///     Draws text centered both horizontally and vertically
    /// </summary>
    /// <param name="canvas">Target canvas</param>
    /// <param name="text">Text to render</param>
    /// <param name="color">Text color</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    public static void DrawBdfTextCenteredFull(this ICanvas canvas, string text, SKColor color,
        string? fontName = null, SKColor? backgroundColor = null)
    {
        var size = canvas.MeasureBdfText(text, fontName);
        var x = (canvas.Width - (int)size.Width) / 2;
        var y = (canvas.Height - (int)size.Height) / 2;

        canvas.DrawBdfText(text, x, y, color, fontName, backgroundColor);
    }

    /// <summary>
    ///     Measures the size of text when rendered with a BDF font
    /// </summary>
    /// <param name="canvas">Canvas (not used, but keeps API consistent)</param>
    /// <param name="text">Text to measure</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <returns>Size of the rendered text</returns>
    public static SKSize MeasureBdfText(this ICanvas canvas, string text, string? fontName = null)
    {
        var font = BdfFontRegistry.GetFont(fontName);
        return TextRenderer.GetTextSize(font, text);
    }

    /// <summary>
    ///     Draws right-aligned text using a BDF font
    /// </summary>
    /// <param name="canvas">Target canvas</param>
    /// <param name="text">Text to render</param>
    /// <param name="y">Y position</param>
    /// <param name="color">Text color</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    /// <param name="marginRight">Right margin in pixels (default: 0)</param>
    public static void DrawBdfTextRight(this ICanvas canvas, string text, int y, SKColor color,
        string? fontName = null, SKColor? backgroundColor = null, int marginRight = 0)
    {
        var size = canvas.MeasureBdfText(text, fontName);
        var x = canvas.Width - (int)size.Width - marginRight;

        canvas.DrawBdfText(text, x, y, color, fontName, backgroundColor);
    }

    /// <summary>
    ///     Draws multi-line text using a BDF font
    /// </summary>
    /// <param name="canvas">Target canvas</param>
    /// <param name="text">Text to render (use \n for line breaks)</param>
    /// <param name="x">X position</param>
    /// <param name="y">Y position (top line)</param>
    /// <param name="color">Text color</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    /// <param name="lineSpacing">Additional spacing between lines in pixels (default: 2)</param>
    public static void DrawBdfTextMultiline(this ICanvas canvas, string text, int x, int y, SKColor color,
        string? fontName = null, SKColor? backgroundColor = null, int lineSpacing = 2)
    {
        var lines = text.Split('\n');
        var currentY = y;

        foreach (var line in lines)
        {
            canvas.DrawBdfText(line, x, currentY, color, fontName);
            var size = canvas.MeasureBdfText(line, fontName);
            currentY += (int)size.Height + lineSpacing;
        }
    }
}