using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     One row in HA Waste: a date sensor, or a filter against a schedule entity
///     (dates as attributes, e.g. Stadtreinigung Hamburg).
/// </summary>
public class HaWasteItem
{
    [ExtensionParameter("Entity ID", "Schedule entity or a date sensor", DefaultValue = "", Order = 1)]
    public string EntityId { get; set; } = "";

    [ExtensionParameter("Match", "Only this bin (substring of the name). Empty = every pickup from the entity",
        DefaultValue = "", Order = 2)]
    public string Match { get; set; } = "";

    [ExtensionParameter("Label", "Override the displayed name (empty = HA name / bin name)", DefaultValue = "",
        Order = 3)]
    public string Label { get; set; } = "";
}
