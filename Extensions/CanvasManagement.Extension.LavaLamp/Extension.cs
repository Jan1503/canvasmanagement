using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.LavaLamp;

/// <summary>
///     Fluent factory for the Lava Lamp extension.
/// </summary>
public static class Extension
{
    public static LavaLampExtension GetLavaLamp(this ICanvas canvas)
    {
        return new LavaLampExtension(canvas);
    }
}
