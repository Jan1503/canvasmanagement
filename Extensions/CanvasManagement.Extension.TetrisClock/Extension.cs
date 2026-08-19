using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.TetrisClock
{
    public static class Extension
    {
        public static TetrisClockExtension GetTetrisClock(this ICanvas canvas)
        {
            return new TetrisClockExtension(canvas);
        }
    }
}