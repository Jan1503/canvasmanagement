using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.FallingSand;

public static class Extension
{
    public static FallingSandExtension GetFallingSand(this ICanvas canvas)
    {
        return new FallingSandExtension(canvas);
    }
}
