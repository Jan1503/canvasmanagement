namespace CanvasManagement.Interfaces;

/// <summary>
///     Metadata about an available filter type
/// </summary>
public class FilterTypeInfo
{
    /// <summary>
    ///     The actual Type of the filter
    /// </summary>
    public Type Type { get; set; } = null!;

    /// <summary>
    ///     The type name (e.g., "NeoCodeVisionFilter")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     User-friendly display name from [FilterInfo] attribute or derived from Name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Description of what the filter does from [FilterInfo] attribute
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Category for grouping filters (e.g., "Artistic", "Matrix Effects")
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    ///     Base64-encoded SVG icon data (48x48) for UI display
    /// </summary>
    public string? IconData { get; set; }

    /// <summary>
    ///     The full type name including namespace
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    ///     The assembly where the filter is defined
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    ///     Indicates if the filter can be instantiated without errors
    /// </summary>
    public bool CanInstantiate { get; set; } = true;

    /// <summary>
    ///     List of configurable parameters for this filter
    /// </summary>
    public List<FilterParameterInfo> Parameters { get; set; } = new();

    /// <summary>
    ///     Gets the simple type name without "Filter" suffix
    /// </summary>
    public string SimpleName => Name.EndsWith("Filter") ? Name.Substring(0, Name.Length - 6) : Name;

    /// <summary>
    ///     Gets all possible name variations for matching
    /// </summary>
    public IEnumerable<string> GetNameVariations()
    {
        yield return Name; // "NeoCodeVisionFilter"
        yield return DisplayName; // "Neo Code Vision"
        yield return SimpleName; // "NeoCodeVision"
        yield return FullName; // "CanvasManagement.Filters.NeoCodeVisionFilter"
        yield return Name.ToLowerInvariant(); // "neocodevisionfilter"
        yield return DisplayName.ToLowerInvariant(); // "neo code vision"
        yield return SimpleName.ToLowerInvariant(); // "neocodevision"
    }

    public override string ToString()
    {
        var status = CanInstantiate ? "" : " [Cannot Instantiate]";
        var paramCount = Parameters.Count > 0 ? $" ({Parameters.Count} parameters)" : "";
        return $"{DisplayName} ({AssemblyName}){status}{paramCount}";
    }
}