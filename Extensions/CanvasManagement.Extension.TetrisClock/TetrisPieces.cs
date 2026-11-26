namespace CanvasManagement.Extension.TetrisClock;

/// <summary>A cell of a block grid: column, row (row 0 is the top).</summary>
internal readonly record struct Cell(int X, int Y);

/// <summary>A piece kind with its colour slot; <see cref="Cells" /> is the shape in rotation 0.</summary>
internal sealed record PieceKind(char Name, Cell[] Cells, int ColorIndex)
{
    private Cell[]?[] _rotations = new Cell[]?[4];

    /// <summary>
    ///     The shape turned <paramref name="quarterTurns" /> times clockwise, moved to start at (0, 0), cells in
    ///     reading order. Cached: the clock asks for it every frame.
    /// </summary>
    public Cell[] Rotated(int quarterTurns)
    {
        var turns = ((quarterTurns % 4) + 4) % 4;
        return _rotations[turns] ??= Rotate(turns);
    }

    private Cell[] Rotate(int turns)
    {
        var cells = Cells;
        for (var i = 0; i < turns; i++)
            cells = cells.Select(c => new Cell(-c.Y, c.X)).ToArray(); // clockwise with y pointing down
        var minX = cells.Min(c => c.X);
        var minY = cells.Min(c => c.Y);
        return cells.Select(c => new Cell(c.X - minX, c.Y - minY))
            .OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
    }
}

/// <summary>A piece placed in a digit: kind, rotation and the cells it covers (digit coordinates).</summary>
internal sealed record PlacedPiece(PieceKind Kind, int Rotation, Cell[] Cells)
{
    public int Left => Cells.Min(c => c.X);
    public int Top => Cells.Min(c => c.Y);
    public int Bottom => Cells.Max(c => c.Y);
}

/// <summary>
///     The digits as 6×10 cell masks, and a randomised exact cover of a mask with Tetris pieces: every time a
///     digit is built it is made of a different set of pieces in different orientations.
/// </summary>
internal static class TetrisPieces
{
    public const int DigitWidth = 6;
    public const int DigitHeight = 10;

    // Shapes of the old fall tables at rest (2-cell strokes).
    private static readonly string[][] Glyphs =
    [
        ["######", "######", "##..##", "##..##", "##..##", "##..##", "##..##", "##..##", "######", "######"],
        ["....##", "....##", "....##", "....##", "....##", "....##", "....##", "....##", "....##", "....##"],
        ["######", "######", "....##", "....##", "######", "######", "##....", "##....", "######", "######"],
        ["######", "######", "....##", "....##", "######", "######", "....##", "....##", "######", "######"],
        ["##..##", "##..##", "##..##", "##..##", "######", "######", "....##", "....##", "....##", "....##"],
        ["######", "######", "##....", "##....", "######", "######", "....##", "....##", "######", "######"],
        ["######", "######", "##....", "##....", "######", "######", "##..##", "##..##", "######", "######"],
        ["######", "######", "....##", "....##", "....##", "....##", "....##", "....##", "....##", "....##"],
        ["######", "######", "##..##", "##..##", "######", "######", "##..##", "##..##", "######", "######"],
        ["######", "######", "##..##", "##..##", "######", "######", "....##", "....##", "######", "######"]
    ];

    private static Cell[] C(params (int x, int y)[] cells) => cells.Select(c => new Cell(c.x, c.y)).ToArray();

    /// <summary>The seven tetrominoes (colour slots 0..6), tried first.</summary>
    public static readonly PieceKind[] Tetrominoes =
    [
        new('I', C((0, 0), (1, 0), (2, 0), (3, 0)), 0),
        new('O', C((0, 0), (1, 0), (0, 1), (1, 1)), 1),
        new('T', C((0, 0), (1, 0), (2, 0), (1, 1)), 2),
        new('S', C((1, 0), (2, 0), (0, 1), (1, 1)), 3),
        new('Z', C((0, 0), (1, 0), (1, 1), (2, 1)), 4),
        new('J', C((0, 0), (0, 1), (1, 1), (2, 1)), 5),
        new('L', C((2, 0), (0, 1), (1, 1), (2, 1)), 6)
    ];

    /// <summary>Smaller pieces, only if a mask cannot be covered by tetrominoes alone.</summary>
    private static readonly PieceKind[] Fillers =
    [
        new('V', C((0, 0), (0, 1), (1, 1)), 7),
        new('D', C((0, 0), (1, 0)), 7),
        new('M', C((0, 0)), 7)
    ];

    /// <summary>The cells of digit <paramref name="digit" />.</summary>
    public static HashSet<Cell> Mask(int digit)
    {
        var set = new HashSet<Cell>();
        var rows = Glyphs[Math.Clamp(digit, 0, 9)];
        for (var y = 0; y < rows.Length; y++)
        for (var x = 0; x < rows[y].Length; x++)
            if (rows[y][x] == '#') set.Add(new Cell(x, y));
        return set;
    }

    /// <summary>
    ///     Covers the digit with pieces, randomly (backtracking over random orders), ordered bottom-up as they
    ///     should fall. Tetrominoes only if possible, otherwise with smaller fillers.
    /// </summary>
    public static List<PlacedPiece> Decompose(int digit, Random random)
    {
        var mask = Mask(digit);
        var result = new List<PlacedPiece>();
        if (!Cover(mask, Tetrominoes, random, result, 0))
        {
            result.Clear();
            Cover(mask, [.. Tetrominoes, .. Fillers], random, result, 0);
        }
        // bottom pieces first, so each one lands on what is already there
        return result.OrderByDescending(p => p.Bottom).ThenBy(_ => random.Next()).ToList();
    }

    private static bool Cover(HashSet<Cell> open, PieceKind[] kinds, Random random, List<PlacedPiece> placed, int depth)
    {
        if (open.Count == 0) return true;
        if (depth > 64) return false;

        // the first open cell in reading order must be the first cell of the piece that covers it
        var first = open.OrderBy(c => c.Y).ThenBy(c => c.X).First();
        foreach (var kind in kinds.Take(7).OrderBy(_ => random.Next()).Concat(kinds.Skip(7)))
        foreach (var rotation in Enumerable.Range(0, 4).OrderBy(_ => random.Next()))
        {
            var shape = kind.Rotated(rotation);
            var anchor = shape[0];
            var cells = shape.Select(c => new Cell(first.X + c.X - anchor.X, first.Y + c.Y - anchor.Y)).ToArray();
            if (!cells.All(open.Contains)) continue;

            foreach (var c in cells) open.Remove(c);
            placed.Add(new PlacedPiece(kind, rotation, cells));
            if (Cover(open, kinds, random, placed, depth + 1)) return true;
            placed.RemoveAt(placed.Count - 1);
            foreach (var c in cells) open.Add(c);
        }
        return false;
    }
}
