using SkiaSharp;

namespace CanvasManagement.Interfaces;

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

    // ===== BASIC DRAWING OPERATIONS =====

    void MakeTransparent();
    void Show();
    void Hide();
    void Clear();
    void Clear(SKColor color);

    /// <summary>Erases a rectangular region to fully transparent (alpha 0), revealing layers beneath.</summary>
    void ClearRect(int x, int y, int width, int height);

    // ===== PIXEL OPERATIONS =====

    void SetPixel(int x, int y, SKColor color);
    SKColor GetPixel(int x, int y);
    IntPtr GetPixels();

    // ===== BITMAP OPERATIONS =====

    void DrawBitmap(SKBitmap bitmap, int xPos, int yPos, int width, int height, float rotateDegrees = 0,
        float scale = 0);

    SKCanvas DrawBitmap(SKBitmap bitmap, int xPos, int yPos, float rotateDegrees = 0, float scale = 0,
        bool fitToCanvas = false);

    /// <summary>
    ///     Draws a bitmap with alpha blending (transparency support)
    /// </summary>
    void DrawBitmapWithAlpha(SKBitmap bitmap, int xPos, int yPos, byte alpha = 255);

    /// <summary>
    ///     Draws a portion of a bitmap (sprite sheet support)
    /// </summary>
    void DrawBitmapRegion(SKBitmap bitmap, SKRectI sourceRect, SKRect destRect);

    /// <summary>
    ///     Draws a bitmap with tint color
    /// </summary>
    void DrawBitmapTinted(SKBitmap bitmap, int xPos, int yPos, SKColor tintColor);

    // ===== SHAPE DRAWING =====

    void DrawCircle(float xPos, float yPos, float radius, SKColor color);

    /// <summary>
    ///     Draws a filled circle
    /// </summary>
    void DrawFilledCircle(float xPos, float yPos, float radius, SKColor color);

    /// <summary>
    ///     Draws an ellipse
    /// </summary>
    void DrawEllipse(float xPos, float yPos, float radiusX, float radiusY, SKColor color,
        SKPaintStyle style = SKPaintStyle.Stroke);

    void DrawRect(int xPos, int yPos, int width, int height, SKColor color, SKPaintStyle style);

    /// <summary>
    ///     Draws a rounded rectangle
    /// </summary>
    void DrawRoundRect(int xPos, int yPos, int width, int height, float cornerRadius, SKColor color,
        SKPaintStyle style = SKPaintStyle.Stroke);

    /// <summary>
    ///     Draws a polygon
    /// </summary>
    void DrawPolygon(SKPoint[] points, SKColor color, SKPaintStyle style = SKPaintStyle.Stroke);

    /// <summary>
    ///     Draws a triangle
    /// </summary>
    void DrawTriangle(SKPoint p1, SKPoint p2, SKPoint p3, SKColor color, SKPaintStyle style = SKPaintStyle.Stroke);

    void DrawLine(int x1Pos, int y1Pos, int x2Pos, int y2Pos, SKColor color);

    /// <summary>
    ///     Draws a line with specified thickness
    /// </summary>
    void DrawLine(int x1Pos, int y1Pos, int x2Pos, int y2Pos, SKColor color, float strokeWidth);

    /// <summary>
    ///     Draws multiple connected lines (polyline)
    /// </summary>
    void DrawPolyline(SKPoint[] points, SKColor color, float strokeWidth = 1);

    /// <summary>
    ///     Draws an arc
    /// </summary>
    void DrawArc(float xPos, float yPos, float radius, float startAngle, float sweepAngle, SKColor color,
        float strokeWidth = 1);

    // ===== TEXT OPERATIONS =====

    void DrawText(string text, int xPos, int yPos, int width, int height, SKPaint paintStyle, bool centered = false);

    /// <summary>
    ///     Draws text with custom font and size
    /// </summary>
    void DrawText(string text, int xPos, int yPos, SKColor color, float fontSize = 12, string fontFamily = "Arial");

    /// <summary>
    ///     Draws text with alignment options
    /// </summary>
    void DrawTextAligned(string text, int xPos, int yPos, int width, int height, SKColor color,
        float fontSize = 12, SKTextAlign alignment = SKTextAlign.Left, string fontFamily = "Arial");

    /// <summary>
    ///     Measures text dimensions
    /// </summary>
    SKRect MeasureText(string text, float fontSize, string fontFamily = "Arial");

    // ===== GRADIENT AND EFFECTS =====

    /// <summary>
    ///     Fills a rectangle with a linear gradient
    /// </summary>
    void FillGradient(int xPos, int yPos, int width, int height, SKColor startColor, SKColor endColor,
        bool horizontal = true);

    /// <summary>
    ///     Fills a rectangle with a radial gradient
    /// </summary>
    void FillRadialGradient(float centerX, float centerY, float radius, SKColor centerColor, SKColor edgeColor);

    /// <summary>
    ///     Draws with a drop shadow effect
    /// </summary>
    void DrawWithShadow(Action drawAction, float offsetX, float offsetY, float blurRadius, SKColor shadowColor);

    // ===== PATH OPERATIONS =====

    /// <summary>
    ///     Draws a Bezier curve
    /// </summary>
    void DrawBezier(SKPoint start, SKPoint control1, SKPoint control2, SKPoint end, SKColor color,
        float strokeWidth = 1);

    /// <summary>
    ///     Draws a custom path
    /// </summary>
    void DrawPath(SKPath path, SKColor color, SKPaintStyle style = SKPaintStyle.Stroke, float strokeWidth = 1);

    // ===== TRANSFORMATION OPERATIONS =====

    void Scale(float x, float y);
    void Rotate(float degrees);

    /// <summary>
    ///     Translates (moves) the drawing origin
    /// </summary>
    void Translate(float dx, float dy);

    /// <summary>
    ///     Saves the current transformation state
    /// </summary>
    int SaveTransform();

    /// <summary>
    ///     Restores a saved transformation state
    /// </summary>
    void RestoreTransform(int saveCount);

    /// <summary>
    ///     Resets all transformations
    /// </summary>
    void ResetTransform();

    // ===== IMAGE MANIPULATION =====

    /// <summary>
    ///     Applies a color filter to the entire canvas
    /// </summary>
    void ApplyColorFilter(SKColorFilter filter);

    /// <summary>
    ///     Inverts colors on the canvas
    /// </summary>
    void InvertColors();

    /// <summary>
    ///     Converts canvas to grayscale
    /// </summary>
    void Grayscale();

    /// <summary>
    ///     Adjusts brightness (-1.0 to 1.0)
    /// </summary>
    void AdjustBrightness(float amount);

    /// <summary>
    ///     Adjusts contrast (0.0 to 2.0, 1.0 = normal)
    /// </summary>
    void AdjustContrast(float amount);

    /// <summary>
    ///     Applies a blur effect
    /// </summary>
    void ApplyBlur(float sigma);

    // ===== SPECIAL DRAWING =====

    void DrawPicture(SKPicture picture, float xPos, float yPos);

    Task DrawAnalogClock(int xPos, int yPos, int radius, SKColor circleColor, SKColor quarterMarkColor,
        SKColor hourHandColor, SKColor minuteHandColor, SKColor secondHandColor, CancellationToken ct = default);

    /// <summary>
    ///     Draws a grid pattern
    /// </summary>
    void DrawGrid(int cellWidth, int cellHeight, SKColor color, float strokeWidth = 1);

    /// <summary>
    ///     Fills the entire canvas with a pattern
    /// </summary>
    void FillPattern(SKBitmap patternBitmap, SKShaderTileMode tileMode = SKShaderTileMode.Repeat);

    /// <summary>
    ///     Clips drawing to a rectangular region
    /// </summary>
    void ClipRect(int xPos, int yPos, int width, int height);

    /// <summary>
    ///     Clips drawing to a circular region
    /// </summary>
    void ClipCircle(float xPos, float yPos, float radius);

    /// <summary>
    ///     Removes clipping region
    /// </summary>
    void ResetClip();

    // ===== BDF FONT OPERATIONS =====

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