using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.WordClock;

public static class Extension
{
    public static WordClockExtension GetWordClock(this ICanvas canvas)
    {
        return new WordClockExtension(canvas);
    }
}
