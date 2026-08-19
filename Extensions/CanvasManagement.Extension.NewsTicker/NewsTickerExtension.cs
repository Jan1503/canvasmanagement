using System.Net;
using System.Timers;
using System.Xml.Linq;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.NewsTicker;

/// <summary>
///     Scrolling headline ticker pulling RSS/Atom from German news providers (Tagesschau, heise, n-tv).
///     No API keys. Headlines refresh on a timer and scroll seamlessly; scales to any panel.
/// </summary>
[ExtensionInfo("News Ticker",
    "Scrolling German news headlines (Tagesschau, heise, n-tv)",
    "Information",
    IconResourceName = "news-ticker.svg")]
public class NewsTickerExtension : ICanvasExtension, IDisposable
{
    private const string Sep = "   ◆   ";

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private HttpClient? _http;
    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;
    private volatile bool _fetching;
    private DateTime _lastFetch = DateTime.MinValue;

    private string _ticker = "Loading headlines…";
    private float _scrollX;
    private float _textWidth = -1;
    private float _lastFontSize = -1;
    private string _lastSig = "";

    internal NewsTickerExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Tagesschau", "Include tagesschau.de headlines", DefaultValue = true, Order = 1)]
    public bool Tagesschau { get; set; } = true;

    [ExtensionParameter("heise", "Include heise.de headlines", DefaultValue = true, Order = 2)]
    public bool Heise { get; set; } = true;

    [ExtensionParameter("n-tv", "Include n-tv.de headlines", DefaultValue = true, Order = 3)]
    public bool NTV { get; set; } = true;

    [ExtensionParameter("Show Source", "Prefix each headline with its source", DefaultValue = true, Order = 4)]
    public bool ShowSource { get; set; } = true;

    [ExtensionParameter("Scroll Speed", "Pixels per frame", DefaultValue = 2, MinValue = 1, MaxValue = 8, Order = 5)]
    public int ScrollSpeed { get; set; } = 2;

    [ExtensionParameter("Font Size", "Text height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Order = 6)]
    public int FontSize { get; set; }

    [ExtensionParameter("Refresh (min)", "How often to refetch headlines", DefaultValue = 10, MinValue = 2,
        MaxValue = 120, Unit = "min", Order = 7)]
    public int RefreshMinutes { get; set; } = 10;

    [ExtensionParameter("Text Color", "Headline colour", DefaultValue = "#FFFFFF", Order = 8)]
    public SKColor TextColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Color", "Background colour", DefaultValue = "#0A0A0A", Order = 9)]
    public SKColor BackgroundColor { get; set; } = new(10, 10, 10);

    public string Name => "News Ticker";
    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _http?.Dispose();
        _http = null;
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _scale = DisplayScale.GetScale(_canvas.Width, _canvas.Height);
            _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("verpixeld-newsticker/1.0");
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            _scrollX = _canvas.Width;
            _lastFetch = DateTime.MinValue;
            _ = FetchAsync();

            _timer = new Timer(33) { AutoReset = true };
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
        if (!IsRunning) return;

        // Re-fetch immediately when the source selection / source-label toggle changes.
        var sig = $"{Tagesschau}|{Heise}|{NTV}|{ShowSource}";
        if (sig != _lastSig)
        {
            _lastSig = sig;
            _lastFetch = DateTime.MinValue;
        }

        if ((DateTime.UtcNow - _lastFetch).TotalMinutes >= Math.Max(2, RefreshMinutes)) _ = FetchAsync();

        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try { Render(); }
            catch (Exception ex) { Console.WriteLine($"[NewsTicker] render: {ex.Message}"); }
        }
    }

    private async Task FetchAsync()
    {
        if (_fetching) return;
        _fetching = true;
        try
        {
            _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var sources = new List<(string label, string url)>();
            if (Tagesschau) sources.Add(("Tagesschau", "https://www.tagesschau.de/index~rss2.xml"));
            if (Heise) sources.Add(("heise", "https://www.heise.de/rss/heise-atom.xml"));
            if (NTV) sources.Add(("n-tv", "https://www.n-tv.de/rss"));

            var headlines = new List<string>();
            foreach (var (label, url) in sources)
                try
                {
                    var xml = await _http.GetStringAsync(url);
                    foreach (var title in ParseTitles(xml).Take(12))
                        headlines.Add(ShowSource ? $"[{label}] {title}" : title);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NewsTicker] {label} failed: {ex.Message}");
                }

            var ticker = headlines.Count > 0
                ? string.Join(Sep, headlines) + Sep
                : "No headlines available — check the network connection.";

            lock (_lock)
            {
                _ticker = ticker;
                _textWidth = -1; // force re-measure
            }

            _lastFetch = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NewsTicker] fetch failed: {ex.Message}");
            _lastFetch = DateTime.UtcNow.AddMinutes(-Math.Max(2, RefreshMinutes) + 1);
        }
        finally
        {
            _fetching = false;
        }
    }

    private static IEnumerable<string> ParseTitles(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { yield break; }

        XNamespace atom = "http://www.w3.org/2005/Atom";

        // RSS 2.0 / RDF: <item><title>
        foreach (var item in doc.Descendants("item"))
        {
            var t = item.Element("title")?.Value;
            if (!string.IsNullOrWhiteSpace(t)) yield return Clean(t);
        }

        // Atom: <entry><title>
        foreach (var entry in doc.Descendants(atom + "entry"))
        {
            var t = entry.Element(atom + "title")?.Value;
            if (!string.IsNullOrWhiteSpace(t)) yield return Clean(t);
        }
    }

    private static string Clean(string s)
    {
        return WebUtility.HtmlDecode(s).Replace("\n", " ").Replace("\r", " ").Trim();
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var canvas = new SKCanvas(bb);
        canvas.Clear(BackgroundColor);

        var fontSize = FontSize > 0 ? FontSize : Math.Clamp(_canvas.Height * 0.55f, 8f, 40f);
        using var font = new SKFont { Size = fontSize };
        using var paint = new SKPaint { Color = TextColor, IsAntialias = true };

        string text;
        lock (_lock) text = _ticker;

        if (Math.Abs(fontSize - _lastFontSize) > 0.1f || _textWidth < 0)
        {
            _textWidth = font.MeasureText(text);
            _lastFontSize = fontSize;
        }

        var baseline = _canvas.Height / 2f + fontSize * 0.36f;
        _scrollX -= Math.Max(1, ScrollSpeed) * Math.Max(1f, _scale);

        var loop = Math.Max(_textWidth + _canvas.Width * 0.15f, _canvas.Width);
        if (_scrollX <= -loop) _scrollX += loop;

        // Draw as many copies as needed to cover the whole width (handles short tickers without gaps).
        for (var x = _scrollX; x < _canvas.Width; x += loop)
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }
}
