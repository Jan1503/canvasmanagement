using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.HomeAssistant;

public static class Extension
{
    public static HomeAssistantSensorExtension GetHomeAssistantSensor(this ICanvas canvas)
    {
        return new HomeAssistantSensorExtension(canvas);
    }

    public static HomeAssistantGridExtension GetHomeAssistantGrid(this ICanvas canvas)
    {
        return new HomeAssistantGridExtension(canvas);
    }

    public static HomeAssistantGraphExtension GetHomeAssistantGraph(this ICanvas canvas)
    {
        return new HomeAssistantGraphExtension(canvas);
    }

    public static HomeAssistantEnergyExtension GetHomeAssistantEnergy(this ICanvas canvas)
    {
        return new HomeAssistantEnergyExtension(canvas);
    }

    public static HomeAssistantWeatherExtension GetHomeAssistantWeather(this ICanvas canvas)
    {
        return new HomeAssistantWeatherExtension(canvas);
    }

    public static HomeAssistantMediaExtension GetHomeAssistantMedia(this ICanvas canvas)
    {
        return new HomeAssistantMediaExtension(canvas);
    }

    public static HomeAssistantClimateExtension GetHomeAssistantClimate(this ICanvas canvas)
    {
        return new HomeAssistantClimateExtension(canvas);
    }

    public static HomeAssistantWasteExtension GetHomeAssistantWaste(this ICanvas canvas)
    {
        return new HomeAssistantWasteExtension(canvas);
    }
}
