using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.FlipClock;

public static class Extension
{
    public static FlipClockExtension GetFlipClock(this ICanvas canvas)
    {
        return new FlipClockExtension(canvas);
    }
}
