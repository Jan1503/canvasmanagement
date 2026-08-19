using CanvasManagement.Interfaces;

namespace CanvasManagement.Canvas.Extension.GameOfLife;

public static class Extension
{
    public static GameOfLifeExtension GetGameOfLife(this ICanvas canvas)
    {
        return new GameOfLifeExtension(canvas);
    }
}