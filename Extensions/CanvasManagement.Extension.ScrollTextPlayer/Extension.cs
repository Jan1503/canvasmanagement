using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.ScrollTextPlayer
{
    public static class Extension
    {
        public static ScrollTextPlayerExtension GetScrollText(this ICanvas canvas)
        {
            return new ScrollTextPlayerExtension(canvas);
        }
    }
}
