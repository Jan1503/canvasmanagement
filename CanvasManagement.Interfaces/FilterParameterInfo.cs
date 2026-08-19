namespace CanvasManagement.Interfaces;

/// <summary>
///     Metadata about a filter parameter/property
/// </summary>
public class FilterParameterInfo
{
    /// <summary>
    ///     Property name (e.g., "Intensity", "StreamDensity")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     User-friendly display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     The .NET type of the parameter
    /// </summary>
    public Type ParameterType { get; set; } = null!;

    /// <summary>
    ///     Type name as string for web serialization (e.g., "System.Single", "System.Int32")
    /// </summary>
    public string TypeName => ParameterType.FullName ?? ParameterType.Name;

    /// <summary>
    ///     Simple type name for display (e.g., "float", "int", "bool")
    /// </summary>
    public string SimpleTypeName
    {
        get
        {
            return ParameterType.Name switch
            {
                "Single" => "float",
                "Double" => "double",
                "Int32" => "int",
                "Int64" => "long",
                "Boolean" => "bool",
                "String" => "string",
                "Byte" => "byte",
                _ => ParameterType.Name
            };
        }
    }

    /// <summary>
    ///     Default value for the parameter
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    ///     Minimum allowed value (for numeric types)
    /// </summary>
    public object? MinValue { get; set; }

    /// <summary>
    ///     Maximum allowed value (for numeric types)
    /// </summary>
    public object? MaxValue { get; set; }

    /// <summary>
    ///     Description of what the parameter controls
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Current value of the parameter (if from an instance)
    /// </summary>
    public object? CurrentValue { get; set; }

    /// <summary>
    ///     Whether this parameter can be written to
    /// </summary>
    public bool IsReadOnly { get; set; }

    public override string ToString()
    {
        var range = MinValue != null && MaxValue != null ? $" ({MinValue}-{MaxValue})" : "";
        return $"{DisplayName} ({SimpleTypeName}){range}";
    }
}