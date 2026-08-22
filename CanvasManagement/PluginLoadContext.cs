using System.Reflection;
using System.Runtime.Loader;

namespace CanvasManagement;

/// <summary>
///     Collectible load context for one plugin DLL. Shared host assemblies
///     (Interfaces, SkiaSharp, CanvasManagement, …) resolve from the default context so plugins
///     never bind a second copy of those types.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDir;
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath)
        : base($"plugin:{Path.GetFileName(pluginPath)}:{Guid.NewGuid():N}", isCollectible: true)
    {
        _pluginDir = Path.GetDirectoryName(Path.GetFullPath(pluginPath)) ?? AppContext.BaseDirectory;
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    public Assembly LoadPlugin(string pluginPath)
    {
        using var pe = new MemoryStream(File.ReadAllBytes(pluginPath), writable: false);
        var pdbPath = Path.ChangeExtension(pluginPath, ".pdb");
        if (File.Exists(pdbPath))
        {
            using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath), writable: false);
            return LoadFromStream(pe, pdb);
        }

        return LoadFromStream(pe);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var loaded in Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        if (PluginAssemblyHub.IsSharedHostName(name))
            return null;

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved) && !PluginAssemblyHub.IsSharedHostPath(resolved))
            return LoadFromStream(Copy(resolved));

        var sibling = Path.Combine(_pluginDir, name + ".dll");
        if (File.Exists(sibling) && !PluginAssemblyHub.IsSharedHostPath(sibling))
            return LoadFromStream(Copy(sibling));

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
            return LoadUnmanagedDllFromPath(resolved);

        foreach (var candidate in new[]
                 {
                     Path.Combine(_pluginDir, unmanagedDllName),
                     Path.Combine(_pluginDir, unmanagedDllName + ".so"),
                     Path.Combine(_pluginDir, "lib" + unmanagedDllName + ".so")
                 })
        {
            if (File.Exists(candidate))
                return LoadUnmanagedDllFromPath(candidate);
        }

        return nint.Zero;
    }

    private static MemoryStream Copy(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new MemoryStream(bytes, writable: false);
    }
}
