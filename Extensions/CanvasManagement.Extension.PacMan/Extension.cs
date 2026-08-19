using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.PacMan;

public static class Extension
{
    public static PacManExtension GetPacMan(this ICanvas canvas)
    {
        return new PacManExtension(canvas);
    }
}
