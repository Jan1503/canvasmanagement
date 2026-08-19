namespace CanvasManagement.Interfaces;

/// <summary>
///     Attribute to provide metadata about filter parameters
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class FilterParameterAttribute(string displayName, string description = "") : Attribute
{
    /// <summary>
    ///     User-friendly display name for the parameter
    /// </summary>
    public string DisplayName { get; set; } = displayName;

    /// <summary>
    ///     Description of what the parameter controls
    /// </summary>
    public string Description { get; set; } = description;

    /// <summary>
    ///     Minimum allowed value (for numeric types)
    /// </summary>
    public object? MinValue { get; set; }

    /// <summary>
    ///     Maximum allowed value (for numeric types)
    /// </summary>
    public object? MaxValue { get; set; }

    /// <summary>
    ///     Default value for the parameter
    /// </summary>
    public object? DefaultValue { get; set; }
}