using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.LAV1StreamPlayer;

/// <summary>
/// Extension methods for creating LAV1 stream player instances.
/// </summary>
public static class Extension
{
    /// <summary>
    /// Creates a new LAV1 stream player extension for this canvas.
    /// </summary>
    public static Lav1StreamPlayerExtension GetLav1StreamPlayer(this ICanvas canvas)
    {
        return new Lav1StreamPlayerExtension(canvas);
    }
}
