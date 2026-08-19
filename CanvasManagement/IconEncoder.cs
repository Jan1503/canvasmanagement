using System.Text;

namespace CanvasManagement;

/// <summary>
///     Helper class to convert SVG icons to base64-encoded strings for embedding in ExtensionInfo attributes
/// </summary>
public static class IconEncoder
{
    /// <summary>
    ///     Converts an SVG file to a base64-encoded string
    /// </summary>
    public static string SvgToBase64(string svgContent)
    {
        var bytes = Encoding.UTF8.GetBytes(svgContent);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    ///     Converts a base64-encoded string back to SVG content
    /// </summary>
    public static string Base64ToSvg(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    ///     Creates a data URI for direct use in img src attributes
    ///     Usage: &lt;img src="@extension.GetIconDataUri()" /&gt;
    /// </summary>
    public static string ToDataUri(string base64IconData)
    {
        return $"data:image/svg+xml;base64,{base64IconData}";
    }

    /// <summary>
    ///     Reads an SVG file and converts it to base64
    /// </summary>
    public static string SvgFileToBase64(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"SVG file not found: {filePath}");

        var svgContent = File.ReadAllText(filePath);
        return SvgToBase64(svgContent);
    }

    /// <summary>
    ///     Generate C# code snippet for embedding icon data in attribute
    /// </summary>
    public static string GenerateAttributeCode(string extensionName, string iconFilePath)
    {
        var base64 = SvgFileToBase64(iconFilePath);
        return $@"[ExtensionInfo(""{extensionName}"",
    ""Description here"",
    ""Category"",
    IconData = ""{base64}"")]";
    }
}