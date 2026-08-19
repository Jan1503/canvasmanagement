namespace CanvasManagement.Interfaces;

/// <summary>
///     Service interface for discovering and creating canvas filters
/// </summary>
public interface IFilterDiscovery
{
    /// <summary>
    ///     Loads filter assemblies from a directory to make them discoverable
    /// </summary>
    /// <param name="filtersPath">Path to filters directory (defaults to "Filters" folder)</param>
    void LoadAssemblies(string? filtersPath = null);

    /// <summary>
    ///     Loads filters from commonly used locations (Filters, Plugins, Addons folders)
    /// </summary>
    void LoadAssembliesFromCommonLocations();

    /// <summary>
    ///     Discovers all available filter types
    /// </summary>
    IEnumerable<Type> GetAvailableTypes();

    /// <summary>
    ///     Gets detailed information about all available filters
    /// </summary>
    /// <param name="includeFailedInstances">Whether to include filters that failed to instantiate</param>
    IEnumerable<FilterTypeInfo> GetAvailableInfo(bool includeFailedInstances = true);

    /// <summary>
    ///     Gets filters grouped by category
    /// </summary>
    Dictionary<string, List<FilterTypeInfo>> GetByCategory();

    /// <summary>
    ///     Gets filter info by display name
    /// </summary>
    FilterTypeInfo? GetByDisplayName(string displayName);

    /// <summary>
    ///     Gets filter info by type name
    /// </summary>
    FilterTypeInfo? GetByTypeName(string typeName);

    /// <summary>
    ///     Creates a filter instance by type name
    /// </summary>
    /// <param name="typeName">Type name or full type name of the filter</param>
    /// <returns>Filter instance or null if not found</returns>
    ICanvasFilter? Create(string typeName);

    /// <summary>
    ///     Creates a filter instance by display name
    /// </summary>
    /// <param name="displayName">Display name of the filter</param>
    /// <returns>Filter instance or null if not found</returns>
    ICanvasFilter? CreateByDisplayName(string displayName);

    /// <summary>
    ///     Creates a filter instance with error reporting
    /// </summary>
    /// <param name="typeName">Type name of the filter</param>
    /// <param name="filter">Output: created filter instance</param>
    /// <param name="errorMessage">Output: error message if creation failed</param>
    /// <returns>True if creation succeeded, false otherwise</returns>
    bool TryCreate(string typeName, out ICanvasFilter? filter, out string? errorMessage);

    /// <summary>
    ///     Extracts parameter information from a filter type
    /// </summary>
    /// <param name="filterType">The filter type to inspect</param>
    /// <param name="instance">Optional filter instance to get current values</param>
    /// <returns>List of filter parameter information</returns>
    List<FilterParameterInfo> ExtractParameters(Type filterType, ICanvasFilter? instance = null);
}