using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.BdfFontManager;

/// <summary>
///     Per-canvas BDF font manager for advanced text operations
///     Provides scrolling text and other features tied to a specific canvas
/// </summary>
public class BdfFontManager(ICanvas canvas)
{
    private string? _currentFontName;
    private ScrollText? _currentScrollText;
    private BdfFont? _font;

    /// <summary>
    ///     Creates a BDF font manager for a canvas
    ///     Internal factory method used by Canvas implementation
    /// </summary>
    internal static BdfFontManager Create(ICanvas canvas)
    {
        return new BdfFontManager(canvas);
    }

    /// <summary>
    ///     Loads a BDF font from file path (bypasses registry)
    /// </summary>
    /// <param name="bdfFontFilePath">Path to .bdf font file</param>
    [Obsolete("Use BdfFontRegistry.RegisterFont() and SetFont() instead for better font management")]
    public void LoadBDFFont(string bdfFontFilePath)
    {
        _font = new BdfFont(bdfFontFilePath);
        _currentFontName = null;
        Console.WriteLine($"[BDF] Loaded font from file: {bdfFontFilePath}");
    }

    /// <summary>
    ///     Sets the font to use from the registry
    /// </summary>
    /// <param name="fontName">Font name (null = default font)</param>
    public void SetFont(string? fontName = null)
    {
        var resolved = fontName ?? BdfFontRegistry.DefaultFontName;

        // No-op if the requested font is already active. This is called on every BDF measure/render
        // (potentially many times per frame), so skipping redundant work also stops log flooding.
        if (_font != null && _currentFontName == resolved)
            return;

        _font = BdfFontRegistry.GetFont(fontName);
        _currentFontName = resolved;
    }

    /// <summary>
    ///     Gets the current font, loading default if none set
    /// </summary>
    private BdfFont GetCurrentFont()
    {
        if (_font == null)
        {
            _font = BdfFontRegistry.GetFont();
            _currentFontName = BdfFontRegistry.DefaultFontName;
        }

        return _font;
    }

    /// <summary>
    ///     Renders text at specified location
    /// </summary>
    /// <param name="text">Text to render</param>
    /// <param name="location">Position on canvas</param>
    /// <param name="color">Text color</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    public void RenderText(string text, SKPoint location, SKColor color, SKColor? backgroundColor = null)
    {
        var font = GetCurrentFont();
        using var renderedBitmap = font.RenderText(text, color, backgroundColor);

        if (renderedBitmap != null)
            canvas.DrawBitmap(renderedBitmap, (int)location.X, (int)location.Y, fitToCanvas: false);
    }

    /// <summary>
    ///     Gets the size of rendered text
    /// </summary>
    public SKSize GetTextSize(string text)
    {
        var font = GetCurrentFont();
        return font.MeasureText(text);
    }

    /// <summary>
    ///     Creates a scrolling text layer
    ///     IMPORTANT: Dispose previous ScrollText before creating a new one to avoid memory leaks
    /// </summary>
    public ScrollText GetScrollTextLayer()
    {
        // Dispose previous scroll text if exists
        _currentScrollText?.Dispose();

        var font = GetCurrentFont();
        _currentScrollText = new ScrollText(font, canvas);
        return _currentScrollText;
    }

    /// <summary>
    ///     Disposes resources used by the font manager
    /// </summary>
    public void Dispose()
    {
        _currentScrollText?.Dispose();
        _currentScrollText = null;
    }
}