using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Trailer;

public static class Extension
{
    public static TrailerExtension GetTrailers(this ICanvas canvas) => new(canvas);
}
