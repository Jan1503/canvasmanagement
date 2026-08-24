using System.Collections.Concurrent;

namespace CanvasManagement.BdfFontManager;

/// <summary>
///     Global BDF font registry and manager
///     Provides system-wide font caching and management for pixel-perfect text rendering on LED matrices
/// </summary>
public static class BdfFontRegistry
{
    private static readonly ConcurrentDictionary<string, BdfFont> FontCache = new();
    private static readonly ConcurrentDictionary<string, string> FontPaths = new();
    private static readonly object Lock = new();

    private static string? _defaultFontName;
    private static string? _fontsDirectory;
    private static bool _autoLoadAttempted;

    /// <summary>
    ///     Gets or sets the default font name used when no specific font is requested
    /// </summary>
    public static string? DefaultFontName
    {
        get => _defaultFontName;
        set
        {
            _defaultFontName = value;
            Console.WriteLine($"[BDF] Default font set to: {value}");
        }
    }

    /// <summary>
    ///     Gets or sets the directory containing BDF font files
    ///     All .bdf files in this directory will be automatically discovered
    /// </summary>
    public static string? FontsDirectory
    {
        get => _fontsDirectory;
        set
        {
            _fontsDirectory = value;
            if (!string.IsNullOrWhiteSpace(value)) LoadFontsFromDirectory(value);
        }
    }

    /// <summary>
    ///     Gets the number of fonts currently loaded in the registry
    /// </summary>
    public static int LoadedFontCount => FontCache.Count;

    /// <summary>
    ///     Gets the number of registered fonts (loaded or pending)
    /// </summary>
    public static int RegisteredFontCount => FontPaths.Count;

    /// <summary>
    ///     Gets all registered font names
    /// </summary>
    public static IEnumerable<string> RegisteredFonts => FontPaths.Keys;

    /// <summary>
    ///     Picks the registered bitmap font whose glyph height best fits the target pixel height,
    ///     enabling resolution-independent text. Relies on the conventional "WxH" font naming
    ///     (e.g. "10x20", "5x8", "6x13B"). Returns the largest font not taller than
    ///     <paramref name="targetHeight" />, or the smallest available size-named font if all are
    ///     taller. Returns <see langword="null" /> when no size-named fonts are registered.
    /// </summary>
    public static string? GetBestFontForHeight(int targetHeight)
    {
        if (FontPaths.Count == 0 && !_autoLoadAttempted)
            LoadFontsFromCommonLocations();

        string? bestFit = null;
        var bestFitHeight = 0;
        string? smallest = null;
        var smallestHeight = int.MaxValue;

        foreach (var name in FontPaths.Keys)
        {
            if (!TryParseFontHeight(name, out var h)) continue;

            if (h < smallestHeight)
            {
                smallestHeight = h;
                smallest = name;
            }

            if (h <= targetHeight && h > bestFitHeight)
            {
                bestFitHeight = h;
                bestFit = name;
            }
        }

        return bestFit ?? smallest;
    }

    /// <summary>
    ///     Parses the glyph height from a conventional "WxH" font name (e.g. "10x20" -> 20,
    ///     "6x13B" -> 13). Returns false for names that don't follow the convention.
    /// </summary>
    private static bool TryParseFontHeight(string fontName, out int height)
    {
        height = 0;
        if (string.IsNullOrEmpty(fontName)) return false;

        var xIndex = fontName.IndexOf('x');
        if (xIndex <= 0 || xIndex >= fontName.Length - 1) return false;

        // Characters before 'x' must all be digits (the width).
        for (var i = 0; i < xIndex; i++)
            if (!char.IsDigit(fontName[i]))
                return false;

        // Collect the digit run after 'x' (the height); ignore any trailing style suffix (B/O).
        var start = xIndex + 1;
        var end = start;
        while (end < fontName.Length && char.IsDigit(fontName[end])) end++;
        if (end == start) return false;

        return int.TryParse(fontName.AsSpan(start, end - start), out height);
    }

    /// <summary>
    ///     Automatically loads fonts from common locations
    ///     Called automatically on first font request if no fonts are registered
    /// </summary>
    public static void LoadFontsFromCommonLocations()
    {
        if (_autoLoadAttempted)
            return;

        _autoLoadAttempted = true;
        Console.WriteLine("[BDF] Auto-discovering fonts from common locations...");

        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var commonLocations = new[]
        {
            // Local application directories
            Path.Combine(baseDirectory, "Fonts"),
            Path.Combine(baseDirectory, "BDF"),
            Path.Combine(baseDirectory, "fonts"),
            Path.Combine(baseDirectory, "bdf"),
            Path.Combine(baseDirectory, "Resources", "Fonts"),
            Path.Combine(baseDirectory, "Assets", "Fonts"),
            Path.Combine(baseDirectory, "Data", "Fonts"),

            // Parent directory
            Path.Combine(Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory, "Fonts"),

            // User profile directories
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".canvasmanagement",
                "fonts"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CanvasManagement",
                "Fonts"),

            // System-wide Linux locations
            "/usr/share/fonts/X11/misc",
            "/usr/share/fonts/misc",
            "/usr/local/share/fonts",

            // Windows common locations
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "BDF")
        };

        var fontsFound = 0;
        foreach (var location in commonLocations)
            if (Directory.Exists(location))
            {
                Console.WriteLine($"[BDF] Scanning: {location}");
                var count = LoadFontsFromDirectory(location, true);
                if (count > 0)
                {
                    Console.WriteLine($"[BDF] ✓ Found {count} font(s) in: {location}");
                    fontsFound += count;
                }
            }

        if (fontsFound > 0)
        {
            Console.WriteLine($"[BDF] Auto-discovery complete: {fontsFound} font(s) registered");

            // Set a sensible default font if none is set
            if (_defaultFontName == null && FontPaths.Count > 0)
            {
                // Prefer common sizes in order of usefulness for LED matrices
                var preferredFonts = new[] { "8x13", "7x13", "6x10", "8x16", "10x20", "6x13", "5x8", "4x6" };

                foreach (var preferred in preferredFonts)
                    if (FontPaths.ContainsKey(preferred))
                    {
                        _defaultFontName = preferred;
                        Console.WriteLine($"[BDF] Auto-selected default font: {preferred}");
                        break;
                    }

                // If no preferred font found, use the first one
                if (_defaultFontName == null)
                {
                    _defaultFontName = FontPaths.Keys.First();
                    Console.WriteLine($"[BDF] Auto-selected default font: {_defaultFontName}");
                }
            }
        }
        else
        {
            Console.WriteLine("[BDF] ⚠ No fonts found in common locations. You can:");
            Console.WriteLine("[BDF]   - Register fonts: BdfFontRegistry.RegisterFont(\"name\", \"path.bdf\")");
            Console.WriteLine("[BDF]   - Set directory: BdfFontRegistry.FontsDirectory = \"path\"");
            Console.WriteLine("[BDF]   - Download fonts: https://github.com/Tecate/bitmap-fonts");
            Console.WriteLine("[BDF]   - Convert TrueType fonts: Use CanvasManagement.Tools.TtfToBdf");
        }
    }

    /// <summary>
    ///     Registers a BDF font file with a friendly name
    /// </summary>
    /// <param name="name">Friendly name for the font (e.g., "8x16", "tiny", "console")</param>
    /// <param name="fontFilePath">Path to the .bdf font file</param>
    public static void RegisterFont(string name, string fontFilePath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Font name cannot be empty", nameof(name));

        if (!File.Exists(fontFilePath))
            throw new FileNotFoundException($"BDF font file not found: {fontFilePath}");

        lock (Lock)
        {
            FontPaths[name] = fontFilePath;

            // Set as default if it's the first font registered
            if (_defaultFontName == null)
            {
                _defaultFontName = name;
                Console.WriteLine($"[BDF] Registered and set default font: {name}");
            }
            else
            {
                Console.WriteLine($"[BDF] Registered font: {name}");
            }
        }
    }

    /// <summary>
    ///     Loads all .bdf font files from a directory
    ///     Font names are derived from filenames (without extension)
    /// </summary>
    /// <param name="directory">Directory containing .bdf files</param>
    /// <param name="silent">If true, suppresses console output except errors</param>
    /// <returns>Number of fonts registered</returns>
    public static int LoadFontsFromDirectory(string directory, bool silent = false)
    {
        if (!Directory.Exists(directory))
        {
            if (!silent)
                Console.WriteLine($"[BDF] Fonts directory not found: {directory}");
            return 0;
        }

        var bdfFiles = Directory.GetFiles(directory, "*.bdf", SearchOption.TopDirectoryOnly);

        if (!silent)
        {
            Console.WriteLine($"[BDF] Loading fonts from: {directory}");
            Console.WriteLine($"[BDF] Found {bdfFiles.Length} .bdf file(s)");
        }

        var registered = 0;
        foreach (var bdfFile in bdfFiles)
            try
            {
                var fontName = Path.GetFileNameWithoutExtension(bdfFile);

                // Don't overwrite existing registrations
                if (!FontPaths.ContainsKey(fontName))
                {
                    lock (Lock)
                    {
                        FontPaths[fontName] = bdfFile;

                        // Set as default if it's the first font registered
                        if (_defaultFontName == null)
                        {
                            _defaultFontName = fontName;
                            if (!silent)
                                Console.WriteLine($"[BDF] Registered and set default font: {fontName}");
                        }
                        else
                        {
                            if (!silent)
                                Console.WriteLine($"[BDF] Registered font: {fontName}");
                        }
                    }

                    registered++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BDF] Failed to register {Path.GetFileName(bdfFile)}: {ex.Message}");
            }

        return registered;
    }

    /// <summary>
    ///     Gets a font by name, loading it from disk if necessary
    /// </summary>
    /// <param name="fontName">Name of the font to retrieve (null = default font)</param>
    /// <returns>BdfFont instance</returns>
    public static BdfFont GetFont(string? fontName = null)
    {
        // Auto-load fonts from common locations if none are registered
        if (FontPaths.Count == 0 && !_autoLoadAttempted) LoadFontsFromCommonLocations();

        // Use default font if none specified
        fontName ??= _defaultFontName;

        if (string.IsNullOrWhiteSpace(fontName))
            throw new InvalidOperationException(
                "No font name specified and no default font set. " +
                "Available options:\n" +
                "  1. Call BdfFontRegistry.LoadFontsFromCommonLocations()\n" +
                "  2. Call BdfFontRegistry.RegisterFont(\"name\", \"path.bdf\")\n" +
                "  3. Set BdfFontRegistry.FontsDirectory = \"path\"\n" +
                "  4. Download fonts: https://github.com/Tecate/bitmap-fonts");

        // Check if font is registered
        if (!FontPaths.ContainsKey(fontName))
        {
            // Try auto-loading one more time
            if (!_autoLoadAttempted) LoadFontsFromCommonLocations();

            if (!FontPaths.ContainsKey(fontName))
            {
                var availableFonts = FontPaths.Keys.Take(10).ToList();
                var fontList = availableFonts.Any()
                    ? string.Join(", ", availableFonts) + (FontPaths.Count > 10 ? "..." : "")
                    : "none";

                throw new ArgumentException($"Font '{fontName}' is not registered. " +
                                            $"Available fonts: {fontList}");
            }
        }

        // Return cached font or load from disk
        return FontCache.GetOrAdd(fontName, name =>
        {
            var fontPath = FontPaths[name];
            Console.WriteLine($"[BDF] Loading font '{name}' from: {fontPath}");
            var font = new BdfFont(fontPath);
            Console.WriteLine($"[BDF] Font '{name}' loaded successfully - {font}");
            return font;
        });
    }

    /// <summary>
    ///     Checks if a font is registered
    /// </summary>
    public static bool IsFontRegistered(string fontName)
    {
        return FontPaths.ContainsKey(fontName);
    }

    /// <summary>
    ///     Unloads a font from the cache (keeps registration)
    /// </summary>
    public static void UnloadFont(string fontName)
    {
        if (FontCache.TryRemove(fontName, out var font))
            Console.WriteLine($"[BDF] Unloaded font from cache: {fontName}");
    }

    /// <summary>
    ///     Clears all fonts from cache (keeps registrations)
    /// </summary>
    public static void ClearCache()
    {
        var count = FontCache.Count;
        FontCache.Clear();
        Console.WriteLine($"[BDF] Cleared {count} font(s) from cache");
    }

    /// <summary>
    ///     Clears all registrations and cache
    /// </summary>
    public static void Reset()
    {
        FontCache.Clear();
        FontPaths.Clear();
        _defaultFontName = null;
        _fontsDirectory = null;
        _autoLoadAttempted = false;
        Console.WriteLine("[BDF] Font registry reset");
    }

    /// <summary>
    ///     Gets font information
    /// </summary>
    public static string GetFontInfo(string fontName)
    {
        if (!FontPaths.ContainsKey(fontName))
            return $"Font '{fontName}' not registered";

        var isLoaded = FontCache.ContainsKey(fontName);
        var path = FontPaths[fontName];
        var isDefault = fontName == _defaultFontName;

        var info = $"Font: {fontName}\n" +
                   $"Path: {path}\n" +
                   $"Loaded: {(isLoaded ? "Yes" : "No")}\n" +
                   $"Default: {(isDefault ? "Yes" : "No")}";

        if (isLoaded && FontCache.TryGetValue(fontName, out var font)) info += $"\n{font}";

        return info;
    }

    /// <summary>
    ///     Gets a summary of all registered fonts
    /// </summary>
    public static string GetRegistrySummary()
    {
        var summary = "BDF Font Registry\n";
        summary += "================\n";
        summary += $"Registered: {RegisteredFontCount} font(s)\n";
        summary += $"Loaded: {LoadedFontCount} font(s)\n";
        summary += $"Default: {_defaultFontName ?? "none"}\n\n";

        if (RegisteredFontCount > 0)
        {
            summary += "Available Fonts:\n";
            foreach (var fontName in FontPaths.Keys.OrderBy(f => f))
            {
                var isDefault = fontName == _defaultFontName ? " [DEFAULT]" : "";
                var isLoaded = FontCache.ContainsKey(fontName) ? " [LOADED]" : "";
                summary += $"  - {fontName}{isDefault}{isLoaded}\n";
            }
        }

        return summary;
    }
}