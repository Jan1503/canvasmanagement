namespace CanvasManagement.Interfaces;

/// <summary>
///     Attribute to provide rich metadata about a filter
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class FilterInfoAttribute(string displayName, string description, string category = "General")
    : Attribute
{
    /// <summary>
    ///     User-friendly display name for the filter
    /// </summary>
    public string DisplayName { get; set; } = displayName;

    /// <summary>
    ///     Description of what the filter does
    /// </summary>
    public string Description { get; set; } = description;

    /// <summary>
    ///     Category for grouping filters (e.g., "Artistic", "Matrix Effects", "Image Enhancement")
    /// </summary>
    public string Category { get; set; } = category;

    /// <summary>
    ///     Base64-encoded SVG icon data (48x48) for UI display
    ///     Use IconData property to set embedded icon data directly
    /// </summary>
    public string? IconData { get; set; }

    /// <summary>
    ///     Icon resource name for loading from embedded resources (alternative to IconData)
    ///     Specify just the filename (e.g., "christmas.svg"), the discovery system will find it
    /// </summary>
    public string? IconResourceName { get; set; }
}