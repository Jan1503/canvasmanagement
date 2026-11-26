using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.ScrollTextPlayer;

[ExtensionInfo("Scrolling Text",
    "Smooth scrolling text display with pixel-perfect BDF fonts or Skia rendering",
    "Text & Display",
    IconResourceName = "scrolltext.svg")]
public class ScrollTextPlayerExtension : FrameExtension
{
    private float _currentX;
    private bool _isInEndPause;
    private bool _isInStartDelay;

    private bool _isReversed;
    private bool _lastAntiAlias = true;
    private string _lastBdfFont = "";
    private bool _lastBold;
    private int _lastDirection;
    private string _lastFontFamily = "Arial";
    private int _lastFontSize;
    private bool _lastItalic;
    private int _lastLoopMode;
    private bool _lastOutline;
    private SKColor _lastOutlineColor = SKColors.Black;
    private int _lastOutlineWidth = 2;
    private bool _lastShadow;
    private SKColor _lastShadowColor = new(0, 0, 0, 128);
    private int _lastShadowOffsetX = 3;
    private int _lastShadowOffsetY = 3;

    // Track property changes for hot reload
    private string _lastText = "Hello World!";
    private SKColor _lastTextColor = SKColors.White;
    private bool _lastUseBdfFonts = true;
    private int _lastVerticalAlignment = 1;
    private int _loopCount;
    private DateTime _pauseStartTime;
    private bool _paused;
    private SKBitmap? _textBitmap;

    internal ScrollTextPlayerExtension(ICanvas canvas) : base(canvas)
    {
    }

    public override string Name => "Scrolling Text";

    protected override double FrameIntervalMs => 1000.0 / Math.Clamp(FrameRate, 1, 60);

    protected override bool ClearOnStop => false;

    private ScrollDirection CurrentDirection => (ScrollDirection)Direction;
    private LoopBehavior CurrentLoopMode => (LoopBehavior)LoopMode;
    private VerticalAlign CurrentVerticalAlignment => (VerticalAlign)VerticalAlignment;

    protected override void OnStart()
    {
        _paused = false;
        _textBitmap?.Dispose();
        _textBitmap = null; // prepared for the canvas size in OnResize, before the first frame
    }

    protected override void OnResize(int width, int height) => PrepareText();

    protected override void OnStop()
    {
        _textBitmap?.Dispose();
        _textBitmap = null;
        try { Canvas.Clear(BackgroundColor); } catch { /* canvas already gone */ }
        Console.WriteLine("ScrollText stopped");
    }

    /// <summary>Renders the text (BDF, falling back to Skia) and starts the scroll from the beginning.</summary>
    private void PrepareText()
    {
        try
        {
            if (UseBdfFonts) PrepareBdfTextBitmap();
            else PrepareTextBitmap();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error preparing ScrollText: {ex.Message}");
            _textBitmap?.Dispose();
            _textBitmap = null;
            if (UseBdfFonts)
            {
                Console.WriteLine("Falling back to Skia rendering...");
                try
                {
                    UseBdfFonts = false;
                    PrepareTextBitmap();
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"Fallback to Skia also failed: {fallbackEx.Message}");
                    _textBitmap?.Dispose();
                    _textBitmap = null;
                }
            }
        }

        UpdateLastValues();
        _currentX = GetStartPosition();
        _isReversed = false;
        _loopCount = 0;
        _isInStartDelay = StartDelay > 0;
        _isInEndPause = false;
        _pauseStartTime = DateTime.Now;
        if (_textBitmap != null)
            Console.WriteLine($"ScrollText started: '{Text}' ({_textBitmap.Width}x{_textBitmap.Height}, " +
                              $"{(UseBdfFonts ? "BDF" : "Skia")}) at {FrameRate}fps");
    }

    private void PrepareBdfTextBitmap()
    {
        lock (Sync)
        {
            _textBitmap?.Dispose();

            var targetH = FontSize > 0 ? FontSize : Math.Max(8f, Canvas.Height * 0.8f);
            var fontName = !string.IsNullOrWhiteSpace(BdfFont)
                ? BdfFont
                : Canvas.GetBestBdfFontForHeight(Math.Max(5, (int)Math.Round(targetH)));

            var baseBitmap = Canvas.RenderBdfTextToBitmap(Text, TextColor, fontName);

            if (baseBitmap == null || baseBitmap.Width == 0 || baseBitmap.Height == 0)
                throw new InvalidOperationException("BDF rendering returned empty bitmap");

            var (dw, dh) = CanvasText.DestSize(baseBitmap, targetH);
            var destW = Math.Max(1, (int)Math.Ceiling(dw));
            var destH = Math.Max(1, (int)Math.Ceiling(dh));

            // Handle vertical alignment by creating a canvas-height bitmap
            _textBitmap = new SKBitmap(destW, Canvas.Height);
            using var canvas = new SKCanvas(_textBitmap);
            canvas.Clear(SKColors.Transparent);

            var yPos = CurrentVerticalAlignment switch
            {
                VerticalAlign.Top => 0,
                VerticalAlign.Center => (Canvas.Height - destH) / 2,
                VerticalAlign.Bottom => Canvas.Height - destH,
                _ => (Canvas.Height - destH) / 2
            };

            CanvasText.Blit(canvas, baseBitmap, 0, yPos, dw, dh);

            baseBitmap.Dispose();

            Console.WriteLine($"[BDF] Pre-rendered text bitmap: {_textBitmap.Width}x{_textBitmap.Height}");
        }
    }

    /// <summary>Freezes the text where it is.</summary>
    public void Pause()
    {
        lock (Sync) _paused = true;
    }

    public void Resume()
    {
        lock (Sync) _paused = false;
    }

    private bool HasVisualPropertiesChanged()
    {
        return Text != _lastText ||
               UseBdfFonts != _lastUseBdfFonts ||
               BdfFont != _lastBdfFont ||
               FontSize != _lastFontSize ||
               FontFamily != _lastFontFamily ||
               Bold != _lastBold ||
               Italic != _lastItalic ||
               TextColor != _lastTextColor ||
               Direction != _lastDirection ||
               VerticalAlignment != _lastVerticalAlignment ||
               Outline != _lastOutline ||
               OutlineWidth != _lastOutlineWidth ||
               OutlineColor != _lastOutlineColor ||
               Shadow != _lastShadow ||
               ShadowOffsetX != _lastShadowOffsetX ||
               ShadowOffsetY != _lastShadowOffsetY ||
               ShadowColor != _lastShadowColor ||
               AntiAlias != _lastAntiAlias;
    }

    private void UpdateLastValues()
    {
        _lastText = Text;
        _lastUseBdfFonts = UseBdfFonts;
        _lastBdfFont = BdfFont;
        _lastFontSize = FontSize;
        _lastFontFamily = FontFamily;
        _lastBold = Bold;
        _lastItalic = Italic;
        _lastTextColor = TextColor;
        _lastDirection = Direction;
        _lastLoopMode = LoopMode;
        _lastVerticalAlignment = VerticalAlignment;
        _lastOutline = Outline;
        _lastOutlineWidth = OutlineWidth;
        _lastOutlineColor = OutlineColor;
        _lastShadow = Shadow;
        _lastShadowOffsetX = ShadowOffsetX;
        _lastShadowOffsetY = ShadowOffsetY;
        _lastShadowColor = ShadowColor;
        _lastAntiAlias = AntiAlias;
    }

    // Skia rendering methods
    private void PrepareTextBitmap()
    {
        if (string.IsNullOrWhiteSpace(Text)) Text = "Scrolling Text";

        var fontSize = FontSize > 0 ? FontSize : Canvas.Height * 0.8f;

        var fontStyle = GetFontStyle();

        // Use modern SKFont for text rendering
        using var font = new SKFont
        {
            Size = fontSize,
            Typeface = SKTypeface.FromFamilyName(FontFamily, fontStyle)
        };

        using var paint = new SKPaint
        {
            Color = TextColor,
            IsAntialias = AntiAlias // Respects user setting for LED displays
        };

        var textBounds = new SKRect();
        font.MeasureText(Text, out textBounds);

        while (textBounds.Height > Canvas.Height && font.Size > 10)
        {
            font.Size *= 0.95f;
            font.MeasureText(Text, out textBounds);
        }

        var bitmapWidth = (int)Math.Ceiling(textBounds.Width) + Math.Abs((int)textBounds.Left) + 20;
        var bitmapHeight = Canvas.Height;

        lock (Sync)
        {
            _textBitmap?.Dispose();
            _textBitmap = new SKBitmap(bitmapWidth, bitmapHeight);

            using var canvas = new SKCanvas(_textBitmap);
            canvas.Clear(SKColors.Transparent);

            var yPos = CurrentVerticalAlignment switch
            {
                VerticalAlign.Top => -textBounds.Top + 5,
                VerticalAlign.Center => (bitmapHeight - textBounds.Height) / 2 - textBounds.Top,
                VerticalAlign.Bottom => bitmapHeight - textBounds.Height - textBounds.Top - 5,
                _ => (bitmapHeight - textBounds.Height) / 2 - textBounds.Top
            };

            var xPos = -textBounds.Left + 10;

            if (Shadow)
            {
                using var shadowPaint = new SKPaint
                {
                    Color = ShadowColor,
                    IsAntialias = AntiAlias
                };
                canvas.DrawText(Text, xPos + ShadowOffsetX, yPos + ShadowOffsetY, SKTextAlign.Left, font, shadowPaint);
            }

            if (Outline)
            {
                using var outlinePaint = new SKPaint
                {
                    Color = OutlineColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = OutlineWidth,
                    IsAntialias = AntiAlias
                };
                canvas.DrawText(Text, xPos, yPos, SKTextAlign.Left, font, outlinePaint);
            }

            canvas.DrawText(Text, xPos, yPos, SKTextAlign.Left, font, paint);
        }
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        if (HasVisualPropertiesChanged())
        {
            Console.WriteLine("Properties changed - re-rendering the text");
            PrepareText();
        }

        if (!_paused) Advance();
        DrawFrame(canvas);
    }

    private void Advance()
    {
        if (_textBitmap == null) return;

        if (_isInStartDelay)
        {
            if ((DateTime.Now - _pauseStartTime).TotalSeconds < StartDelay) return;
            _isInStartDelay = false;
        }

        if (_isInEndPause)
        {
            if ((DateTime.Now - _pauseStartTime).TotalSeconds < EndPause) return;
            _isInEndPause = false;

            if (CurrentLoopMode == LoopBehavior.Once)
            {
                // Stop waits for this frame to end, so it runs on another thread.
                _ = Task.Run(Stop);
                return;
            }

            if (CurrentLoopMode == LoopBehavior.Bounce) _isReversed = !_isReversed;

            _currentX = GetStartPosition();
            _loopCount++;
        }

        float movement = ScrollSpeed * (_isReversed ? -1 : 1);
        if (CurrentDirection == ScrollDirection.Left)
            _currentX -= movement;
        else
            _currentX += movement;

        if (HasReachedEnd())
        {
            if (CurrentLoopMode == LoopBehavior.Infinite)
            {
                _currentX = GetStartPosition();
            }
            else
            {
                _isInEndPause = true;
                _pauseStartTime = DateTime.Now;
            }
        }
    }

    private void DrawFrame(SKCanvas canvas)
    {
        canvas.Clear(BackgroundColor);
        if (_textBitmap != null) canvas.DrawBitmap(_textBitmap, (int)_currentX, 0);
    }

    private float GetStartPosition()
    {
        if (_textBitmap == null) return 0;

        return CurrentDirection switch
        {
            ScrollDirection.Left => Canvas.Width,
            ScrollDirection.Right => -_textBitmap.Width,
            _ => 0
        };
    }

    private bool HasReachedEnd()
    {
        if (_textBitmap == null) return false;

        return CurrentDirection switch
        {
            ScrollDirection.Left => _currentX <= -_textBitmap.Width,
            ScrollDirection.Right => _currentX >= Canvas.Width,
            _ => false
        };
    }

    private SKFontStyle GetFontStyle()
    {
        var weight = Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        return new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
    }

    ~ScrollTextPlayerExtension()
    {
        Dispose();
    }

    #region Parameters

    [ExtensionParameter("Text", "Text to display",
        DefaultValue = "Hello World!")]
    public string Text { get; set; } = "Hello World!";

    [ExtensionParameter("Use BDF Fonts", "Use pixel-perfect BDF fonts (recommended for LED matrices)",
        DefaultValue = true)]
    public bool UseBdfFonts { get; set; } = true;

    [ExtensionParameter("BDF Font",
        "BDF font name (leave empty for auto-selection, only used if 'Use BDF Fonts' is enabled)",
        DefaultValue = "")]
    public string BdfFont { get; set; } = "";

    [ExtensionParameter("Font Size", "Font size in pixels (0 = auto-fit)",
        DefaultValue = 0, MinValue = 0, MaxValue = 200)]
    public int FontSize { get; set; } = 0;

    [ExtensionParameter("Font Family", "Font family name (only for Skia rendering)",
        DefaultValue = "Arial")]
    public string FontFamily { get; set; } = "Arial";

    [ExtensionParameter("Bold", "Use bold font (only for Skia rendering)",
        DefaultValue = false)]
    public bool Bold { get; set; } = false;

    [ExtensionParameter("Italic", "Use italic font (only for Skia rendering)",
        DefaultValue = false)]
    public bool Italic { get; set; } = false;

    [ExtensionParameter("Text Color", "Text color",
        DefaultValue = "#FFFFFF")]
    public SKColor TextColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Color", "Background color for the text",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Scroll Speed", "Pixels to move per frame (higher = faster)",
        DefaultValue = 2, MinValue = 1, MaxValue = 20)]
    public int ScrollSpeed { get; set; } = 2;

    [ExtensionParameter("Frame Rate", "Animation frame rate (FPS)",
        DefaultValue = 30, MinValue = 10, MaxValue = 60)]
    public int FrameRate { get; set; } = 30;

    [ExtensionParameter("Direction", "Scroll direction (0=Left, 1=Right)",
        DefaultValue = 0, MinValue = 0, MaxValue = 1)]
    public int Direction { get; set; } = 0;

    [ExtensionParameter("Loop Mode", "How to loop (0=Infinite, 1=Once, 2=Bounce)",
        DefaultValue = 0, MinValue = 0, MaxValue = 2)]
    public int LoopMode { get; set; } = 0;

    [ExtensionParameter("Start Delay", "Delay before scrolling starts (seconds)",
        DefaultValue = 0, MinValue = 0, MaxValue = 10)]
    public int StartDelay { get; set; } = 0;

    [ExtensionParameter("End Pause", "Pause at end before looping (seconds)",
        DefaultValue = 1, MinValue = 0, MaxValue = 10)]
    public int EndPause { get; set; } = 1;

    [ExtensionParameter("Outline", "Draw text outline (only for Skia rendering)",
        DefaultValue = false)]
    public bool Outline { get; set; } = false;

    [ExtensionParameter("Outline Width", "Outline width in pixels (only for Skia rendering)",
        DefaultValue = 2, MinValue = 1, MaxValue = 10)]
    public int OutlineWidth { get; set; } = 2;

    [ExtensionParameter("Outline Color", "Outline color (only for Skia rendering)",
        DefaultValue = "#000000")]
    public SKColor OutlineColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Shadow", "Draw drop shadow (only for Skia rendering)",
        DefaultValue = false)]
    public bool Shadow { get; set; } = false;

    [ExtensionParameter("Shadow Offset X", "Shadow horizontal offset (only for Skia rendering)",
        DefaultValue = 3, MinValue = -10, MaxValue = 10)]
    public int ShadowOffsetX { get; set; } = 3;

    [ExtensionParameter("Shadow Offset Y", "Shadow vertical offset (only for Skia rendering)",
        DefaultValue = 3, MinValue = -10, MaxValue = 10)]
    public int ShadowOffsetY { get; set; } = 3;

    [ExtensionParameter("Shadow Color", "Shadow color (only for Skia rendering)",
        DefaultValue = "#80000000")]
    public SKColor ShadowColor { get; set; } = new(0, 0, 0, 128);

    [ExtensionParameter("Vertical Alignment", "Vertical text position (0=Top, 1=Center, 2=Bottom)",
        DefaultValue = 1, MinValue = 0, MaxValue = 2)]
    public int VerticalAlignment { get; set; } = 1;

    [ExtensionParameter("Anti-Alias", "Enable text anti-aliasing (only for Skia rendering)",
        DefaultValue = false)]
    public bool AntiAlias { get; set; } = false;

    #endregion
}

public enum ScrollDirection
{
    Left = 0,
    Right = 1
}

public enum LoopBehavior
{
    Infinite = 0,
    Once = 1,
    Bounce = 2
}

public enum VerticalAlign
{
    Top = 0,
    Center = 1,
    Bottom = 2
}