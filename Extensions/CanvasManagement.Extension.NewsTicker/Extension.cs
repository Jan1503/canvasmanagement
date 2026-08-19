using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.NewsTicker;

public static class Extension
{
    public static NewsTickerExtension GetNewsTicker(this ICanvas canvas)
    {
        return new NewsTickerExtension(canvas);
    }
}
