using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.DigitalClock;

public static class Extension
{
    public static DigitalClockExtension GetDigitalClock(this ICanvas canvas)
    {
        return new DigitalClockExtension(canvas);
    }
}
