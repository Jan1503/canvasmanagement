using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Snake;

public static class Extension
{
    public static SnakeExtension GetSnake(this ICanvas canvas)
    {
        return new SnakeExtension(canvas);
    }
}
