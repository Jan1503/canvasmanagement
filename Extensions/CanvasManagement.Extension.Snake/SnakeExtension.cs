using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.Snake;

[ExtensionInfo("Snake Game",
    "Classic snake — autopilot, or play with arrows / WASD in Studio",
    "Games",
    IconResourceName = "snake.svg")]
public class SnakeExtension : IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _gameLock = new();

    // Double buffering to prevent flicker
    private SKBitmap? _backBuffer;

    private SKColor _backgroundColor = SKColors.Black;
    private Direction _direction = Direction.Right;
    private bool _disposed;
    private Point _food;
    private bool _gameOver;
    private Timer? _gameTimer;
    private int _gridSize = 20;
    private int _autoGridSize = 20;
    private int _appliedGridSize = -1;
    private Direction _nextDirection = Direction.Right;
    private readonly Random _random = new();
    private bool _human;

    private readonly List<Point> _snake = new();

    internal SnakeExtension(ICanvas canvas)
    {
        _canvas = canvas;

        // Auto-fit the cell size so the play-field keeps a similar column count on any panel
        // (the 20px reference cell is sized for 384x192 and is too coarse on small displays).
        var s = DisplayScale.GetScale(canvas.Width, canvas.Height);
        _autoGridSize = Math.Max(2, (int)Math.Round(20 * s));
        _gridSize = _autoGridSize;
    }

    public bool IsRunning { get; private set; }
    public int Score { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _backBuffer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        if (IsRunning) return;

        lock (_gameLock)
        {
            _human = false;
            InitializeGame();

            // Create back buffer
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

            _gameTimer = new Timer(1000.0 / GameSpeed);
            _gameTimer.Elapsed += OnGameTick;
            _gameTimer.AutoReset = true;
            _gameTimer.Start();

            IsRunning = true;
            Console.WriteLine($"Snake started - Speed: {GameSpeed}, Grid: {GridSize}");
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _gameTimer?.Stop();
        _gameTimer?.Dispose();
        _gameTimer = null;

        _backBuffer?.Dispose();
        _backBuffer = null;

        try
        {
            _canvas.Clear(BackgroundColor);
        }
        catch
        {
        }

        IsRunning = false;
        Console.WriteLine($"Snake stopped - Final Score: {Score}");
    }

    private void InitializeGame()
    {
        // GridSize 0 = auto-fit to the panel; otherwise honour the user's value.
        _gridSize = GridSize > 0 ? Math.Max(2, GridSize) : _autoGridSize;
        _appliedGridSize = GridSize;
        var gridWidth = _canvas.Width / _gridSize;
        var gridHeight = _canvas.Height / _gridSize;

        _snake.Clear();
        _snake.Add(new Point(gridWidth / 2, gridHeight / 2));
        _snake.Add(new Point(gridWidth / 2 - 1, gridHeight / 2));
        _snake.Add(new Point(gridWidth / 2 - 2, gridHeight / 2));

        _direction = Direction.Right;
        _nextDirection = Direction.Right;
        Score = 0;
        _gameOver = false;

        PlaceFood();
    }

    private void PlaceFood()
    {
        var gridWidth = _canvas.Width / _gridSize;
        var gridHeight = _canvas.Height / _gridSize;

        do
        {
            _food = new Point(_random.Next(gridWidth), _random.Next(gridHeight));
        } while (_snake.Contains(_food));
    }

    private void OnGameTick(object? sender, ElapsedEventArgs e)
    {
        if (!IsRunning) return;

        try
        {
            _gameTimer?.Stop();

            // Update grid size if the user changed the parameter.
            if (GridSize != _appliedGridSize)
            {
                InitializeGame();
                _backBuffer?.Dispose();
                _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            }

            // Update speed if changed
            if (_gameTimer != null && Math.Abs(_gameTimer.Interval - 1000.0 / GameSpeed) > 0.1)
                _gameTimer.Interval = 1000.0 / GameSpeed;

            if (!_gameOver)
            {
                // AI decision
                if (AutoPilot && !_human) _nextDirection = GetAIDirection();

                // Apply direction change
                if (!IsOppositeDirection(_nextDirection, _direction)) _direction = _nextDirection;

                // Move snake
                var newHead = GetNextPosition(_snake[0], _direction);

                // Check collisions
                if (CheckCollision(newHead))
                {
                    _gameOver = true;
                    if (AutoRestart)
                        Task.Delay(2000).ContinueWith(_ =>
                        {
                            if (IsRunning)
                                lock (_gameLock)
                                {
                                    InitializeGame();
                                }
                        });
                }
                else
                {
                    _snake.Insert(0, newHead);

                    // Check if food eaten
                    if (newHead == _food)
                    {
                        Score += 10;
                        PlaceFood();
                    }
                    else
                    {
                        _snake.RemoveAt(_snake.Count - 1);
                    }
                }
            }

            Render();

            if (IsRunning && _gameTimer != null) _gameTimer.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Snake tick error: {ex.Message}");
            try
            {
                _gameTimer?.Start();
            }
            catch
            {
                Stop();
            }
        }
    }

    private Direction GetAIDirection()
    {
        // Simple pathfinding towards food
        var head = _snake[0];
        var dx = _food.X - head.X;
        var dy = _food.Y - head.Y;

        // Try to move towards food, avoiding immediate death
        var possibleDirections = new List<Direction>();

        if (dy < 0 && _direction != Direction.Down) possibleDirections.Add(Direction.Up);
        if (dy > 0 && _direction != Direction.Up) possibleDirections.Add(Direction.Down);
        if (dx < 0 && _direction != Direction.Right) possibleDirections.Add(Direction.Left);
        if (dx > 0 && _direction != Direction.Left) possibleDirections.Add(Direction.Right);

        // Filter out dangerous moves
        var safeDirections = possibleDirections.Where(d =>
        {
            var nextPos = GetNextPosition(head, d);
            return !CheckSelfCollision(nextPos) && (!WallCollision || !CheckWallCollision(nextPos));
        }).ToList();

        if (safeDirections.Any())
            return safeDirections[_random.Next(safeDirections.Count)];

        // If no safe move towards food, try any safe direction
        var allDirections = new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right };
        var anySafe = allDirections.Where(d =>
        {
            if (IsOppositeDirection(d, _direction)) return false;
            var nextPos = GetNextPosition(head, d);
            return !CheckSelfCollision(nextPos) && (!WallCollision || !CheckWallCollision(nextPos));
        }).ToList();

        return anySafe.Any() ? anySafe[_random.Next(anySafe.Count)] : _direction;
    }

    private Point GetNextPosition(Point current, Direction dir)
    {
        var gridWidth = _canvas.Width / _gridSize;
        var gridHeight = _canvas.Height / _gridSize;

        var next = dir switch
        {
            Direction.Up => new Point(current.X, current.Y - 1),
            Direction.Down => new Point(current.X, current.Y + 1),
            Direction.Left => new Point(current.X - 1, current.Y),
            Direction.Right => new Point(current.X + 1, current.Y),
            _ => current
        };

        // Wrap around if no wall collision
        if (!WallCollision)
        {
            if (next.X < 0) next = new Point(gridWidth - 1, next.Y);
            if (next.X >= gridWidth) next = new Point(0, next.Y);
            if (next.Y < 0) next = new Point(next.X, gridHeight - 1);
            if (next.Y >= gridHeight) next = new Point(next.X, 0);
        }

        return next;
    }

    private bool CheckCollision(Point position)
    {
        return CheckSelfCollision(position) || (WallCollision && CheckWallCollision(position));
    }

    private bool CheckSelfCollision(Point position)
    {
        return _snake.Contains(position);
    }

    private bool CheckWallCollision(Point position)
    {
        var gridWidth = _canvas.Width / _gridSize;
        var gridHeight = _canvas.Height / _gridSize;
        return position.X < 0 || position.X >= gridWidth || position.Y < 0 || position.Y >= gridHeight;
    }

    private bool IsOppositeDirection(Direction a, Direction b)
    {
        return (a == Direction.Up && b == Direction.Down) ||
               (a == Direction.Down && b == Direction.Up) ||
               (a == Direction.Left && b == Direction.Right) ||
               (a == Direction.Right && b == Direction.Left);
    }

    private void Render()
    {
        if (_backBuffer == null) return;

        lock (_gameLock)
        {
            try
            {
                // Render to back buffer
                using var canvas = new SKCanvas(_backBuffer);

                // Clear with background color (supports transparency)
                if (_backgroundColor.Alpha == 0)
                {
                    canvas.Clear(SKColors.Transparent);
                }
                else if (_backgroundColor.Alpha == 255)
                {
                    canvas.Clear(_backgroundColor);
                }
                else
                {
                    canvas.Clear(SKColors.Transparent);
                    using var bgPaint = new SKPaint { Color = _backgroundColor, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, bgPaint);
                }

                // Draw grid
                if (ShowGrid)
                {
                    using var gridPaint = new SKPaint
                    {
                        Color = new SKColor(50, 50, 50),
                        StrokeWidth = 1,
                        IsAntialias = false
                    };

                    for (var x = 0; x <= _canvas.Width; x += _gridSize)
                        canvas.DrawLine(x, 0, x, _canvas.Height, gridPaint);
                    for (var y = 0; y <= _canvas.Height; y += _gridSize)
                        canvas.DrawLine(0, y, _canvas.Width, y, gridPaint);
                }

                // Draw caterpillar body with beautiful segments
                for (var i = _snake.Count - 1; i >= 0; i--)
                {
                    var segment = _snake[i];
                    var isHead = i == 0;

                    var centerX = segment.X * _gridSize + _gridSize / 2f;
                    var centerY = segment.Y * _gridSize + _gridSize / 2f;

                    // Calculate segment size with smooth tapering
                    var sizeMultiplier = 1.0f - i * 0.05f / Math.Max(_snake.Count, 1);
                    sizeMultiplier = Math.Max(0.6f, sizeMultiplier);
                    var segmentRadius = (_gridSize / 2f - 2) * sizeMultiplier;

                    if (isHead)
                        // Draw head with eyes
                        DrawCaterpillarHead(canvas, centerX, centerY, segmentRadius);
                    else
                        // Draw body segment with gradient and stripes
                        DrawCaterpillarSegment(canvas, centerX, centerY, segmentRadius, i);
                }

                // Draw food as a leaf
                DrawLeaf(canvas, _food.X * _gridSize + _gridSize / 2f, _food.Y * _gridSize + _gridSize / 2f);

                // Draw score
                var scoreSize = CanvasText.ResolveSize(FontSize, Math.Max(6f, _canvas.ScaleSizeF(16)));
                CanvasText.Draw(canvas, _canvas, $"Score: {Score}", SKColors.White, _canvas.ScaleSize(10),
                    _canvas.ScaleSize(20), scoreSize, SKTextAlign.Left, UseBdfFont);

                if (_gameOver)
                {
                    var overSize = CanvasText.ResolveSize(FontSize, Math.Max(7f, _canvas.ScaleSizeF(24)));
                    CanvasText.Draw(canvas, _canvas, "GAME OVER", SKColors.Red, _canvas.Width / 2f,
                        _canvas.Height / 2f, overSize, SKTextAlign.Center, UseBdfFont);
                }

                // Blit to canvas in one operation
                _canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render error: {ex.Message}");
            }
        }
    }

    private void DrawCaterpillarHead(SKCanvas canvas, float x, float y, float radius)
    {
        // Use SnakeColor for head base with gradient (brighter version)
        var brightColor = new SKColor(
            (byte)Math.Min(255, SnakeColor.Red + 50),
            (byte)Math.Min(255, SnakeColor.Green + 50),
            (byte)Math.Min(255, SnakeColor.Blue + 50));

        var mediumColor = SnakeColor;

        using var headGradient = SKShader.CreateRadialGradient(
            new SKPoint(x - radius * 0.3f, y - radius * 0.3f),
            radius,
            new[] { brightColor, mediumColor },
            null,
            SKShaderTileMode.Clamp);

        using var headPaint = new SKPaint
        {
            Shader = headGradient,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(x, y, radius, headPaint);

        // Draw eyes
        var eyeOffset = radius * 0.4f;
        var eyeSize = radius * 0.35f;

        // Left eye
        using var eyeWhitePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(x - eyeOffset, y - eyeOffset * 0.5f, eyeSize, eyeWhitePaint);

        using var pupilPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(x - eyeOffset + eyeSize * 0.2f, y - eyeOffset * 0.5f, eyeSize * 0.5f, pupilPaint);

        // Right eye
        canvas.DrawCircle(x + eyeOffset, y - eyeOffset * 0.5f, eyeSize, eyeWhitePaint);
        canvas.DrawCircle(x + eyeOffset + eyeSize * 0.2f, y - eyeOffset * 0.5f, eyeSize * 0.5f, pupilPaint);

        // Draw antennae using darker version of snake color
        var antennaColor = new SKColor(
            (byte)(SnakeColor.Red * 0.8),
            (byte)(SnakeColor.Green * 0.8),
            (byte)(SnakeColor.Blue * 0.8));

        using var antennaPaint = new SKPaint
        {
            Color = antennaColor,
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        using var antennaPath1 = new SKPath();
        antennaPath1.MoveTo(x - eyeOffset, y - radius * 0.8f);
        antennaPath1.CubicTo(
            x - eyeOffset - radius * 0.2f, y - radius * 1.2f,
            x - eyeOffset - radius * 0.3f, y - radius * 1.4f,
            x - eyeOffset - radius * 0.4f, y - radius * 1.5f);
        canvas.DrawPath(antennaPath1, antennaPaint);

        using var antennaPath2 = new SKPath();
        antennaPath2.MoveTo(x + eyeOffset, y - radius * 0.8f);
        antennaPath2.CubicTo(
            x + eyeOffset + radius * 0.2f, y - radius * 1.2f,
            x + eyeOffset + radius * 0.3f, y - radius * 1.4f,
            x + eyeOffset + radius * 0.4f, y - radius * 1.5f);
        canvas.DrawPath(antennaPath2, antennaPaint);

        // Antenna tips - use complementary color
        using var tipPaint = new SKPaint
        {
            Color = new SKColor(
                (byte)Math.Min(255, SnakeColor.Red + 100),
                (byte)Math.Min(255, SnakeColor.Green + 80),
                (byte)Math.Min(255, SnakeColor.Blue + 60)),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(x - eyeOffset - radius * 0.4f, y - radius * 1.5f, radius * 0.15f, tipPaint);
        canvas.DrawCircle(x + eyeOffset + radius * 0.4f, y - radius * 1.5f, radius * 0.15f, tipPaint);

        // Smile
        using var smilePaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 150),
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        using var smilePath = new SKPath();
        smilePath.MoveTo(x - radius * 0.3f, y + radius * 0.2f);
        smilePath.QuadTo(x, y + radius * 0.5f, x + radius * 0.3f, y + radius * 0.2f);
        canvas.DrawPath(smilePath, smilePaint);
    }

    private void DrawCaterpillarSegment(SKCanvas canvas, float x, float y, float radius, int segmentIndex)
    {
        // Use SnakeColor for body segments with alternating shades
        var isDark = segmentIndex % 2 == 0;

        SKColor color1, color2;
        if (isDark)
        {
            // Darker shade
            color1 = new SKColor(
                (byte)(SnakeColor.Red * 0.8),
                (byte)(SnakeColor.Green * 0.8),
                (byte)(SnakeColor.Blue * 0.8));
            color2 = new SKColor(
                (byte)(SnakeColor.Red * 0.6),
                (byte)(SnakeColor.Green * 0.6),
                (byte)(SnakeColor.Blue * 0.6));
        }
        else
        {
            // Lighter shade
            color1 = new SKColor(
                (byte)Math.Min(255, SnakeColor.Red * 1.1),
                (byte)Math.Min(255, SnakeColor.Green * 1.1),
                (byte)Math.Min(255, SnakeColor.Blue * 1.1));
            color2 = SnakeColor;
        }

        // Body segment with radial gradient
        using var segmentGradient = SKShader.CreateRadialGradient(
            new SKPoint(x - radius * 0.3f, y - radius * 0.3f),
            radius,
            new[] { color1, color2 },
            null,
            SKShaderTileMode.Clamp);

        using var segmentPaint = new SKPaint
        {
            Shader = segmentGradient,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(x, y, radius, segmentPaint);

        // Add shine/highlight
        using var shinePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 60),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(x - radius * 0.3f, y - radius * 0.3f, radius * 0.4f, shinePaint);

        // Subtle outline for definition using darker snake color
        var outlineColor = new SKColor(
            (byte)(SnakeColor.Red * 0.4),
            (byte)(SnakeColor.Green * 0.4),
            (byte)(SnakeColor.Blue * 0.4),
            100);

        using var outlinePaint = new SKPaint
        {
            Color = outlineColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(x, y, radius, outlinePaint);
    }

    private void DrawLeaf(SKCanvas canvas, float x, float y)
    {
        var size = _gridSize * 0.8f;

        // Leaf shape
        using var leafPath = new SKPath();
        leafPath.MoveTo(x, y - size / 2);
        leafPath.QuadTo(x + size / 2, y - size / 4, x + size / 3, y);
        leafPath.QuadTo(x + size / 2, y + size / 4, x, y + size / 2);
        leafPath.QuadTo(x - size / 2, y + size / 4, x - size / 3, y);
        leafPath.QuadTo(x - size / 2, y - size / 4, x, y - size / 2);
        leafPath.Close();

        // Use FoodColor for leaf with gradient
        var lightFoodColor = new SKColor(
            (byte)Math.Min(255, FoodColor.Red * 1.3),
            (byte)Math.Min(255, FoodColor.Green * 1.3),
            (byte)Math.Min(255, FoodColor.Blue * 1.3));

        var darkFoodColor = new SKColor(
            (byte)(FoodColor.Red * 0.7),
            (byte)(FoodColor.Green * 0.7),
            (byte)(FoodColor.Blue * 0.7));

        // Leaf gradient
        using var leafGradient = SKShader.CreateLinearGradient(
            new SKPoint(x - size / 2, y),
            new SKPoint(x + size / 2, y),
            new[] { lightFoodColor, darkFoodColor },
            null,
            SKShaderTileMode.Clamp);

        using var leafPaint = new SKPaint
        {
            Shader = leafGradient,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(leafPath, leafPaint);

        // Leaf vein using darker food color
        var veinColor = new SKColor(
            (byte)(FoodColor.Red * 0.5),
            (byte)(FoodColor.Green * 0.5),
            (byte)(FoodColor.Blue * 0.5),
            150);

        using var veinPaint = new SKPaint
        {
            Color = veinColor,
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawLine(x, y - size / 2, x, y + size / 2, veinPaint);

        // Leaf outline using darker food color
        var outlineColor = new SKColor(
            (byte)(FoodColor.Red * 0.5),
            (byte)(FoodColor.Green * 0.5),
            (byte)(FoodColor.Blue * 0.5));

        using var leafOutline = new SKPaint
        {
            Color = outlineColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawPath(leafPath, leafOutline);
    }

    private void Steer(Direction dir)
    {
        lock (_gameLock)
        {
            _human = true;
            _nextDirection = dir;
        }
    }

    [ExtensionMethod("Go Up", "Turn up — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Up|W", Order = 1)]
    public void GoUp() => Steer(Direction.Up);

    [ExtensionMethod("Go Down", "Turn down — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Down|S", Order = 2)]
    public void GoDown() => Steer(Direction.Down);

    [ExtensionMethod("Go Left", "Turn left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 3)]
    public void GoLeft() => Steer(Direction.Left);

    [ExtensionMethod("Go Right", "Turn right — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 4)]
    public void GoRight() => Steer(Direction.Right);

    #region Parameters

    [ExtensionParameter("Game Speed", "Game speed (higher = faster)",
        DefaultValue = 7, MinValue = 1, MaxValue = 30)]
    public int GameSpeed { get; set; } = 7;

    [ExtensionParameter("Grid Size", "Cell size in pixels (0 = auto-fit to the display)",
        DefaultValue = 0, MinValue = 0, MaxValue = 50)]
    public int GridSize { get; set; }

    [ExtensionParameter("Snake Color", "Color of the snake",
        DefaultValue = "#00FF00")]
    public SKColor SnakeColor { get; set; } = SKColors.LimeGreen;

    [ExtensionParameter("Food Color", "Color of the food",
        DefaultValue = "#FF0000")]
    public SKColor FoodColor { get; set; } = SKColors.Red;

    [ExtensionParameter("Background Color", "Background color (use transparent for layering)",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }

    [ExtensionParameter("Grid Lines", "Show grid lines",
        DefaultValue = true)]
    public bool ShowGrid { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render score text with the crisp bitmap (BDF) font", DefaultValue = false)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "Score height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px")]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI controls the snake",
        DefaultValue = true)]
    public bool AutoPilot { get; set; } = true;

    [ExtensionParameter("Wall Collision", "Snake dies on wall collision",
        DefaultValue = false)]
    public bool WallCollision { get; set; } = false;

    [ExtensionParameter("Auto Restart", "Automatically restart after game over",
        DefaultValue = true)]
    public bool AutoRestart { get; set; } = true;

    #endregion
}

public record struct Point(int X, int Y);

public enum Direction
{
    Up,
    Down,
    Left,
    Right
}