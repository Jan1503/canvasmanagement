namespace CanvasManagement.Interfaces;

/// <summary>
///     Information about an extension method that can be called dynamically
/// </summary>
public class ExtensionMethodInfo
{
    /// <summary>
    ///     Method name (for reflection)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     User-friendly display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Description of what the method does
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Category for grouping (e.g., "Playback", "Game Control")
    /// </summary>
    public string Category { get; set; } = "Actions";

    /// <summary>
    ///     Return type of the method
    /// </summary>
    public Type ReturnType { get; set; } = typeof(void);

    /// <summary>
    ///     Return type name as string (for serialization)
    /// </summary>
    public string ReturnTypeName => ReturnType.Name;

    /// <summary>
    ///     Method parameters
    /// </summary>
    public List<ExtensionMethodParameterInfo> Parameters { get; set; } = new();

    /// <summary>
    ///     Whether the method has no parameters (can be called directly)
    /// </summary>
    public bool IsParameterless => Parameters.Count == 0;

    /// <summary>
    ///     Icon name for UI
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    ///     Whether this is a dangerous action
    /// </summary>
    public bool IsDangerous { get; set; }

    /// <summary>
    ///     Keyboard shortcut hint
    /// </summary>
    public string? KeyboardShortcut { get; set; }

    /// <summary>
    ///     Order for display in UI (lower numbers appear first)
    /// </summary>
    public int Order { get; set; } = 100;

    /// <summary>
    ///     Whether the method returns a value that should be displayed
    /// </summary>
    public bool ReturnsValue { get; set; }
}