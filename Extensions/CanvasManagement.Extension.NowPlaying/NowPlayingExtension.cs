using System.Text.Json;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.NowPlaying;

/// <summary>
///     "Now Playing" display: album art + track/artist/album + a progress bar. Reads a small snapshot file
///     written by verpixeld's /api/nowplaying endpoint, which a companion agent on your PC pushes to
///     (capturing Apple Music / browser-YouTube / any system media session). Scales to any panel.
/// </summary>
[ExtensionInfo("Now Playing",
    "Shows the track, artist and album art currently playing (fed by the PC companion agent)",
    "Information",
    IconResourceName = "now-playing.svg")]
public class NowPlayingExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private SKBitmap? _art;
    private Timer? _timer;
    private float _scale = 1f;
    private int _frame;

    private string _dir = "";
    private string _artStamp = "";
    private long _jsonStamp;

    private Track _track = new();
    private float _titleScroll;

    internal NowPlayingExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Data Folder", "Folder with current.json (blank = app's nowplaying folder)",
        DefaultValue = "", Order = 1)]
    public string DataFolder { get; set; } = "";

    [ExtensionParameter("Idle After (s)", "Show 'nothing playing' if no update for this long", DefaultValue = 30,
        MinValue = 5, MaxValue = 600, Unit = "s", Order = 2)]
    public int IdleAfterSeconds { get; set; } = 30;

    [ExtensionParameter("Accent Color", "Progress bar / accent colour", DefaultValue = "#1DB954", Order = 3)]
    public SKColor AccentColor { get; set; } = new(29, 185, 84);

    [ExtensionParameter("Background Color", "Background colour", DefaultValue = "#101015", Order = 4)]
    public SKColor BackgroundColor { get; set; } = new(16, 16, 21);

    [ExtensionParameter("Use BDF Font", "Render track text with the crisp bitmap (BDF) font", DefaultValue = false,
        Order = 5)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "Title height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 64, Unit = "px", Order = 6)]
    public int FontSize { get; set; }

    public string Name => "Now Playing";
    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        _art?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _scale = DisplayScale.GetScale(_canvas.Width, _canvas.Height);
            _dir = string.IsNullOrWhiteSpace(DataFolder)
                ? Path.Combine(AppContext.BaseDirectory, "nowplaying")
                : DataFolder;
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
            _art?.Dispose();
            _art = null;
            try { _canvas.Clear(SKColors.Black); }
            catch { }
        }
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try
            {
                LoadSnapshot();
                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NowPlaying] {ex.Message}");
            }
        }
    }

    private void LoadSnapshot()
    {
        var jsonFile = Path.Combine(_dir, "current.json");
        if (!File.Exists(jsonFile)) return;

        try
        {
            var stamp = File.GetLastWriteTimeUtc(jsonFile).Ticks;
            if (stamp == _jsonStamp) return; // unchanged
            _jsonStamp = stamp;

            using var doc = JsonDocument.Parse(File.ReadAllText(jsonFile));
            var r = doc.RootElement;
            _track = new Track
            {
                Title = Str(r, "title"),
                Artist = Str(r, "artist"),
                Album = Str(r, "album"),
                IsPlaying = r.TryGetProperty("isPlaying", out var p) && p.ValueKind == JsonValueKind.True,
                Position = Num(r, "position"),
                Duration = Num(r, "duration"),
                UpdatedUtc = DateTime.TryParse(Str(r, "updatedUtc"), out var u) ? u.ToUniversalTime() : DateTime.UtcNow,
                Art = Str(r, "art")
            };
            _titleScroll = 0;

            // (Re)load album art if present and changed.
            var artFile = Path.Combine(_dir, string.IsNullOrEmpty(_track.Art) ? "art.png" : _track.Art);
            if (File.Exists(artFile))
            {
                var artStamp = File.GetLastWriteTimeUtc(artFile).Ticks.ToString();
                if (artStamp != _artStamp)
                {
                    _artStamp = artStamp;
                    _art?.Dispose();
                    _art = SKBitmap.Decode(artFile);
                }
            }
            else
            {
                _art?.Dispose();
                _art = null;
                _artStamp = "";
            }
        }
        catch
        {
            // partially-written file; try again next tick
        }
    }

    private static string Str(JsonElement r, string n) =>
        r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement r, string n) =>
        r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var canvas = new SKCanvas(bb);
        canvas.Clear(BackgroundColor);

        var idle = _track.Title.Length == 0 ||
                   (DateTime.UtcNow - _track.UpdatedUtc).TotalSeconds > Math.Max(5, IdleAfterSeconds);

        if (idle)
        {
            var idleSize = CanvasText.ResolveSize(FontSize, Math.Max(9f, _canvas.Height * 0.12f));
            CanvasText.Draw(canvas, _canvas, "Nothing playing", new SKColor(150, 150, 160),
                _canvas.Width / 2f, _canvas.Height / 2f, idleSize, SKTextAlign.Center, UseBdfFont);
            canvas.Flush();
            _canvas.SubmitCompletedFrame(bb);
            return;
        }

        var h = _canvas.Height;
        var w = _canvas.Width;
        var pad = Math.Max(2f, 4f * _scale);

        // Album art (square) on the left.
        var artSize = h - pad * 2;
        var textX = pad;
        if (_art != null)
        {
            var dst = new SKRect(pad, pad, pad + artSize, pad + artSize);
            canvas.DrawBitmap(_art, dst);
            textX = pad + artSize + pad * 2;
        }
        else
        {
            // Placeholder note icon.
            using var ph = new SKPaint { Color = new SKColor(40, 40, 52), IsAntialias = true };
            canvas.DrawRoundRect(pad, pad, artSize, artSize, pad, pad, ph);
            using var note = new SKFont { Size = artSize * 0.6f };
            using var np = new SKPaint { Color = AccentColor, IsAntialias = true };
            canvas.DrawText("♪", pad + artSize / 2, pad + artSize * 0.72f, SKTextAlign.Center, note, np);
            textX = pad + artSize + pad * 2;
        }

        var textW = w - textX - pad;
        var barH = Math.Max(2f, 3f * _scale);
        var barY = h - pad - barH;

        var titleSize = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.26f));
        var subSize = FontSize > 0 ? Math.Max(6f, FontSize * 0.7f) : Math.Max(8f, h * 0.18f);
        var ty = pad + titleSize;
        var tw = CanvasText.Measure(_canvas, _track.Title, titleSize, UseBdfFont);
        if (tw <= textW)
        {
            CanvasText.Draw(canvas, _canvas, _track.Title, SKColors.White, textX, ty, titleSize,
                SKTextAlign.Left, UseBdfFont);
        }
        else
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(textX, 0, w - pad, h));
            _titleScroll += 0.6f * _scale;
            var span = tw + textW * 0.4f;
            if (_titleScroll > span) _titleScroll = 0;
            CanvasText.Draw(canvas, _canvas, _track.Title, SKColors.White, textX - _titleScroll, ty, titleSize,
                SKTextAlign.Left, UseBdfFont);
            canvas.Restore();
        }

        var ay = pad + titleSize + subSize * 1.3f;
        CanvasText.Draw(canvas, _canvas, Trunc(_track.Artist, subSize, textW), new SKColor(200, 205, 215),
            textX, ay, subSize, SKTextAlign.Left, UseBdfFont);
        if (h > 40 && !string.IsNullOrEmpty(_track.Album))
            CanvasText.Draw(canvas, _canvas, Trunc(_track.Album, subSize, textW), new SKColor(140, 145, 160),
                textX, ay + subSize * 1.25f, subSize, SKTextAlign.Left, UseBdfFont);

        // Progress bar.
        using (var track = new SKPaint { Color = new SKColor(55, 58, 68), Style = SKPaintStyle.Fill })
        using (var fill = new SKPaint { Color = AccentColor, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(textX, barY, textW, barH, track);
            var prog = _track.Duration > 0 ? (float)Math.Clamp(_track.Position / _track.Duration, 0, 1) : 0;
            canvas.DrawRect(textX, barY, textW * prog, barH, fill);
        }

        // Pause glyph when paused.
        if (!_track.IsPlaying)
        {
            using var pf = new SKPaint { Color = new SKColor(255, 255, 255, 180), IsAntialias = true };
            var s = Math.Max(2f, 3f * _scale);
            canvas.DrawRect(w - pad - s * 3, pad, s, s * 3, pf);
            canvas.DrawRect(w - pad - s, pad, s, s * 3, pf);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private string Trunc(string s, float size, float maxW)
    {
        if (string.IsNullOrEmpty(s) || CanvasText.Measure(_canvas, s, size, UseBdfFont) <= maxW) return s;
        while (s.Length > 1 && CanvasText.Measure(_canvas, s + "…", size, UseBdfFont) > maxW) s = s[..^1];
        return s + "…";
    }

    private sealed class Track
    {
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public bool IsPlaying;
        public double Position;
        public double Duration;
        public DateTime UpdatedUtc = DateTime.MinValue;
        public string Art = "";
    }
}
