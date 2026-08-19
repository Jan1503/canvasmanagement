namespace CanvasManagement.Interfaces;

/// <summary>
///     Service interface for discovering and creating canvas extensions
/// </summary>
public interface IExtensionDiscovery
{
    /// <summary>
    ///     Loads extension assemblies from a directory to make them discoverable
    /// </summary>
    /// <param name="extensionsPath">Path to extensions directory (defaults to "Extensions" folder)</param>
    void LoadAssemblies(string? extensionsPath = null);

    /// <summary>
    ///     Loads extensions from commonly used locations (Extensions, Plugins, Addons folders)
    /// </summary>
    void LoadAssembliesFromCommonLocations();

    /// <summary>
    ///     Discovers all available canvas extension types
    /// </summary>
    IEnumerable<Type> GetAvailableTypes();

    /// <summary>
    ///     Gets detailed information about all available canvas extensions
    /// </summary>
    /// <param name="includeFailedInstances">Whether to include extensions that failed to instantiate</param>
    IEnumerable<ExtensionTypeInfo> GetAvailableInfo(bool includeFailedInstances = true);

    /// <summary>
    ///     Gets extensions grouped by category
    /// </summary>
    Dictionary<string, List<ExtensionTypeInfo>> GetByCategory();

    /// <summary>
    ///     Gets extension info by display name
    /// </summary>
    ExtensionTypeInfo? GetByDisplayName(string displayName);

    /// <summary>
    ///     Gets extension info by type name
    /// </summary>
    ExtensionTypeInfo? GetByTypeName(string typeName);

    /// <summary>
    ///     Creates an extension instance by type name
    /// </summary>
    /// <param name="canvas">The canvas the extension will render to</param>
    /// <param name="typeName">Type name or full type name of the extension</param>
    /// <returns>Extension instance or null if not found</returns>
    object? Create(ICanvas canvas, string typeName);

    /// <summary>
    ///     Creates an extension instance by display name
    /// </summary>
    /// <param name="canvas">The canvas the extension will render to</param>
    /// <param name="displayName">Display name of the extension</param>
    /// <returns>Extension instance or null if not found</returns>
    object? CreateByDisplayName(ICanvas canvas, string displayName);

    /// <summary>
    ///     Creates an extension instance with error reporting
    /// </summary>
    /// <param name="canvas">The canvas the extension will render to</param>
    /// <param name="typeName">Type name of the extension</param>
    /// <param name="extension">Output: created extension instance</param>
    /// <param name="errorMessage">Output: error message if creation failed</param>
    /// <returns>True if creation succeeded, false otherwise</returns>
    bool TryCreate(ICanvas canvas, string typeName, out object? extension, out string? errorMessage);

    /// <summary>
    ///     Extracts method information from an extension type
    /// </summary>
    /// <param name="extensionType">The extension type to inspect</param>
    /// <returns>List of extension method information</returns>
    List<ExtensionMethodInfo> ExtractMethods(Type extensionType);

    /// <summary>
    ///     Extracts parameter information from an extension type
    /// </summary>
    /// <param name="extensionType">The extension type to inspect</param>
    /// <returns>List of extension parameter information</returns>
    List<ExtensionParameterInfo> ExtractParameters(Type extensionType);
}