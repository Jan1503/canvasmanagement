namespace CanvasManagement;

/// <summary>
///     Raised after the composite surface changes size. Overlay canvases should follow
///     <see cref="Width"/> / <see cref="Height"/>; user canvases keep their own geometry.
/// </summary>
public sealed class DisplayResizedEventArgs : EventArgs
{
    public DisplayResizedEventArgs(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
}
