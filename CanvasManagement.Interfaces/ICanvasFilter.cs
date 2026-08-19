using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     Interface for post-processing filters that can be applied to the final rendered output
/// </summary>
public interface ICanvasFilter
{
    /// <summary>
    ///     Filter name for identification
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Filter intensity/strength (0.0 = disabled, 1.0 = full effect)
    /// </summary>
    float Intensity { get; set; }

    /// <summary>
    ///     Whether the filter is currently enabled
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>
    ///     Apply the filter to the bitmap in-place or return a new bitmap
    /// </summary>
    /// <param name="source">Source bitmap to filter</param>
    /// <param name="inPlace">If true, modify source bitmap directly. If false, return new bitmap.</param>
    /// <returns>Filtered bitmap (either modified source or new bitmap)</returns>
    SKBitmap Apply(SKBitmap source, bool inPlace = true);
}