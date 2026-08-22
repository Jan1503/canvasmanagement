using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     Now-playing tile from a Home Assistant <c>media_player.*</c>. Album art via <c>entity_picture</c>
///     needs HA auth, so v1 uses a colour block hashed from the title.
/// </summary>
[ExtensionInfo("HA Now Playing",
    "Track / artist / progress from a Home Assistant media player",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantMediaExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;

    internal HomeAssistantMediaExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entity ID", "media_player.* entity", DefaultValue = "media_player.living_room", Order = 1)]
    public string EntityId { get; set; } = "media_player.living_room";

    [ExtensionParameter("Label", "Idle title (empty = entity friendly name)", DefaultValue = "", Order = 2)]
    public string Label { get; set; } = "";

    [ExtensionParameter("Show Artist", "Show artist / album under the title", DefaultValue = true, Order = 3)]
    public bool ShowArtist { get; set; } = true;

    [ExtensionParameter("Show Progress", "Show the playback progress bar", DefaultValue = true, Order = 4)]
    public bool ShowProgress { get; set; } = true;

    [ExtensionParameter("Show Art", "Draw the colour block on the left", DefaultValue = true, Order = 5)]
    public bool ShowArt { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render with the crisp bitmap (BDF) font", DefaultValue = false, Order = 6)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Align", "Horizontal alignment of the title", DefaultValue = HaTileAlign.Left, Order = 7)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Left;

    [ExtensionParameter("Value Color", "Colour of the track title", DefaultValue = "#FFFFFF", Order = 8)]
    public SKColor ValueColor { get; set; } = SKColors.White;

    [ExtensionParameter("Label Color", "Colour of the artist / album", DefaultValue = "#B4BEC8", Order = 9)]
    public SKColor LabelColor { get; set; } = new(180, 190, 200);

    [ExtensionParameter("Accent Color", "Progress / idle accent", DefaultValue = "#1DB954", Order = 10)]
    public SKColor AccentColor { get; set; } = new(29, 185, 84);

    [ExtensionParameter("Background Color", "Background (alpha 0 for overlay)", DefaultValue = "#101015", Order = 11)]
    public SKColor BackgroundColor { get; set; } = new(16, 16, 21);

    public string Name => "HA Now Playing";
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
            _timer = new Timer(200) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
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
            catch (Exception ex) { Console.WriteLine($"[HA Media] {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var c = new SKCanvas(bb);
        c.Clear(BackgroundColor);

        float w = _canvas.Width, h = _canvas.Height;
        var found = HomeAssistantBridge.TryGet(EntityId, out var entity);
        var state = found ? entity.State : "";
        var title = HomeAssistantBridge.Attr(EntityId, "media_title")
                    ?? (found ? entity.FriendlyName : null)
                    ?? "Nothing playing";
        var artist = HomeAssistantBridge.Attr(EntityId, "media_artist") ?? "";
        var album = HomeAssistantBridge.Attr(EntityId, "media_album_name") ?? "";
        HomeAssistantBridge.TryAttrDouble(EntityId, "media_duration", out var duration);
        HomeAssistantBridge.TryAttrDouble(EntityId, "media_position", out var position);

        var idle = !found || state is "idle" or "off" or "standby" or "unavailable" or "unknown";
        if (idle && string.IsNullOrWhiteSpace(HomeAssistantBridge.Attr(EntityId, "media_title")))
        {
            title = !string.IsNullOrWhiteSpace(Label)
                ? Label
                : found && !string.IsNullOrWhiteSpace(entity.FriendlyName)
                    ? entity.FriendlyName!
                    : HomeAssistantBridge.Connected ? "Nothing playing" : "HA offline";
        }

        var artSize = 0f;
        if (ShowArt)
        {
            var art = w * 0.32f;
            artSize = Math.Min(art, h - 8);
            var artColor = HashColor(title);
            using var artPaint = new SKPaint { Color = artColor, IsAntialias = true };
            c.DrawRoundRect(4, (h - artSize) / 2f, artSize, artSize, 4, 4, artPaint);
            using var note = new SKPaint { Color = new SKColor(255, 255, 255, 180), IsAntialias = true };
            c.DrawCircle(4 + artSize * 0.5f, (h - artSize) / 2f + artSize * 0.42f, artSize * 0.12f, note);
        }

        var tx = ShowArt ? 8 + artSize : 4;
        var tw = w - tx - 4;
        var align = HaText.ToSk(Align);
        HaText.Draw(c, _canvas, title, ValueColor, tx, 2, tw, h * 0.4f, h * 0.28f, align, UseBdfFont);
        var sub = string.IsNullOrWhiteSpace(artist) ? album : artist;
        if (ShowArtist && !string.IsNullOrWhiteSpace(sub))
            HaText.Draw(c, _canvas, sub, LabelColor, tx, h * 0.4f, tw, h * 0.28f, h * 0.2f, align, UseBdfFont);

        if (ShowProgress)
        {
            var barY = h * 0.78f;
            var barH = Math.Max(3f, h * 0.08f);
            using var track = new SKPaint { Color = new SKColor(50, 55, 62) };
            using var fill = new SKPaint { Color = idle ? new SKColor(80, 80, 80) : AccentColor };
            c.DrawRect(tx, barY, tw, barH, track);
            var frac = duration > 0 ? Math.Clamp(position / duration, 0, 1) : (idle ? 0 : 0.15);
            c.DrawRect(tx, barY, (float)(tw * frac), barH, fill);
        }

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private static SKColor HashColor(string s)
    {
        unchecked
        {
            var h = 2166136261;
            foreach (var ch in s) h = (h ^ ch) * 16777619;
            var r = (byte)(40 + (h & 0x7F));
            var g = (byte)(40 + ((h >> 8) & 0x7F));
            var b = (byte)(40 + ((h >> 16) & 0x7F));
            return new SKColor(r, g, b);
        }
    }
}
