using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>One entity slot in the Home Assistant grid.</summary>
public class HaGridItem
{
    [ExtensionParameter("Entity ID", "Home Assistant entity id", DefaultValue = "", Order = 1)]
    public string EntityId { get; set; } = "";

    [ExtensionParameter("Label", "Override label (empty = entity friendly name)", DefaultValue = "", Order = 2)]
    public string Label { get; set; } = "";
}
