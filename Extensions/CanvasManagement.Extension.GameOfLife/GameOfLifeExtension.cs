using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Canvas.Extension.GameOfLife;

[ExtensionInfo("Conway's Game of Life",
    "Classic cellular automaton simulation with evolving patterns",
    "Games",
    IconResourceName = "gameoflife.svg")]
public class GameOfLifeExtension : FrameExtension
{
    // Pre-generated random colour palette for "random cell color on restart".
    private static readonly SKColor[] _colorPalette;
    private readonly Random _random = new();
    private Board? _board;

    static GameOfLifeExtension()
    {
        var random = new Random();
        _colorPalette = new SKColor[100];
        var colorFields = typeof(SKColors).GetFields();

        for (var i = 0; i < _colorPalette.Length; i++)
        {
            var field = colorFields[random.Next(colorFields.Length)];
            _colorPalette[i] = (SKColor)field.GetValue(null)!;
        }
    }

    internal GameOfLifeExtension(ICanvas canvas) : base(canvas)
    {
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

    public override string Name => "Conway's Game of Life";

    protected override double FrameIntervalMs => UpdateSpeed;

    protected override bool ClearOnStop => false;

    protected override void OnStart() =>
        Console.WriteLine($"Game of Life started - Density: {InitialDensity:P0}, Speed: {UpdateSpeed}ms");

    protected override void OnStop() => Console.WriteLine("Game of Life stopped");

    /// <summary>One cell per pixel: a new board for every canvas size.</summary>
    protected override void OnResize(int width, int height)
    {
        _board = new Board(width, height);
        _board.Start(InitialDensity);
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        if (_board == null || BackBuffer == null) return;

        _board.Advance();
        BoardRenderer.DrawBoard(_board, BackBuffer, CellColor, BackgroundColor);

        // Every 20 generations: a pattern that only repeats a few states is stuck; start a new one.
        if (AutoRestart && _board.Generations.Count > 20 && _board.CurrentGeneration.No % 20 == 0 &&
            _board.Generations.TakeLast(20).DistinctBy(a => new { a.CellsAlive, a.CellsDead }).Count() <= 6)
        {
            Console.WriteLine("Pattern stuck - Restarting with new configuration");
            if (AutoRestartRandomCellColor) CellColor = _colorPalette[_random.Next(_colorPalette.Length)];
            _board.Clear();
            _board.Start(InitialDensity);
        }
    }

    /// <summary>Empties the board.</summary>
    public void Clear()
    {
        lock (Sync)
        {
            _board?.Clear();
            Canvas.Clear(BackgroundColor);
        }
    }
}
