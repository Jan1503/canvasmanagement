using SkiaSharp;

namespace CanvasManagement.Extension.AdvertisingDisplay;

/// <summary>Shared colour/easing helpers and small value types used by the advertising display.</summary>
internal static class SignageFx
{
    /// <summary>Nearest-neighbour sampling keeps bitmap-font glyphs crisp when scaled/moved.</summary>
    public static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    /// <summary>7-colour rainbow palette (wraps) for sweep gradients.</summary>
    public static readonly SKColor[] RainbowPalette =
    {
        new(255, 0, 0), new(255, 127, 0), new(255, 255, 0), new(0, 255, 0),
        new(0, 180, 255), new(75, 0, 255), new(200, 0, 255), new(255, 0, 0)
    };

    /// <summary>HSV (full saturation/value) to RGB for animated colour cycling.</summary>
    public static SKColor Hue(float hueDegrees)
    {
        var h = (hueDegrees % 360f + 360f) % 360f / 60f;
        var i = (int)h;
        var f = h - i;
        var q = (byte)(255 * (1 - f));
        var t = (byte)(255 * f);
        return i switch
        {
            0 => new SKColor(255, t, 0),
            1 => new SKColor(q, 255, 0),
            2 => new SKColor(0, 255, t),
            3 => new SKColor(0, q, 255),
            4 => new SKColor(t, 0, 255),
            _ => new SKColor(255, 0, q)
        };
    }

    public static float EaseOutCubic(float t)
    {
        return 1 - (float)Math.Pow(1 - t, 3);
    }

    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1;
        return 1 + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2);
    }

    public static float EaseOutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (t < 1 / d1) return n1 * t * t;
        if (t < 2 / d1) return n1 * (t -= 1.5f / d1) * t + 0.75f;
        if (t < 2.5 / d1) return n1 * (t -= 2.25f / d1) * t + 0.9375f;
        return n1 * (t -= 2.625f / d1) * t + 0.984375f;
    }
}

/// <summary>A per-character slice of the rendered text mask, with random fly-in direction/phase.</summary>
internal struct CharCell
{
    public int X;
    public int W;
    public float DirX;
    public float DirY;
    public float Phase;
    /// <summary>
    ///     True when the cell contains lit pixels (i.e. it's a real glyph). False for whitespace
    ///     characters, which advance the cursor but render nothing. Narrative effects use this to
    ///     skip acting on invisible cells (no stick figure carrying a space, no pac-man eating it,
    ///     etc.).
    /// </summary>
    public bool HasContent;
}

/// <summary>A short-lived twinkle particle.</summary>
internal struct SparkleParticle
{
    public float X;
    public float Y;
    public float Life;
    public float Max;
    public SKColor Color;
}

/// <summary>Computed per-character animation transform for one frame.</summary>
internal struct GlyphAnim
{
    public float Dx;
    public float Dy;
    public float Scale;
    public float ScaleX;
    public float ScaleY;
    public float Rotation;
    public float Alpha;
    public float Reveal;
    public int SlotIndex;

    public static GlyphAnim Default => new()
    {
        Scale = 1, ScaleX = 1, ScaleY = 1, Alpha = 1, Reveal = 1, SlotIndex = -1
    };
}
