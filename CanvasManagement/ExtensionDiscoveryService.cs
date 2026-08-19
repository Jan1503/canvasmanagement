using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CanvasManagement.Interfaces;

namespace CanvasManagement;

/// <summary>
///     Default implementation of IExtensionDiscovery for discovering and creating canvas extensions
/// </summary>
public class ExtensionDiscoveryService : IExtensionDiscovery
{
    /// <summary>
    ///     Default singleton instance for backwards compatibility with static methods
    /// </summary>
    public static ExtensionDiscoveryService Default { get; } = new();

    // PERFORMANCE: Cache reflection-based discovery results; invalidated when assemblies load.
    private readonly object _cacheLock = new();
    private List<Type>? _cachedTypes;
    private List<ExtensionTypeInfo>? _cachedInfo;

    private void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedTypes = null;
            _cachedInfo = null;
        }
    }

    /// <inheritdoc />
    public void LoadAssemblies(string? extensionsPath = null)
    {
        // Default to "Extensions" folder next to executable
        if (string.IsNullOrWhiteSpace(extensionsPath))
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            extensionsPath = Path.Combine(baseDirectory, "Extensions");
        }

        if (!Directory.Exists(extensionsPath))
        {
            Console.WriteLine($"Extensions directory not found: {extensionsPath}");
            return;
        }

        // Load all DLLs from the extensions directory
        var dllFiles = Directory.GetFiles(extensionsPath, "*.dll", SearchOption.AllDirectories);

        Console.WriteLine($"Loading extensions from: {extensionsPath}");
        Console.WriteLine($"Found {dllFiles.Length} DLL files");

        foreach (var dllFile in dllFiles)
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(dllFile);

                // Check if already loaded
                if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName == assemblyName.FullName))
                {
                    Console.WriteLine($"  Already loaded: {assemblyName.Name}");
                    continue;
                }

                // Load the assembly
                var assembly = Assembly.LoadFrom(dllFile);
                Console.WriteLine($"  Loaded: {assembly.GetName().Name}");

                // Count extensions in this assembly
                var extensionCount = assembly.GetTypes()
                    .Count(t => t.IsClass && !t.IsAbstract &&
                                t.GetCustomAttributes(typeof(ExtensionInfoAttribute), false).Any());

                if (extensionCount > 0) Console.WriteLine($"    Found {extensionCount} extension(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Failed to load {Path.GetFileName(dllFile)}: {ex.Message}");
            }

        // New assemblies may contribute extensions - drop cached discovery results.
        InvalidateCache();
    }

    /// <inheritdoc />
    public void LoadAssembliesFromCommonLocations()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Try common locations
        var locations = new[]
        {
            Path.Combine(baseDirectory, "Extensions"),
            Path.Combine(baseDirectory, "Plugins"),
            Path.Combine(baseDirectory, "Addons"),
            baseDirectory // Also check the main directory
        };

        foreach (var location in locations)
            if (Directory.Exists(location))
                LoadAssemblies(location);
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetAvailableTypes()
    {
        lock (_cacheLock)
        {
            if (_cachedTypes != null) return _cachedTypes;

            var result = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
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
                    // Look for classes with ExtensionInfo attribute
                    if (type.IsClass && !type.IsAbstract)
                    {
                        var extAttr = type.GetCustomAttributes(typeof(ExtensionInfoAttribute), false)
                            .FirstOrDefault() as ExtensionInfoAttribute;

                        if (extAttr != null) result.Add(type);
                    }
            }

            _cachedTypes = result;
            return result;
        }
    }

    /// <inheritdoc />
    public IEnumerable<ExtensionTypeInfo> GetAvailableInfo(bool includeFailedInstances = true)
    {
        lock (_cacheLock)
        {
            if (_cachedInfo != null) return _cachedInfo;
            _cachedInfo = BuildAvailableInfo();
            return _cachedInfo;
        }
    }

    private List<ExtensionTypeInfo> BuildAvailableInfo()
    {
        var result = new List<ExtensionTypeInfo>();

        foreach (var extensionType in GetAvailableTypes())
        {
            var extensionName = extensionType.Name;
            var displayName = extensionType.Name;
            var description = "";
            var category = "General";
            string? iconData = null;
            var canInstantiate = true;
            var parameters = new List<ExtensionParameterInfo>();
            string? extensionMethod = null;

            // Get metadata from ExtensionInfo attribute
            var extensionInfoAttr = extensionType.GetCustomAttributes(typeof(ExtensionInfoAttribute), false)
                .FirstOrDefault() as ExtensionInfoAttribute;

            if (extensionInfoAttr != null)
            {
                displayName = extensionInfoAttr.DisplayName;
                description = extensionInfoAttr.Description;
                category = extensionInfoAttr.Category;

                // Get icon data - either directly from IconData property or load from embedded resource
                if (!string.IsNullOrEmpty(extensionInfoAttr.IconData))
                {
                    iconData = extensionInfoAttr.IconData;
                }
                else if (!string.IsNullOrEmpty(extensionInfoAttr.IconResourceName))
                {
                    // Try to load from embedded resource
                    try
                    {
                        var assembly = extensionType.Assembly;
                        var resourceName = extensionInfoAttr.IconResourceName;

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
                            $"Failed to load icon resource '{extensionInfoAttr.IconResourceName}' for {extensionType.Name}: {ex.Message}");
                    }
                }
            }

            // Try to find the extension method name
            extensionMethod = FindExtensionMethodName(extensionType);

            // Extract parameters from public properties
            parameters = ExtractParameters(extensionType);

            // Extract callable methods
            var methods = ExtractMethods(extensionType);

            result.Add(new ExtensionTypeInfo
            {
                Type = extensionType,
                Name = extensionName,
                DisplayName = displayName,
                Description = description,
                Category = category,
                IconData = iconData,
                FullName = extensionType.FullName ?? extensionType.Name,
                AssemblyName = extensionType.Assembly.GetName().Name ?? "Unknown",
                CanInstantiate = canInstantiate,
                Parameters = parameters,
                Methods = methods,
                ExtensionMethodName = extensionMethod
            });
        }

        return result;
    }

    /// <inheritdoc />
    public Dictionary<string, List<ExtensionTypeInfo>> GetByCategory()
    {
        return GetAvailableInfo()
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <inheritdoc />
    public ExtensionTypeInfo? GetByDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        return GetAvailableInfo()
            .FirstOrDefault(e => e.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public ExtensionTypeInfo? GetByTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        return GetAvailableInfo()
            .FirstOrDefault(e => e.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                                 e.FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <inheritdoc />
    public object? Create(ICanvas canvas, string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var extensionTypes = GetAvailableTypes().ToList();

        var extensionType =
            extensionTypes.FirstOrDefault(t => t.Name == typeName) ??
            extensionTypes.FirstOrDefault(t => t.FullName == typeName) ??
            extensionTypes.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) ??
            extensionTypes.FirstOrDefault(t =>
                t.FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true);

        if (extensionType == null)
            return null;

        try
        {
            // Extensions take ICanvas as constructor parameter
            return Activator.CreateInstance(extensionType,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new object[] { canvas },
                null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create extension {extensionType.Name}: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public object? CreateByDisplayName(ICanvas canvas, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var extensionInfo = GetByDisplayName(displayName);
        return extensionInfo != null ? Create(canvas, extensionInfo.Name) : null;
    }

    /// <inheritdoc />
    public bool TryCreate(ICanvas canvas, string typeName, out object? extension, out string? errorMessage)
    {
        extension = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(typeName))
        {
            errorMessage = "Extension type name cannot be empty";
            return false;
        }

        var extensionTypes = GetAvailableTypes().ToList();

        if (extensionTypes.Count == 0)
        {
            errorMessage = "No extensions found in loaded assemblies";
            return false;
        }

        extension = Create(canvas, typeName);

        if (extension == null)
        {
            var similarExtensions = extensionTypes
                .Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .Take(3)
                .ToList();

            if (similarExtensions.Any())
                errorMessage =
                    $"Extension '{typeName}' not found. Did you mean: {string.Join(", ", similarExtensions)}?";
            else
                errorMessage =
                    $"Extension '{typeName}' not found. Available extensions: {string.Join(", ", extensionTypes.Take(5).Select(t => t.Name))}...";
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public List<ExtensionMethodInfo> ExtractMethods(Type extensionType)
    {
        var methods = new List<ExtensionMethodInfo>();
        var typeMethods = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        foreach (var method in typeMethods)
        {
            // Only include methods decorated with ExtensionMethodAttribute
            var methodAttr = method.GetCustomAttributes(typeof(ExtensionMethodAttribute), false)
                .FirstOrDefault() as ExtensionMethodAttribute;

            if (methodAttr == null)
                continue;

            var methodInfo = new ExtensionMethodInfo
            {
                Name = method.Name,
                DisplayName = methodAttr.DisplayName,
                Description = methodAttr.Description,
                Category = methodAttr.Category,
                ReturnType = method.ReturnType,
                IconName = methodAttr.IconName,
                IsDangerous = methodAttr.IsDangerous,
                KeyboardShortcut = methodAttr.KeyboardShortcut,
                Order = methodAttr.Order,
                ReturnsValue = methodAttr.ReturnsValue,
                Parameters = method.GetParameters().Select(p => new ExtensionMethodParameterInfo
                {
                    Name = p.Name ?? "param",
                    ParameterType = p.ParameterType,
                    IsOptional = p.IsOptional,
                    DefaultValue = p.HasDefaultValue ? p.DefaultValue : null,
                    IsParams = p.IsDefined(typeof(ParamArrayAttribute), false)
                }).ToList()
            };

            methods.Add(methodInfo);
        }

        // Sort by Order, then by DisplayName
        return methods.OrderBy(m => m.Order).ThenBy(m => m.DisplayName).ToList();
    }

    /// <inheritdoc />
    public List<ExtensionParameterInfo> ExtractParameters(Type extensionType)
    {
        return ExtractParameters(extensionType, 0);
    }

    /// <summary>
    ///     Recursively extracts the parameter schema for a type. Nested config objects (and lists of them)
    ///     are described by their own [ExtensionParameter] properties, so the schema is fully self-similar
    ///     and the GUI can render arbitrarily structured parameters from a single renderer.
    /// </summary>
    private static List<ExtensionParameterInfo> ExtractParameters(Type type, int depth)
    {
        var parameters = new List<ExtensionParameterInfo>();
        if (depth > 4) return parameters; // guard against accidental cycles / very deep graphs

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (prop.GetCustomAttributes(typeof(ExtensionParameterAttribute), false)
                    .FirstOrDefault() is not ExtensionParameterAttribute paramAttr)
                continue;

            var paramInfo = new ExtensionParameterInfo
            {
                Name = prop.Name,
                DisplayName = paramAttr.DisplayName,
                Description = paramAttr.Description,
                ParameterType = prop.PropertyType,
                MinValue = paramAttr.MinValue,
                MaxValue = paramAttr.MaxValue,
                DefaultValue = paramAttr.DefaultValue,
                Unit = paramAttr.Unit,
                IsReadOnly = paramAttr.ReadOnly || !prop.CanWrite,
                Order = paramAttr.Order,
                CurrentValue = null,
                Kind = ClassifyKind(prop.PropertyType, out var itemType)
            };

            switch (paramInfo.Kind)
            {
                case ExtensionParameterKind.Enum:
                    paramInfo.EnumValues = Enum.GetNames(prop.PropertyType);
                    break;
                case ExtensionParameterKind.Object:
                    paramInfo.Fields = ExtractParameters(prop.PropertyType, depth + 1);
                    break;
                case ExtensionParameterKind.List when itemType != null:
                    paramInfo.Fields = ExtractParameters(itemType, depth + 1);
                    break;
            }

            parameters.Add(paramInfo);
        }

        return parameters
            .OrderBy(p => p.Order)
            .ToList();
    }

    /// <summary>
    ///     Determines the editing shape of a property type. A "config object" is any non-system class/struct
    ///     that exposes at least one [ExtensionParameter] property; a List of such objects becomes a List kind.
    /// </summary>
    private static ExtensionParameterKind ClassifyKind(Type type, out Type? itemType)
    {
        itemType = null;
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t.IsEnum) return ExtensionParameterKind.Enum;
        if (t.Name == "SKColor") return ExtensionParameterKind.Color;

        // List<T> / IEnumerable<T> of a config object.
        var enumerableItem = GetEnumerableItemType(t);
        if (enumerableItem != null && IsConfigObject(enumerableItem))
        {
            itemType = enumerableItem;
            return ExtensionParameterKind.List;
        }

        if (IsConfigObject(t)) return ExtensionParameterKind.Object;

        return ExtensionParameterKind.Scalar;
    }

    /// <summary>A class/struct (not string/primitive/system type) that carries [ExtensionParameter] props.</summary>
    private static bool IsConfigObject(Type t)
    {
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) ||
            t == typeof(DateTime) || t.Name == "SKColor")
            return false;
        if (t.Namespace != null && (t.Namespace.StartsWith("System") || t.Namespace.StartsWith("SkiaSharp")))
            return false;

        return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetCustomAttributes(typeof(ExtensionParameterAttribute), false).Any());
    }

    /// <summary>Returns the element type of an array or generic IEnumerable&lt;T&gt; (excluding string).</summary>
    private static Type? GetEnumerableItemType(Type t)
    {
        if (t == typeof(string)) return null;
        if (t.IsArray) return t.GetElementType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            return t.GetGenericArguments()[0];

        foreach (var i in t.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return i.GetGenericArguments()[0];

        return null;
    }

    /// <summary>
    ///     Finds the extension method name for a given extension type
    /// </summary>
    private static string? FindExtensionMethodName(Type extensionType)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
            try
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    if (!type.IsSealed || !type.IsAbstract) continue; // Must be static class

                    var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public);

                    foreach (var method in methods)
                        // Check if it's an extension method that returns our type
                        if (method.IsDefined(typeof(ExtensionAttribute), false) &&
                            method.ReturnType == extensionType)
                            return method.Name;
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }

        return null;
    }
}
