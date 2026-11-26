using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     What an extension sees of its canvas: size, visibility and layering, audio, frame statistics, and
///     <see cref="SubmitCompletedFrame"/> to hand over a finished frame. Extensions draw into their own buffer
///     with SkiaSharp (see <see cref="FrameExtension"/>); the immediate-mode drawing helpers of the concrete
///     canvas are not part of this contract. <see cref="DrawBitmap(SKBitmap,int,int,float,float,bool)"/> and
///     the BDF members remain for the BDF text helpers.
/// </summary>
public interface ICanvas
{
    int Width { get; }

    int Height { get; }

    bool IsHidden { get; }

    /// <summary>
    ///     Gets or sets the brightness level for this canvas (0.0 = black, 1.0 = full brightness)
    ///     This is applied to this canvas only, independent of global CanvasManager brightness
    /// </summary>
    float Brightness { get; set; }

    /// <summary>
    ///     Gets or sets the opacity level for this canvas (0.0 = invisible, 1.0 = fully visible)
    ///     This is applied during canvas compositing for layered canvas support
    /// </summary>
    float Opacity { get; set; }

    /// <summary>
    ///     Preferred panel colour depth when this canvas is visible on a network LED wall: 8 (triple-buffer,
    ///     high fps) or 14 (double-buffer, video quality). Default 14. HDMI / SPI / GPIO / simulation ignore
    ///     this; the wall uses the maximum of all visible canvases.
    /// </summary>
    int PanelColorBits { get; set; }

    /// <summary>
    ///     Gets or sets the Z-order of this canvas (lower values drawn first, higher values on top)
    ///     Changes to ZOrder should be coordinated with CanvasManager to maintain proper layering
    /// </summary>
    int ZOrder { get; set; }

    /// <summary>
    ///     Optional sound output for the extension on this canvas. Null when the host has no audio board.
    ///     Calls are safe from the extension thread. The host mixes them; the extension does not open a device.
    /// </summary>
    IExtensionAudio? Audio { get; }

    /// <summary>
    ///     Frame timing of this canvas (fps, render time). Pass it to <see cref="ExtensionLoop"/> so render
    ///     times are measured. Null when the host does not track it.
    /// </summary>
    FrameStats? Stats => null;

    void Show();
    void Hide();
    void Clear();
    void Clear(SKColor color);

    void DrawBitmap(SKBitmap bitmap, int xPos, int yPos, int width, int height, float rotateDegrees = 0,
        float scale = 0);

    SKCanvas DrawBitmap(SKBitmap bitmap, int xPos, int yPos, float rotateDegrees = 0, float scale = 0,
        bool fitToCanvas = false);

    /// <summary>
    ///     Draws text using a BDF (Bitmap Distribution Format) font
    ///     Perfect for pixel-perfect text on LED matrices
    /// </summary>
    /// <param name="text">Text to draw</param>
    /// <param name="xPos">X position</param>
    /// <param name="yPos">Y position</param>
    /// <param name="color">Text color</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    void DrawBdfText(string text, int xPos, int yPos, SKColor color, string? fontName = null,
        SKColor? backgroundColor = null);

    /// <summary>
    ///     Measures the size of text rendered with a BDF font
    /// </summary>
    /// <param name="text">Text to measure</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <returns>Size of the rendered text</returns>
    SKSize MeasureBdfText(string text, string? fontName = null);

    /// <summary>
    ///     Renders BDF text to a bitmap (for scrolling, animations, caching, etc.)
    ///     Returns a pre-rendered bitmap that can be used for scrolling without flickering
    /// </summary>
    /// <param name="text">Text to render</param>
    /// <param name="color">Text color</param>
    /// <param name="fontName">BDF font name (null = default font)</param>
    /// <param name="backgroundColor">Background color (null = transparent)</param>
    /// <returns>Rendered text as bitmap, or null if rendering fails</returns>
    SKBitmap? RenderBdfTextToBitmap(string text, SKColor color, string? fontName = null,
        SKColor? backgroundColor = null);

    /// <summary>
    ///     Registered BDF font whose glyph height best fits <paramref name="targetHeight"/> (largest
    ///     that is not taller, or the smallest available). Null if no size-named fonts are loaded.
    /// </summary>
    string? GetBestBdfFontForHeight(int targetHeight);

    /// <summary>
    ///     Allows extensions to signal they want to update the canvas with a complete frame.
    ///     This ensures atomic updates - the entire frame is written before CanvasManager reads it.
    /// </summary>
    /// <param name="completedFrame">The completed frame to copy to canvas background</param>
    void SubmitCompletedFrame(SKBitmap completedFrame);
}