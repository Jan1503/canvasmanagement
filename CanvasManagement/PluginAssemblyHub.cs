using System.Reflection;
using System.Runtime.Loader;

namespace CanvasManagement;

/// <summary>
///     Loads extension/filter DLLs into collectible <see cref="AssemblyLoadContext"/>s from a memory
///     copy so the files on disk are not locked. Host assemblies are never loaded from the plugin
///     folder — plugins must bind <c>CanvasManagement.Interfaces</c> (and SkiaSharp) from verpixeld.
/// </summary>
public static class PluginAssemblyHub
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SharedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CanvasManagement",
        "CanvasManagement.Interfaces",
        "CanvasManagement.BdfFontManager",
        "PixPlane",
        "verpixeld",
        "SkiaSharp",
        "SkiaSharp.HarfBuzz",
        "SkiaSharp.NativeAssets.Linux",
        "SkiaSharp.NativeAssets.Linux.NoDependencies",
        "netstandard"
    };

    public static IReadOnlyList<Assembly> LoadedAssemblies
    {
        get
        {
            lock (Gate) return Loaded.Values.Select(e => e.Assembly).ToList();
        }
    }

    /// <summary>Plugin ALCs plus the host default context (in-process types, tests).</summary>
    public static IEnumerable<Assembly> AssembliesToScan()
    {
        foreach (var a in LoadedAssemblies) yield return a;
        foreach (var a in AssemblyLoadContext.Default.Assemblies) yield return a;
    }

    public static bool IsSharedHostName(string simpleName)
    {
        if (string.IsNullOrWhiteSpace(simpleName)) return false;
        if (SharedNames.Contains(simpleName)) return true;
        if (simpleName.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase)) return true;
        if (simpleName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) return true;
        if (simpleName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsSharedHostPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return IsSharedHostName(name);
    }

    /// <summary>
    ///     Load a managed plugin DLL. Returns null when the file is a host/shared copy that must be skipped.
    /// </summary>
    public static Assembly? Load(string dllPath)
    {
        dllPath = Path.GetFullPath(dllPath);
        if (IsSharedHostPath(dllPath))
        {
            Console.WriteLine($"  Skipping host assembly copy: {Path.GetFileName(dllPath)}");
            return null;
        }

        try
        {
            _ = AssemblyName.GetAssemblyName(dllPath);
        }
        catch
        {
            return null; // native / not a managed assembly
        }

        lock (Gate)
        {
            if (Loaded.TryGetValue(dllPath, out var existing))
                return existing.Assembly;

            var alc = new PluginLoadContext(dllPath);
            Assembly asm;
            try
            {
                asm = alc.LoadPlugin(dllPath);
            }
            catch
            {
                try { alc.Unload(); } catch { /* ignore */ }
                throw;
            }

            Loaded[dllPath] = new Entry(alc, asm, dllPath);
            return asm;
        }
    }

    public static int UnloadUnder(string directory)
    {
        directory = Path.GetFullPath(directory);
        List<PluginLoadContext> toUnload;
        lock (Gate)
        {
            var keys = Loaded.Keys
                .Where(p => p.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                .ToList();
            toUnload = [];
            foreach (var key in keys)
            {
                toUnload.Add(Loaded[key].Context);
                Loaded.Remove(key);
            }
        }

        foreach (var alc in toUnload)
        {
            try { alc.Unload(); }
            catch (Exception ex) { Console.WriteLine($"  ALC unload: {ex.Message}"); }
        }

        return toUnload.Count;
    }

    /// <summary>GC after unload. Returns how many ALCs still look pinned.</summary>
    public static int CollectUnloaded()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return 0;
    }

    private readonly record struct Entry(PluginLoadContext Context, Assembly Assembly, string Path);
}
