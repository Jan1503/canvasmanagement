namespace CanvasManagement.Interfaces;

/// <summary>
///     Attribute to provide rich metadata about a canvas extension
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ExtensionInfoAttribute(string displayName, string description, string category = "General")
    : Attribute
{
    /// <summary>
    ///     User-friendly display name for the extension
    /// </summary>
    public string DisplayName { get; set; } = displayName;

    /// <summary>
    ///     Description of what the extension does
    /// </summary>
    public string Description { get; set; } = description;

    /// <summary>
    ///     Category for grouping extensions (e.g., "Animations", "Games", "Visualizations", "Clocks")
    /// </summary>
    public string Category { get; set; } = category;

    /// <summary>
    ///     Base64-encoded SVG icon data (48x48) for UI display
    ///     Use IconData property to set embedded icon data directly
    /// </summary>
    public string? IconData { get; set; }

    /// <summary>
    ///     Icon resource name for loading from embedded resources (alternative to IconData)
    /// </summary>
    public string? IconResourceName { get; set; }
}