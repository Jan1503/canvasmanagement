using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CanvasManagement.Interfaces;

namespace CanvasManagement;

/// <summary>
///     Default implementation of IFilterDiscovery for discovering and creating canvas filters
/// </summary>
public class FilterDiscoveryService : IFilterDiscovery
{
    /// <summary>
    ///     Default singleton instance for backwards compatibility with static methods
    /// </summary>
    public static FilterDiscoveryService Default { get; } = new();

    // PERFORMANCE: Discovering types and instantiating every filter to extract metadata is
    // expensive and was previously repeated on every call (e.g. each web API request).
    // Results are cached and invalidated whenever new assemblies are loaded.
    private readonly object _cacheLock = new();
    private List<Type>? _cachedTypes;
    private List<FilterTypeInfo>? _cachedInfo;

    private void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedTypes = null;
            _cachedInfo = null;
        }
    }

    /// <inheritdoc />
    public void LoadAssemblies(string? filtersPath = null)
    {
        // Default to "Filters" folder next to executable
        if (string.IsNullOrWhiteSpace(filtersPath))
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            filtersPath = Path.Combine(baseDirectory, "Filters");
        }

        if (!Directory.Exists(filtersPath))
        {
            Console.WriteLine($"Filters directory not found: {filtersPath}");
            return;
        }

        // Load all DLLs from the filters directory
        var dllFiles = Directory.GetFiles(filtersPath, "*.dll", SearchOption.AllDirectories);

        Console.WriteLine($"Loading filters from: {filtersPath}");
        Console.WriteLine($"Found {dllFiles.Length} DLL files");

        foreach (var dllFile in dllFiles)
            try
            {
                var assembly = PluginAssemblyHub.Load(dllFile);
                if (assembly == null) continue;
                Console.WriteLine($"  Loaded: {assembly.GetName().Name}");

                var filterCount = assembly.GetTypes()
                    .Count(t => t.IsClass && !t.IsAbstract &&
                                typeof(ICanvasFilter).IsAssignableFrom(t));

                if (filterCount > 0) Console.WriteLine($"    Found {filterCount} filter(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Failed to load {Path.GetFileName(dllFile)}: {ex.Message}");
            }

        // New assemblies may contribute filters - drop cached discovery results.
        InvalidateCache();
    }

    /// <inheritdoc />
    public void LoadAssembliesFromCommonLocations()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Try common locations
        var locations = new[]
        {
            Path.Combine(baseDirectory, "Filters"),
            Path.Combine(baseDirectory, "Plugins"),
            Path.Combine(baseDirectory, "Addons"),
            baseDirectory // Also check the main directory
        };

        foreach (var location in locations)
            if (Directory.Exists(location))
                LoadAssemblies(location);
    }

    /// <inheritdoc />
    public void ReloadAssemblies(string? filtersPath = null)
    {
        if (string.IsNullOrWhiteSpace(filtersPath))
            filtersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Filters");

        Console.WriteLine($"Reloading filters from: {filtersPath}");
        var n = Directory.Exists(filtersPath) ? PluginAssemblyHub.UnloadUnder(filtersPath) : 0;
        PluginAssemblyHub.CollectUnloaded();
        Console.WriteLine($"  Unloaded {n} plugin context(s)");
        LoadAssemblies(filtersPath);
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetAvailableTypes()
    {
        lock (_cacheLock)
        {
            if (_cachedTypes != null) return _cachedTypes;

            var filterInterface = typeof(ICanvasFilter);
            var result = new List<Type>();

            foreach (var assembly in PluginAssemblyHub.AssembliesToScan())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                    if (type.IsClass && !type.IsAbstract && filterInterface.IsAssignableFrom(type))
                        result.Add(type);
            }

            _cachedTypes = result;
            return result;
        }
    }

    /// <inheritdoc />
    public IEnumerable<FilterTypeInfo> GetAvailableInfo(bool includeFailedInstances = true)
    {
        List<FilterTypeInfo> all;
        lock (_cacheLock)
        {
            all = _cachedInfo ??= BuildAvailableInfo();
        }

        // The cached list always includes filters that failed to instantiate (CanInstantiate == false);
        // honor the caller's preference without rebuilding.
        return includeFailedInstances ? all : all.Where(f => f.CanInstantiate).ToList();
    }

    private List<FilterTypeInfo> BuildAvailableInfo()
    {
        var result = new List<FilterTypeInfo>();

        foreach (var filterType in GetAvailableTypes())
        {
            var filterName = filterType.Name;
            var displayName = filterType.Name;
            var description = "";
            var category = "General";
            string? iconData = null;
            var canInstantiate = true;
            var parameters = new List<FilterParameterInfo>();

            // Get metadata from FilterInfo attribute
            var filterInfoAttr = filterType.GetCustomAttributes(typeof(FilterInfoAttribute), false)
                .FirstOrDefault() as FilterInfoAttribute;

            if (filterInfoAttr != null)
            {
                displayName = filterInfoAttr.DisplayName;
                description = filterInfoAttr.Description;
                category = filterInfoAttr.Category;

                // Get icon data - either directly from IconData property or load from embedded resource
                if (!string.IsNullOrEmpty(filterInfoAttr.IconData))
                {
                    iconData = filterInfoAttr.IconData;
                }
                else if (!string.IsNullOrEmpty(filterInfoAttr.IconResourceName))
                {
                    // Try to load from embedded resource
                    try
                    {
                        var assembly = filterType.Assembly;
                        var resourceName = filterInfoAttr.IconResourceName;

                        // Try to find the resource
                        var fullResourceName = assembly.GetManifestResourceNames()
                            .FirstOrDefault(r => r.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

                        if (fullResourceName != null)
                        {
                            using var stream = assembly.GetManifestResourceStream(fullResourceName);
                            if (stream != null)
                            {
                                using var reader = new StreamReader(stream);
                                var svgContent = reader.ReadToEnd();
                                // Convert to base64
                                var bytes = Encoding.UTF8.GetBytes(svgContent);
                                iconData = Convert.ToBase64String(bytes);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Failed to load icon resource '{filterInfoAttr.IconResourceName}' for {filterType.Name}: {ex.Message}");
                    }
                }
            }

            try
            {
                if (Activator.CreateInstance(filterType) is ICanvasFilter instance)
                {
                    if (filterInfoAttr == null)
                    {
                        filterName = instance.Name;
                        displayName = instance.Name;
                    }

                    parameters = ExtractParameters(filterType, instance);
                }
            }
            catch (Exception ex)
            {
                canInstantiate = false;
                description = description == "" ? $"Cannot instantiate: {ex.Message}" : description;
                parameters = ExtractParameters(filterType, null);
            }

            // Derive display name from type name if still not set
            if (displayName == filterName && displayName.EndsWith("Filter"))
                displayName = Regex.Replace(
                    displayName.Substring(0, displayName.Length - 6),
                    "([a-z])([A-Z])",
                    "$1 $2"
                );

            result.Add(new FilterTypeInfo
            {
                Type = filterType,
                Name = filterName,
                DisplayName = displayName,
                Description = description,
                Category = category,
                IconData = iconData,
                FullName = filterType.FullName ?? filterType.Name,
                AssemblyName = filterType.Assembly.GetName().Name ?? "Unknown",
                CanInstantiate = canInstantiate,
                Parameters = parameters
            });
        }

        return result;
    }

    /// <inheritdoc />
    public Dictionary<string, List<FilterTypeInfo>> GetByCategory()
    {
        return GetAvailableInfo()
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <inheritdoc />
    public FilterTypeInfo? GetByDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        return GetAvailableInfo()
            .FirstOrDefault(f => f.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase) ||
                                 f.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public FilterTypeInfo? GetByTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        return GetAvailableInfo()
            .FirstOrDefault(f => f.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                                 f.FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <inheritdoc />
    public ICanvasFilter? Create(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var filterTypes = GetAvailableTypes().ToList();

        var filterType =
            filterTypes.FirstOrDefault(t => t.Name == typeName) ??
            filterTypes.FirstOrDefault(t => t.FullName == typeName) ??
            filterTypes.FirstOrDefault(t => t.Name == typeName + "Filter") ??
            filterTypes.FirstOrDefault(t =>
                t.Name.EndsWith("Filter", StringComparison.Ordinal) &&
                t.Name.Substring(0, t.Name.Length - 6) == typeName) ??
            filterTypes.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) ??
            filterTypes.FirstOrDefault(t =>
                t.FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true) ??
            filterTypes.FirstOrDefault(t =>
                t.Name.Equals(typeName + "Filter", StringComparison.OrdinalIgnoreCase)) ??
            filterTypes.FirstOrDefault(t =>
                t.Name.EndsWith("Filter", StringComparison.OrdinalIgnoreCase) &&
                t.Name.Substring(0, t.Name.Length - 6).Equals(typeName, StringComparison.OrdinalIgnoreCase));

        if (filterType == null)
            return null;

        try
        {
            return Activator.CreateInstance(filterType) as ICanvasFilter;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to create filter {filterType.Name}: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public ICanvasFilter? CreateByDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var filterInfo = GetByDisplayName(displayName);
        return filterInfo != null ? Create(filterInfo.Name) : null;
    }

    /// <inheritdoc />
    public bool TryCreate(string typeName, out ICanvasFilter? filter, out string? errorMessage)
    {
        filter = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(typeName))
        {
            errorMessage = "Filter type name cannot be empty";
            return false;
        }

        var filterTypes = GetAvailableTypes().ToList();

        if (filterTypes.Count == 0)
        {
            errorMessage = "No filters found in loaded assemblies";
            return false;
        }

        filter = Create(typeName);

        if (filter == null)
        {
            var similarFilters = filterTypes
                .Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .Take(3)
                .ToList();

            if (similarFilters.Any())
                errorMessage = $"Filter '{typeName}' not found. Did you mean: {string.Join(", ", similarFilters)}?";
            else
                errorMessage =
                    $"Filter '{typeName}' not found. Available filters: {string.Join(", ", filterTypes.Take(5).Select(t => t.Name))}...";
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public List<FilterParameterInfo> ExtractParameters(Type filterType, ICanvasFilter? instance = null)
    {
        var parameters = new List<FilterParameterInfo>();
        var properties = filterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (prop.Name == "Name" || prop.Name == "Enabled")
                continue;

            var paramInfo = new FilterParameterInfo
            {
                Name = prop.Name,
                DisplayName = prop.Name,
                ParameterType = prop.PropertyType,
                IsReadOnly = !prop.CanWrite,
                CurrentValue = instance != null && prop.CanRead ? prop.GetValue(instance) : null
            };

            var paramAttr = prop.GetCustomAttributes(typeof(FilterParameterAttribute), false)
                .FirstOrDefault() as FilterParameterAttribute;

            if (paramAttr != null)
            {
                paramInfo.DisplayName = paramAttr.DisplayName;
                paramInfo.Description = paramAttr.Description;
                paramInfo.MinValue = paramAttr.MinValue;
                paramInfo.MaxValue = paramAttr.MaxValue;
                paramInfo.DefaultValue = paramAttr.DefaultValue ?? paramInfo.CurrentValue;
            }
            else
            {
                paramInfo.DefaultValue = paramInfo.CurrentValue;

                if (prop.Name == "Intensity" && prop.PropertyType == typeof(float))
                {
                    paramInfo.MinValue = 0.0f;
                    paramInfo.MaxValue = 1.0f;
                    paramInfo.Description = "Effect intensity";
                }
            }

            parameters.Add(paramInfo);
        }

        return parameters;
    }
}
