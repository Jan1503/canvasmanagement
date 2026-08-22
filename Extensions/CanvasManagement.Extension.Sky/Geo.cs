using System.Globalization;

namespace CanvasManagement.Extension.Sky;

internal static class Geo
{
    public static bool TryCoord(string? value, double fallback, out double v)
    {
        if (double.TryParse((value ?? "").Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out v))
            return true;
        v = fallback;
        return false;
    }
}
