namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Grid walker. It stands on tile (<see cref="X"/>, <see cref="Y"/>) and is <see cref="Off"/> (0..1) of the
///     way to the next tile in <see cref="Dir"/>. Off 0 means centred on the tile: the only place it may turn.
/// </summary>
internal abstract class Walker
{
    public int X, Y;
    public Direction Dir = Direction.Left;
    public float Off;

    public float Fx(Maze m) => X + Dir.Dx() * Off;

    public float Fy => Y + Dir.Dy() * Off;

    public (int X, int Y) Next(Maze m)
    {
        m.Step(X, Y, Dir, out var nx, out var ny);
        return (nx, ny);
    }

    /// <summary>The tile it is mostly on (the next one once past half way).</summary>
    public (int X, int Y) Near(Maze m) => Off >= 0.5f ? Next(m) : (X, Y);

    public void Reverse(Maze m)
    {
        if (Off > 0)
        {
            m.Step(X, Y, Dir, out var nx, out var ny);
            X = nx;
            Y = ny;
            Off = 1 - Off;
        }
        Dir = Dir.Opposite();
    }

    /// <summary>
    ///     Moves <paramref name="dist"/> tiles. At every tile centre <see cref="Choose"/> may pick a new direction.
    ///     Returns false when it ran into a wall and stopped.
    /// </summary>
    protected bool Walk(Maze m, float dist)
    {
        var guard = 0;
        while (dist > 1e-5f && guard++ < 8)
        {
            if (Off <= 0)
            {
                Off = 0;
                OnCentre(m);
                var d = Choose(m);
                if (d != Direction.None) Dir = d;
                if (!m.Step(X, Y, Dir, out _, out _)) return false;
            }

            var step = Math.Min(dist, 1 - Off);
            Off += step;
            dist -= step;
            if (Off >= 1 - 1e-4f)
            {
                m.Step(X, Y, Dir, out var nx, out var ny);
                X = nx;
                Y = ny;
                Off = 0;
            }
        }
        return true;
    }

    protected virtual void OnCentre(Maze m) { }

    protected abstract Direction Choose(Maze m);
}

internal sealed class Pac : Walker
{
    /// <summary>Buffered input: kept until it can be taken, like holding the stick into a corner.</summary>
    public Direction Want = Direction.None;
    public bool Moving;
    public float Chomp;
    public Action<int, int>? Arrived;

    public void Reset(Maze m)
    {
        X = m.PacSpawn.X;
        Y = m.PacSpawn.Y;
        Dir = Direction.Left;
        Off = 0;
        Want = Direction.None;
        Moving = false;
        Chomp = 0;
    }

    public void Update(Maze m, float dist)
    {
        // Reversing is allowed any time, mid-tile included.
        if (Want != Direction.None && Want == Dir.Opposite() && Off > 0)
        {
            Reverse(m);
            Want = Direction.None;
        }
        Moving = Walk(m, dist);
        if (Moving) Chomp += dist;
    }

    protected override void OnCentre(Maze m) => Arrived?.Invoke(X, Y);

    protected override Direction Choose(Maze m)
    {
        if (Want != Direction.None && m.Step(X, Y, Want, out _, out _))
        {
            var d = Want;
            Want = Direction.None;
            return d;
        }
        return Direction.None;
    }
}

internal sealed class Ghost : Walker
{
    public readonly int Index;
    public GhostState State = GhostState.Home;
    public bool Frightened;
    public float HomeTimer;
    public float Bob;

    /// <summary>Free position while it is scripted in and out of the pen (tile units).</summary>
    public float PenX, PenY;

    public Func<Ghost, Maze, Direction>? Brain;

    public Ghost(int index) => Index = index;

    public bool OnGrid => State is GhostState.Active or GhostState.Eyes;
    public bool Dangerous => State == GhostState.Active && !Frightened;

    public float DrawX(Maze m) => OnGrid ? Fx(m) : PenX;
    public float DrawY => OnGrid ? Fy : PenY;

    public void Step(Maze m, float dist) => Walk(m, dist);

    protected override Direction Choose(Maze m) => Brain?.Invoke(this, m) ?? Direction.None;
}
