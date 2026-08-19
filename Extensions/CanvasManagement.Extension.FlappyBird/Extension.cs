using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.FlappyBird;

public static class Extension
{
    public static FlappyBirdExtension GetFlappyBird(this ICanvas canvas)
    {
        return new FlappyBirdExtension(canvas);
    }
}
