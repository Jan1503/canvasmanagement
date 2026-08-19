using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.AdvertisingDisplay;

/// <summary>
///     Configuration for a single line (lane) of the advertising display. Each property is an
///     <see cref="ExtensionParameterAttribute" />, so the web GUI renders an editable card for every line
///     automatically (text box, effect dropdown, colour picker, sliders, toggles) - no JSON required.
/// </summary>
public sealed class LineConfig
{
    [ExtensionParameter("Text", "What this line shows", Order = 0)]
    public string Text { get; set; } = "NEW LINE";

    [ExtensionParameter("Effect", "Entrance / animation for this line", Order = 1)]
    public TextEffect Effect { get; set; } = TextEffect.FlyIn;

    [ExtensionParameter("Direction", "Scroll direction (None = static, animated in place)", Order = 2)]
    public ScrollDirection Direction { get; set; } = ScrollDirection.None;

    [ExtensionParameter("Multi-colour", "Cycle rainbow colours instead of a fixed colour", Order = 3)]
    public bool MultiColor { get; set; } = true;

    [ExtensionParameter("Colour", "Fixed text colour (used when Multi-colour is off)", Order = 4)]
    public SKColor Color { get; set; } = SKColors.White;

    [ExtensionParameter("Height weight", "Relative band height vs the other lines", Order = 5,
        MinValue = 0.2, MaxValue = 5.0)]
    public float Weight { get; set; } = 1f;

    [ExtensionParameter("Font size", "Text height in pixels (0 = auto-fit the band)", Order = 6,
        MinValue = 0, MaxValue = 64)]
    public int FontSize { get; set; }

    [ExtensionParameter("Scroll speed", "Pixels per frame when scrolling", Order = 7,
        MinValue = 1, MaxValue = 20)]
    public int Speed { get; set; } = 4;

    [ExtensionParameter("Hold (seconds)", "How long a static line stays before the next message", Order = 8,
        MinValue = 1, MaxValue = 30)]
    public int DurationSeconds { get; set; } = 5;

    [ExtensionParameter("Glow", "Neon glow around the text", Order = 9)]
    public bool Glow { get; set; } = true;

    [ExtensionParameter("Sparkle", "Emit little sparkles", Order = 10)]
    public bool Sparkle { get; set; }

    [ExtensionParameter("Twinkle", "Twinkle the characters", Order = 11)]
    public bool Twinkle { get; set; }

    [ExtensionParameter("Blink", "Blink the whole line on/off", Order = 12)]
    public bool Blink { get; set; }

    [ExtensionParameter("Bitmap font", "Use the crisp bitmap (BDF) font for this line", Order = 13)]
    public bool UseBdfFont { get; set; } = true;
}
