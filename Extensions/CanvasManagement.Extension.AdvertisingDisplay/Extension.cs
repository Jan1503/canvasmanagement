using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.AdvertisingDisplay;

/// <summary>
///     Fluent factory for the Advertising Display extension.
/// </summary>
public static class Extension
{
    public static AdvertisingDisplayExtension GetAdvertisingDisplay(this ICanvas canvas)
    {
        return new AdvertisingDisplayExtension(canvas);
    }
}
