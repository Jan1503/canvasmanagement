using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.SpaceInvaders;

public static class Extension
{
    public static SpaceInvadersExtension GetSpaceInvaders(this ICanvas canvas)
    {
        return new SpaceInvadersExtension(canvas);
    }
}
