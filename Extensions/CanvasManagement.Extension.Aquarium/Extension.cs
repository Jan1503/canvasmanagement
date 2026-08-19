using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Aquarium;

public static class Extension
{
    public static AquariumExtension GetAquarium(this ICanvas canvas)
    {
        return new AquariumExtension(canvas);
    }
}
