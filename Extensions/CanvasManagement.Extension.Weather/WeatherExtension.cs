using System.Globalization;
using System.Text.Json;
using System.Timers;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.Weather;

/// <summary>
///     Current weather + multi-day forecast from the free, key-less Open-Meteo API. Renders a large current
///     reading with a drawn condition icon and a small forecast strip. Scales to any panel.
/// </summary>
[ExtensionInfo("Weather",
    "Live weather & forecast (Open-Meteo, no API key required)",
    "Information",
    IconResourceName = "weather.svg")]
public class WeatherExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private HttpClient? _http;
    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private float _scale = 1f;
    private volatile bool _fetching;
    private DateTime _lastFetch = DateTime.MinValue;
    private WeatherData? _data;
    private string _status = "Loading…";

    internal WeatherExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Location", "City or place — use the search button", DefaultValue = "Berlin", Order = 1)]
    public string Location { get; set; } = "Berlin";

    [ExtensionParameter("Latitude", "Filled by the location picker", DefaultValue = "52.52", Order = 2)]
    public string Latitude { get; set; } = "52.52";

    [ExtensionParameter("Longitude", "Filled by the location picker", DefaultValue = "13.405", Order = 3)]
    public string Longitude { get; set; } = "13.405";

    [ExtensionParameter("Fahrenheit", "Use °F instead of °C", DefaultValue = false, Order = 4)]
    public bool Fahrenheit { get; set; }

    [ExtensionParameter("Show Forecast", "Show the multi-day forecast strip", DefaultValue = true, Order = 5)]
    public bool ShowForecast { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render text with the crisp bitmap (BDF) font", DefaultValue = false,
        Order = 8)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Refresh (min)", "How often to refetch the weather", DefaultValue = 15, MinValue = 5,
        MaxValue = 180, Unit = "min", Order = 6)]
    public int RefreshMinutes { get; set; } = 15;

    [ExtensionParameter("Background Color", "Background colour", DefaultValue = "#0A1A2F", Order = 7)]
    public SKColor BackgroundColor { get; set; } = new(10, 26, 47);

    public string Name => "Weather";
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
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

            _lastFetch = DateTime.MinValue;
            _ = FetchAsync();

            _timer = new Timer(1000) { AutoReset = true };
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

        if ((DateTime.UtcNow - _lastFetch).TotalMinutes >= Math.Max(5, RefreshMinutes))
            _ = FetchAsync();

        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try { Render(); }
            catch (Exception ex) { Console.WriteLine($"[Weather] render: {ex.Message}"); }
        }
    }

    [ExtensionMethod("Update Now", "Fetch the weather for the current coordinates immediately",
        Category = "Data", Order = 1)]
    public void UpdateNow()
    {
        _lastFetch = DateTime.MinValue;
        _ = FetchAsync();
    }

    private async Task FetchAsync()
    {
        if (_fetching) return;
        _fetching = true;
        try
        {
            _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            var lat = ParseCoord(Latitude, 52.52);
            var lon = ParseCoord(Longitude, 13.405);
            var url =
                $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                "&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max," +
                "temperature_2m_min&timezone=auto&forecast_days=4" +
                (Fahrenheit ? "&temperature_unit=fahrenheit" : "");

            var json = await _http.GetStringAsync(url);
            var data = Parse(json);
            lock (_lock) { _data = data; _status = ""; }
            _lastFetch = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            lock (_lock) _status = "Weather unavailable";
            Console.WriteLine($"[Weather] fetch failed: {ex.Message}");
            _lastFetch = DateTime.UtcNow.AddMinutes(-Math.Max(5, RefreshMinutes) + 1); // retry in ~1 min
        }
        finally
        {
            _fetching = false;
        }
    }

    private static double ParseCoord(string? value, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim().Replace(',', '.'); // tolerate "52,52"
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static WeatherData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var data = new WeatherData
        {
            Temp = cur.GetProperty("temperature_2m").GetDouble(),
            Code = cur.GetProperty("weather_code").GetInt32(),
            IsDay = cur.TryGetProperty("is_day", out var d) && d.GetInt32() == 1
        };

        var daily = root.GetProperty("daily");
        var times = daily.GetProperty("time");
        var codes = daily.GetProperty("weather_code");
        var max = daily.GetProperty("temperature_2m_max");
        var min = daily.GetProperty("temperature_2m_min");
        var n = times.GetArrayLength();
        for (var i = 0; i < n; i++)
            data.Days.Add(new DayForecast
            {
                Date = DateTime.TryParse(times[i].GetString(), out var dt) ? dt : DateTime.Today.AddDays(i),
                Code = codes[i].GetInt32(),
                Max = max[i].GetDouble(),
                Min = min[i].GetDouble()
            });

        return data;
    }

    // ── Render ──────────────────────────────────────────────────────────────

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var canvas = new SKCanvas(bb);
        canvas.Clear(BackgroundColor);

        WeatherData? data;
        string status;
        lock (_lock) { data = _data; status = _status; }

        var w = _canvas.Width;
        var h = _canvas.Height;

        if (data == null)
        {
            using var f = new SKFont { Size = Math.Max(9f, 12f * _scale) };
            using var p = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText(status, w / 2f, h / 2f, SKTextAlign.Center, f, p);
            canvas.Flush();
            _canvas.SubmitCompletedFrame(bb);
            return;
        }

        var unit = Fahrenheit ? "°F" : "°C";
        var pad = Math.Max(2f, 3f * _scale);
        var forecastH = ShowForecast && data.Days.Count > 0 ? h * 0.34f : 0f;
        var topH = h - forecastH;

        // Top area: icon (left) + temperature (right), with a location/condition line beneath.
        var infoH = Math.Max(9f, topH * 0.2f);
        var mainH = topH - infoH;

        var iconSize = Math.Min(mainH * 0.85f, w * 0.42f);
        var iconCx = pad + iconSize * 0.5f;
        var iconCy = mainH * 0.5f;
        DrawIcon(canvas, data.Code, data.IsDay, iconCx, iconCy, iconSize);

        // Temperature, auto-sized to the space right of the icon so it never overflows the panel.
        var tempStr = $"{Math.Round(data.Temp)}{unit}";
        var tempLeft = iconCx + iconSize * 0.5f + pad;
        var availW = Math.Max(8f, w - tempLeft - pad);
        var tempH = mainH * 0.62f;
        DrawTextDual(canvas, tempStr, tempLeft, iconCy + tempH * 0.35f, tempH, SKColors.White, SKTextAlign.Left,
            availW);

        // Location · condition, centered and auto-fit.
        var locText = string.IsNullOrWhiteSpace(Location)
            ? ConditionText(data.Code)
            : $"{Location} · {ConditionText(data.Code)}";
        DrawTextDual(canvas, locText, w / 2f, mainH + infoH * 0.8f, infoH * 0.82f, new SKColor(180, 205, 235),
            SKTextAlign.Center, w - pad * 2);

        // Forecast strip: per-day column, all centred.
        if (forecastH > 0)
        {
            var cols = Math.Min(data.Days.Count, 4);
            var colW = w / (float)cols;
            using var sep = new SKPaint { Color = new SKColor(255, 255, 255, 30) };
            canvas.DrawRect(0, topH, w, Math.Max(1f, _scale), sep);

            var fH = Math.Max(6f, forecastH * 0.22f);
            for (var i = 0; i < cols; i++)
            {
                var day = data.Days[i];
                var cx = colW * (i + 0.5f);
                var label = i == 0 ? "Now" : day.Date.ToString("ddd", CultureInfo.InvariantCulture);
                DrawTextDual(canvas, label, cx, topH + fH * 1.15f, fH, SKColors.White, SKTextAlign.Center, colW - 2);
                DrawIcon(canvas, day.Code, true, cx, topH + forecastH * 0.52f, forecastH * 0.3f);
                DrawTextDual(canvas, $"{Math.Round(day.Max)}°/{Math.Round(day.Min)}°", cx,
                    topH + forecastH - fH * 0.35f, fH, new SKColor(160, 185, 220), SKTextAlign.Center, colW - 2);
            }
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    /// <summary>Draws text either with the system font (auto-fit) or the crisp BDF bitmap font.</summary>
    private void DrawTextDual(SKCanvas canvas, string text, float anchorX, float baselineY, float targetH,
        SKColor color, SKTextAlign align, float maxW)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (UseBdfFont)
        {
            var fontName = BdfFontRegistry.GetBestFontForHeight(Math.Max(5, (int)Math.Round(targetH)));
            using var bmp = _canvas.RenderBdfTextToBitmap(text, color, fontName);
            if (bmp is not { Width: > 0, Height: > 0 }) return;

            var scale = maxW > 0 && bmp.Width > maxW ? maxW / bmp.Width : 1f;
            var dw = bmp.Width * scale;
            var dh = bmp.Height * scale;
            var left = align == SKTextAlign.Center ? anchorX - dw / 2f :
                align == SKTextAlign.Right ? anchorX - dw : anchorX;
            var top = baselineY - dh; // BDF bitmap height ≈ glyph height; align its bottom near the baseline
            canvas.DrawBitmap(bmp, new SKRect(left, top, left + dw, top + dh));
            return;
        }

        using var font = new SKFont { Size = targetH };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var tw = font.MeasureText(text);
        if (maxW > 0 && tw > maxW && tw > 0) font.Size *= maxW / tw;
        canvas.DrawText(text, anchorX, baselineY, align, font, paint);
    }

    private static string ConditionText(int code)
    {
        return code switch
        {
            0 => "Clear",
            1 or 2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Fog",
            >= 51 and <= 57 => "Drizzle",
            >= 61 and <= 67 => "Rain",
            >= 71 and <= 77 => "Snow",
            >= 80 and <= 82 => "Showers",
            85 or 86 => "Snow showers",
            >= 95 => "Thunderstorm",
            _ => "—"
        };
    }

    private enum Cond { Clear, PartCloud, Cloud, Fog, Rain, Snow, Storm }

    private static Cond Classify(int code)
    {
        return code switch
        {
            0 => Cond.Clear,
            1 or 2 => Cond.PartCloud,
            3 => Cond.Cloud,
            45 or 48 => Cond.Fog,
            >= 51 and <= 67 => Cond.Rain,
            >= 71 and <= 77 => Cond.Snow,
            >= 80 and <= 82 => Cond.Rain,
            85 or 86 => Cond.Snow,
            >= 95 => Cond.Storm,
            _ => Cond.Cloud
        };
    }

    private void DrawIcon(SKCanvas canvas, int code, bool day, float cx, float cy, float size)
    {
        var cond = Classify(code);
        var r = size * 0.5f;

        using var sun = new SKPaint { Color = day ? new SKColor(255, 200, 60) : new SKColor(225, 230, 245), IsAntialias = true };
        using var cloud = new SKPaint { Color = new SKColor(210, 220, 235), IsAntialias = true };
        using var dark = new SKPaint { Color = new SKColor(150, 162, 180), IsAntialias = true };
        using var rain = new SKPaint { Color = new SKColor(90, 150, 240), IsAntialias = true, StrokeWidth = Math.Max(1, size * 0.05f), Style = SKPaintStyle.Stroke };
        using var bolt = new SKPaint { Color = new SKColor(255, 220, 70), IsAntialias = true };
        using var ray = new SKPaint
        {
            Color = sun.Color, StrokeWidth = Math.Max(1, size * 0.05f), IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        void Sun(float ox, float oy, float rr)
        {
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4;
                canvas.DrawLine(ox + (float)Math.Cos(a) * rr * 1.1f, oy + (float)Math.Sin(a) * rr * 1.1f,
                    ox + (float)Math.Cos(a) * rr * 1.6f, oy + (float)Math.Sin(a) * rr * 1.6f, ray);
            }

            canvas.DrawCircle(ox, oy, rr, sun);
        }

        void Cloud(float ox, float oy, float s, SKPaint p)
        {
            canvas.DrawCircle(ox - s * 0.35f, oy, s * 0.42f, p);
            canvas.DrawCircle(ox + s * 0.05f, oy - s * 0.12f, s * 0.52f, p);
            canvas.DrawCircle(ox + s * 0.45f, oy, s * 0.4f, p);
            canvas.DrawRect(ox - s * 0.6f, oy, s * 1.2f, s * 0.45f, p);
        }

        switch (cond)
        {
            case Cond.Clear:
                Sun(cx, cy, r * 0.55f);
                break;
            case Cond.PartCloud:
                Sun(cx - r * 0.3f, cy - r * 0.3f, r * 0.4f);
                Cloud(cx + r * 0.1f, cy + r * 0.1f, r, cloud);
                break;
            case Cond.Cloud:
            case Cond.Fog:
                Cloud(cx, cy, r, cond == Cond.Fog ? dark : cloud);
                break;
            case Cond.Rain:
                Cloud(cx, cy - r * 0.2f, r, dark);
                for (var i = -1; i <= 1; i++)
                    canvas.DrawLine(cx + i * r * 0.35f, cy + r * 0.35f, cx + i * r * 0.35f - r * 0.1f, cy + r * 0.75f, rain);
                break;
            case Cond.Snow:
                Cloud(cx, cy - r * 0.2f, r, cloud);
                for (var i = -1; i <= 1; i++)
                    canvas.DrawCircle(cx + i * r * 0.35f, cy + r * 0.55f, Math.Max(1, r * 0.08f), dark);
                break;
            case Cond.Storm:
                Cloud(cx, cy - r * 0.2f, r, dark);
                using (var path = new SKPath())
                {
                    path.MoveTo(cx, cy + r * 0.25f);
                    path.LineTo(cx - r * 0.18f, cy + r * 0.65f);
                    path.LineTo(cx + r * 0.02f, cy + r * 0.65f);
                    path.LineTo(cx - r * 0.12f, cy + r * 0.95f);
                    path.LineTo(cx + r * 0.22f, cy + r * 0.5f);
                    path.LineTo(cx + r * 0.02f, cy + r * 0.5f);
                    path.Close();
                    canvas.DrawPath(path, bolt);
                }

                break;
        }
    }

    private sealed class WeatherData
    {
        public double Temp;
        public int Code;
        public bool IsDay;
        public List<DayForecast> Days = new();
    }

    private sealed class DayForecast
    {
        public DateTime Date;
        public int Code;
        public double Max;
        public double Min;
    }
}
