namespace CanvasManagement.Interfaces;

/// <summary>
///     The editing "shape" of a parameter. Drives which widget the GUI renders. Scalar/Enum/Color are
///     leaf widgets; Object renders a group of <see cref="ExtensionParameterInfo.Fields" />; List renders
///     a repeatable stack of objects described by <see cref="ExtensionParameterInfo.Fields" />.
/// </summary>
public enum ExtensionParameterKind
{
    Scalar,
    Enum,
    Color,
    Object,
    List
}

/// <summary>
///     Information about an extension parameter
/// </summary>
public class ExtensionParameterInfo
{
    /// <summary>
    ///     Property name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     User-friendly display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Description of the parameter
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Parameter type
    /// </summary>
    public Type ParameterType { get; set; } = null!;

    /// <summary>
    ///     Minimum value (for numeric types)
    /// </summary>
    public object? MinValue { get; set; }

    /// <summary>
    ///     Maximum value (for numeric types)
    /// </summary>
    public object? MaxValue { get; set; }

    /// <summary>
    ///     Default value
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    ///     Current value (if instance available)
    /// </summary>
    public object? CurrentValue { get; set; }

    /// <summary>
    ///     Unit of measurement
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    ///     Whether the property is read-only
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    ///     Display order within its group (lower first). Ties keep declaration order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    ///     The editing shape of this parameter (scalar / enum / colour / object / list).
    /// </summary>
    public ExtensionParameterKind Kind { get; set; } = ExtensionParameterKind.Scalar;

    /// <summary>
    ///     Enum option names (when <see cref="Kind" /> is <see cref="ExtensionParameterKind.Enum" />).
    /// </summary>
    public string[]? EnumValues { get; set; }

    /// <summary>
    ///     For <see cref="ExtensionParameterKind.Object" />: the object's fields. For
    ///     <see cref="ExtensionParameterKind.List" />: the schema of a single list item. Built recursively
    ///     from the nested type's [ExtensionParameter] properties.
    /// </summary>
    public List<ExtensionParameterInfo>? Fields { get; set; }
}