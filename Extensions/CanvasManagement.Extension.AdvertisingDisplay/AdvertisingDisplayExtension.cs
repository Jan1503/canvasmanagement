using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.AdvertisingDisplay;

/// <summary>Direction the message text travels.</summary>
public enum ScrollDirection
{
    Left,
    Right,
    Up,
    Down,
    None
}

/// <summary>Animated decorative border style drawn around the display.</summary>
public enum BorderStyle
{
    None,
    Marquee,
    Pulse,
    Chase,
    Rainbow,
    Dashed,
    Neon,
    Snake
}

/// <summary>How to parse messages fetched from a live data source URL.</summary>
public enum DataSourceFormat
{
    Lines,
    RssTitles
}

/// <summary>One-click theme that configures a pleasant combination of options.</summary>
public enum SignagePreset
{
    None,
    CoffeeShop,
    Party,
    News,
    Retro,
    Minimal,
    // ─── Hollywood Presets (original) ───────────────────────────
    Matrix,      // The Matrix digital rain
    Cyberpunk,   // Glitchy neon cyberpunk
    SciFi,       // Sci-fi hologram
    Action,      // Explosive action movie
    Horror,      // Spooky lightning effect
    // ─── Blockbuster Studio Intros ──────────────────────────────
    Blockbuster, // Marvel-style flash-in with big scale
    Galaxy,      // Star Wars perspective crawl
    Animation,   // Pixar-style bouncy character intro
    Cartoon,     // Minions goofy wobble
    Trailer      // MTV action-trailer jump-cuts
}

/// <summary>Per-character animation used in static (non-scrolling) mode.</summary>
public enum TextEffect
{
    None,
    FlyIn,
    Drop,
    Typewriter,
    Bounce,
    ZoomIn,
    Wave,
    Spiral,
    Rain,
    Paint,
    Roll,
    Flip,
    Slot,
    PixelPaint,
    Dissolve,
    // ─── Cinematic Effects (original round) ──────────────────────
    MatrixRain,    // Digital rain like The Matrix
    Glitch,        // Digital glitch/distortion
    Shatter,       // Glass shatter effect
    Vortex,        // Spinning vortex pull-in
    FadeReveal,    // Cinematic fade with mask
    Neon,          // Neon sign flicker
    Hologram,      // Sci-fi hologram materialization
    Fire,          // Burning/flame effect
    Ripple,        // Water ripple distortion
    Explode,       // Particle explosion
    Assemble,      // Puzzle pieces assembling
    Lightning,     // Electric arc effect
    // ─── Blockbuster Movie Intros ────────────────────────────────
    StarWars,      // Perspective scroll receding into distance
    Pixar,         // Bouncy squash & stretch character intro
    Minion,        // Goofy anticipation wobble (Despicable Me style)
    MarvelFlash,   // Explosive flash-in with dramatic zoom (comic book)
    LightSpeed,    // Hyperspace streak from vanishing point
    Portal,        // Swirling portal materialisation (Doctor Strange)
    Domino,        // Chain-reaction falling forward
    CameraShake,   // Tremor / earthquake reveal
    FilmReel,      // Old projector: flicker + judder + missed frames
    Bubble,        // Elastic soap-bubble swell & settle
    HeroLanding,   // Superhero smash-down with radial shockwave
    JumpCut,       // Rapid jump-cuts between positions before settling
    // ─── Narrative Effects (with drawn actors / props) ──────────
    StickBuild,    // Stick figures walk in carrying characters and place them
    StickKick,     // Stick figure kicks each character into position
    DominoPush,    // First char slams in and shoves each next char into place
    Conveyor,      // Characters ride in on a conveyor belt
    Magnet,        // A cartoon horseshoe magnet drags characters in one by one
    PoolBreak,     // Chars start piled centre-stage; cue ball smashes them into position
    Builder,       // Construction worker assembles each char piece by piece
    DogWalk,       // A dog walks along dropping each char behind it
    PacManEat,     // Pac-Man mows through a scrambled pile revealing chars in place
    NeoVision      // Matrix scanline reveals + rain of glyphs settling into text
}

/// <summary>
///     A feature-rich digital-signage / advertising display: cycles through messages with
///     configurable scrolling (left/right/up/down), blinking, fading, multi-colour rainbow text,
///     emoji support and an animated decorative border. Optionally pulls live messages from a news
///     feed or stats endpoint. Everything is configurable at runtime via extension properties and
///     methods.
/// </summary>
[ExtensionInfo("Advertising Display",
    "Coffee-shop style animated signage: scrolling/blinking/fading multi-colour text with animated " +
    "borders and optional live news/stats feed. Fully runtime-configurable.",
    "Text & Display",
    IconResourceName = "advertising.svg")]
public class AdvertisingDisplayExtension : ICanvasExtension, IDisposable
{
    private static readonly char[] MessageSeparators = { '|', '\n' };

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly float _scale;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private bool _disposed;

    // Animation
    private int _frame;

    // Message lanes (one per line). Rebuilt from the simple properties or from the Lines list.
    private readonly List<MessageLane> _lanes = new();
    private volatile bool _rebuildLanes;
    private List<LineConfig> _lines = new();
    private string[] _messages = DefaultMessages;

    // Global particle / decoration layers shared across all lanes.
    private readonly List<ConfettiParticle> _confetti = new();
    private readonly List<DecoSprite> _deco = new();
    private readonly Dictionary<string, SKBitmap> _decoMasks = new();

    // Live data source
    private HttpClient? _http;
    private Timer? _dataTimer;
    private volatile bool _fetching;

    private static string[] DefaultMessages =>
        new[] { "WELCOME!", "FRESH COFFEE - 2.50", "OPEN 7:00 - 19:00", "TRY OUR CAKES" };

    internal AdvertisingDisplayExtension(ICanvas canvas)
    {
        _canvas = canvas;
        _scale = DisplayScale.GetScale(canvas.Width, canvas.Height);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Parameters (runtime-configurable)
    // ───────────────────────────────────────────────────────────────────────

    [ExtensionParameter("Messages", "Messages to display, separated by '|' (or new lines)",
        DefaultValue = "WELCOME!|FRESH COFFEE - 2.50|OPEN 7:00 - 19:00|TRY OUR CAKES")]
    public string Messages
    {
        get => string.Join("|", _messages);
        set => SetMessagesInternal(value);
    }

    [ExtensionParameter("Lines",
        "Multi-line layout: add a card per line, each with its own text, effect, colour, font and height. " +
        "Leave empty to use the single-line settings above.")]
    public List<LineConfig> Lines
    {
        get => _lines;
        set
        {
            _lines = value ?? new List<LineConfig>();
            _rebuildLanes = true;
        }
    }

    [ExtensionParameter("Scroll Direction", "Direction the text scrolls (Left/Right/Up/Down/None)",
        DefaultValue = "Left")]
    public ScrollDirection Direction { get; set; } = ScrollDirection.Left;

    [ExtensionParameter("Scroll Speed", "Scrolling speed in pixels per frame",
        DefaultValue = 4, MinValue = 1, MaxValue = 30, Unit = "px")]
    public int ScrollSpeed { get; set; } = 4;

    [ExtensionParameter("Effect Speed",
        "Playback rate for entrance animations (per-char and narrative). " +
        "1.0 = default, lower = slower/more readable, higher = faster.",
        DefaultValue = 0.5, MinValue = 0.1, MaxValue = 3.0)]
    public double EffectSpeed { get; set; } = 0.5;

    [ExtensionParameter("Message Duration", "How long each message is shown when not scrolling",
        DefaultValue = 5, MinValue = 1, MaxValue = 120, Unit = "s")]
    public int MessageDurationSeconds { get; set; } = 5;

    [ExtensionParameter("Multi Colour", "Animate the text through rainbow colours",
        DefaultValue = true)]
    public bool MultiColor { get; set; } = true;

    [ExtensionParameter("Text Colour", "Text colour when multi-colour is off", DefaultValue = "#FFFFFF")]
    public SKColor TextColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Colour", "Background colour", DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Blink", "Blink the text on and off", DefaultValue = false)]
    public bool Blink { get; set; }

    [ExtensionParameter("Blink Interval", "Blink on/off interval", DefaultValue = 500, MinValue = 100,
        MaxValue = 3000, Unit = "ms")]
    public int BlinkIntervalMs { get; set; } = 500;

    [ExtensionParameter("Fade", "Fade messages in and out", DefaultValue = true)]
    public bool Fade { get; set; } = true;

    [ExtensionParameter("Text Effect",
        "Per-character animation when Scroll Direction is None. Classics: " +
        "None/FlyIn/Drop/Typewriter/Bounce/ZoomIn/Wave/Spiral/Rain/Paint/Roll/Flip/Slot/PixelPaint/Dissolve. " +
        "Cinematic: MatrixRain/Glitch/Shatter/Vortex/FadeReveal/Neon/Hologram/Fire/Ripple/Explode/Assemble/Lightning. " +
        "Blockbuster: StarWars/Pixar/Minion/MarvelFlash/LightSpeed/Portal/Domino/CameraShake/FilmReel/Bubble/HeroLanding/JumpCut. " +
        "Narrative (drawn actors): StickBuild/StickKick/DominoPush/Conveyor/Magnet/PoolBreak/Builder/DogWalk/PacManEat/NeoVision",
        DefaultValue = "FlyIn")]
    public TextEffect Effect { get; set; } = TextEffect.FlyIn;

    [ExtensionParameter("Effect Stagger", "Delay between each character animating in",
        DefaultValue = 60, MinValue = 0, MaxValue = 500, Unit = "ms")]
    public int CharStagger { get; set; } = 60;

    [ExtensionParameter("Sparkle", "Emit twinkling sparkles around the text", DefaultValue = true)]
    public bool Sparkle { get; set; } = true;

    [ExtensionParameter("Glow", "Neon glow / soft outline around the text", DefaultValue = true)]
    public bool Glow { get; set; } = true;

    [ExtensionParameter("Twinkle", "Per-character brightness/colour twinkle", DefaultValue = false)]
    public bool Twinkle { get; set; }

    [ExtensionParameter("Confetti", "Burst of falling confetti on each new message", DefaultValue = true)]
    public bool Confetti { get; set; } = true;

    private string _decorations = "";
    private int _decorationCount = 8;
    private SignagePreset _preset = SignagePreset.None;

    [ExtensionParameter("Decorations", "Floating background symbols/emoji, space separated " +
        "(e.g. \"* + . o\" or emoji)", DefaultValue = "")]
    public string Decorations
    {
        get => _decorations;
        set
        {
            var v = value ?? "";
            if (_decorations == v) return;
            _decorations = v;
            ReinitDecorations(); // re-build so runtime changes take effect immediately
        }
    }

    [ExtensionParameter("Decoration Count", "How many floating decoration sprites to show",
        DefaultValue = 8, MinValue = 0, MaxValue = 40)]
    public int DecorationCount
    {
        get => _decorationCount;
        set
        {
            var v = Math.Clamp(value, 0, 40);
            if (_decorationCount == v) return;
            _decorationCount = v;
            ReinitDecorations();
        }
    }

    [ExtensionParameter("Preset",
        "One-click theme - Classic: None/CoffeeShop/Party/News/Retro/Minimal " +
        "Hollywood: Matrix/Cyberpunk/SciFi/Action/Horror - sets a nice combination of the options below",
        DefaultValue = "None")]
    public SignagePreset Preset
    {
        get => _preset;
        set
        {
            _preset = value;
            // Selecting None must actually RESET the preset-influenced fields back to their
            // safe defaults; previously it did nothing so users couldn't back out of a preset.
            if (value == SignagePreset.None) ApplyPresetNone();
            else ApplyPreset(value);
        }
    }

    private int _fontSize;
    private bool _useBdfFont = true;
    private string _bdfFontName = "";
    private string _fontFamily = "Arial";
    private bool _emojis;

    [ExtensionParameter("Font Size", "Text height in pixels (0 = auto-fit to the display)",
        DefaultValue = 0, MinValue = 0, MaxValue = 200, Unit = "px")]
    public int FontSize
    {
        get => _fontSize;
        set { if (_fontSize == value) return; _fontSize = value; _rebuildLanes = true; }
    }

    [ExtensionParameter("Use Bitmap Font", "Use crisp BDF bitmap font (best for small LED panels)",
        DefaultValue = true)]
    public bool UseBdfFont
    {
        get => _useBdfFont;
        set { if (_useBdfFont == value) return; _useBdfFont = value; _rebuildLanes = true; }
    }

    [ExtensionParameter("Bitmap Font Name", "BDF font name to load dynamically (empty = framework default)",
        DefaultValue = "")]
    public string BdfFontName
    {
        get => _bdfFontName;
        set { var v = value ?? ""; if (_bdfFontName == v) return; _bdfFontName = v; _rebuildLanes = true; }
    }

    [ExtensionParameter("Font Family", "Font family used for the Skia / emoji text path", DefaultValue = "Arial")]
    public string FontFamily
    {
        get => _fontFamily;
        set { var v = value ?? "Arial"; if (_fontFamily == v) return; _fontFamily = v; _rebuildLanes = true; }
    }

    [ExtensionParameter("Emojis", "Render emojis / unicode symbols (uses the Skia text path)",
        DefaultValue = false)]
    public bool Emojis
    {
        get => _emojis;
        set { if (_emojis == value) return; _emojis = value; _rebuildLanes = true; }
    }

    [ExtensionParameter("Border Style", "Animated border (None/Marquee/Pulse/Chase)", DefaultValue = "Marquee")]
    public BorderStyle Border { get; set; } = BorderStyle.Marquee;

    [ExtensionParameter("Rainbow Border", "Cycle the border through rainbow colours", DefaultValue = true)]
    public bool RainbowBorder { get; set; } = true;

    [ExtensionParameter("Border Colour", "Border colour when rainbow is off", DefaultValue = "#FF1493")]
    public SKColor BorderColor { get; set; } = new(255, 20, 147);

    [ExtensionParameter("Data Source URL", "Optional URL (RSS or plain text) to pull live messages from",
        DefaultValue = "")]
    public string DataSourceUrl { get; set; } = "";

    [ExtensionParameter("Data Source Format", "How to parse the data source (Lines/RssTitles)",
        DefaultValue = "RssTitles")]
    public DataSourceFormat DataFormat { get; set; } = DataSourceFormat.RssTitles;

    [ExtensionParameter("Data Refresh", "How often to re-fetch the data source", DefaultValue = 300,
        MinValue = 30, MaxValue = 86400, Unit = "s")]
    public int DataRefreshSeconds { get; set; } = 300;

    // ───────────────────────────────────────────────────────────────────────
    // ICanvasExtension
    // ───────────────────────────────────────────────────────────────────────

    public string Name => "Advertising Display";
    public bool IsRunning { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888,
                SKAlphaType.Premul));

            _frame = 0;
            BuildLanes();
            InitDecorations();
            SpawnConfetti();

            _timer = new Timer(33) { AutoReset = true }; // ~30 FPS
            _timer.Elapsed += OnTick;
            _timer.Start();

            IsRunning = true;

            StartDataSource();
            Console.WriteLine($"[ADVERT] Started ({_messages.Length} message(s), dir={Direction})");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;

            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;

            _dataTimer?.Stop();
            _dataTimer?.Dispose();
            _dataTimer = null;

            _backBuffer?.Dispose();
            _backBuffer = null;

            foreach (var lane in _lanes) lane.Dispose();
            _lanes.Clear();

            _confetti.Clear();
            _deco.Clear();
            foreach (var m in _decoMasks.Values) m.Dispose();
            _decoMasks.Clear();

            IsRunning = false;

            try { _canvas.Clear(); }
            catch { /* ignore */ }

            Console.WriteLine("[ADVERT] Stopped");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _http?.Dispose();
        _http = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Runtime methods
    // ───────────────────────────────────────────────────────────────────────

    [ExtensionMethod("Set Messages", "Replace all messages (separate with '|')", Category = "Content", Order = 1)]
    public void SetMessages(string messages)
    {
        SetMessagesInternal(messages);
        _rebuildLanes = true;
    }

    [ExtensionMethod("Add Message", "Append a single message to the rotation", Category = "Content", Order = 2)]
    public void AddMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_lock)
        {
            var list = new List<string>(_messages) { message.Trim() };
            _messages = list.ToArray();
        }
        _rebuildLanes = true;
    }

    [ExtensionMethod("Next Message", "Skip to the next message immediately", Category = "Playback", Order = 3)]
    public void NextMessage()
    {
        lock (_lock)
        {
            var now = _clock.Elapsed.TotalMilliseconds;
            foreach (var lane in _lanes) lane.ForceNext(now);
        }
    }

    [ExtensionMethod("Refresh Feed", "Re-fetch the live data source now", Category = "Data", Order = 4)]
    public void RefreshFeed()
    {
        _ = FetchDataAsync();
    }

    // ───────────────────────────────────────────────────────────────────────
    // Render loop
    // ───────────────────────────────────────────────────────────────────────

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (!IsRunning) return;

        lock (_lock)
        {
            if (_backBuffer == null) return;

            if (_rebuildLanes)
            {
                BuildLanes();
                _rebuildLanes = false;
            }

            var nowMs = _clock.Elapsed.TotalMilliseconds;

            // In single-line mode (no Lines defined) the simple properties feed the one lane live so
            // changing them in the GUI takes effect immediately without a full rebuild.
            if (_lines.Count == 0 && _lanes.Count == 1)
                RefreshLaneFromGlobals(_lanes[0]);

            try
            {
                using var canvas = new SKCanvas(_backBuffer);
                var w = _backBuffer.Width;
                var h = _backBuffer.Height;
                canvas.Clear(BackgroundColor);

                DrawDecorations(canvas, w, h); // floating background graphics/emoji
                DrawBorder(canvas, w, h);

                // Stack the lanes into horizontal bands sized by their weight.
                var totalWeight = 0f;
                foreach (var l in _lanes) totalWeight += Math.Max(0.01f, l.Weight);
                if (totalWeight <= 0f) totalWeight = 1f;

                var advanced = false;
                float yCursor = 0f;
                for (var i = 0; i < _lanes.Count; i++)
                {
                    var lane = _lanes[i];
                    var bandH = i == _lanes.Count - 1
                        ? (int)Math.Round(h - yCursor)
                        : (int)Math.Round(Math.Max(0.01f, lane.Weight) / totalWeight * h);
                    if (bandH <= 0) continue;

                    canvas.Save();
                    canvas.Translate(0, (int)Math.Round(yCursor));
                    canvas.ClipRect(new SKRect(0, 0, w, bandH));
                    advanced |= lane.Render(canvas, w, bandH, _frame, nowMs, _scale);
                    canvas.Restore();
                    yCursor += bandH;
                }

                if (advanced) SpawnConfetti();
                UpdateAndDrawConfetti(canvas, w, h); // on top of everything

                canvas.Flush();
                _canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADVERT] Render error: {ex.Message}");
            }

            _frame++;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Lane orchestration
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>(Re)builds the lane list from the Lines config, or a single lane from the simple props.</summary>
    private void BuildLanes()
    {
        foreach (var l in _lanes) l.Dispose();
        _lanes.Clear();

        var now = _clock.Elapsed.TotalMilliseconds;
        var built = new List<MessageLane>();

        foreach (var line in _lines)
            if (line != null)
                built.Add(CreateLaneFromConfig(line));

        if (built.Count == 0)
            built.Add(CreateLaneFromGlobals());

        foreach (var l in built) l.Reset(now);
        _lanes.AddRange(built);
    }

    /// <summary>A lane seeded from every global property (the single-line default).</summary>
    private MessageLane CreateLaneFromGlobals()
    {
        return new MessageLane(_canvas, _random)
        {
            Messages = _messages,
            Direction = Direction,
            Effect = Effect,
            ScrollSpeed = ScrollSpeed,
            MessageDurationSeconds = MessageDurationSeconds,
            MultiColor = MultiColor,
            TextColor = TextColor,
            Blink = Blink,
            BlinkIntervalMs = BlinkIntervalMs,
            Fade = Fade,
            FontSize = FontSize,
            UseBdfFont = UseBdfFont,
            BdfFontName = BdfFontName,
            FontFamily = FontFamily,
            Emojis = Emojis,
            CharStagger = CharStagger,
            Sparkle = Sparkle,
            Glow = Glow,
            Twinkle = Twinkle,
            Weight = 1f,
            EffectSpeed = (float)EffectSpeed
        };
    }

    /// <summary>Copies the live-tunable global props onto a lane (no rebuild required).</summary>
    private void RefreshLaneFromGlobals(MessageLane lane)
    {
        // Effect/Direction/colour are latched per message inside the lane, so a change there needs a
        // (cheap) rebuild of the current message to take effect immediately.
        var needsRebuild = lane.Direction != Direction || lane.Effect != Effect ||
                           lane.TextColor != TextColor || lane.MultiColor != MultiColor;

        lane.Direction = Direction;
        lane.Effect = Effect;
        lane.ScrollSpeed = ScrollSpeed;
        lane.MessageDurationSeconds = MessageDurationSeconds;
        lane.MultiColor = MultiColor;
        lane.TextColor = TextColor;
        lane.Blink = Blink;
        lane.BlinkIntervalMs = BlinkIntervalMs;
        lane.Fade = Fade;
        lane.CharStagger = CharStagger;
        lane.Sparkle = Sparkle;
        lane.Glow = Glow;
        lane.Twinkle = Twinkle;
        lane.EffectSpeed = (float)EffectSpeed;

        if (needsRebuild) lane.Rebuild();
    }

    /// <summary>Builds one lane from a typed line config, inheriting non-per-line settings from globals.</summary>
    private MessageLane CreateLaneFromConfig(LineConfig line)
    {
        var lane = CreateLaneFromGlobals();

        lane.Messages = new[] { line.Text ?? "" };
        lane.Effect = line.Effect;
        lane.Direction = line.Direction;
        lane.MultiColor = line.MultiColor;
        lane.TextColor = line.Color;
        lane.FontSize = line.FontSize;
        lane.ScrollSpeed = Math.Max(1, line.Speed);
        lane.MessageDurationSeconds = Math.Max(1, line.DurationSeconds);
        lane.Weight = Math.Max(0.01f, line.Weight);
        lane.Glow = line.Glow;
        lane.Sparkle = line.Sparkle;
        lane.Twinkle = line.Twinkle;
        lane.Blink = line.Blink;
        lane.UseBdfFont = line.UseBdfFont;

        return lane;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Confetti (burst on each new message)
    // ───────────────────────────────────────────────────────────────────────

    private void SpawnConfetti()
    {
        if (!Confetti || _backBuffer == null) return;

        var w = _backBuffer.Width;
        var h = _backBuffer.Height;
        var size = Math.Max(2, _canvas.ScaleSize(3));
        var count = Math.Clamp(w / 6, 20, 80);

        for (var i = 0; i < count; i++)
            _confetti.Add(new ConfettiParticle
            {
                X = _random.Next(w),
                Y = -_random.Next(Math.Max(1, h / 2)),
                Vx = (_random.NextSingle() - 0.5f) * 3f * _scale,
                Vy = (1f + _random.NextSingle() * 3f) * _scale,
                Rot = _random.Next(360),
                Vrot = (_random.NextSingle() - 0.5f) * 24f,
                Life = 0,
                Max = 150,
                Size = size,
                Color = SignageFx.Hue(_random.Next(360))
            });
    }

    private void UpdateAndDrawConfetti(SKCanvas canvas, int w, int h)
    {
        if (_confetti.Count == 0) return;

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        for (var i = _confetti.Count - 1; i >= 0; i--)
        {
            var c = _confetti[i];
            c.Vy += 0.08f * _scale; // gravity
            c.X += c.Vx;
            c.Y += c.Vy;
            c.Rot += c.Vrot;
            c.Life++;

            if (c.Life >= c.Max || c.Y > h + 10)
            {
                _confetti.RemoveAt(i);
                continue;
            }

            paint.Color = c.Color;
            canvas.Save();
            canvas.Translate(c.X, c.Y);
            canvas.RotateDegrees(c.Rot);
            canvas.DrawRect(-c.Size / 2f, -c.Size / 2f, c.Size, c.Size * 0.6f, paint);
            canvas.Restore();

            _confetti[i] = c;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Floating decorations (little graphics / emoji)
    // ───────────────────────────────────────────────────────────────────────

    private void ReinitDecorations()
    {
        lock (_lock)
        {
            if (IsRunning && _backBuffer != null) InitDecorations();
        }
    }

    /// <summary>
    ///     Resets all preset-influenced properties to sensible defaults. Called when the user
    ///     selects Preset = None so they can back out of any theme without restarting the app.
    /// </summary>
    private void ApplyPresetNone()
    {
        Direction = ScrollDirection.Left;
        Effect = TextEffect.FlyIn;
        ScrollSpeed = 4;
        CharStagger = 60;
        MultiColor = true;
        TextColor = SKColors.White;
        BackgroundColor = SKColors.Black;
        Border = BorderStyle.Marquee;
        RainbowBorder = true;
        BorderColor = new SKColor(255, 20, 147);
        Glow = true;
        Sparkle = true;
        Twinkle = false;
        Confetti = true;
        Blink = false;
        Fade = true;
        UseBdfFont = true;
        _decorations = "";
        _decorationCount = 8;
        EffectSpeed = 0.5;

        _rebuildLanes = true;
        lock (_lock)
        {
            if (IsRunning && _backBuffer != null) InitDecorations();
        }
    }

    /// <summary>Applies a ready-made theme (combination of options). Reconfigures live if running.</summary>
    private void ApplyPreset(SignagePreset p)
    {
        switch (p)
        {
            case SignagePreset.CoffeeShop:
                Direction = ScrollDirection.Left;
                ScrollSpeed = 4;
                MultiColor = false;
                TextColor = new SKColor(255, 210, 150);
                BackgroundColor = SKColors.Black;
                Border = BorderStyle.Marquee;
                RainbowBorder = true;
                Glow = true;
                Sparkle = false;
                Twinkle = false;
                Confetti = false;
                Blink = false;
                UseBdfFont = true;
                _decorations = "* . o";
                _decorationCount = 6;
                break;
            case SignagePreset.Party:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Bounce;
                CharStagger = 50;
                MultiColor = true;
                Border = BorderStyle.Rainbow;
                Glow = true;
                Sparkle = true;
                Twinkle = true;
                Confetti = true;
                Blink = false;
                UseBdfFont = false;
                _decorations = "* + o";
                _decorationCount = 12;
                break;
            case SignagePreset.News:
                Direction = ScrollDirection.Left;
                ScrollSpeed = 4;
                MultiColor = false;
                TextColor = SKColors.White;
                Border = BorderStyle.Dashed;
                RainbowBorder = false;
                BorderColor = new SKColor(200, 0, 0);
                Glow = false;
                Sparkle = false;
                Twinkle = false;
                Confetti = false;
                Blink = false;
                UseBdfFont = true;
                _decorations = "";
                break;
            case SignagePreset.Retro:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Slot;
                CharStagger = 40;
                MultiColor = false;
                TextColor = new SKColor(0, 255, 90);
                Border = BorderStyle.Neon;
                RainbowBorder = false;
                BorderColor = new SKColor(0, 255, 120);
                Glow = true;
                Sparkle = false;
                Twinkle = false;
                Confetti = false;
                UseBdfFont = true;
                _decorations = "";
                break;
            case SignagePreset.Minimal:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Typewriter;
                CharStagger = 80;
                MultiColor = false;
                TextColor = SKColors.White;
                Border = BorderStyle.None;
                Glow = false;
                Sparkle = false;
                Twinkle = false;
                Confetti = false;
                _decorations = "";
                break;
            // ─── Hollywood Presets ───────────────────────────────────────────
            case SignagePreset.Matrix:
                Direction = ScrollDirection.None;
                Effect = TextEffect.MatrixRain;
                CharStagger = 30;
                MultiColor = false;
                TextColor = new SKColor(0, 255, 70);
                BackgroundColor = SKColors.Black;
                Border = BorderStyle.None;
                Glow = true;
                Sparkle = false;
                Twinkle = true;
                Confetti = false;
                UseBdfFont = true;
                _decorations = "";
                break;
            case SignagePreset.Cyberpunk:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Glitch;
                CharStagger = 25;
                MultiColor = true;
                BackgroundColor = new SKColor(10, 0, 20);
                Border = BorderStyle.Neon;
                RainbowBorder = true;
                Glow = true;
                Sparkle = true;
                Twinkle = false;
                Confetti = false;
                UseBdfFont = false;
                _decorations = "";
                break;
            case SignagePreset.SciFi:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Hologram;
                CharStagger = 40;
                MultiColor = false;
                TextColor = new SKColor(0, 180, 255);
                BackgroundColor = SKColors.Black;
                Border = BorderStyle.Pulse;
                RainbowBorder = false;
                BorderColor = new SKColor(0, 150, 255);
                Glow = true;
                Sparkle = true;
                Twinkle = false;
                Confetti = false;
                UseBdfFont = false;
                _decorations = "";
                break;
            case SignagePreset.Action:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Explode;
                CharStagger = 20;
                MultiColor = true;
                BackgroundColor = new SKColor(20, 0, 0);
                Border = BorderStyle.Pulse;
                RainbowBorder = false;
                BorderColor = new SKColor(255, 50, 0);
                Glow = true;
                Sparkle = true;
                Twinkle = false;
                Confetti = true;
                UseBdfFont = false;
                _decorations = "";
                break;
            case SignagePreset.Horror:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Lightning;
                CharStagger = 35;
                MultiColor = false;
                TextColor = new SKColor(200, 200, 255);
                BackgroundColor = new SKColor(0, 0, 10);
                Border = BorderStyle.Pulse;
                RainbowBorder = false;
                BorderColor = new SKColor(150, 150, 200);
                Glow = true;
                Sparkle = false;
                Twinkle = true;
                Confetti = false;
                UseBdfFont = false;
                _decorations = "";
                break;
            // ─── Blockbuster Studio Intros ───────────────────────────────────
            case SignagePreset.Blockbuster:
                Direction = ScrollDirection.None;
                Effect = TextEffect.MarvelFlash;
                CharStagger = 45;
                MultiColor = false;
                TextColor = new SKColor(255, 230, 90);
                BackgroundColor = SKColors.Black;
                Border = BorderStyle.Pulse;
                RainbowBorder = false;
                BorderColor = new SKColor(255, 80, 0);
                Glow = true;
                Sparkle = true;
                Twinkle = false;
                Confetti = false;
                UseBdfFont = false;
                _decorations = "";
                break;
            case SignagePreset.Galaxy:
                Direction = ScrollDirection.None;
                Effect = TextEffect.StarWars;
                CharStagger = 15;
                MultiColor = false;
                TextColor = new SKColor(255, 220, 60);
                BackgroundColor = SKColors.Black;
                Border = BorderStyle.None;
                Glow = true;
                Sparkle = true;
                Twinkle = false;
                Confetti = false;
                UseBdfFont = false;
                _decorations = ". * .";
                _decorationCount = 20;
                break;
            case SignagePreset.Animation:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Pixar;
                CharStagger = 70;
                MultiColor = true;
                BackgroundColor = new SKColor(20, 30, 80);
                Border = BorderStyle.None;
                Glow = true;
                Sparkle = true;
                Twinkle = true;
                Confetti = true;
                UseBdfFont = false;
                _decorations = "* . o";
                _decorationCount = 10;
                break;
            case SignagePreset.Cartoon:
                Direction = ScrollDirection.None;
                Effect = TextEffect.Minion;
                CharStagger = 55;
                MultiColor = false;
                TextColor = new SKColor(255, 230, 60);
                BackgroundColor = new SKColor(60, 90, 160);
                Border = BorderStyle.Marquee;
                RainbowBorder = true;
                Glow = true;
                Sparkle = false;
                Twinkle = true;
                Confetti = true;
                UseBdfFont = false;
                _decorations = "";
                break;
            case SignagePreset.Trailer:
                Direction = ScrollDirection.None;
                Effect = TextEffect.JumpCut;
                CharStagger = 10;
                MultiColor = true;
                BackgroundColor = SKColors.Black;
                Border = BorderStyle.Chase;
                RainbowBorder = false;
                BorderColor = new SKColor(255, 40, 40);
                Glow = true;
                Sparkle = true;
                Twinkle = false;
                Confetti = false;
                UseBdfFont = false;
                _decorations = "";
                break;
        }

        _rebuildLanes = true;
        lock (_lock)
        {
            if (IsRunning && _backBuffer != null) InitDecorations();
        }
    }

    private void InitDecorations()
    {
        _deco.Clear();
        foreach (var m in _decoMasks.Values) m.Dispose();
        _decoMasks.Clear();

        var symbols = (Decorations ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (symbols.Length == 0 || DecorationCount <= 0 || _backBuffer == null) return;

        var size = Math.Max(6f, _canvas.Height * 0.35f);
        foreach (var sym in symbols.Distinct())
        {
            try
            {
                var m = RenderSymbolMask(sym, size);
                if (m is { Width: > 0, Height: > 0 }) _decoMasks[sym] = m;
            }
            catch
            {
                // skip unrenderable symbol
            }
        }

        if (_decoMasks.Count == 0) return;

        var keys = _decoMasks.Keys.ToArray();
        var w = _backBuffer.Width;
        var h = _backBuffer.Height;
        for (var i = 0; i < DecorationCount; i++)
            _deco.Add(new DecoSprite
            {
                Symbol = keys[_random.Next(keys.Length)],
                X = _random.Next(w),
                Y = _random.Next(h),
                Vx = (_random.NextSingle() - 0.5f) * 1.2f * _scale,
                Vy = (_random.NextSingle() - 0.5f) * 1.2f * _scale
            });
    }

    private void DrawDecorations(SKCanvas canvas, int w, int h)
    {
        if (_deco.Count == 0) return;

        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(140), IsAntialias = true };
        for (var i = 0; i < _deco.Count; i++)
        {
            var d = _deco[i];
            d.X += d.Vx;
            d.Y += d.Vy;
            if (d.X < -40) d.X = w + 40;
            else if (d.X > w + 40) d.X = -40;
            if (d.Y < -40) d.Y = h + 40;
            else if (d.Y > h + 40) d.Y = -40;
            _deco[i] = d;

            if (_decoMasks.TryGetValue(d.Symbol, out var mask))
                canvas.DrawBitmap(mask, d.X - mask.Width / 2f, d.Y - mask.Height / 2f, paint);
        }
    }

    private SKBitmap RenderSymbolMask(string symbol, float size)
    {
        var tf = SKFontManager.Default.MatchCharacter(char.ConvertToUtf32(symbol, 0)) ?? SKTypeface.Default;
        using var font = new SKFont(tf, size);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        font.MeasureText(symbol, out var bounds);

        var bw = Math.Max(1, (int)Math.Ceiling(bounds.Width) + 2);
        var bh = Math.Max(1, (int)Math.Ceiling(bounds.Height) + 2);
        var bmp = new SKBitmap(bw, bh);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        c.DrawText(symbol, -bounds.Left + 1, -bounds.Top + 1, SKTextAlign.Left, font, paint);
        return bmp;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Animated border
    // ───────────────────────────────────────────────────────────────────────

    private void DrawBorder(SKCanvas canvas, int w, int h)
    {
        if (Border == BorderStyle.None) return;

        var thickness = Math.Max(1, _canvas.ScaleSize(3));
        var baseHue = _frame * 3f % 360f;

        switch (Border)
        {
            case BorderStyle.Pulse:
            {
                var pulse = 0.45f + 0.55f * (float)Math.Sin(_frame * 0.12);
                var col = (RainbowBorder ? SignageFx.Hue(baseHue) : BorderColor).WithAlpha((byte)(255 * pulse));
                using var p = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = thickness,
                    Color = col,
                    IsAntialias = false
                };
                canvas.DrawRect(thickness / 2f, thickness / 2f, w - thickness, h - thickness, p);
                break;
            }
            case BorderStyle.Chase:
            {
                DrawChase(canvas, w, h, thickness, baseHue);
                break;
            }
            case BorderStyle.Rainbow:
            {
                DrawRainbowBorder(canvas, w, h, thickness);
                break;
            }
            case BorderStyle.Dashed:
            {
                DrawDashedBorder(canvas, w, h, thickness, baseHue);
                break;
            }
            case BorderStyle.Neon:
            {
                DrawNeonBorder(canvas, w, h, thickness, baseHue);
                break;
            }
            case BorderStyle.Snake:
            {
                DrawSnakeBorder(canvas, w, h, thickness, baseHue);
                break;
            }
            default: // Marquee
            {
                DrawMarquee(canvas, w, h, thickness, baseHue);
                break;
            }
        }
    }

    /// <summary>Rotating rainbow sweep-gradient outline.</summary>
    private void DrawRainbowBorder(SKCanvas canvas, int w, int h, int thickness)
    {
        using var baseShader = SKShader.CreateSweepGradient(new SKPoint(w / 2f, h / 2f), SignageFx.RainbowPalette, null);
        using var shader = baseShader.WithLocalMatrix(SKMatrix.CreateRotationDegrees(_frame * 4f % 360f, w / 2f, h / 2f));
        using var p = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            Shader = shader,
            IsAntialias = false
        };
        canvas.DrawRect(thickness / 2f, thickness / 2f, w - thickness, h - thickness, p);
    }

    /// <summary>Marching-ants dashed outline.</summary>
    private void DrawDashedBorder(SKCanvas canvas, int w, int h, int thickness, float baseHue)
    {
        var dash = thickness * 2f;
        using var effect = SKPathEffect.CreateDash(new[] { dash, dash }, _frame % (dash * 2));
        using var p = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            Color = RainbowBorder ? SignageFx.Hue(baseHue) : BorderColor,
            PathEffect = effect,
            IsAntialias = false
        };
        canvas.DrawRect(thickness / 2f, thickness / 2f, w - thickness, h - thickness, p);
    }

    /// <summary>Glowing, pulsing neon outline.</summary>
    private void DrawNeonBorder(SKCanvas canvas, int w, int h, int thickness, float baseHue)
    {
        var pulse = 0.5f + 0.5f * (float)Math.Sin(_frame * 0.1);
        var col = RainbowBorder ? SignageFx.Hue(baseHue) : BorderColor;
        var rect = new SKRect(thickness, thickness, w - thickness, h - thickness);
        var r = Math.Max(1, _canvas.ScaleSize(4));

        using var glow = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness * 2,
            Color = col.WithAlpha((byte)(140 * pulse)),
            ImageFilter = SKImageFilter.CreateBlur(r, r),
            IsAntialias = true
        };
        canvas.DrawRect(rect, glow);

        using var core = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, thickness / 2f),
            Color = SKColors.White.WithAlpha((byte)(180 + 75 * pulse)),
            IsAntialias = true
        };
        canvas.DrawRect(rect, core);
    }

    /// <summary>A bright "snake" segment of fixed length sliding around the perimeter.</summary>
    private void DrawSnakeBorder(SKCanvas canvas, int w, int h, int thickness, float baseHue)
    {
        var dot = Math.Max(1, thickness);
        var spacing = Math.Max(dot + 1, _canvas.ScaleSize(5));
        var perimeter = BuildPerimeter(w, h, spacing, dot);
        if (perimeter.Count == 0) return;

        var len = Math.Max(4, perimeter.Count / 3);
        var head = _frame % perimeter.Count;

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        for (var t = 0; t < len; t++)
        {
            var idx = (head - t + perimeter.Count) % perimeter.Count;
            var fade = 1f - t / (float)len;
            paint.Color = (RainbowBorder ? SignageFx.Hue(baseHue + t * 6f) : BorderColor).WithAlpha((byte)(255 * fade));
            var pt = perimeter[idx];
            canvas.DrawRect(pt.X, pt.Y, dot, dot, paint);
        }
    }

    /// <summary>Chasing marquee dots around the perimeter (classic light-bulb sign).</summary>
    private void DrawMarquee(SKCanvas canvas, int w, int h, int thickness, float baseHue)
    {
        var dot = Math.Max(1, thickness);
        var spacing = Math.Max(dot + 1, _canvas.ScaleSize(8));
        var perimeter = BuildPerimeter(w, h, spacing, dot);

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        for (var i = 0; i < perimeter.Count; i++)
        {
            // Light up every other dot, marching with the frame.
            var on = ((i + _frame / 3) % 2) == 0;
            if (!on) continue;

            paint.Color = RainbowBorder ? SignageFx.Hue(baseHue + i * 12f) : BorderColor;
            var pt = perimeter[i];
            canvas.DrawRect(pt.X, pt.Y, dot, dot, paint);
        }
    }

    /// <summary>A bright comet segment running around the border.</summary>
    private void DrawChase(SKCanvas canvas, int w, int h, int thickness, float baseHue)
    {
        var dot = Math.Max(1, thickness);
        var spacing = Math.Max(dot + 1, _canvas.ScaleSize(6));
        var perimeter = BuildPerimeter(w, h, spacing, dot);
        if (perimeter.Count == 0) return;

        const int tail = 8;
        var head = _frame % perimeter.Count;

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        for (var t = 0; t < tail; t++)
        {
            var idx = (head - t + perimeter.Count) % perimeter.Count;
            var fade = 1f - t / (float)tail;
            var col = RainbowBorder ? SignageFx.Hue(baseHue) : BorderColor;
            paint.Color = col.WithAlpha((byte)(255 * fade));
            var pt = perimeter[idx];
            canvas.DrawRect(pt.X, pt.Y, dot, dot, paint);
        }
    }

    private static List<SKPointI> BuildPerimeter(int w, int h, int spacing, int dot)
    {
        var pts = new List<SKPointI>();
        for (var x = 0; x < w - dot; x += spacing) pts.Add(new SKPointI(x, 0)); // top
        for (var y = 0; y < h - dot; y += spacing) pts.Add(new SKPointI(w - dot, y)); // right
        for (var x = w - dot; x > 0; x -= spacing) pts.Add(new SKPointI(x, h - dot)); // bottom
        for (var y = h - dot; y > 0; y -= spacing) pts.Add(new SKPointI(0, y)); // left
        return pts;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Live data source
    // ───────────────────────────────────────────────────────────────────────

    private void StartDataSource()
    {
        if (string.IsNullOrWhiteSpace(DataSourceUrl)) return;

        _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ = FetchDataAsync();

        _dataTimer?.Dispose();
        _dataTimer = new Timer(Math.Max(30, DataRefreshSeconds) * 1000.0) { AutoReset = true };
        _dataTimer.Elapsed += (_, _) => _ = FetchDataAsync();
        _dataTimer.Start();
    }

    private async Task FetchDataAsync()
    {
        if (_fetching || string.IsNullOrWhiteSpace(DataSourceUrl)) return;
        _fetching = true;
        try
        {
            _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var content = await _http.GetStringAsync(DataSourceUrl);

            var items = DataFormat == DataSourceFormat.RssTitles
                ? ParseRssTitles(content)
                : ParseLines(content);

            if (items.Count > 0)
            {
                lock (_lock)
                {
                    _messages = items.ToArray();
                }

                _rebuildLanes = true;
                Console.WriteLine($"[ADVERT] Loaded {items.Count} message(s) from data source");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ADVERT] Data source fetch failed: {ex.Message}");
        }
        finally
        {
            _fetching = false;
        }
    }

    private static List<string> ParseLines(string content)
    {
        return content
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(50)
            .ToList();
    }

    private static List<string> ParseRssTitles(string content)
    {
        // Extract <title> values (RSS/Atom). Skip the first (usually the feed/channel title).
        var titles = new List<string>();
        foreach (Match m in Regex.Matches(content, "<title[^>]*>(.*?)</title>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var raw = m.Groups[1].Value;
            raw = Regex.Replace(raw, "<!\\[CDATA\\[(.*?)\\]\\]>", "$1", RegexOptions.Singleline);
            raw = WebUtilityHtmlDecode(raw).Trim();
            if (raw.Length > 0) titles.Add(raw);
        }

        if (titles.Count > 1) titles.RemoveAt(0); // drop the channel/feed title
        return titles.Take(30).ToList();
    }

    private static string WebUtilityHtmlDecode(string s)
    {
        return s
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────

    private void SetMessagesInternal(string value)
    {
        var parts = (value ?? "")
            .Split(MessageSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        lock (_lock)
        {
            _messages = parts.Length > 0 ? parts : DefaultMessages;
        }
    }

    /// <summary>A falling confetti piece.</summary>
    private struct ConfettiParticle
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Rot;
        public float Vrot;
        public float Life;
        public float Max;
        public float Size;
        public SKColor Color;
    }

    /// <summary>A drifting background decoration sprite.</summary>
    private struct DecoSprite
    {
        public string Symbol;
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
    }
}
