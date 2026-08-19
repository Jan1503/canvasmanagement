namespace CanvasManagement.Interfaces;

/// <summary>
///     Attribute to provide metadata about extension parameters (similar to FilterParameterAttribute)
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ExtensionParameterAttribute(string displayName, string description) : Attribute
{
    /// <summary>
    ///     User-friendly display name for the parameter
    /// </summary>
    public string DisplayName { get; set; } = displayName;

    /// <summary>
    ///     Description of what the parameter does
    /// </summary>
    public string Description { get; set; } = description;

    /// <summary>
    ///     Minimum value (for numeric parameters)
    /// </summary>
    public object? MinValue { get; set; }

    /// <summary>
    ///     Maximum value (for numeric parameters)
    /// </summary>
    public object? MaxValue { get; set; }

    /// <summary>
    ///     Default value
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    ///     Unit of measurement (e.g., "ms", "%", "px")
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    ///     Indicates if this parameter is read-only (cannot be modified by user)
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    ///     Display order within its group/card (lower values first). Useful for ordering the fields of a
    ///     nested config object. Ties fall back to declaration order.
    /// </summary>
    public int Order { get; set; }
}