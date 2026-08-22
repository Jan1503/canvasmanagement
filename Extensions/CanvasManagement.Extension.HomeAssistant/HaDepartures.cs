using System.Globalization;
using System.Text.Json;
using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.HomeAssistant;

internal readonly record struct HaDeparture(
    string Line,
    string Product,
    string Direction,
    DateTime When,
    int DelayMin,
    bool Cancelled);

internal static class HaDepartures
{
    private static readonly string[] ListKeys =
        ["departures", "next", "connections", "next_departures", "trains", "buses", "entries"];

    public static List<HaDeparture> FromEntity(string entityId)
    {
        var list = new List<HaDeparture>();
        if (string.IsNullOrWhiteSpace(entityId) || !HomeAssistantBridge.TryGet(entityId, out var st))
            return list;

        var attrs = HomeAssistantBridge.Attrs(entityId);
        foreach (var key in ListKeys)
        {
            if (!attrs.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            if (TryParseList(raw, list) && list.Count > 0) return list;
        }

        foreach (var kv in attrs)
        {
            if (kv.Value.Length > 2 && kv.Value[0] == '[')
            {
                TryParseList(kv.Value, list);
                if (list.Count > 0) return list;
            }
        }

        if (TryOne(st.State, attrs, out var one))
            list.Add(one);
        return list;
    }

    private static bool TryParseList(string raw, List<HaDeparture> into)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (TryItem(el, out var d)) into.Add(d);
            }
            return true;
        }
        catch { return false; }
    }

    private static bool TryItem(JsonElement el, out HaDeparture d)
    {
        d = default;
        var line = Str(el, "line", "line_name", "lineName", "name", "train", "catOutL", "productName") ?? "";
        if (line.Length == 0)
        {
            if (el.TryGetProperty("line", out var lineObj) && lineObj.ValueKind == JsonValueKind.Object)
                line = Str(lineObj, "name", "id", "fahrtNr") ?? "";
        }

        var product = Str(el, "product", "type", "category", "catOut", "mode", "class", "trainType") ?? "";
        if (product.Length == 0 && el.TryGetProperty("line", out var lo) && lo.ValueKind == JsonValueKind.Object)
            product = Str(lo, "product", "productName", "mode") ?? "";

        var direction = Str(el, "direction", "destination", "heading", "towards", "direction_name",
            "target", "terminus") ?? "";

        var when = Time(el, "when", "plannedWhen", "planned_when", "realtime", "time", "departure",
            "datetime", "planned", "scheduled", "departure_time", "next");
        if (when == default) return false;

        var delaySec = Num(el, "delay", "delaySeconds", "delay_seconds");
        var delayMin = Num(el, "delayMinutes", "delay_minutes", "delayMin");
        var delay = delayMin != 0 ? delayMin : delaySec is > 10 or < -10 ? (int)Math.Round(delaySec / 60.0) : delaySec;

        var cancelled = Bool(el, "cancelled", "canceled", "isCancelled", "is_cancelled");
        if (string.IsNullOrWhiteSpace(line) && string.IsNullOrWhiteSpace(direction)) return false;

        d = new HaDeparture(line, product, direction, when, delay, cancelled);
        return true;
    }

    private static bool TryOne(string state, IReadOnlyDictionary<string, string> attrs, out HaDeparture d)
    {
        d = default;
        var line = Pick(attrs, "line", "line_name", "route") ?? "";
        var direction = Pick(attrs, "direction", "destination", "heading") ?? "";
        var product = Pick(attrs, "product", "type") ?? "";
        DateTime when = default;
        if (DateTime.TryParse(state, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var stWhen))
            when = stWhen;
        else if (Pick(attrs, "when", "next", "departure", "time") is { } t)
            when = ParseTime(t);
        else if (int.TryParse(state, out var mins) && mins is >= 0 and < 24 * 60)
            when = DateTime.Now.AddMinutes(mins);
        if (when == default) return false;
        d = new HaDeparture(line, product, direction, when, 0, false);
        return true;
    }

    private static string? Str(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            else if (v.ValueKind == JsonValueKind.Number)
                return v.GetRawText();
        }
        return null;
    }

    private static int Num(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
            if (v.ValueKind == JsonValueKind.String &&
                int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                return s;
        }
        return 0;
    }

    private static bool Bool(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.String &&
                bool.TryParse(v.GetString(), out var b) && b) return true;
        }
        return false;
    }

    private static DateTime Time(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.String)
            {
                var t = ParseTime(v.GetString() ?? "");
                if (t != default) return t;
            }
            else if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt64(out var unix))
                {
                    if (unix > 10_000_000_000) unix /= 1000; // ms
                    if (unix > 1_000_000_000)
                        return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
                }
                if (v.TryGetDouble(out var mins) && mins is >= 0 and < 24 * 60)
                    return DateTime.Now.AddMinutes(mins);
            }
        }
        return default;
    }

    private static DateTime ParseTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return default;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            return dto.LocalDateTime;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var de))
            return de;
        if (TimeSpan.TryParse(s, out var tod))
            return DateTime.Today.Add(tod);
        return default;
    }

    private static string? Pick(IReadOnlyDictionary<string, string> attrs, params string[] keys)
    {
        foreach (var k in keys)
            if (attrs.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && v[0] != '[' && v[0] != '{')
                return v.Trim();
        return null;
    }
}
