using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Maze chase on a generated, symmetric maze. Runs as a self-playing demo until someone presses a key or a
///     gamepad button in Studio, then it is a real game: arrows / WASD / D-pad steer (inputs are buffered, so
///     pressing early into a corner works), Start or Enter pauses, Back hands the wall back to the demo.
/// </summary>
[ExtensionInfo("Pac-Man",
    "Maze chase with smart ghosts and an expert autopilot — play with arrows, WASD or a gamepad in Studio",
    "Games",
    IconResourceName = "pacman.svg")]
public sealed class PacManExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly ConcurrentQueue<Direction> _steer = new();
    private readonly Stopwatch _watch = new();
    private readonly Autopilot _pilot = new();
    private readonly GameRenderer _renderer = new();
    private ExtensionLoop? _loop;
    private SKBitmap? _bitmap;
    private Frame? _frame;
    private Game? _game;
    private PacSound? _sound;
    private double _last;
    private volatile bool _human;
    private volatile int _pause; // 0 none, 1 toggle requested
    private volatile bool _start, _back;
    private (int W, int H) _builtFor;
    private float _overTime;

    public PacManExtension(ICanvas canvas) => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    [ExtensionParameter("Game Speed", "Lower is faster (35 is the normal pace)",
        DefaultValue = 35, MinValue = 16, MaxValue = 200, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 35;

    [ExtensionParameter("Difficulty", "Ghost speed, how long they stay blue and how fast they leave the pen",
        DefaultValue = 3, MinValue = 1, MaxValue = 10, Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Ghosts", "How many ghosts hunt Pac-Man", DefaultValue = 4, MinValue = 1, MaxValue = 8,
        Order = 3)]
    public int GhostCount { get; set; } = 4;

    [ExtensionParameter("Caution", "Autopilot: how wide a berth it keeps from ghosts (1 brave … 12 careful)",
        DefaultValue = 5, MinValue = 1, MaxValue = 12, Order = 4)]
    public int Caution { get; set; } = 5;

    [ExtensionParameter("Auto Pilot", "Plays a demo until you press a key or a gamepad button in Studio",
        DefaultValue = true, Order = 5)]
    public bool AutoPilot { get; set; } = true;

    [ExtensionParameter("Sound", "Siren, chomps and jingles on the audio board", DefaultValue = true, Order = 6)]
    public bool Sound { get; set; } = true;

    [ExtensionParameter("Volume", "How loud this game is on the audio board", DefaultValue = 70, MinValue = 0,
        MaxValue = 100, Order = 7)]
    public int Volume { get; set; } = 70;

    [ExtensionParameter("Music", "Background music (the siren gets quieter under it)", DefaultValue = true,
        Order = 7)]
    public bool Music { get; set; } = true;

    [ExtensionParameter("Music Volume", "Music level relative to the effects", DefaultValue = 40, MinValue = 0,
        MaxValue = 100, Order = 7)]
    public int MusicVolume { get; set; } = 40;

    [ExtensionParameter("Background Color", "Colour behind the maze", DefaultValue = "#000000", Order = 8)]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Show Debug Info", "Mark the autopilot's goal and what it is doing", DefaultValue = false,
        Order = 9)]
    public bool ShowDebugInfo { get; set; }

    [ExtensionParameter("Score", "Current score", ReadOnly = true, Order = 10)]
    public int Score => _game?.Score ?? 0;

    [ExtensionParameter("Lives", "Lives left", ReadOnly = true, Order = 11)]
    public int Lives => _game?.Lives ?? 0;

    [ExtensionParameter("Level", "Current level", ReadOnly = true, Order = 12)]
    public int Level => _game?.Level ?? 0;

    public string Name => "Pac-Man";
    public bool IsRunning { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            IsRunning = true;
            _human = false;
            _steer.Clear();
            _game = null;
            _sound = new PacSound(_canvas);
            _watch.Restart();
            _last = 0;
            _loop = new ExtensionLoop(60, Tick, _canvas.Stats);
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
            _sound?.Silence();
            _sound = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _game = null;
            try { _canvas.Clear(SKColors.Black); } catch { /* canvas gone */ }
        }
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ controls

    private void Steer(Direction d)
    {
        _human = true;
        _steer.Enqueue(d);
    }

    [ExtensionMethod("Go Up", "Steer up (takes over from the demo)", Category = "Controls",
        KeyboardShortcut = "Up|W|PadUp", Order = 1)]
    public void GoUp() => Steer(Direction.Up);

    [ExtensionMethod("Go Down", "Steer down (takes over from the demo)", Category = "Controls",
        KeyboardShortcut = "Down|S|PadDown", Order = 2)]
    public void GoDown() => Steer(Direction.Down);

    [ExtensionMethod("Go Left", "Steer left (takes over from the demo)", Category = "Controls",
        KeyboardShortcut = "Left|A|PadLeft", Order = 3)]
    public void GoLeft() => Steer(Direction.Left);

    [ExtensionMethod("Go Right", "Steer right (takes over from the demo)", Category = "Controls",
        KeyboardShortcut = "Right|D|PadRight", Order = 4)]
    public void GoRight() => Steer(Direction.Right);

    [ExtensionMethod("Start", "Start a new game / continue after game over", Category = "Controls",
        KeyboardShortcut = "Space|PadA|PadX", Order = 5)]
    public void StartGame()
    {
        _human = true;
        _start = true;
    }

    [ExtensionMethod("Pause", "Pause or resume", Category = "Controls", KeyboardShortcut = "Enter|P|PadStart",
        Order = 6)]
    public void Pause() => _pause = 1;

    [ExtensionMethod("Demo", "Hand the wall back to the autopilot", Category = "Controls",
        KeyboardShortcut = "Escape|PadBack", Order = 7)]
    public void Demo() => _back = true;

    // ------------------------------------------------------------------ loop

    private Task Tick(CancellationToken ct)
    {
        var w = _canvas.Width;
        var h = _canvas.Height;
        if (w < 48 || h < 40) return Task.CompletedTask;

        var now = _watch.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _last, 0.001, 0.05);
        _last = now;

        var (cols, rows, _, _) = GameRenderer.Fit(w, h);
        if (_game == null || _builtFor != (w, h))
        {
            _builtFor = (w, h);
            _game = NewGame(cols, rows);
        }
        var game = _game;
        game.Difficulty = Difficulty;
        game.SpeedFactor = Math.Clamp(35f / Math.Max(1, GameSpeed), 0.3f, 1.8f);
        game.SetGhostCount(GhostCount);
        _pilot.Caution = Math.Clamp(Caution, 1, 12);

        var demo = AutoPilot && !_human;
        Input(game, ref demo);
        game = _game!;
        game.Pilot = demo ? _pilot.Decide : null;
        game.Panic = demo ? _pilot.Panic : null;

        game.Update(dt);
        var events = game.TakeEvents();

        if (game.Phase == Phase.GameOver)
        {
            _overTime += dt;
            // The demo starts over by itself; a player gets a few seconds to press Start, then the demo returns.
            if (demo && _overTime > 4f || !demo && _overTime > 20f)
            {
                _human = false;
                _game = NewGame(cols, rows);
                _overTime = 0;
                game = _game;
                events |= game.TakeEvents();
            }
        }
        else _overTime = 0;

        try
        {
            _sound?.Update(game, events, Sound, Volume / 100f, dt, demo, Music, MusicVolume / 100f);
        }
        catch (Exception ex)
        {
            // Sound is a bonus; never let it stop the game.
            Console.WriteLine("[PacMan] sound: " + ex.Message);
            _sound = null;
        }

        _frame = _frame?.W == w && _frame.H == h ? _frame : new Frame(w, h);
        _renderer.Demo = demo;
        _renderer.Debug = ShowDebugInfo;
        _renderer.Pilot = _pilot;
        _renderer.Background = (uint)BackgroundColor | 0xFF000000u;
        _renderer.Render(_frame, game, dt);

        if (_bitmap == null || _bitmap.Width != w || _bitmap.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        }
        var px = _frame.Px;
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try
        {
            unsafe
            {
                Buffer.MemoryCopy((void*)handle.AddrOfPinnedObject(), (void*)_bitmap.GetPixels(), (long)w * h * 4,
                    (long)w * h * 4);
            }
        }
        finally
        {
            handle.Free();
        }
        _canvas.SubmitCompletedFrame(_bitmap);
        return Task.CompletedTask;
    }

    private Game NewGame(int cols, int rows) => new(cols, rows, GhostCount, Difficulty);

    private void Input(Game game, ref bool demo)
    {
        if (_back)
        {
            _back = false;
            _human = false;
            _steer.Clear();
            demo = AutoPilot;
            if (demo) _game = game = ReplaceGame(game);
            return;
        }

        var steered = !_steer.IsEmpty;
        var start = _start;
        _start = false;

        // First touch in the demo: a fresh game for the player.
        if ((steered || start) && !demo && game.Phase != Phase.Paused && _wasDemo)
        {
            _wasDemo = false;
            _game = game = ReplaceGame(game);
            _steer.Clear();
            return;
        }
        _wasDemo = demo;

        if (start && game.Phase == Phase.GameOver)
        {
            _game = ReplaceGame(game);
            return;
        }

        if (_pause == 1)
        {
            _pause = 0;
            game.Pause(game.Phase != Phase.Paused);
        }

        while (_steer.TryDequeue(out var d))
        {
            if (game.Phase == Phase.Paused) game.Pause(false);
            game.Pac.Want = d;
        }
    }

    private bool _wasDemo = true;

    private Game ReplaceGame(Game old)
    {
        var (cols, rows) = (old.Maze.W, old.Maze.H);
        _overTime = 0;
        return NewGame(cols, rows);
    }
}
