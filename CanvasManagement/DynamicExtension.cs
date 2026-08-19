using CanvasManagement.Interfaces;

namespace CanvasManagement;

/// <summary>
///     Provides strongly-typed access to dynamically created extensions
/// </summary>
public class DynamicExtension : IDisposable
{
    internal DynamicExtension(object instance)
    {
        Instance = instance;
        Type = instance.GetType();
    }

    /// <summary>
    ///     Gets the underlying extension instance
    /// </summary>
    public object Instance { get; }

    /// <summary>
    ///     Gets the extension type
    /// </summary>
    public Type Type { get; }

    /// <summary>
    ///     Gets whether the extension is running
    /// </summary>
    public bool IsRunning
    {
        get
        {
            var prop = Type.GetProperty("IsRunning");
            return prop != null && (bool)(prop.GetValue(Instance) ?? false);
        }
    }

    /// <summary>
    ///     Gets the extension name
    /// </summary>
    public string Name
    {
        get
        {
            var prop = Type.GetProperty("Name");
            return prop?.GetValue(Instance)?.ToString() ?? Type.Name;
        }
    }

    public void Dispose()
    {
        if (Instance is IDisposable disposable) disposable.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Gets a property value
    /// </summary>
    public object? GetProperty(string propertyName)
    {
        var prop = Type.GetProperty(propertyName);
        return prop?.GetValue(Instance);
    }

    /// <summary>
    ///     Sets a property value
    /// </summary>
    public void SetProperty(string propertyName, object value)
    {
        var prop = Type.GetProperty(propertyName);
        if (prop != null && prop.CanWrite) prop.SetValue(Instance, value);
    }

    /// <summary>
    ///     Calls the Start method
    /// </summary>
    public void Start()
    {
        var method = Type.GetMethod("Start");
        method?.Invoke(Instance, null);
    }

    /// <summary>
    ///     Calls the Stop method
    /// </summary>
    public void Stop()
    {
        var method = Type.GetMethod("Stop");
        method?.Invoke(Instance, null);
    }

    /// <summary>
    ///     Invokes a method by name with optional parameters
    /// </summary>
    /// <param name="methodName">Name of the method to invoke</param>
    /// <param name="parameters">Parameters to pass to the method</param>
    /// <returns>Return value of the method, or null if void</returns>
    /// <exception cref="InvalidOperationException">If method not found</exception>
    public object? InvokeMethod(string methodName, params object?[] parameters)
    {
        var method = Type.GetMethod(methodName);
        if (method == null)
            throw new InvalidOperationException($"Method '{methodName}' not found on extension '{Name}'");

        try
        {
            return method.Invoke(Instance, parameters.Length > 0 ? parameters : null);
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            // Unwrap the inner exception for clearer error messages
            throw ex.InnerException ?? ex;
        }
    }

    /// <summary>
    ///     Tries to invoke a method by name, returning success status
    /// </summary>
    /// <param name="methodName">Name of the method to invoke</param>
    /// <param name="result">Output: return value of the method</param>
    /// <param name="error">Output: error message if failed</param>
    /// <param name="parameters">Parameters to pass to the method</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool TryInvokeMethod(string methodName, out object? result, out string? error, params object?[] parameters)
    {
        result = null;
        error = null;

        var method = Type.GetMethod(methodName);
        if (method == null)
        {
            error = $"Method '{methodName}' not found on extension '{Name}'";
            return false;
        }

        try
        {
            result = method.Invoke(Instance, parameters.Length > 0 ? parameters : null);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
    }

    /// <summary>
    ///     Checks if the extension has a method with the given name
    /// </summary>
    public bool HasMethod(string methodName)
    {
        return Type.GetMethod(methodName) != null;
    }

    /// <summary>
    ///     Gets information about available methods marked with ExtensionMethodAttribute
    /// </summary>
    public IEnumerable<ExtensionMethodInfo> GetAvailableMethods()
    {
        return ExtensionDiscoveryService.Default.ExtractMethods(Type);
    }
}