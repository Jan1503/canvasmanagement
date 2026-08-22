using System.Globalization;
using System.Reflection;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

public enum HaWasteDateStyle { Relative, Short, Weekday, Hidden }

public enum HaWasteColumns { NameThenDate, DateThenName }

/// <summary>
///     Next waste pickups. Two layouts work:
///     <list type="bullet">
///         <item>One schedule entity whose attributes are dates → bin names (Stadtreinigung Hamburg).</item>
///         <item>Classic date sensors (state = next pickup date), one entity per bin type.</item>
///     </list>
/// </summary>
[ExtensionInfo("HA Waste",
    "Next bin pickups: one schedule entity (dates as attributes) or date sensors",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantWasteExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;

    private static readonly CultureInfo German = CreateGerman();

    private static CultureInfo CreateGerman()
    {
        try { return CultureInfo.GetCultureInfo("de-DE"); }
        catch { return CultureInfo.InvariantCulture; }
    }

    internal HomeAssistantWasteExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entities",
        "Each row: Entity + optional Match (bin name) + Label override. Empty Match = every pickup from that entity",
        Order = 1)]
    public List<HaWasteItem> Entities { get; set; } =
    [
        new() { EntityId = "sensor.stadtreinigung_hamburg" }
    ];

    [ExtensionParameter("Max Rows", "How many upcoming pickups to show", DefaultValue = 4, MinValue = 1,
        MaxValue = 8, Order = 2)]
    public int MaxRows { get; set; } = 4;

    [ExtensionParameter("Date Style", "How to write the date", DefaultValue = HaWasteDateStyle.Relative, Order = 3)]
    public HaWasteDateStyle DateStyle { get; set; } = HaWasteDateStyle.Relative;

    [ExtensionParameter("Columns", "Name on top / date below, or swapped", DefaultValue = HaWasteColumns.NameThenDate,
        Order = 4)]
    public HaWasteColumns Columns { get; set; } = HaWasteColumns.NameThenDate;

    [ExtensionParameter("Bin Icon", "Wheelie-bin glyph for the pickup type", DefaultValue = true, Order = 5)]
    public bool ShowSwatch { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render with the crisp bitmap (BDF) font", DefaultValue = false, Order = 6)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Align", "Horizontal alignment of the name", DefaultValue = HaTileAlign.Left, Order = 7)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Left;

    [ExtensionParameter("Value Size", "Text height in px (0 = auto-fit)", DefaultValue = 0, MinValue = 0,
        MaxValue = 200, Unit = "px", Order = 8)]
    public int ValueSize { get; set; }

    [ExtensionParameter("Value Color", "Colour of the date", DefaultValue = "#C8CDB4", Order = 9)]
    public SKColor ValueColor { get; set; } = new(200, 205, 180);

    [ExtensionParameter("Label Color", "Colour of the bin names", DefaultValue = "#FFFFFF", Order = 10)]
    public SKColor LabelColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Color", "Background (alpha 0 for overlay)", DefaultValue = "#12110E", Order = 11)]
    public SKColor BackgroundColor { get; set; } = new(18, 17, 14);

    public string Name => "HA Waste";
    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            _timer = new Timer(1000) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
            try { Render(); }
            catch (Exception ex) { Console.WriteLine($"[HA Waste] render: {ex.Message}"); }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); }
            catch { }
        }
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try { Render(); }
            catch (Exception ex) { Console.WriteLine($"[HA Waste] {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        float w = _canvas.Width, h = _canvas.Height;
        var items = Parse().OrderBy(i => i.When).Take(Math.Clamp(MaxRows, 1, 8)).ToList();
        if (items.Count == 0)
        {
            var msg = HomeAssistantBridge.Connected ? "No pickups" : "HA offline";
            HaText.Draw(c, _canvas, msg, LabelColor, 4, 0, w - 8, h, Math.Max(10f, h * 0.28f),
                HaText.ToSk(Align), UseBdfFont, shrinkToWidth: false);
            c.Flush();
            _canvas.SubmitCompletedFrame(bb);
            return;
        }

        var rowH = h / items.Count;
        var pad = Math.Max(2f, rowH * 0.08f);
        var twoLine = DateStyle != HaWasteDateStyle.Hidden;
        var nameH = ValueSize > 0 ? ValueSize : Math.Max(8f, rowH * (twoLine ? 0.46f : 0.7f));
        var dateH = ValueSize > 0 ? Math.Max(6f, ValueSize * 0.75f) : Math.Max(7f, rowH * 0.32f);
        var nameOnTop = Columns != HaWasteColumns.DateThenName;

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var y = i * rowH;
            var x = pad;
            if (ShowSwatch)
            {
                var icon = Math.Max(8f, rowH - pad * 2f);
                HaWasteIcons.Draw(c, it.Kind, x + icon * 0.5f, y + rowH * 0.5f, icon);
                x += icon + pad;
            }

            var textW = Math.Max(8f, w - x - pad);
            var dateText = FormatDate(it.When);
            if (!twoLine)
            {
                HaText.Draw(c, _canvas, it.Label, LabelColor,
                    x, y, textW, rowH, nameH, HaText.ToSk(Align), UseBdfFont, shrinkToWidth: false);
                continue;
            }

            var topH = rowH * 0.58f;
            var botY = y + topH;
            var botH = rowH - topH;
            var top = nameOnTop ? it.Label : dateText;
            var bot = nameOnTop ? dateText : it.Label;
            var topCol = nameOnTop ? LabelColor : ValueColor;
            var botCol = nameOnTop ? ValueColor : LabelColor;
            var topSize = nameOnTop ? nameH : dateH;
            var botSize = nameOnTop ? dateH : nameH;
            var align = HaText.ToSk(Align);

            HaText.Draw(c, _canvas, top, topCol,
                x, y, textW, topH, topSize, align, UseBdfFont, shrinkToWidth: false);
            HaText.Draw(c, _canvas, bot, botCol,
                x, botY, textW, botH, botSize, align, UseBdfFont, shrinkToWidth: false);
        }

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private List<(string Label, DateTime When, HaBinKind Kind)> Parse()
    {
        var list = new List<(string, DateTime, HaBinKind)>();
        foreach (var item in Entities ?? [])
        {
            var raw = item.EntityId;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!HomeAssistantBridge.TryGet(raw, out var st)) continue;

            var fromAttrs = ParseDateKeyedAttrs(raw, item.Match, item.Label);
            if (fromAttrs.Count > 0)
            {
                // Combined calendar (many bin types) → every upcoming pickup.
                // Dedicated WCS sensor (one type, or Match set) → only the next date.
                var mixed = fromAttrs.Select(a => a.Kind).Distinct().Count() > 1;
                if (mixed && string.IsNullOrWhiteSpace(item.Match))
                    list.AddRange(fromAttrs);
                else
                    list.Add(fromAttrs.OrderBy(a => a.When).First());
                continue;
            }

            if (TryWcsNext(raw, item.Label, st, out var wcs))
            {
                list.Add(wcs);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.Match)) continue;

            if (TryDate(st.State, out var when) && when.Date >= DateTime.Today)
            {
                var label = !string.IsNullOrWhiteSpace(item.Label)
                    ? item.Label
                    : st.FriendlyName ?? raw.Split('.').Last();
                list.Add((label, when, HaWasteIcons.Kind(label, raw)));
                continue;
            }

            if (TryPhrase(st.State, out var phraseLabel, out var phraseWhen) && phraseWhen.Date >= DateTime.Today)
            {
                var label = !string.IsNullOrWhiteSpace(item.Label) ? item.Label : phraseLabel;
                list.Add((label, phraseWhen, HaWasteIcons.Kind(label, raw)));
            }
        }

        return list;
    }

    /// <summary>
    ///     Dedicated Waste Collection Schedule sensor: <c>daysTo</c>, <c>next_date</c>, or a date state.
    /// </summary>
    private static bool TryWcsNext(string entityId, string labelOverride, HaEntityState st,
        out (string Label, DateTime When, HaBinKind Kind) row)
    {
        row = default;
        var when = default(DateTime);
        var got = false;
        if (HomeAssistantBridge.TryAttrDouble(entityId, "daysTo", out var days) ||
            HomeAssistantBridge.TryAttrDouble(entityId, "days_to", out days))
        {
            when = DateTime.Today.AddDays((int)Math.Round(days));
            got = when.Date >= DateTime.Today;
        }

        if (!got)
        {
            foreach (var key in new[] { "next_date", "Next Date", "date" })
            {
                var raw = HomeAssistantBridge.Attr(entityId, key);
                if (raw != null && TryDate(raw, out when) && when.Date >= DateTime.Today)
                {
                    got = true;
                    break;
                }
            }
        }

        if (!got) return false;
        var name = !string.IsNullOrWhiteSpace(labelOverride)
            ? labelOverride
            : !string.IsNullOrWhiteSpace(st.FriendlyName) ? st.FriendlyName! : entityId.Split('.').Last();
        row = (name, when, HaWasteIcons.Kind(name, entityId));
        return true;
    }

    private static List<(string Label, DateTime When, HaBinKind Kind)> ParseDateKeyedAttrs(string entityId,
        string? match, string? labelOverride)
    {
        var list = new List<(string, DateTime, HaBinKind)>();
        var filter = (match ?? "").Trim();
        foreach (var (key, value) in DumpAttrs(entityId))
        {
            if (SkipAttr(key) || string.IsNullOrWhiteSpace(value)) continue;
            if (!TryDate(key, out var when) || when.Date < DateTime.Today) continue;
            var bin = value.Trim();
            if (filter.Length > 0 && bin.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var label = !string.IsNullOrWhiteSpace(labelOverride) ? labelOverride : bin;
            list.Add((label, when, HaWasteIcons.Kind(bin, entityId)));
        }

        return list;
    }

    /// <summary>
    ///     Reads date-keyed attributes without a compile-time dependency on
    ///     <c>HomeAssistantBridge.Attrs</c> (older host Interfaces.dll does not have it).
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> DumpAttrs(string entityId)
    {
        var dumped = TryAttrs(entityId);
        if (dumped != null)
        {
            foreach (var kv in dumped) yield return kv;
            yield break;
        }

        // Host stores extras but this Interfaces.dll has no Attrs(): probe typical date keys.
        for (var i = 0; i < 90; i++)
        {
            var day = DateTime.Today.AddDays(i);
            foreach (var key in new[]
                     {
                         day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                         day.ToString("d.M.yyyy", CultureInfo.InvariantCulture),
                         day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                     })
            {
                var value = HomeAssistantBridge.Attr(entityId, key);
                if (string.IsNullOrWhiteSpace(value)) continue;
                yield return new KeyValuePair<string, string>(key, value);
                break;
            }
        }
    }

    private static IReadOnlyDictionary<string, string>? TryAttrs(string entityId)
    {
        try
        {
            var m = typeof(HomeAssistantBridge).GetMethod("Attrs", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return null;
            return m.Invoke(null, [entityId]) as IReadOnlyDictionary<string, string>;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HA Waste] Attrs unavailable ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static bool SkipAttr(string key)
    {
        return key.Equals("attribution", StringComparison.OrdinalIgnoreCase)
               || key.Equals("icon", StringComparison.OrdinalIgnoreCase)
               || key.Equals("friendly_name", StringComparison.OrdinalIgnoreCase)
               || key.Equals("device_class", StringComparison.OrdinalIgnoreCase)
               || key.Equals("unit_of_measurement", StringComparison.OrdinalIgnoreCase)
               || key.Equals("entity_picture", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryPhrase(string state, out string label, out DateTime when)
    {
        label = "";
        when = default;
        if (string.IsNullOrWhiteSpace(state)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(state.Trim(),
            @"^(?<bin>.+?)\s+in\s+(?<n>\d+)\s+Tag", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        label = m.Groups["bin"].Value.Trim();
        if (!int.TryParse(m.Groups["n"].Value, out var days)) return false;
        when = DateTime.Today.AddDays(days);
        return !string.IsNullOrWhiteSpace(label);
    }

    private static bool TryDate(string state, out DateTime when)
    {
        when = default;
        if (string.IsNullOrWhiteSpace(state)) return false;
        var t = state.Trim();
        string[] formats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK"];
        if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out when))
            return true;
        if (DateTime.TryParse(t, German, DateTimeStyles.AssumeLocal, out when))
            return true;
        return DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out when);
    }

    private string FormatDate(DateTime when)
    {
        var days = (when.Date - DateTime.Today).Days;
        return DateStyle switch
        {
            HaWasteDateStyle.Hidden => "",
            HaWasteDateStyle.Short => when.ToString("d.M.", German),
            HaWasteDateStyle.Weekday => when.ToString("ddd d.M.", German),
            _ => days switch
            {
                0 => "heute",
                1 => "morgen",
                _ => when.ToString("ddd d.M", German)
            }
        };
    }
}
