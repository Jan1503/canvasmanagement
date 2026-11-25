namespace CanvasManagement.Extension.PacMan;

public enum Direction
{
    None,
    Up,
    Down,
    Left,
    Right
}

internal static class Dirs
{
    public static readonly Direction[] All = { Direction.Up, Direction.Left, Direction.Down, Direction.Right };

    public static int Dx(this Direction d) => d switch { Direction.Left => -1, Direction.Right => 1, _ => 0 };
    public static int Dy(this Direction d) => d switch { Direction.Up => -1, Direction.Down => 1, _ => 0 };

    public static Direction Opposite(this Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        Direction.Right => Direction.Left,
        _ => Direction.None
    };
}

internal enum Cell : byte
{
    Wall,
    Open,
    House,
    Door
}

internal enum Phase
{
    Ready,
    Playing,
    GhostEaten,
    Dying,
    LevelClear,
    GameOver,
    Paused
}

internal enum GhostMode
{
    Scatter,
    Chase
}

internal enum GhostState
{
    /// <summary>Waiting in the pen, bobbing.</summary>
    Home,

    /// <summary>Scripted walk out through the door.</summary>
    Leaving,

    /// <summary>On the grid, hunting or scattering (or frightened, see <see cref="Ghost.Frightened"/>).</summary>
    Active,

    /// <summary>Eaten: eyes run back to the door.</summary>
    Eyes,

    /// <summary>Scripted walk down into the pen, then it comes back to life.</summary>
    Entering
}

/// <summary>Everything the sound side needs to hear about, raised once per frame by <see cref="Game"/>.</summary>
[Flags]
internal enum GameEvent
{
    None = 0,
    Dot = 1,
    Power = 2,
    GhostEaten = 4,
    Fruit = 8,
    Death = 16,
    ExtraLife = 32,
    LevelClear = 64,
    GameStart = 128,
    GameOver = 256,
    FruitAppears = 512
}
