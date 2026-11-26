using System.Globalization;

namespace CanvasManagement.Interfaces;

/// <summary>Numbers as a wall shows them.</summary>
public static class DisplayNumber
{
    /// <summary>
    ///     <paramref name="value"/> rounded to <paramref name="decimals"/> places, invariant culture, never
    ///     "-0": -0.3 °C shows as 0, not as the negative zero plain rounding gives.
    /// </summary>
    public static string Round(double value, int decimals = 0)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "–";
        var r = Math.Round(value, Math.Clamp(decimals, 0, 6), MidpointRounding.AwayFromZero);
        if (r == 0) r = 0; // turns -0.0 into +0.0
        return r.ToString("F" + Math.Clamp(decimals, 0, 6), CultureInfo.InvariantCulture);
    }
}
