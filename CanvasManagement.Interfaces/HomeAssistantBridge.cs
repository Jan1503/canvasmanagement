using System.Collections.Concurrent;
using System.Globalization;

namespace CanvasManagement.Interfaces;

/// <summary>
///     A single live entity state pulled from Home Assistant.
/// </summary>
public readonly record struct HaEntityState(
    string EntityId,
    string State,
    string? Unit,
    string? FriendlyName,
    string? Icon,
    string? DeviceClass,
    DateTime? LastChangedUtc,
    DateTime UpdatedUtc);

/// <summary>A single numeric history sample for an entity.</summary>
public readonly record struct HaSample(DateTime Utc, double Value);

/// <summary>
///     In-process bridge between the host's Home Assistant connection (verpixeld) and extensions loaded as
///     plugins. The host keeps this populated from its WebSocket connection; extensions only read it, so the
///     long-lived token never has to live in an extension parameter or a saved layout.
/// </summary>
public static class HomeAssistantBridge
{
    private const int MaxHistoryPoints = 512;

    private static readonly ConcurrentDictionary<string, HaEntityState> States =
        new(StringComparer.OrdinalIgnoreCase);

    // Numeric history is only buffered for entities a graph/sparkline has registered interest in (Watched),
    // to bound memory. Seeded marks entities whose backlog has been fetched from the HA History API.
    private static readonly ConcurrentDictionary<string, List<HaSample>> History =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> Watched = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Seeded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True once the host has authenticated and received an initial state snapshot.</summary>
    public static bool Connected { get; set; }

    public static void Set(string entityId, string state, string? unit, string? friendlyName,
        string? icon = null, string? deviceClass = null, DateTime? lastChangedUtc = null)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return;
        States[entityId] = new HaEntityState(entityId, state ?? string.Empty, unit, friendlyName, icon,
            deviceClass, lastChangedUtc, DateTime.UtcNow);
    }

    public static bool TryGet(string entityId, out HaEntityState value)
    {
        if (!string.IsNullOrWhiteSpace(entityId)) return States.TryGetValue(entityId, out value);
        value = default;
        return false;
    }

    /// <summary>All currently-known entities (used to offer a picker in the GUI).</summary>
    public static IReadOnlyList<HaEntityState> All()
    {
        return States.Values.OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void Clear()
    {
        States.Clear();
        History.Clear();
        Watched.Clear();
        Seeded.Clear();
        Extra.Clear();
    }

    /// <summary>Title + message for a Home Assistant persistent notification (host overlay).</summary>
    public static event Action<string, string>? Notification;

    public static void RaiseNotification(string title, string message) =>
        Notification?.Invoke(title ?? "", message ?? "");

    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Extra =
        new(StringComparer.OrdinalIgnoreCase);

    public static void SetExtra(string entityId, Dictionary<string, string> attrs)
    {
        if (!string.IsNullOrWhiteSpace(entityId)) Extra[entityId] = attrs;
    }

    public static string? Attr(string entityId, string key)
    {
        if (string.IsNullOrWhiteSpace(entityId) || !Extra.TryGetValue(entityId, out var d)) return null;
        return d.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>All scalar attributes the host stored for this entity (empty if unknown).</summary>
    public static IReadOnlyDictionary<string, string> Attrs(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId) || !Extra.TryGetValue(entityId, out var d))
            return EmptyAttrs;
        return d;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyAttrs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static bool TryAttrDouble(string entityId, string key, out double value)
    {
        value = 0;
        var s = Attr(entityId, key);
        return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ── Numeric history ──────────────────────────────────────────────────────

    /// <summary>Registers interest in an entity's history (called by graph/sparkline extensions each render).</summary>
    public static void RequestHistory(string entityId)
    {
        if (!string.IsNullOrWhiteSpace(entityId)) Watched[entityId] = 1;
    }

    public static bool IsWatched(string entityId)
    {
        return !string.IsNullOrEmpty(entityId) && Watched.ContainsKey(entityId);
    }

    /// <summary>Watched entities whose backlog hasn't been seeded from the History API yet.</summary>
    public static IReadOnlyList<string> GetUnseededWatched()
    {
        return Watched.Keys.Where(k => !Seeded.ContainsKey(k)).ToArray();
    }

    public static void MarkSeeded(string entityId)
    {
        if (!string.IsNullOrWhiteSpace(entityId)) Seeded[entityId] = 1;
    }

    /// <summary>Appends a live sample (no-op if the entity isn't being watched by a graph).</summary>
    public static void AddSample(string entityId, double value, DateTime utc)
    {
        if (!IsWatched(entityId)) return;
        var list = History.GetOrAdd(entityId, _ => new List<HaSample>(MaxHistoryPoints));
        lock (list)
        {
            if (list.Count > 0 && utc <= list[^1].Utc) utc = list[^1].Utc.AddMilliseconds(1);
            list.Add(new HaSample(utc, value));
            if (list.Count > MaxHistoryPoints) list.RemoveRange(0, list.Count - MaxHistoryPoints);
        }
    }

    /// <summary>Merges a fetched backlog (from the History API) with any live samples already collected.</summary>
    public static void SeedHistory(string entityId, IEnumerable<HaSample> samples)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return;
        var list = History.GetOrAdd(entityId, _ => new List<HaSample>(MaxHistoryPoints));
        lock (list)
        {
            var merged = samples.Concat(list).OrderBy(s => s.Utc).ToList();
            if (merged.Count > MaxHistoryPoints) merged.RemoveRange(0, merged.Count - MaxHistoryPoints);
            list.Clear();
            list.AddRange(merged);
        }
    }

    public static HaSample[] GetHistory(string entityId)
    {
        if (string.IsNullOrEmpty(entityId) || !History.TryGetValue(entityId, out var list))
            return Array.Empty<HaSample>();
        lock (list) return list.ToArray();
    }
}
