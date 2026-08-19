using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Pong;

public static class Extension
{
    public static PongExtension GetPong(this ICanvas canvas)
    {
        return new PongExtension(canvas);
    }
}
