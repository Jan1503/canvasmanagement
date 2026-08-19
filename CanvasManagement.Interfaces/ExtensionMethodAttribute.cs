namespace CanvasManagement.Interfaces;

/// <summary>
///     Attribute to expose extension methods that can be called dynamically.
///     Apply this to methods you want to expose to the UI/API for dynamic invocation.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ExtensionMethodAttribute(string displayName, string description) : Attribute
{
    /// <summary>
    ///     User-friendly display name for the method (shown in UI)
    /// </summary>
    public string DisplayName { get; set; } = displayName;

    /// <summary>
    ///     Description of what the method does
    /// </summary>
    public string Description { get; set; } = description;

    /// <summary>
    ///     Category for grouping methods (e.g., "Playback", "Game Control", "Navigation")
    /// </summary>
    public string Category { get; set; } = "Actions";

    /// <summary>
    ///     Icon name for UI display (e.g., "play", "pause", "stop", "skip-forward")
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    ///     Whether this is a dangerous/destructive action (UI can show warning)
    /// </summary>
    public bool IsDangerous { get; set; }

    /// <summary>
    ///     Keyboard shortcut hint for UI display (e.g., "Space", "P", "Ctrl+R")
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