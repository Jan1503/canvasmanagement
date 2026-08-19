using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.VLCPlayer;

public static class Extension
{
    public static VLCMediaPlayerExtension VlcMediaPlayer(this ICanvas canvas)
    {
        return new VLCMediaPlayerExtension(canvas);
    }
}