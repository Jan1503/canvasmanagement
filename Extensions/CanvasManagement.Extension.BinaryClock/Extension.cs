using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.BinaryClock;

public static class Extension
{
    public static BinaryClockExtension GetBinaryClock(this ICanvas canvas)
    {
        return new BinaryClockExtension(canvas);
    }
}
