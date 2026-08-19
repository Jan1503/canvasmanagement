namespace CanvasManagement.Interfaces;

/// <summary>
///     Information about a discovered canvas extension type
/// </summary>
public class ExtensionTypeInfo
{
    /// <summary>
    ///     The actual Type of the extension
    /// </summary>
    public Type Type { get; set; } = null!;

    /// <summary>
    ///     Internal type name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     User-friendly display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Description of what the extension does
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Category for grouping (e.g., "Animations", "Games", "Clocks")
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    ///     Base64-encoded icon data (SVG or PNG, 48x48 pixels)
    ///     Can be used directly in UI: img src="data:image/svg+xml;base64,{IconData}"
    /// </summary>
    public string? IconData { get; set; }

    /// <summary>
    ///     Full qualified type name
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    ///     Assembly name where the extension is defined
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the extension can be instantiated
    /// </summary>
    public bool CanInstantiate { get; set; } = true;

    /// <summary>
    ///     List of configurable parameters
    /// </summary>
    public List<ExtensionParameterInfo> Parameters { get; set; } = new();

    /// <summary>
    ///     List of callable methods exposed by the extension
    /// </summary>
    public List<ExtensionMethodInfo> Methods { get; set; } = new();

    /// <summary>
    ///     Extension method name (e.g., "GetTetrisAnimation", "GetMatrixAnimation")
    /// </summary>
    public string? ExtensionMethodName { get; set; }
}