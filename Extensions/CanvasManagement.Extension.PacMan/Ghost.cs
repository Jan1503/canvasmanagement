using SkiaSharp;

namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Ghost with personality-based AI like the original Pac-Man. Each ghost greedily steps toward its
///     own target at every junction (no reversing), and the target depends on its colour and the global
///     scatter/chase mode. Targets are scaled to the actual grid so it works on any panel size.
/// </summary>
public class Ghost
{
    private const float BaseSpeed = 0.085f;
    private const float MaxSpeed = 0.112f; // can match Pac-Man (0.1) at higher difficulty so they actually catch him
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
            GhostState.Frightened => BaseSpeed * 0.55f,
            _ => Math.Min(MaxSpeed, BaseSpeed + (difficulty - 1) * 0.0045f)
        };
        // Blinky (red) is the closer — a touch faster so the pack doesn't all take the same slow route.
        if (GhostColorType == GhostColor.Red && State != GhostState.Frightened)
            speed = Math.Min(MaxSpeed, speed * 1.08f);

        var atTarget = Math.Abs(_x - _targetX) < 0.05f && Math.Abs(_y - _targetY) < 0.05f;
        if (atTarget)
        {
            _x = _targetX;
            _y = _targetY;

            var target = GetTarget(pacmanPos, pacmanDir, blinky, maze, mode);
            Direction = ChooseDirection(maze, (int)_x, (int)_y, target, pacmanPos);

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
        // Scale the "too close → scatter" radius with the maze so Clyde actually peels off on big panels.
        var shy = Math.Max(8f, Math.Min(maze.GridWidth, maze.GridHeight) * 0.35f);
        return Vector2.Distance(Position, pacmanPos) > shy ? pacmanPos : new Vector2(1, maze.GridHeight - 2);
    }

    private Direction ChooseDirection(Maze maze, int cellX, int cellY, Vector2 target, Vector2 pacmanPos)
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

        var flee = State == GhostState.Frightened && !maze.IsInsideGhostHouse(cellX, cellY);
        if (flee)
            return FleeDirection(maze, cellX, cellY, pacmanPos, opposite);

        // Greedy Euclidean targeting loops on generated mazes. Pathfind the first step toward the
        // nearest open cell to the personality target, and never reverse unless it's a dead-end.
        var (tx, ty) = SnapOpen(maze, (int)Math.Round(target.X), (int)Math.Round(target.Y));
        var blocked = BlockOpposite(maze, cellX, cellY, opposite);
        var step = maze.NextStepToNearestTarget((cellX, cellY), (x, y) => x == tx && y == ty, blocked);
        if (step != Direction.None) return step;

        step = maze.NextStepToNearestTarget((cellX, cellY), (x, y) => x == tx && y == ty);
        if (step != Direction.None) return step;

        var od = opposite.ToVector();
        if (opposite != Direction.None && maze.IsOpenCell(cellX + (int)od.X, cellY + (int)od.Y))
            return opposite;

        return Direction.None;
    }

    private Direction FleeDirection(Maze maze, int cellX, int cellY, Vector2 danger, Direction opposite)
    {
        var best = Direction.None;
        var bestScore = float.MinValue;
        foreach (var dir in new[] { Direction.Up, Direction.Left, Direction.Down, Direction.Right })
        {
            if (dir == opposite) continue;
            var delta = dir.ToVector();
            int nx = cellX + (int)delta.X, ny = cellY + (int)delta.Y;
            if (!maze.IsOpenCell(nx, ny)) continue;
            var dist = Vector2.Distance(new Vector2(nx, ny), danger) + (float)_rand.NextDouble() * 0.4f;
            if (dist > bestScore)
            {
                bestScore = dist;
                best = dir;
            }
        }

        if (best != Direction.None) return best;
        var od = opposite.ToVector();
        if (opposite != Direction.None && maze.IsOpenCell(cellX + (int)od.X, cellY + (int)od.Y))
            return opposite;
        return Direction.None;
    }

    private static bool[,]? BlockOpposite(Maze maze, int cellX, int cellY, Direction opposite)
    {
        if (opposite == Direction.None) return null;
        var d = opposite.ToVector();
        int bx = cellX + (int)d.X, by = cellY + (int)d.Y;
        if (bx < 0 || by < 0 || bx >= maze.GridWidth || by >= maze.GridHeight) return null;
        var blocked = new bool[maze.GridWidth, maze.GridHeight];
        blocked[bx, by] = true;
        return blocked;
    }

    private static (int x, int y) SnapOpen(Maze maze, int x, int y)
    {
        x = Math.Clamp(x, 0, maze.GridWidth - 1);
        y = Math.Clamp(y, 0, maze.GridHeight - 1);
        if (maze.IsOpenCell(x, y)) return (x, y);
        for (var r = 1; r <= 12; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                int nx = x + dx, ny = y + dy;
                if (maze.IsOpenCell(nx, ny)) return (nx, ny);
            }
        }

        return (x, y);
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
