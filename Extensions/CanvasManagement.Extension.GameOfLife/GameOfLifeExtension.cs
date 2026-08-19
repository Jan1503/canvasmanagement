using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Canvas.Extension.GameOfLife;

[ExtensionInfo("Conway's Game of Life",
    "Classic cellular automaton simulation with evolving patterns",
    "Games",
    IconResourceName = "gameoflife.svg")]
public class GameOfLifeExtension : IDisposable
{
    // OPTIMIZATION: Pre-generated random color palette
    private static readonly SKColor[] _colorPalette;
    private readonly SKBitmap _backBuffer;
    private readonly Board _board;
    private readonly ICanvas _canvas;
    private readonly Random _random = new();

    private readonly SKBitmap _targetBitmap;
    private bool _disposed;
    private Timer? _timer;

    static GameOfLifeExtension()
    {
        // OPTIMIZATION: Pre-generate random colors once
        var random = new Random();
        _colorPalette = new SKColor[100];
        var colorFields = typeof(SKColors).GetFields();

        for (var i = 0; i < _colorPalette.Length; i++)
        {
            var field = colorFields[random.Next(colorFields.Length)];
            _colorPalette[i] = (SKColor)field.GetValue(null)!;
        }
    }

    internal GameOfLifeExtension(ICanvas canvas)
    {
        _canvas = canvas;
        _targetBitmap = new SKBitmap(canvas.Width, canvas.Height);
        _backBuffer = new SKBitmap(canvas.Width, canvas.Height);
        _board = new Board(canvas.Width, canvas.Height);
    }

    [ExtensionParameter("Initial Density", "Initial population density (0.0 = empty, 1.0 = full)",
        DefaultValue = 0.2, MinValue = 0.0, MaxValue = 1.0)]
    public double InitialDensity { get; set; } = 0.2;

    [ExtensionParameter("Update Speed", "Time between generations in milliseconds",
        DefaultValue = 100, MinValue = 10, MaxValue = 1000, Unit = "ms")]
    public int UpdateSpeed { get; set; } = 100;

    [ExtensionParameter("Cell Color", "Color of alive cells", DefaultValue = "#32CD32")]
    public SKColor CellColor { get; set; } = SKColors.LimeGreen;

    [ExtensionParameter("Background Color", "Background color for the game",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Auto Restart", "Automatically restart when pattern becomes stuck", DefaultValue = true)]
    public bool AutoRestart { get; set; } = true;

    [ExtensionParameter("Random cell color on restart", "Use random cell color on restart when pattern becomes stuck",
        DefaultValue = true)]
    public bool AutoRestartRandomCellColor { get; set; } = true;

    public string Name => "Conway's Game of Life";

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _targetBitmap?.Dispose();
        _backBuffer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        if (IsRunning) return;

        _board.Start(InitialDensity);

        _timer?.Dispose();
        _timer = new Timer();
        _timer.Elapsed += TimerOnElapsed;
        _timer.Interval = UpdateSpeed;
        _timer.AutoReset = true;
        _timer.Start();

        IsRunning = true;
        Console.WriteLine($"Game of Life started - Density: {InitialDensity:P0}, Speed: {UpdateSpeed}ms");
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        IsRunning = false;

        Console.WriteLine("Game of Life stopped");
    }

    public void Clear()
    {
        _board.Clear();
        _canvas.Clear(BackgroundColor);
    }

    // OPTIMIZATION: Use pre-generated color palette
    private SKColor GetRandomColor()
    {
        return _colorPalette[_random.Next(_colorPalette.Length)];
    }

    private void TimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!IsRunning) return;

        _board.Advance();

        // Render to target bitmap (this is the game state)
        Render.DrawBoard(_board, _targetBitmap, CellColor, BackgroundColor);

        // Copy target bitmap to back buffer (already has background from Render.DrawBoard)
        // The Render.DrawBoard method already handles background, so we can just copy
        _targetBitmap.CopyTo(_backBuffer);// Use SubmitCompletedFrame for atomic, flicker-free rendering
        _canvas.SubmitCompletedFrame(_backBuffer);

        // OPTIMIZATION: Check for stuck patterns less frequently
        if (AutoRestart && _board.Generations.Count > 20 && _board.CurrentGeneration.No % 20 == 0)
            if (_board.Generations.TakeLast(20).DistinctBy(a => new { a.CellsAlive, a.CellsDead }).Count() <= 6)
            {
                Console.WriteLine("Pattern stuck - Restarting with new configuration");
                Stop();
                Clear();

                if (AutoRestartRandomCellColor) CellColor = GetRandomColor();

                Start();
            }
    }

    ~GameOfLifeExtension()
    {
        Dispose();
    }
}