namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Pac-Man character with simple grid-based movement.
///     Moves from cell center to cell center.
/// </summary>
public class PacManCharacter
{
    private const float Speed = 0.1f; // Slower for better gameplay

    private float _animationState;

    // Current movement direction

    // Queued direction (set by AI, applied at next cell)
    private Direction _nextDirection = Direction.None;

    // Target cell we're moving toward
    private int _targetX;

    private int _targetY;

    // Current position (can be between cells during movement)
    private float _x;
    private float _y;

    public PacManCharacter(float startX, float startY)
    {
        _x = startX;
        _y = startY;
        _targetX = (int)startX;
        _targetY = (int)startY;
    }

    public Vector2 Position => new(_x, _y);
    public Direction Direction { get; private set; } = Direction.None;

    public float MouthAngle => 45f * (float)Math.Sin(_animationState * 10);

    public void Reset(float startX, float startY)
    {
        _x = startX;
        _y = startY;
        _targetX = (int)startX;
        _targetY = (int)startY;
        Direction = Direction.None;
        _nextDirection = Direction.None;
        _animationState = 0;
    }

    public void SetNextDirection(Direction dir)
    {
        _nextDirection = dir;
    }

    /// <summary>True when Pac-Man is centred on a cell (a decision point for the AI).</summary>
    public bool AtCellCenter => Math.Abs(_x - _targetX) < 0.05f && Math.Abs(_y - _targetY) < 0.05f;

    public void Update(Maze maze)
    {
        // Are we at the target cell?
        var atTarget = AtCellCenter;

        if (atTarget)
        {
            // Snap to target
            _x = _targetX;
            _y = _targetY;

            // Try to apply queued direction
            if (_nextDirection != Direction.None && CanMove(maze, _targetX, _targetY, _nextDirection))
                Direction = _nextDirection;

            // If current direction is blocked, stop or find alternative
            if (Direction != Direction.None && !CanMove(maze, _targetX, _targetY, Direction))
            {
                // Try queued direction
                if (_nextDirection != Direction.None && CanMove(maze, _targetX, _targetY, _nextDirection))
                    Direction = _nextDirection;
                else
                    Direction = Direction.None;
            }

            // Set new target based on direction
            if (Direction != Direction.None)
            {
                var delta = Direction.ToVector();
                _targetX = (int)_x + (int)delta.X;
                _targetY = (int)_y + (int)delta.Y;
            }
        }

        // Move toward target
        if (Direction != Direction.None)
        {
            var dx = _targetX - _x;
            var dy = _targetY - _y;
            var dist = (float)Math.Sqrt(dx * dx + dy * dy);

            if (dist > 0.01f)
            {
                var move = Math.Min(Speed, dist);
                _x += dx / dist * move;
                _y += dy / dist * move;
            }
        }

        // Animation
        if (Direction != Direction.None)
        {
            _animationState += 0.2f;
            if (_animationState > Math.PI * 2) _animationState = 0;
        }
    }

    private bool CanMove(Maze maze, int fromX, int fromY, Direction dir)
    {
        var delta = dir.ToVector();
        var toX = fromX + (int)delta.X;
        var toY = fromY + (int)delta.Y;
        return maze.IsOpenCell(toX, toY);
    }
}