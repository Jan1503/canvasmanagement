namespace CanvasManagement.Canvas.Extension.GameOfLife;

public class Board
{
    // OPTIMIZATION: Reusable neighbor array (avoid allocation per generation)
    private readonly byte[,] _neighbors;
    private int _currentGeneration;
    public byte[,] Cells;
    public int Columns;
    public int Rows;

    public Board(int columns, int rows)
    {
        Cells = new byte[rows, columns];
        _neighbors = new byte[rows, columns];
        Columns = columns;
        Rows = rows;
        Generations = new List<Generation>();
    }

    public Generation CurrentGeneration => Generations[_currentGeneration - 1];
    public List<Generation> Generations { get; }

    public void Start(double density)
    {
        Random rand = new();
        for (var i = 0; i < Cells.GetLength(0); i++)
        for (var j = 0; j < Cells.GetLength(1); j++)
            if (rand.NextDouble() < density)
                Cells[i, j] = 1;
    }

    public void Clear()
    {
        Generations.Clear();
        _currentGeneration = 0;

        // OPTIMIZATION: Use Array.Clear for better performance
        Array.Clear(Cells, 0, Cells.Length);
    }

    public void Advance()
    {
        var generation = new Generation { No = _currentGeneration };
        var cellsAlive = 0;
        var cellsDead = 0;

        // OPTIMIZATION: Clear neighbors array instead of reallocating
        Array.Clear(_neighbors, 0, _neighbors.Length);

        // Count neighbors
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
        {
            var isLeftEdge = x == 0;
            var isRightEdge = x == Columns - 1;
            var isTopEdge = y == 0;
            var isBottomEdge = y == Rows - 1;
            var isEdge = isLeftEdge | isRightEdge | isTopEdge | isBottomEdge;

            if (isEdge)
                continue;

            // OPTIMIZATION: Direct indexing without wrapping calculations when not on edge
            var neighborCount =
                Cells[y - 1, x - 1] + Cells[y - 1, x] + Cells[y - 1, x + 1] +
                Cells[y, x - 1] + Cells[y, x + 1] +
                Cells[y + 1, x - 1] + Cells[y + 1, x] + Cells[y + 1, x + 1];

            _neighbors[y, x] = (byte)neighborCount;
        }

        // Apply Game of Life rules
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
        {
            var isAlive = Cells[y, x] == 1;
            int liveNeighbors = _neighbors[y, x];

            // OPTIMIZATION: Combined rule evaluation
            if (isAlive)
            {
                // Live cell rules
                if (liveNeighbors < 2 || liveNeighbors > 3)
                {
                    Cells[y, x] = 0; // Dies
                    cellsDead++;
                }
                else
                {
                    cellsAlive++; // Survives
                }
            }
            else
            {
                // Dead cell rule
                if (liveNeighbors == 3)
                {
                    Cells[y, x] = 1; // Birth
                    cellsAlive++;
                }
                else
                {
                    cellsDead++;
                }
            }
        }

        generation.CellsAlive = cellsAlive;
        generation.CellsDead = cellsDead;
        Generations.Add(generation);
        _currentGeneration++;
    }
}