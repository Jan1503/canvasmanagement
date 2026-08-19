using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.DinoRunner;

public static class Extension
{
    public static DinoRunnerExtension GetDinoRunner(this ICanvas canvas)
    {
        return new DinoRunnerExtension(canvas);
    }
}
