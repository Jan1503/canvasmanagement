namespace CanvasManagement.Interfaces;

/// <summary>
///     Information about a parameter of an extension method
/// </summary>
public class ExtensionMethodParameterInfo
{
    /// <summary>
    ///     Parameter name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Parameter type
    /// </summary>
    public Type ParameterType { get; set; } = typeof(object);

    /// <summary>
    ///     Type name as string (for serialization)
    /// </summary>
    public string TypeName => ParameterType.Name;

    /// <summary>
    ///     Full type name (for precise type resolution)
    /// </summary>
    public string FullTypeName => ParameterType.FullName ?? ParameterType.Name;

    /// <summary>
    ///     Whether the parameter is optional
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>
    ///     Default value if optional
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    ///     Whether this is a params array
    /// </summary>
    public bool IsParams { get; set; }
}