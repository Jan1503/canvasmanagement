using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.AnalogClock
{
    public static class Extension
    {
        public static AnalogClockExtension GetAnalogClock(this ICanvas canvas)
        {
            return new AnalogClockExtension(canvas);
        }
    }
}