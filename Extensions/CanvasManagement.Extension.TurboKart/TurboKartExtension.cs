using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.TurboKart;

/// <summary>
///     Pseudo-3D kart racer: 8 Mode-7 circuits in two cups, Grand Prix and single races, 7 CPU rivals, items,
///     drift mini-turbos. Keyboard or gamepad (Studio maps the pad onto the Controls methods below).
/// </summary>
[ExtensionInfo("Turbo Kart GP",
    "Pseudo-3D kart racing: Grand Prix with 2 cups / 8 tracks, CPU rivals, items — keyboard or gamepad",
    "Games",
    IconResourceName = "turbo-kart.svg")]
public sealed class TurboKartExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Stopwatch _watch = new();
    private readonly Dictionary<(string, uint, float, uint), CachedText> _textCache = new();
    private readonly ConcurrentQueue<Btn> _presses = new();
    private ExtensionLoop? _loop;
    private SKBitmap? _backBuffer;
    private Game? _game;
    private double _lastTime;
    private volatile bool _up, _down, _left, _right, _gas, _brake;

    internal TurboKartExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Laps", "Laps per race", DefaultValue = 3, MinValue = 1, MaxValue = 9, Order = 1)]
    public int Laps { get; set; } = 3;

    [ExtensionParameter("CPU Racers", "Number of computer opponents", DefaultValue = 7, MinValue = 0, MaxValue = 7,
        Order = 2)]
    public int CpuRacers { get; set; } = 7;

    [ExtensionParameter("Difficulty", "CPU pace", DefaultValue = 5, MinValue = 1, MaxValue = 10, Order = 3)]
    public int Difficulty { get; set; } = 5;

    [ExtensionParameter("Frame Rate", "Target frames per second", DefaultValue = 30, MinValue = 15, MaxValue = 60,
        Unit = "fps", Order = 4)]
    public int FrameRate { get; set; } = 30;

    [ExtensionParameter("Auto Pilot", "Demo races cycle through all tracks until someone presses a key",
        DefaultValue = true, Order = 5)]
    public bool AutoPilot { get; set; } = true;

    [ExtensionParameter("Weather", "Auto picks per track (rain, snow, embers)", DefaultValue = TurboKartWeather.Auto,
        Order = 6)]
    public TurboKartWeather Weather { get; set; } = TurboKartWeather.Auto;

    [ExtensionParameter("Day/Night Cycle", "Sunset and night fall over the laps", DefaultValue = true, Order = 7)]
    public bool DayNight { get; set; } = true;

    [ExtensionParameter("Finish Replay", "Slow-motion side-camera replay after you cross the line",
        DefaultValue = true, Order = 8)]
    public bool FinishReplay { get; set; } = true;

    [ExtensionParameter("Sound", "Engine, countdown and race effects on the audio board",
        DefaultValue = true, Order = 9)]
    public bool Sound { get; set; } = true;

    [ExtensionParameter("Volume", "How loud this game is on the audio board",
        DefaultValue = 80, MinValue = 0, MaxValue = 100, Order = 10)]
    public int Volume { get; set; } = 80;

    [ExtensionParameter("Music", "Background music (quieter while racing)", DefaultValue = true, Order = 11)]
    public bool Music { get; set; } = true;

    [ExtensionParameter("Music Volume", "Music level relative to the effects", DefaultValue = 40, MinValue = 0,
        MaxValue = 100, Order = 12)]
    public int MusicVolume { get; set; } = 40;

    private KartAudio? _audio;

    public string Name => "Turbo Kart GP";
    public bool IsRunning { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            IsRunning = true;
            _up = _down = _left = _right = _gas = _brake = false;
            _presses.Clear();
            _game = new Game();
            _audio = new KartAudio(_canvas);
            _watch.Restart();
            _lastTime = 0;
            _loop = new ExtensionLoop(FrameRate, Tick, _canvas.Stats);
            _loop.Start();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _loop?.Dispose();
            _loop = null;
            _audio?.Silence();
            _audio = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            _game = null;
            ClearTextCache();
            try { _canvas.Clear(SKColors.Black); } catch { /* canvas gone */ }
        }
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ controls
    // Studio maps the D-pad / left stick onto the arrow bindings and gamepad buttons onto the Pad* names.

    [ExtensionMethod("Up", "Accelerate / menu up", Category = "Controls", KeyboardShortcut = "Up", Order = 1)]
    public void UpDown() { _up = true; _presses.Enqueue(Btn.Up); }

    [ExtensionMethod("Up Release", "Release up", Category = "Controls", KeyboardShortcut = "Up:up", Order = 2)]
    public void UpRelease() => _up = false;

    [ExtensionMethod("Down", "Brake; hold while steering to drift / menu down", Category = "Controls",
        KeyboardShortcut = "Down", Order = 3)]
    public void DownDown() { _down = true; _presses.Enqueue(Btn.Down); }

    [ExtensionMethod("Down Release", "Release brake (fires a charged drift boost)", Category = "Controls",
        KeyboardShortcut = "Down:up", Order = 4)]
    public void DownRelease() => _down = false;

    [ExtensionMethod("Left", "Steer left", Category = "Controls", KeyboardShortcut = "Left", Order = 5)]
    public void LeftDown() { _left = true; _presses.Enqueue(Btn.Left); }

    [ExtensionMethod("Left Release", "Release left", Category = "Controls", KeyboardShortcut = "Left:up", Order = 6)]
    public void LeftRelease() => _left = false;

    [ExtensionMethod("Right", "Steer right", Category = "Controls", KeyboardShortcut = "Right", Order = 7)]
    public void RightDown() { _right = true; _presses.Enqueue(Btn.Right); }

    [ExtensionMethod("Right Release", "Release right", Category = "Controls", KeyboardShortcut = "Right:up",
        Order = 8)]
    public void RightRelease() => _right = false;

    [ExtensionMethod("Gas (A)", "Gamepad accelerate / confirm", Category = "Controls",
        KeyboardShortcut = "PadA|PadRT", Order = 9)]
    public void GasDown() { _gas = true; _presses.Enqueue(Btn.A); }

    [ExtensionMethod("Gas Release", "Release gamepad accelerator", Category = "Controls",
        KeyboardShortcut = "PadA:up|PadRT:up", Order = 10)]
    public void GasRelease() => _gas = false;

    [ExtensionMethod("Brake (B)", "Gamepad brake / drift / back", Category = "Controls",
        KeyboardShortcut = "PadB|PadLT|PadRB", Order = 11)]
    public void BrakeDown() { _brake = true; _presses.Enqueue(Btn.B); }

    [ExtensionMethod("Brake Release", "Release gamepad brake", Category = "Controls",
        KeyboardShortcut = "PadB:up|PadLT:up|PadRB:up", Order = 12)]
    public void BrakeRelease() => _brake = false;

    [ExtensionMethod("Use Item", "Fire / drop the held item; confirm in menus", Category = "Controls",
        KeyboardShortcut = "Space|PadX|PadY|PadLB", Order = 13)]
    public void UseItem() => _presses.Enqueue(Btn.Item);

    [ExtensionMethod("Pause", "Pause the race / confirm", Category = "Controls", KeyboardShortcut = "Enter|PadStart",
        Order = 14)]
    public void Pause() => _presses.Enqueue(Btn.Start);

    [ExtensionMethod("Back", "Back / pause", Category = "Controls", KeyboardShortcut = "Escape|Backspace|PadBack",
        Order = 15)]
    public void Back() => _presses.Enqueue(Btn.Back);

    // ------------------------------------------------------------------ loop

    private Task Tick(CancellationToken ct)
    {
        var game = _game;
        var w = _canvas.Width;
        var h = _canvas.Height;
        if (game == null || w < 32 || h < 32) return Task.CompletedTask;

        game.W = w;
        game.H = h;
        game.Laps = Laps;
        game.Cpu = CpuRacers;
        game.Difficulty = Difficulty;
        game.AutoPilot = AutoPilot;
        game.Weather = Weather;
        game.DayNight = DayNight;
        game.Replay = FinishReplay;
        game.EnsureRenderer();

        var now = _watch.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastTime, 0.001, 1.0 / 15);
        _lastTime = now;

        var presses = new List<Btn>();
        while (_presses.TryDequeue(out var b)) presses.Add(b);
        var hold = new DriveInput
        {
            Up = _up || _gas, Down = _down || _brake, Left = _left, Right = _right
        };

        game.Update(dt, hold, presses);
        _audio?.Update(game, Sound, Volume / 100f, presses.Count > 0);
        _audio?.Music(game, Music, Volume / 100f, MusicVolume / 100f);
        var px = game.RenderFrame();

        var bmp = Buffer(w, h);
        Marshal.Copy(px, 0, bmp.GetPixels(), w * h);
        using (var sk = new SKCanvas(bmp))
        {
            game.DrawText((text, color, x, y, size, align, shadow, outline) =>
                Text(sk, text, color, x, y, size, align, shadow, outline));
            sk.Flush();
        }

        _canvas.SubmitCompletedFrame(bmp);
        return Task.CompletedTask;
    }

    private SKBitmap Buffer(int w, int h)
    {
        if (_backBuffer == null || _backBuffer.Width != w || _backBuffer.Height != h)
        {
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        }

        return _backBuffer;
    }

    // ------------------------------------------------------------------ text

    private sealed class CachedText : IDisposable
    {
        public SKBitmap Bitmap = null!;
        public float TextWidth, Pad, Baseline;
        public void Dispose() => Bitmap.Dispose();
    }

    private void ClearTextCache()
    {
        foreach (var t in _textCache.Values) t.Dispose();
        _textCache.Clear();
    }

    /// <summary>Outlined BDF text, rendered once per (text, colour, size) and blitted afterwards.</summary>
    private void Text(SKCanvas sk, string text, SKColor color, float x, float y, float size, SKTextAlign align,
        SKColor? shadow = null, bool outline = true)
    {
        var sh = shadow ?? new SKColor(10, 10, 20);
        var key = (text, (uint)color, size, outline ? (uint)sh : 0u);
        if (!_textCache.TryGetValue(key, out var entry))
        {
            if (_textCache.Count > 256) ClearTextCache();
            var tw = CanvasText.Measure(_canvas, text, size, true);
            if (tw <= 0) return;
            var o = outline ? Math.Max(1, (int)(size / 10)) : 0;
            var bw = (int)Math.Ceiling(tw) + 2 * o + 2;
            var bh = (int)Math.Ceiling(size * 1.6f) + 2 * o;
            var bmp = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var c = new SKCanvas(bmp))
            {
                c.Clear(SKColors.Transparent);
                var baseline = bh - o;
                if (outline)
                    foreach (var (dx, dy) in new[] { (-o, 0), (o, 0), (0, -o), (0, o), (o, o) })
                        CanvasText.Draw(c, _canvas, text, sh, o + dx, baseline + dy, size, SKTextAlign.Left, true);
                CanvasText.Draw(c, _canvas, text, color, o, baseline, size, SKTextAlign.Left, true);
            }

            entry = new CachedText { Bitmap = bmp, TextWidth = tw, Pad = o, Baseline = bh - o };
            _textCache[key] = entry;
        }

        var left = align switch
        {
            SKTextAlign.Center => x - entry.TextWidth / 2f,
            SKTextAlign.Right => x - entry.TextWidth,
            _ => x
        } - entry.Pad;
        sk.DrawBitmap(entry.Bitmap, MathF.Round(left), MathF.Round(y - entry.Baseline));
    }
}
