using System.Globalization;
using System.Text.Json;
using SkiaSharp;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     Optional Material Design Icons renderer. If the MDI webfont and its name→codepoint metadata are present
///     in the Fonts directory, this renders any icon by name; otherwise the caller falls back to the bold
///     hand-drawn icon set. Drop these two files into the deploy's Fonts/ folder to enable it:
///       - materialdesignicons-webfont.ttf   (from the @mdi/font package)
///       - mdi-meta.json                      (the meta.json from @mdi/svg: [{ name, codepoint, aliases }])
/// </summary>
internal static class MdiFont
{
    private static readonly object Lock = new();
    private static bool _loaded;
    private static SKTypeface? _typeface;
    private static Dictionary<string, int>? _codepoints;

    private static bool Available
    {
        get
        {
            EnsureLoaded();
            return _typeface != null && _codepoints is { Count: > 0 };
        }
    }

    /// <summary>Draws the named MDI glyph centered in a size×size box. Returns false if unavailable/unknown.</summary>
    public static bool TryDraw(SKCanvas c, string? name, float cx, float cy, float size, SKColor color)
    {
        if (string.IsNullOrWhiteSpace(name) || !Available) return false;

        var n = name.Trim();
        if (n.StartsWith("mdi:", StringComparison.OrdinalIgnoreCase)) n = n[4..];
        if (!_codepoints!.TryGetValue(n, out var cp)) return false;

        var glyph = char.ConvertFromUtf32(cp);
        using var font = new SKFont(_typeface) { Size = size, Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        font.MeasureText(glyph, out var bounds, paint);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        // Scale the glyph to fit the icon box, then center it on (cx, cy).
        var scale = Math.Min(size / bounds.Width, size / bounds.Height);
        if (scale is > 0 and < 1f)
        {
            font.Size = size * scale;
            font.MeasureText(glyph, out bounds, paint);
        }

        c.DrawText(glyph, cx - bounds.MidX, cy - bounds.MidY, font, paint);
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Lock)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var ttf = FindAsset("materialdesignicons-webfont.ttf", "MaterialDesignIcons.ttf", "mdi.ttf");
                var meta = FindAsset("mdi-meta.json", "meta.json", "materialdesignicons.json");
                if (ttf == null || meta == null)
                {
                    Console.WriteLine("[MDI] Font/meta not found in Fonts dir — using built-in drawn icons.");
                    return;
                }

                _typeface = SKTypeface.FromFile(ttf);
                _codepoints = ParseMeta(meta);
                Console.WriteLine($"[MDI] Loaded {_codepoints.Count} icon names from {Path.GetFileName(ttf)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MDI] Load failed ({ex.Message}) — using built-in drawn icons.");
                _typeface = null;
                _codepoints = null;
            }
        }
    }

    private static Dictionary<string, int> ParseMeta(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!el.TryGetProperty("name", out var nameEl) || !el.TryGetProperty("codepoint", out var cpEl))
                continue;
            var name = nameEl.GetString();
            var cpStr = cpEl.GetString();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cpStr)) continue;
            if (!int.TryParse(cpStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp)) continue;

            map[name] = cp;
            if (el.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
                foreach (var a in aliases.EnumerateArray())
                    if (a.GetString() is { Length: > 0 } alias)
                        map.TryAdd(alias, cp);
        }

        return map;
    }

    private static string? FindAsset(params string[] names)
    {
        var dirs = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fonts"),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..", "Fonts")
        };
        foreach (var dir in dirs)
        foreach (var name in names)
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }

        return null;
    }
}
