using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.HomeAssistant;

/// <summary>
///     Current Home Assistant <c>weather.*</c> condition + temperature, with a simple animated sky.
///     Forecast strips are shown only when HA still publishes a <c>forecast</c> attribute (no service calls).
/// </summary>
[ExtensionInfo("HA Weather",
    "Home Assistant weather entity with an animated sky",
    "Information",
    IconResourceName = "home-assistant.svg")]
public class HomeAssistantWeatherExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _frame;

    internal HomeAssistantWeatherExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Entity ID", "weather.* entity", DefaultValue = "weather.home", Order = 1)]
    public string EntityId { get; set; } = "weather.home";

    [ExtensionParameter("Label", "Custom title (empty = weather condition)", DefaultValue = "", Order = 2)]
    public string Label { get; set; } = "";

    [ExtensionParameter("Show Label", "Show the condition / title line", DefaultValue = true, Order = 3)]
    public bool ShowLabel { get; set; } = true;

    [ExtensionParameter("Show Humidity", "Show the humidity percentage", DefaultValue = true, Order = 4)]
    public bool ShowHumidity { get; set; } = true;

    [ExtensionParameter("Show Icon", "Draw the sun / cloud glyph", DefaultValue = true, Order = 5)]
    public bool ShowIcon { get; set; } = true;

    [ExtensionParameter("Animated Sky", "Paint a live sky instead of a flat background", DefaultValue = true,
        Order = 6)]
    public bool AnimatedSky { get; set; } = true;

    [ExtensionParameter("Fahrenheit", "Show °F instead of °C", DefaultValue = false, Order = 7)]
    public bool Fahrenheit { get; set; }

    [ExtensionParameter("Use BDF Font", "Render with the crisp bitmap (BDF) font", DefaultValue = false, Order = 8)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Align", "Horizontal alignment of the text column", DefaultValue = HaTileAlign.Left,
        Order = 9)]
    public HaTileAlign Align { get; set; } = HaTileAlign.Left;

    [ExtensionParameter("Value Size", "Temperature text height in px (0 = auto-fit)", DefaultValue = 0, MinValue = 0,
        MaxValue = 200, Unit = "px", Order = 10)]
    public int ValueSize { get; set; }

    [ExtensionParameter("Value Color", "Colour of the temperature", DefaultValue = "#FFFFFF", Order = 11)]
    public SKColor ValueColor { get; set; } = SKColors.White;

    [ExtensionParameter("Label Color", "Colour of the condition / humidity", DefaultValue = "#D2DCEB", Order = 12)]
    public SKColor LabelColor { get; set; } = new(210, 220, 235);

    [ExtensionParameter("Background Color", "Fallback colour under the sky (or the whole tile if sky is off)",
        DefaultValue = "#0A1A2F", Order = 13)]
    public SKColor BackgroundColor { get; set; } = new(10, 26, 47);

    public string Name => "HA Weather";
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
            _timer = new Timer(120) { AutoReset = true };
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
            try
            {
                Render();
                _frame++;
            }
            catch (Exception ex) { Console.WriteLine($"[HA Weather] {ex.Message}"); }
        }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var c = new SKCanvas(bb);

        float w = _canvas.Width, h = _canvas.Height;
        var cond = "";
        var temp = double.NaN;
        var humidity = double.NaN;
        var found = HomeAssistantBridge.TryGet(EntityId, out var entity);
        if (found)
        {
            cond = entity.State ?? "";
            if (HomeAssistantBridge.TryAttrDouble(EntityId, "temperature", out var t)) temp = t;
            if (HomeAssistantBridge.TryAttrDouble(EntityId, "humidity", out var hu)) humidity = hu;
        }

        if (AnimatedSky) DrawSky(c, w, h, cond);
        else c.Clear(BackgroundColor);

        var textX = w * 0.08f;
        var textW = w * 0.84f;
        if (ShowIcon)
        {
            DrawIcon(c, cond, w * 0.22f, h * 0.42f, Math.Min(w, h) * 0.42f);
            textX = w * 0.42f;
            textW = w * 0.55f;
        }

        var tempText = !HomeAssistantBridge.Connected ? "HA offline"
            : !found ? "n/a"
            : double.IsNaN(temp) ? cond
            : Fahrenheit ? $"{temp * 9 / 5 + 32:0}°F" : $"{temp:0}°C";

        var condition = !string.IsNullOrWhiteSpace(Label)
            ? Label
            : found ? Pretty(cond) : EntityId;
        var align = HaText.ToSk(Align);
        var tempH = ValueSize > 0 ? ValueSize : h * 0.32f;

        HaText.Draw(c, _canvas, tempText, ValueColor,
            textX, h * 0.12f, textW, h * 0.42f, tempH, align, UseBdfFont);
        if (ShowLabel && !string.IsNullOrWhiteSpace(condition))
            HaText.Draw(c, _canvas, condition, LabelColor,
                textX, h * 0.52f, textW, h * 0.22f, h * 0.16f, align, UseBdfFont);
        if (ShowHumidity && !double.IsNaN(humidity))
            HaText.Draw(c, _canvas, $"{humidity:0}%", LabelColor,
                textX, h * 0.72f, textW, h * 0.2f, h * 0.14f, align, UseBdfFont);

        c.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawSky(SKCanvas c, float w, float h, string cond)
    {
        var night = cond.Contains("night", StringComparison.OrdinalIgnoreCase);
        var rain = cond.Contains("rain", StringComparison.OrdinalIgnoreCase) ||
                   cond.Contains("pour", StringComparison.OrdinalIgnoreCase);
        var snow = cond.Contains("snow", StringComparison.OrdinalIgnoreCase);
        var storm = cond.Contains("lightning", StringComparison.OrdinalIgnoreCase);
        var cloud = cond.Contains("cloud", StringComparison.OrdinalIgnoreCase) ||
                    cond.Contains("fog", StringComparison.OrdinalIgnoreCase);

        SKColor top, bot;
        if (night)
        {
            top = new SKColor(8, 12, 32);
            bot = new SKColor(20, 24, 48);
        }
        else if (storm)
        {
            top = new SKColor(30, 36, 50);
            bot = new SKColor(55, 60, 70);
        }
        else if (rain || snow)
        {
            top = new SKColor(70, 90, 120);
            bot = new SKColor(110, 125, 145);
        }
        else if (cloud)
        {
            top = new SKColor(120, 150, 190);
            bot = new SKColor(170, 185, 205);
        }
        else
        {
            top = new SKColor(70, 150, 230);
            bot = new SKColor(180, 210, 245);
        }

        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h),
            [top, bot], [0, 1], SKShaderTileMode.Clamp);
        using var sky = new SKPaint { Shader = shader };
        c.DrawRect(0, 0, w, h, sky);

        if (night)
        {
            using var star = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (var i = 0; i < 28; i++)
            {
                var x = Hash(i, 1) * w;
                var y = Hash(i, 2) * h * 0.7f;
                var tw = 0.4f + 0.6f * (float)(0.5 + 0.5 * Math.Sin(_frame * 0.08 + i));
                star.Color = new SKColor(255, 255, 255, (byte)(80 + 175 * tw));
                c.DrawCircle(x, y, 0.7f + Hash(i, 3) * 1.2f, star);
            }
        }

        if (cloud || rain || snow || storm)
        {
            using var puff = new SKPaint { Color = new SKColor(220, 228, 240, 180), IsAntialias = true };
            for (var i = 0; i < 4; i++)
            {
                var x = ((Hash(i, 4) * w) + _frame * (0.4f + i * 0.15f)) % (w + 40) - 20;
                var y = h * (0.18f + i * 0.12f);
                var s = w * (0.18f + Hash(i, 5) * 0.12f);
                c.DrawCircle(x, y, s * 0.35f, puff);
                c.DrawCircle(x + s * 0.3f, y - s * 0.08f, s * 0.4f, puff);
                c.DrawCircle(x + s * 0.55f, y, s * 0.32f, puff);
            }
        }

        if (rain || storm)
        {
            using var drop = new SKPaint
            {
                Color = new SKColor(180, 210, 255, 200), StrokeWidth = 1.2f, IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };
            for (var i = 0; i < 18; i++)
            {
                var x = (Hash(i, 6) * w + _frame * 1.6f) % w;
                var y = (Hash(i, 7) * h + _frame * 3.2f) % h;
                c.DrawLine(x, y, x - 2, y + 8, drop);
            }
        }
    }

    private static void DrawIcon(SKCanvas canvas, string cond, float cx, float cy, float size)
    {
        var r = size * 0.5f;
        var night = cond.Contains("night", StringComparison.OrdinalIgnoreCase);
        using var sun = new SKPaint { Color = night ? new SKColor(225, 230, 245) : new SKColor(255, 200, 60), IsAntialias = true };
        using var cloud = new SKPaint { Color = new SKColor(210, 220, 235), IsAntialias = true };
        using var dark = new SKPaint { Color = new SKColor(150, 162, 180), IsAntialias = true };
        using var rain = new SKPaint
        {
            Color = new SKColor(90, 150, 240), IsAntialias = true, StrokeWidth = Math.Max(1, size * 0.05f),
            Style = SKPaintStyle.Stroke
        };

        void Sun()
        {
            canvas.DrawCircle(cx, cy, r * 0.45f, sun);
        }

        void Cloud()
        {
            canvas.DrawCircle(cx - r * 0.3f, cy, r * 0.38f, cloud);
            canvas.DrawCircle(cx + r * 0.05f, cy - r * 0.12f, r * 0.48f, cloud);
            canvas.DrawCircle(cx + r * 0.4f, cy, r * 0.36f, cloud);
        }

        if (cond.Contains("lightning", StringComparison.OrdinalIgnoreCase))
        {
            Cloud();
            using var bolt = new SKPaint { Color = new SKColor(255, 220, 70), IsAntialias = true };
            using var p = new SKPath();
            p.MoveTo(cx, cy + r * 0.1f);
            p.LineTo(cx - r * 0.15f, cy + r * 0.45f);
            p.LineTo(cx + r * 0.05f, cy + r * 0.45f);
            p.LineTo(cx - r * 0.1f, cy + r * 0.8f);
            p.LineTo(cx + r * 0.22f, cy + r * 0.3f);
            p.Close();
            canvas.DrawPath(p, bolt);
        }
        else if (cond.Contains("rain", StringComparison.OrdinalIgnoreCase) ||
                 cond.Contains("pour", StringComparison.OrdinalIgnoreCase))
        {
            Cloud();
            for (var i = -1; i <= 1; i++)
                canvas.DrawLine(cx + i * r * 0.3f, cy + r * 0.35f, cx + i * r * 0.3f - r * 0.1f, cy + r * 0.7f, rain);
        }
        else if (cond.Contains("snow", StringComparison.OrdinalIgnoreCase))
        {
            Cloud();
            for (var i = -1; i <= 1; i++)
                canvas.DrawCircle(cx + i * r * 0.3f, cy + r * 0.5f, Math.Max(1, r * 0.08f), dark);
        }
        else if (cond.Contains("cloud", StringComparison.OrdinalIgnoreCase) ||
                 cond.Contains("fog", StringComparison.OrdinalIgnoreCase))
        {
            Cloud();
        }
        else
        {
            Sun();
        }
    }

    private static string Pretty(string cond)
    {
        if (string.IsNullOrWhiteSpace(cond)) return "";
        return cond.Replace('-', ' ');
    }

    private static float Hash(int i, int salt)
    {
        var n = Math.Sin(i * 12.9898 + salt * 78.233) * 43758.5453;
        return (float)(n - Math.Floor(n));
    }
}
