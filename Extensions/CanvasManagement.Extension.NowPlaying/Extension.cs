using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.NowPlaying;

public static class Extension
{
    public static NowPlayingExtension GetNowPlaying(this ICanvas canvas)
    {
        return new NowPlayingExtension(canvas);
    }
}
