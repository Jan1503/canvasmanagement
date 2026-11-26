using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.TetrisClock;

/// <summary>
///     A clock whose digits are built from falling Tetris pieces. Every new digit is a fresh random set of
///     pieces; each one drops from above the top edge to its place (so taller canvases mean longer falls),
///     turning and sliding on the way like in the game. Size and position follow the canvas.
/// </summary>
[ExtensionInfo("Tetris Clock",
    "A clock built from falling Tetris pieces - every digit a new puzzle",
    "Clocks",
    IconResourceName = "tetris.svg")]
public class TetrisClockExtension : FrameExtension
{
    // classic piece colours: I, O, T, S, Z, J, L, small fillers
    private static readonly SKColor[] ClassicColors =
    [
        new(0, 240, 240), new(240, 220, 0), new(170, 60, 240), new(40, 220, 60),
        new(240, 40, 40), new(40, 90, 255), new(255, 150, 0), new(220, 220, 220)
    ];

    private const int Gap = 2;           // cells between the two digits of a pair
    private const int ColonWidth = 2;    // colon blocks are 2x2 cells
    private const int ColonPad = 1;      // cells left and right of a colon
    private const float SecondsBudget = 0.82f; // the seconds digit must be complete before the next second

    private readonly DigitSlot[] _slots = [new(), new(), new(), new(), new(), new()];
    private readonly Random _random = new();
    private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill, IsAntialias = false };
    private string _lastTime = "";
    private Geometry? _geometry;
    private (int W, int H, int Digits, int Block) _geometryKey;

    [ExtensionParameter("Show Seconds", "Show seconds (off: hours and minutes only, drawn larger)",
        DefaultValue = true)]
    public bool ShowSeconds { get; set; } = true;

    [ExtensionParameter("Block Size", "Pixels per block (0 = as large as the canvas allows)",
        MinValue = 0, MaxValue = 16, DefaultValue = 0)]
    public int BlockSize { get; set; }

    [ExtensionParameter("Fall Speed", "How fast the pieces fall (the seconds always finish in time)",
        MinValue = 0.25, MaxValue = 4.0, DefaultValue = 1.0)]
    public float FallSpeed { get; set; } = 1f;

    [ExtensionParameter("Spin Pieces", "Pieces turn while they fall", DefaultValue = true)]
    public bool SpinPieces { get; set; } = true;

    [ExtensionParameter("Slide Pieces", "Pieces slide sideways into their column while they fall",
        DefaultValue = true)]
    public bool SlidePieces { get; set; } = true;

    [ExtensionParameter("Random Colors", "Random colours instead of the classic piece colours",
        DefaultValue = false)]
    public bool RandomColors { get; set; }

    [ExtensionParameter("Beveled Blocks", "Light and shadow edges on each block (from 3 px block size)",
        DefaultValue = true)]
    public bool BeveledBlocks { get; set; } = true;

    [ExtensionParameter("Show Colon", "Show the colons between hours, minutes and seconds", DefaultValue = true)]
    public bool ShowColon { get; set; } = true;

    [ExtensionParameter("Colon Color", "Colour of the colons", DefaultValue = "#00FFFF")]
    public SKColor ColonColor { get; set; } = SKColors.Cyan;

    [ExtensionParameter("Background Color", "Background colour", DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    public override string Name => "Tetris Clock";

    protected override double FrameIntervalMs => 16;

    internal TetrisClockExtension(ICanvas canvas) : base(canvas)
    {
    }

    protected override void OnStart()
    {
        foreach (var s in _slots) s.Clear();
        _lastTime = "";
    }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        var digits = ShowSeconds ? 6 : 4;
        var key = (width, height, digits, BlockSize);
        if (_geometry is not { } layout || key != _geometryKey)
        {
            layout = Layout(width, height, digits);
            _geometry = layout;
            _geometryKey = key;
        }

        var now = DateTime.Now;
        var time = now.ToString("HHmmss");
        if (time != _lastTime)
        {
            for (var i = 0; i < digits; i++)
            {
                var value = time[i] - '0';
                if (_slots[i].Value != value) Build(_slots[i], value, i == 5, layout);
            }
            _lastTime = time;
        }

        canvas.Clear(BackgroundColor);
        for (var i = 0; i < digits; i++)
        {
            _slots[i].Advance(dt);
            DrawDigit(canvas, _slots[i], layout.DigitX[i], layout);
        }
        if (ShowColon)
        {
            _fill.Color = ColonColor;
            foreach (var x in layout.ColonX)
            {
                DrawBlock(canvas, x, layout.Top + 2 * layout.Size, layout.Size * ColonWidth, ColonColor, false);
                DrawBlock(canvas, x, layout.Top + 6 * layout.Size, layout.Size * ColonWidth, ColonColor, false);
            }
        }
    }

    /// <summary>Block size and positions for the canvas: centred, as large as fits unless a size is set.</summary>
    private Geometry Layout(int width, int height, int digits)
    {
        var pairs = digits / 2;
        var cellsWide = digits * TetrisPieces.DigitWidth + pairs * Gap +
                        (pairs - 1) * (ColonWidth + 2 * ColonPad);
        var fit = Math.Max(1, Math.Min((width - 2) / cellsWide, (int)(height * 0.9f) / TetrisPieces.DigitHeight));
        var size = BlockSize > 0 ? BlockSize : fit;
        var left = (width - cellsWide * size) / 2;
        var top = (height - TetrisPieces.DigitHeight * size) / 2;

        var digitX = new int[6];
        var colonX = new List<int>();
        var x = left;
        for (var i = 0; i < digits; i++)
        {
            digitX[i] = x;
            x += TetrisPieces.DigitWidth * size;
            if (i % 2 == 0) x += Gap * size;
            else if (i < digits - 1)
            {
                colonX.Add(x + ColonPad * size);
                x += (ColonWidth + 2 * ColonPad) * size;
            }
        }
        return new Geometry(size, top, digitX, colonX);
    }

    /// <summary>A new random set of pieces for the digit, with their fall timing.</summary>
    private void Build(DigitSlot slot, int value, bool isSeconds, Geometry layout)
    {
        var pieces = TetrisPieces.Decompose(value, _random);
        slot.Value = value;
        slot.Falling.Clear();
        slot.Landed.Clear();

        // fall distance in cells: from just above the top edge down to the piece's place
        var topCells = layout.Top / (float)Math.Max(1, layout.Size);
        var cellsPerSecond = 45f * Math.Clamp(FallSpeed, 0.25f, 4f);
        var start = 0f;
        var budgetPerPiece = SecondsBudget / Math.Max(1, pieces.Count);
        foreach (var piece in pieces)
        {
            var height = piece.Bottom - piece.Top + 1;
            var distance = topCells + piece.Top + height + 1;
            var duration = distance / cellsPerSecond;
            if (isSeconds) duration = Math.Min(duration, budgetPerPiece * 1.6f);
            var spins = SpinPieces ? _random.Next(0, 4) : 0;
            var slide = SlidePieces ? _random.Next(-2, 3) : 0;
            var color = RandomColors
                ? SKColor.FromHsv(_random.Next(360), 70 + _random.Next(30), 90 + _random.Next(10))
                : ClassicColors[piece.Kind.ColorIndex];
            slot.Falling.Add(new FallingPiece(piece, start, duration, distance, spins, slide, color));
            // the next piece starts before this one lands, so a digit does not take ages
            start += isSeconds ? budgetPerPiece * 0.75f : duration * 0.55f;
        }
        slot.Clock = 0;
    }

    private void DrawDigit(SKCanvas canvas, DigitSlot slot, int x0, Geometry g)
    {
        var size = g.Size;
        foreach (var (piece, color) in slot.Landed)
            foreach (var c in piece.Cells)
                DrawBlock(canvas, x0 + c.X * size, g.Top + c.Y * size, size, color, BeveledBlocks);

        foreach (var f in slot.Falling)
        {
            var t = (slot.Clock - f.Start) / f.Duration;
            if (t < 0) continue;
            t = Math.Min(1f, t);

            // still turning: from (rotation - spins) to the final rotation in equal steps over the fall
            var turnsDone = f.Spins == 0 ? 0 : Math.Min(f.Spins, (int)(t * (f.Spins + 1)));
            var rotation = f.Piece.Rotation - f.Spins + turnsDone;
            var shape = f.Piece.Kind.Rotated(rotation);
            // slide: start a few columns off, reach the column by 80 % of the fall
            var dx = (int)MathF.Round(f.Slide * Math.Max(0f, 1f - t / 0.8f));
            // falling: whole cells, from 'distance' cells above the place to the place
            var dy = (int)MathF.Round((1f - t) * f.Distance);
            var px = x0 + (f.Piece.Left + dx) * size;
            var py = g.Top + (f.Piece.Top - dy) * size;
            foreach (var c in shape)
                DrawBlock(canvas, px + c.X * size, py + c.Y * size, size, f.Color, BeveledBlocks);
        }
    }

    private void DrawBlock(SKCanvas canvas, int x, int y, int size, SKColor color, bool bevel)
    {
        _fill.Color = color;
        canvas.DrawRect(x, y, size, size, _fill);
        if (!bevel || size < 3) return;
        // light top/left, dark bottom/right edge
        _fill.Color = Blend(color, SKColors.White, 0.45f);
        canvas.DrawRect(x, y, size, 1, _fill);
        canvas.DrawRect(x, y, 1, size, _fill);
        _fill.Color = Blend(color, SKColors.Black, 0.45f);
        canvas.DrawRect(x, y + size - 1, size, 1, _fill);
        canvas.DrawRect(x + size - 1, y, 1, size, _fill);
    }

    private static SKColor Blend(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t),
        a.Alpha);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _fill.Dispose();
    }

    private readonly record struct Geometry(int Size, int Top, int[] DigitX, List<int> ColonX);

    private sealed record FallingPiece(PlacedPiece Piece, float Start, float Duration, float Distance, int Spins,
        int Slide, SKColor Color);

    private sealed class DigitSlot
    {
        public int Value = -1;
        public float Clock;
        public readonly List<FallingPiece> Falling = [];
        public readonly List<(PlacedPiece Piece, SKColor Color)> Landed = [];

        public void Clear()
        {
            Value = -1;
            Clock = 0;
            Falling.Clear();
            Landed.Clear();
        }

        /// <summary>Moves time on; pieces whose fall is over join the landed ones.</summary>
        public void Advance(float dt)
        {
            Clock += dt;
            for (var i = Falling.Count - 1; i >= 0; i--)
            {
                var f = Falling[i];
                if (Clock < f.Start + f.Duration) continue;
                Landed.Add((f.Piece, f.Color));
                Falling.RemoveAt(i);
            }
        }
    }
}
