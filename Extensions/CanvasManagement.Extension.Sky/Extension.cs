using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Sky;

public static class Extension
{
    public static NightSkyExtension GetNightSky(this ICanvas canvas) => new(canvas);

    public static TerminatorExtension GetTerminator(this ICanvas canvas) => new(canvas);
}
