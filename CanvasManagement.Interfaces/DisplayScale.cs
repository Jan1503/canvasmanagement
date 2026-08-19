namespace CanvasManagement.Interfaces;

/// <summary>
///     Helpers for writing resolution-independent extensions and effects.
///
///     Most content in this framework was originally authored for a 384x192 panel. These helpers
///     convert those "design" pixel values to the actual panel size so the same code renders
///     proportionally on any resolution (e.g. 128x64) at native resolution - no upscaling.
///
///     Usage in an extension/effect:
///     <code>
///         var fontSize = canvas.ScaleSize(24);      // 24px on a 384x192 panel, ~8px on 128x64
///         var cell     = canvas.ScaleSize(20);      // grid cell that shrinks with the panel
///         var margin   = canvas.ScaleSize(10);
///     </code>
/// </summary>
public static class DisplayScale
{
    /// <summary>
    ///     Reference width the framework's content was designed against.
    /// </summary>
    public const float ReferenceWidth = 384f;

    /// <summary>
    ///     Reference height the framework's content was designed against.
    /// </summary>
    public const float ReferenceHeight = 192f;

    /// <summary>
    ///     Uniform scale factor (relative to the 384x192 reference) that preserves aspect ratio
    ///     by fitting to the more constrained axis. 1.0 on a 384x192 panel, 0.333 on 128x64.
    /// </summary>
    public static float GetScale(int width, int height)
    {
        if (width <= 0 || height <= 0) return 1f;
        return Math.Min(width / ReferenceWidth, height / ReferenceHeight);
    }

    /// <summary>
    ///     Uniform scale factor for the given canvas (see <see cref="GetScale" />).
    /// </summary>
    public static float Scale(this ICanvas canvas)
    {
        return GetScale(canvas.Width, canvas.Height);
    }

    /// <summary>
    ///     Horizontal-only scale factor (width / 384). Use for purely horizontal metrics.
    /// </summary>
    public static float ScaleX(this ICanvas canvas)
    {
        return canvas.Width <= 0 ? 1f : canvas.Width / ReferenceWidth;
    }

    /// <summary>
    ///     Vertical-only scale factor (height / 192). Use for purely vertical metrics.
    /// </summary>
    public static float ScaleY(this ICanvas canvas)
    {
        return canvas.Height <= 0 ? 1f : canvas.Height / ReferenceHeight;
    }

    /// <summary>
    ///     Converts a design pixel value (authored at 384x192) to an integer size for this canvas,
    ///     clamped to a minimum of 1px so elements never disappear entirely.
    /// </summary>
    public static int ScaleSize(this ICanvas canvas, float designValue)
    {
        return Math.Max(1, (int)MathF.Round(designValue * canvas.Scale()));
    }

    /// <summary>
    ///     Converts a design pixel value to a float size for this canvas (no rounding/clamping).
    /// </summary>
    public static float ScaleSizeF(this ICanvas canvas, float designValue)
    {
        return designValue * canvas.Scale();
    }

    /// <summary>
    ///     Scales a design value and clamps it to the provided range (after scaling).
    /// </summary>
    public static int ScaleSizeClamped(this ICanvas canvas, float designValue, int min, int max)
    {
        return Math.Clamp((int)MathF.Round(designValue * canvas.Scale()), min, max);
    }
}
