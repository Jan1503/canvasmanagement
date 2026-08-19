using SkiaSharp;

namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Ghost with personality-based AI like the original Pac-Man. Each ghost greedily steps toward its
///     own target at every junction (no reversing), and the target depends on its colour and the global
///     scatter/chase mode. Targets are scaled to the actual grid so it works on any panel size.
/// </summary>
public class Ghost
{
    private const float BaseSpeed = 0.08f;
    private const float MaxSpeed = 0.095f; // keep ghosts a touch slower than Pac-Man (0.1) so it stays fair
    private static readonly Random _rand = new();
    private GhostMode _lastMode = GhostMode.Scatter;
    private bool _reverseQueued;
    private int _targetX;
    private int _targetY;
    private float _x;
    private float _y;

    public Ghost(float startX, float startY, GhostColor color)
    {
        _x = startX;
        _y = startY;
        _targetX = (int)startX;
        _targetY = (int)startY;
        GhostColorType = color;
        State = GhostState.Chase;
        RespawnTimer = 0;
    }

    public Vector2 Position => new(_x, _y);
    public Direction Direction { get; private set; } = Direction.None;

    public GhostState State { get; set; }
    public int RespawnTimer { get; set; }
    public GhostColor GhostColorType { get; }

    public SKColor Color => State switch
    {
        GhostState.Frightened => SKColors.Blue,
        GhostState.Dead => new SKColor(100, 100, 100, 128),
        _ => GhostColorType switch
        {
            GhostColor.Red => SKColors.Red,
            GhostColor.Pink => new SKColor(255, 182, 193),
            GhostColor.Cyan => SKColors.Cyan,
            GhostColor.Orange => SKColors.Orange,
            _ => SKColors.White
        }
    };

    public void Reset(float startX, float startY)
    {
        _x = startX;
        _y = startY;
        _targetX = (int)startX;
        _targetY = (int)startY;
        Direction = Direction.None;
        State = GhostState.Chase;
        RespawnTimer = 0;
        _reverseQueued = false;
    }

    public void Respawn(float x, float y)
    {
        _x = x;
        _y = y;
        _targetX = (int)x;
        _targetY = (int)y;
        Direction = Direction.None;
    }

    public void Update(Maze maze, Vector2 pacmanPos, Direction pacmanDir, int difficulty, Ghost? blinky,
        GhostMode mode)
    {
        if (maze == null) return;
        if (State == GhostState.Dead) return;

        // Classic behaviour: ghosts reverse the instant the global mode flips (and not while frightened).
        if (mode != _lastMode)
        {
            if (State != GhostState.Frightened) _reverseQueued = true;
            _lastMode = mode;
        }

        var speed = State switch
        {
            GhostState.Frightened => BaseSpeed * 0.5f,
            _ => Math.Min(MaxSpeed, BaseSpeed + (difficulty - 1) * 0.0025f)
        };

        var atTarget = Math.Abs(_x - _targetX) < 0.05f && Math.Abs(_y - _targetY) < 0.05f;
        if (atTarget)
        {
            _x = _targetX;
            _y = _targetY;

            var target = GetTarget(pacmanPos, pacmanDir, blinky, maze, mode);
            Direction = ChooseDirection(maze, (int)_x, (int)_y, target);

            if (Direction != Direction.None)
            {
                var delta = Direction.ToVector();
                _targetX = (int)_x + (int)delta.X;
                _targetY = (int)_y + (int)delta.Y;
            }
        }

        if (Direction != Direction.None)
        {
            var dx = _targetX - _x;
            var dy = _targetY - _y;
            var dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist > 0.01f)
            {
                var move = Math.Min(speed, dist);
                _x += dx / dist * move;
                _y += dy / dist * move;
            }
        }
    }

    /// <summary>
    ///     Targeting per personality. In Scatter mode every ghost heads to its own corner; in Chase mode:
    ///     Blinky=Pac-Man, Pinky=4 ahead, Inky=Blinky-pivoted, Clyde=chase far / scatter near.
    /// </summary>
    private Vector2 GetTarget(Vector2 pacmanPos, Direction pacmanDir, Ghost? blinky, Maze maze, GhostMode mode)
    {
        // Always steer for the gate while inside the house, so ghosts reliably get out.
        if (maze.IsInsideGhostHouse((int)Math.Round(_x), (int)Math.Round(_y)))
            return maze.GhostHouseExit;

        if (State == GhostState.Frightened)
            return new Vector2(_rand.Next(maze.GridWidth), _rand.Next(maze.GridHeight));

        if (mode == GhostMode.Scatter)
            return ScatterCorner(maze);

        var pacmanDelta = pacmanDir.ToVector();
        return GhostColorType switch
        {
            GhostColor.Red => pacmanPos,
            GhostColor.Pink => new Vector2(pacmanPos.X + pacmanDelta.X * 4, pacmanPos.Y + pacmanDelta.Y * 4),
            GhostColor.Cyan => GetInkyTarget(pacmanPos, pacmanDelta, blinky),
            GhostColor.Orange => GetClydeTarget(pacmanPos, maze),
            _ => pacmanPos
        };
    }

    private Vector2 ScatterCorner(Maze maze)
    {
        return GhostColorType switch
        {
            GhostColor.Red => new Vector2(maze.GridWidth - 2, 1), // top-right
            GhostColor.Pink => new Vector2(1, 1), // top-left
            GhostColor.Cyan => new Vector2(maze.GridWidth - 2, maze.GridHeight - 2), // bottom-right
            GhostColor.Orange => new Vector2(1, maze.GridHeight - 2), // bottom-left
            _ => new Vector2(maze.GridWidth / 2f, maze.GridHeight / 2f)
        };
    }

    private Vector2 GetInkyTarget(Vector2 pacmanPos, Vector2 pacmanDelta, Ghost? blinky)
    {
        if (blinky == null) return pacmanPos;
        var pivotX = pacmanPos.X + pacmanDelta.X * 2;
        var pivotY = pacmanPos.Y + pacmanDelta.Y * 2;
        return new Vector2(pivotX + (pivotX - blinky.Position.X), pivotY + (pivotY - blinky.Position.Y));
    }

    private Vector2 GetClydeTarget(Vector2 pacmanPos, Maze maze)
    {
        return Vector2.Distance(Position, pacmanPos) > 8f ? pacmanPos : new Vector2(1, maze.GridHeight - 2);
    }

    private Direction ChooseDirection(Maze maze, int cellX, int cellY, Vector2 target)
    {
        var opposite = Maze.Opposite(Direction);

        // A queued reverse (mode flip) overrides the usual no-reverse rule for one decision.
        if (_reverseQueued)
        {
            _reverseQueued = false;
            var delta = opposite.ToVector();
            if (opposite != Direction.None && maze.IsOpenCell(cellX + (int)delta.X, cellY + (int)delta.Y))
                return opposite;
        }

        var best = Direction.None;
        var bestScore = float.MaxValue;

        // Frightened ghosts flee (maximise distance) - except while escaping the house, where they must
        // still head straight for the gate.
        var flee = State == GhostState.Frightened && !maze.IsInsideGhostHouse(cellX, cellY);

        foreach (var dir in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
        {
            var delta = dir.ToVector();
            int nx = cellX + (int)delta.X, ny = cellY + (int)delta.Y;
            if (!maze.IsOpenCell(nx, ny)) continue;
            if (dir == opposite) continue; // ghosts never reverse at a junction

            var dist = Vector2.Distance(new Vector2(nx, ny), target);

            // Add jitter so ghosts don't all pick identical paths.
            if (flee)
                dist = -dist + (float)_rand.NextDouble() * 0.5f;
            else
                dist += (float)_rand.NextDouble() * 0.01f;

            if (dist < bestScore)
            {
                bestScore = dist;
                best = dir;
            }
        }

        if (best != Direction.None) return best;

        // Dead-end: reversing is the only option.
        var od = opposite.ToVector();
        if (opposite != Direction.None && maze.IsOpenCell(cellX + (int)od.X, cellY + (int)od.Y))
            return opposite;

        return Direction.None;
    }
}

public enum GhostColor
{
    Red,
    Pink,
    Cyan,
    Orange
}

public enum GhostState
{
    Chase,
    Scatter,
    Frightened,
    Dead
}

public enum GhostMode
{
    Scatter,
    Chase
}
