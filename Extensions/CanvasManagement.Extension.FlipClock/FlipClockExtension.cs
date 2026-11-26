using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.FlipClock;

[ExtensionInfo("Flip Clock",
    "Retro-style flip clock with animated number transitions",
    "Clocks",
    IconResourceName = "flipclock.svg")]
public class FlipClockExtension : FrameExtension
{
    // Track each digit separately for individual animations
    private readonly DigitState[] _digits = new DigitState[6]; // HH:MM:SS = 6 digits

    internal FlipClockExtension(ICanvas canvas) : base(canvas)
    {
        // Initialize digit states
        for (var i = 0; i < 6; i++) _digits[i] = new DigitState();
    }

    public override string Name => "Flip Clock";

    protected override double FrameIntervalMs => 1000.0 / 60;

    protected override bool ClearOnStop => false;

    protected override void OnStart()
    {
        // Initialize time
        var now = DateTime.Now;
        var hours = TwentyFourHour ? now.Hour : now.Hour % 12 == 0 ? 12 : now.Hour % 12;
        var minutes = now.Minute;
        var seconds = now.Second;

        // Initialize all digits
        _digits[0].CurrentValue = hours / 10;
        _digits[1].CurrentValue = hours % 10;
        _digits[2].CurrentValue = minutes / 10;
        _digits[3].CurrentValue = minutes % 10;
        _digits[4].CurrentValue = seconds / 10;
        _digits[5].CurrentValue = seconds % 10;

        Console.WriteLine("Flip Clock started");
    }

    protected override void OnStop()
    {
        try { Canvas.Clear(BackgroundColor); } catch { /* canvas already gone */ }

        Console.WriteLine("Flip Clock stopped");
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        var now = DateTime.Now;
        var hours = TwentyFourHour ? now.Hour : now.Hour % 12 == 0 ? 12 : now.Hour % 12;
        var minutes = now.Minute;
        var seconds = now.Second;

        // Check each digit individually and start animation if changed
        int[] currentValues =
        {
            hours / 10, hours % 10,
            minutes / 10, minutes % 10,
            seconds / 10, seconds % 10
        };

        for (var i = 0; i < 6; i++)
        {
            if (_digits[i].CurrentValue != currentValues[i])
            {
                _digits[i].PreviousValue = _digits[i].CurrentValue;
                _digits[i].CurrentValue = currentValues[i];
                _digits[i].FlipProgress = 0;
            }

            // Update animation progress
            if (_digits[i].FlipProgress < 1)
                _digits[i].FlipProgress = Math.Min(1, _digits[i].FlipProgress + FlipSpeed / 500f);
        }

        Draw(canvas);
    }

    private void Draw(SKCanvas canvas)
    {
        // Clear with background color
        canvas.Clear(BackgroundColor);

        // Card size: as big as the height allows, then shrunk until the whole row fits the width with a
        // small margin (a fixed minimum spacing used to push the outer digits off a 256-wide panel).
        var numDigits = ShowSeconds ? 6 : 4;
        var numSeparators = ShowSeconds ? 2 : 1;
        var cardWidth = (int)(Canvas.Height * 0.6f) / 2;
        int spacing, separatorWidth, totalWidth;
        while (true)
        {
            spacing = Math.Max(2, cardWidth / 6);
            separatorWidth = ShowSeparators ? spacing * 2 : 0;
            totalWidth = numDigits * cardWidth + (numDigits - 1) * spacing + numSeparators * separatorWidth;
            if (totalWidth <= Canvas.Width - 4 || cardWidth <= 4) break;
            cardWidth--;
        }

        var cardHeight = (int)(cardWidth * 1.6f);

        var startX = (Canvas.Width - totalWidth) / 2;
        var startY = (Canvas.Height - cardHeight) / 2;

        var currentX = startX;

        // Draw each digit
        for (var i = 0; i < (ShowSeconds ? 6 : 4); i++)
        {
            DrawFlipCard(canvas, _digits[i], currentX, startY, cardWidth, cardHeight);
            currentX += cardWidth + spacing;

            // Draw separators after 2nd and 4th digit
            if (ShowSeparators && (i == 1 || i == 3))
            {
                DrawSeparator(canvas, currentX + spacing / 2, startY, cardHeight);
                currentX += spacing * 2;
            }
        }
    }

    private void DrawFlipCard(SKCanvas canvas, DigitState digit, int x, int y, int width, int height)
    {
        if (CardShadow)
        {
            using var shadowPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 100),
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };
            canvas.DrawRoundRect(x + 3, y + 3, width, height, 6, 6, shadowPaint);
        }

        var radius = Math.Max(2f, width * 0.08f);
        using var cardPaint = new SKPaint
        {
            Color = CardColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };
        canvas.DrawRoundRect(x, y, width, height, radius, radius, cardPaint);

        var p = Math.Clamp(digit.FlipProgress, 0f, 1f);
        var flipping = p < 0.999f;
        var oldDigit = flipping ? digit.PreviousValue : digit.CurrentValue;
        var newDigit = digit.CurrentValue;
        var mid = y + height / 2f;

        // Split-flap: the back of the top half is always the incoming digit; the back of the
        // bottom half is always the outgoing digit. The moving flap is the top of OLD folding
        // down (0–50 %), then the bottom of NEW unfolding (50–100 %).
        DrawDigitHalf(canvas, newDigit, x, y, width, height, true, 0);
        DrawDigitHalf(canvas, oldDigit, x, y, width, height, false, 0);

        if (flipping && p < 0.5f)
        {
            var scaleY = Math.Abs((float)Math.Cos(p * Math.PI));
            DrawFlap(canvas, oldDigit, x, y, width, height, true, scaleY, p * 2f);
        }
        else if (flipping)
        {
            var t = (p - 0.5f) * 2f;
            var scaleY = Math.Abs((float)Math.Sin(t * Math.PI / 2f));
            DrawFlap(canvas, newDigit, x, y, width, height, false, scaleY, t);
        }

        using var hinge = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 160),
            StrokeWidth = Math.Max(1f, height * 0.02f),
            IsAntialias = false
        };
        canvas.DrawLine(x + 2, mid, x + width - 2, mid, hinge);

        using var highlight = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 28),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = false
        };
        canvas.DrawRoundRect(x, y, width, height, radius, radius, highlight);
    }

    private void DrawFlap(SKCanvas canvas, int digit, int x, int y, int width, int height, bool isTop,
        float scaleY, float darkness)
    {
        scaleY = Math.Max(0.02f, scaleY);
        var pivotY = y + height / 2f;
        canvas.Save();
        if (isTop)
            canvas.ClipRect(new SKRect(x, y, x + width, pivotY));
        else
            canvas.ClipRect(new SKRect(x, pivotY, x + width, y + height));

        canvas.Translate(0, pivotY);
        canvas.Scale(1, isTop ? scaleY : scaleY);
        canvas.Translate(0, -pivotY);

        var shade = (byte)(255 * (0.45f + (1f - darkness) * 0.55f));
        var face = new SKColor(
            (byte)(CardColor.Red * shade / 255),
            (byte)(CardColor.Green * shade / 255),
            (byte)(CardColor.Blue * shade / 255));
        using var facePaint = new SKPaint { Color = face, Style = SKPaintStyle.Fill, IsAntialias = false };
        canvas.DrawRect(x, y, width, height, facePaint);
        DrawDigitHalf(canvas, digit, x, y, width, height, isTop, 0, shade);
        canvas.Restore();
    }

    private void DrawDigitHalf(SKCanvas canvas, int digit, int x, int y, int width, int height, bool isTopHalf,
        float unusedAngle, byte alpha = 255)
    {
        _ = unusedAngle;
        var mid = y + height / 2f;
        canvas.Save();
        canvas.ClipRect(isTopHalf
            ? new SKRect(x, y, x + width, mid)
            : new SKRect(x, mid, x + width, y + height));

        var color = new SKColor(TextColor.Red, TextColor.Green, TextColor.Blue, alpha);
        var size = CanvasText.ResolveSize(FontSize, height * 0.62f);
        var text = digit.ToString();
        // The hinge runs through the middle of the digit. Skia: baseline below the centre by half the cap
        // height. BDF: the bitmap's bottom sits on the baseline and includes room for descenders, so the
        // baseline goes lower to centre the visible digit.
        var baseline = UseBdfFont ? y + height / 2f + size * 0.55f : y + height * 0.72f;
        CanvasText.Draw(canvas, Canvas, text, color, x + width / 2f, baseline, size,
            SKTextAlign.Center, UseBdfFont);
        canvas.Restore();
    }

    private void DrawSeparator(SKCanvas canvas, int x, int y, int height)
    {
        using var paint = new SKPaint
        {
            Color = TextColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        var dotSize = Math.Max(1, height / 14); // in proportion to the cards (6 px on a 384x192 wall)
        var centerY = y + height / 2;

        canvas.DrawCircle(x, centerY - height / 4, dotSize, paint);
        canvas.DrawCircle(x, centerY + height / 4, dotSize, paint);
    }

    #region Parameters

    [ExtensionParameter("Flip Speed", "Animation speed (higher = faster)",
        DefaultValue = 20, MinValue = 5, MaxValue = 50)]
    public int FlipSpeed { get; set; } = 20;

    [ExtensionParameter("Card Color", "Color of the flip cards",
        DefaultValue = "#1a1a1a")]
    public SKColor CardColor { get; set; } = new(26, 26, 26);

    [ExtensionParameter("Text Color", "Color of the numbers",
        DefaultValue = "#FFFFFF")]
    public SKColor TextColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Color", "Background color for the clock",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Show Seconds", "Display seconds",
        DefaultValue = true)]
    public bool ShowSeconds { get; set; } = true;

    [ExtensionParameter("24 Hour Format", "Use 24-hour time format",
        DefaultValue = true)]
    public bool TwentyFourHour { get; set; } = true;

    [ExtensionParameter("Show Separators", "Show colon separators",
        DefaultValue = true)]
    public bool ShowSeparators { get; set; } = true;

    [ExtensionParameter("Card Shadow", "Add shadow to cards",
        DefaultValue = true)]
    public bool CardShadow { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render digits with the crisp bitmap (BDF) font", DefaultValue = true)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "Digit height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 96, Unit = "px")]
    public int FontSize { get; set; }

    #endregion
}

// Helper class to track each digit's state
public class DigitState
{
    public int CurrentValue { get; set; }
    public int PreviousValue { get; set; }
    public float FlipProgress { get; set; } = 1.0f; // 1.0 = not animating
}
