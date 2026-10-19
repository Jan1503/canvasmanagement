namespace CanvasManagement;

/// <summary>
///     Keeps a canvas rectangle on the current wall without stretching it.
/// </summary>
public static class DisplayGeometry
{
    public static (int X, int Y, int Width, int Height) Clamp(int x, int y, int width, int height, int wallWidth, int wallHeight)
    {
        var w = Math.Max(1, Math.Min(width, Math.Max(1, wallWidth)));
        var h = Math.Max(1, Math.Min(height, Math.Max(1, wallHeight)));
        var maxX = Math.Max(0, wallWidth - w);
        var maxY = Math.Max(0, wallHeight - h);
        return (Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY), w, h);
    }
}
