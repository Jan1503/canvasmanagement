using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Quake3Screensaver;

public static class Extension
{
    /// <summary>
    /// Creates a Quake 3 Arena themed screensaver extension
    /// </summary>
    public static Quake3ScreensaverExtension GetQuake3Screensaver(this ICanvas canvas)
    {
        return new Quake3ScreensaverExtension(canvas);
    }
}
