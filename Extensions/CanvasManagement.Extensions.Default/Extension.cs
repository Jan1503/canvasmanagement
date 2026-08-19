using CanvasManagement.Interfaces;

namespace CanvasManagement.Extensions.Default;

public static class Extension
{
    public static FireworksExtension GetFirework(this ICanvas canvas)
    {
        return new FireworksExtension(canvas);
    }

    public static PlasmaExtension GetPlasma(this ICanvas canvas)
    {
        return new PlasmaExtension(canvas);
    }

    public static MatrixExtension GetMatrix(this ICanvas canvas)
    {
        return new MatrixExtension(canvas);
    }
}