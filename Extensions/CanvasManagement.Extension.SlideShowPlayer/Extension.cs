using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.SlideShowPlayer
{
    public static class Extension
    {
        public static SlideShowPlayerExtension GetSlideShowPlayer(this ICanvas canvas)
        {
            return new SlideShowPlayerExtension(canvas);
        }
    }
}
