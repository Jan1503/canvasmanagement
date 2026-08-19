using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.YouTubePlayer;

public static class Extension
{
    public static YouTubePlayerExtension YouTubePlayer(this ICanvas canvas)
    {
        return new YouTubePlayerExtension(canvas);
    }
}
