using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.Weather;

public static class Extension
{
    public static WeatherExtension GetWeather(this ICanvas canvas)
    {
        return new WeatherExtension(canvas);
    }
}
