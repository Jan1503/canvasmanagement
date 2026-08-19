using SkiaSharp;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     Bold, code-drawn icons that read well at LED resolution. Mapped from an entity's mdi:* icon name (or
///     its domain) to a small set of canonical glyphs — no font asset required, works offline.
/// </summary>
internal static class HaIcons
{
    public static void Draw(SKCanvas c, string? mdiName, string? deviceClass, string domain,
        float cx, float cy, float size, SKColor color)
    {
        // Prefer the real Material Design Icons glyph when the font asset is present and the icon box is large
        // enough to read; otherwise fall back to the bold hand-drawn set (always available, fully offline).
        if (size >= 12f && MdiFont.TryDraw(c, ResolveMdiName(mdiName, deviceClass, domain), cx, cy, size, color))
            return;

        DrawKey(c, MapKey(mdiName, deviceClass, domain), cx, cy, size, color);
    }

    private static void DrawKey(SKCanvas c, string key, float cx, float cy, float size, SKColor color)
    {
        var r = size * 0.5f;
        var sw = Math.Max(1f, size * 0.1f);

        using var stroke = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = sw, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

        switch (key)
        {
            case "bolt":
                using (var p = new SKPath())
                {
                    p.MoveTo(cx + r * 0.25f, cy - r);
                    p.LineTo(cx - r * 0.45f, cy + r * 0.1f);
                    p.LineTo(cx + r * 0.05f, cy + r * 0.1f);
                    p.LineTo(cx - r * 0.25f, cy + r);
                    p.LineTo(cx + r * 0.5f, cy - r * 0.15f);
                    p.LineTo(cx, cy - r * 0.15f);
                    p.Close();
                    c.DrawPath(p, fill);
                }

                break;

            case "thermometer":
                c.DrawLine(cx, cy - r * 0.8f, cx, cy + r * 0.45f, stroke);
                c.DrawCircle(cx, cy + r * 0.6f, r * 0.32f, fill);
                c.DrawCircle(cx, cy - r * 0.8f, sw * 0.5f, fill);
                break;

            case "droplet":
                using (var p = new SKPath())
                {
                    p.MoveTo(cx, cy - r * 0.9f);
                    p.CubicTo(cx + r * 0.8f, cy, cx + r * 0.5f, cy + r * 0.8f, cx, cy + r * 0.8f);
                    p.CubicTo(cx - r * 0.5f, cy + r * 0.8f, cx - r * 0.8f, cy, cx, cy - r * 0.9f);
                    p.Close();
                    c.DrawPath(p, fill);
                }

                break;

            case "bulb":
                c.DrawCircle(cx, cy - r * 0.2f, r * 0.55f, fill);
                c.DrawRect(cx - r * 0.25f, cy + r * 0.35f, r * 0.5f, r * 0.4f, fill);
                break;

            case "door":
                c.DrawRect(cx - r * 0.55f, cy - r * 0.85f, r * 1.1f, r * 1.7f, stroke);
                c.DrawCircle(cx + r * 0.25f, cy, sw * 0.7f, fill);
                break;

            case "motion":
                c.DrawCircle(cx, cy - r * 0.6f, r * 0.22f, fill); // head
                c.DrawLine(cx, cy - r * 0.35f, cx, cy + r * 0.25f, stroke); // body
                c.DrawLine(cx, cy - r * 0.1f, cx + r * 0.4f, cy - r * 0.3f, stroke); // arm
                c.DrawLine(cx, cy + r * 0.25f, cx - r * 0.35f, cy + r * 0.8f, stroke); // leg
                c.DrawLine(cx, cy + r * 0.25f, cx + r * 0.35f, cy + r * 0.8f, stroke); // leg
                break;

            case "battery":
                c.DrawRect(cx - r * 0.55f, cy - r * 0.6f, r * 1.1f, r * 1.2f, stroke);
                c.DrawRect(cx - r * 0.2f, cy - r * 0.8f, r * 0.4f, r * 0.2f, fill);
                break;

            case "plug":
                c.DrawRect(cx - r * 0.4f, cy - r * 0.5f, r * 0.8f, r * 0.8f, fill);
                c.DrawLine(cx - r * 0.2f, cy - r * 0.8f, cx - r * 0.2f, cy - r * 0.5f, stroke);
                c.DrawLine(cx + r * 0.2f, cy - r * 0.8f, cx + r * 0.2f, cy - r * 0.5f, stroke);
                c.DrawLine(cx, cy + r * 0.3f, cx, cy + r * 0.8f, stroke);
                break;

            case "fan":
                c.DrawCircle(cx, cy, sw * 0.7f, fill);
                for (var i = 0; i < 3; i++)
                {
                    var a = i * 2 * Math.PI / 3;
                    c.DrawLine(cx, cy,
                        cx + (float)Math.Cos(a) * r * 0.85f,
                        cy + (float)Math.Sin(a) * r * 0.85f, stroke);
                }

                break;

            case "lock":
                c.DrawRect(cx - r * 0.5f, cy - r * 0.05f, r, r * 0.8f, fill);
                using (var p = new SKPath())
                {
                    p.MoveTo(cx - r * 0.3f, cy - r * 0.05f);
                    p.LineTo(cx - r * 0.3f, cy - r * 0.45f);
                    p.ArcTo(new SKRect(cx - r * 0.3f, cy - r * 0.75f, cx + r * 0.3f, cy - r * 0.15f), 180, 180, false);
                    p.LineTo(cx + r * 0.3f, cy - r * 0.05f);
                    c.DrawPath(p, stroke);
                }

                break;

            case "cloud": // air quality (CO2, CO, PM, VOC, gas, AQI)
                c.DrawCircle(cx - r * 0.35f, cy + r * 0.1f, r * 0.4f, fill);
                c.DrawCircle(cx + r * 0.05f, cy - r * 0.1f, r * 0.5f, fill);
                c.DrawCircle(cx + r * 0.45f, cy + r * 0.1f, r * 0.38f, fill);
                c.DrawRect(cx - r * 0.7f, cy + r * 0.1f, r * 1.5f, r * 0.45f, fill);
                break;

            default: // generic info dot
                c.DrawCircle(cx, cy, r * 0.7f, stroke);
                c.DrawCircle(cx, cy - r * 0.25f, sw * 0.6f, fill);
                c.DrawLine(cx, cy - r * 0.05f, cx, cy + r * 0.4f, stroke);
                break;
        }
    }

    // Resolve in priority order: explicit mdi icon name → device_class → entity domain → generic.
    private static string MapKey(string? mdiName, string? deviceClass, string domain)
    {
        return FromName(mdiName) ?? FromDeviceClass(deviceClass) ?? FromDomain(domain) ?? "generic";
    }

    private static string? FromName(string? mdiName)
    {
        var n = (mdiName ?? "").ToLowerInvariant();
        if (n.StartsWith("mdi:")) n = n[4..];
        if (n.Length == 0) return null;

        bool Has(params string[] keys) => keys.Any(k => n.Contains(k));

        if (Has("flash", "power", "lightning", "transmission", "watt", "energy", "gauge")) return "bolt";
        if (Has("thermometer", "temperature", "temp")) return "thermometer";
        if (Has("water", "humidity", "droplet", "weather-rainy")) return "droplet";
        if (Has("light", "bulb", "lamp", "ceiling")) return "bulb";
        if (Has("door", "garage", "window", "gate")) return "door";
        if (Has("motion", "walk", "occupancy", "presence", "account", "human")) return "motion";
        if (Has("battery")) return "battery";
        if (Has("plug", "socket", "outlet")) return "plug";
        if (Has("fan")) return "fan";
        if (Has("lock")) return "lock";
        return null;
    }

    // HA derives default icons from device_class when no custom icon is set (so "icon" is absent).
    private static string? FromDeviceClass(string? deviceClass)
    {
        var dc = (deviceClass ?? "").ToLowerInvariant();
        if (dc.Length == 0) return null;

        return dc switch
        {
            "temperature" => "thermometer",
            "power" or "energy" or "current" or "voltage" or "apparent_power" or "reactive_power"
                or "power_factor" or "frequency" => "bolt",
            "humidity" or "moisture" or "water" or "precipitation" or "precipitation_intensity"
                or "volume" or "gas" => "droplet",
            "battery" => "battery",
            "motion" or "occupancy" or "presence" or "moving" or "vibration" => "motion",
            "door" or "garage_door" or "window" or "opening" or "garage" => "door",
            "lock" => "lock",
            "illuminance" or "light" => "bulb",
            "plug" or "outlet" or "power_socket" => "plug",
            "carbon_dioxide" or "carbon_monoxide" or "aqi" or "pm25" or "pm10" or "pm1"
                or "volatile_organic_compounds" or "volatile_organic_compounds_parts" or "nitrogen_dioxide"
                or "ozone" or "gas" => "cloud",
            _ => null
        };
    }

    private static string? FromDomain(string domain)
    {
        return domain switch
        {
            "light" => "bulb",
            "switch" => "plug",
            "binary_sensor" => "motion",
            "lock" => "lock",
            "fan" => "fan",
            "climate" => "thermometer",
            _ => null
        };
    }

    // ── Material Design Icons name resolution (for the real MDI font path) ─────
    // Custom icon attribute → HA default-by-device_class → default-by-domain. Returns an mdi icon NAME
    // (without the "mdi:" prefix). MdiFont looks up the glyph; if missing, Draw() falls back to DrawKey().
    private static string? ResolveMdiName(string? mdiName, string? deviceClass, string domain)
    {
        var n = (mdiName ?? "").Trim();
        if (n.StartsWith("mdi:", StringComparison.OrdinalIgnoreCase)) n = n[4..];
        if (n.Length > 0) return n;

        return DeviceClassToMdi(deviceClass) ?? DomainToMdi(domain);
    }

    // Mirrors Home Assistant's default icons for common device classes (the names HA's frontend would use).
    private static string? DeviceClassToMdi(string? deviceClass)
    {
        return (deviceClass ?? "").ToLowerInvariant() switch
        {
            "temperature" => "thermometer",
            "humidity" => "water-percent",
            "moisture" => "water-alert",
            "pressure" or "atmospheric_pressure" => "gauge",
            "power" => "flash",
            "energy" or "energy_storage" => "lightning-bolt",
            "current" => "current-ac",
            "voltage" => "sine-wave",
            "power_factor" => "angle-acute",
            "frequency" => "sine-wave",
            "battery" => "battery",
            "illuminance" => "brightness-5",
            "carbon_dioxide" => "molecule-co2",
            "carbon_monoxide" => "molecule-co",
            "pm1" or "pm25" or "pm10" => "blur",
            "volatile_organic_compounds" or "volatile_organic_compounds_parts" => "air-filter",
            "nitrogen_dioxide" or "nitrogen_monoxide" or "nitrous_oxide" or "ozone" or "sulphur_dioxide" => "molecule",
            "aqi" => "air-filter",
            "gas" => "meter-gas",
            "water" => "water",
            "volume" or "volume_storage" => "cup-water",
            "precipitation" or "precipitation_intensity" => "weather-rainy",
            "wind_speed" => "weather-windy",
            "speed" => "speedometer",
            "distance" => "ruler",
            "signal_strength" => "wifi",
            "timestamp" => "clock",
            "duration" => "timer",
            "monetary" => "cash",
            "data_size" => "memory",
            "data_rate" => "swap-vertical",
            "motion" => "motion-sensor",
            "occupancy" or "presence" => "home-account",
            "moving" => "run",
            "vibration" => "vibrate",
            "door" => "door",
            "garage_door" or "garage" => "garage",
            "window" => "window-closed-variant",
            "opening" => "square-outline",
            "lock" => "lock",
            "plug" or "outlet" => "power-plug",
            "smoke" => "smoke-detector",
            "sound" => "music-note",
            "connectivity" => "lan-connect",
            "battery_charging" => "battery-charging",
            "problem" => "alert",
            "safety" => "shield-check",
            "tamper" => "alarm-light",
            "update" => "package-up",
            _ => null
        };
    }

    private static string? DomainToMdi(string domain)
    {
        return domain switch
        {
            "light" => "lightbulb",
            "switch" => "toggle-switch",
            "fan" => "fan",
            "lock" => "lock",
            "climate" => "thermostat",
            "binary_sensor" => "checkbox-marked-circle",
            "sensor" => "eye",
            "media_player" => "play-circle",
            "person" => "account",
            "sun" => "weather-sunny",
            "weather" => "weather-partly-cloudy",
            _ => null
        };
    }
}
