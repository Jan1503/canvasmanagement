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
}
