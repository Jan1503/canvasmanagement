using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Starfield;

public static class Extension
{
    public static StarfieldExtension GetStarfield(this ICanvas canvas)
    {
        return new StarfieldExtension(canvas);
    }
}