namespace CanvasManagement.Extension.PacMan;

public struct Vector2
{
    public float X { get; set; }
    public float Y { get; set; }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 operator +(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X + b.X, a.Y + b.Y);
    }

    public static Vector2 operator -(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X - b.X, a.Y - b.Y);
    }

    public static Vector2 operator *(Vector2 a, float scalar)
    {
        return new Vector2(a.X * scalar, a.Y * scalar);
    }

    public static float Distance(Vector2 a, Vector2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public float Length()
    {
        return (float)Math.Sqrt(X * X + Y * Y);
    }

    public Vector2 Normalized()
    {
        var length = Length();
        if (length == 0) return this;
        return new Vector2(X / length, Y / length);
    }

    public override string ToString()
    {
        return $"({X:F2}, {Y:F2})";
    }
}

public enum Direction
{
    None,
    Up,
    Down,
    Left,
    Right
}

public static class DirectionExtensions
{
    public static Vector2 ToVector(this Direction direction)
    {
        return direction switch
        {
            Direction.Up => new Vector2(0, -1),
            Direction.Down => new Vector2(0, 1),
            Direction.Left => new Vector2(-1, 0),
            Direction.Right => new Vector2(1, 0),
            _ => new Vector2(0, 0)
        };
    }
}