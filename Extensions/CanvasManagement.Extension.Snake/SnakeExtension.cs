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
        _sound.Stop();

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
        _shownScore = 0;
        _gameOver = false;
        _wasOver = false;

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
                    FxCrash();
                    _sound.Play(GameSfx.Hit, 0.7f);
                    _sound.Play(GameSfx.GameOver, 0.6f);
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
                        FxEat(_food);
                        _sound.Play(GameSfx.Eat, 0.6f, 0, 1f + Math.Min(_snake.Count, 40) * 0.01f);
                        PlaceFood();
                    }
                    else
                    {
                        _snake.RemoveAt(_snake.Count - 1);
                    }
                }
            }

            Render();
            _sound.Duck(_gameOver ? 0.35f : 1f);
            _sound.Update();

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

    private readonly GameFx _fx = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastRender;
    private bool _wasOver;
    private int _best;
    private int _shownScore;

    /// <summary>
    ///     Neon look: a dark glassy board with a faint dot grid, a continuous body whose colour runs from the
    ///     head down the tail, a head with eyes that look where it goes, a glowing, pulsing fruit, sparks and
    ///     a pop-up when it eats, and a shaking burst of the whole body when it crashes.
    /// </summary>
    private void Render()
    {
        if (_backBuffer == null) return;

        lock (_gameLock)
        {
            try
            {
                var now = _clock.Elapsed.TotalSeconds;
                var dt = (float)Math.Clamp(now - _lastRender, 0.001, 0.1);
                _lastRender = now;
                _fx.Update(dt);

                var w = _canvas.Width;
                var h = _canvas.Height;
                var g = _gridSize;
                var cols = w / g;
                var rows = h / g;
                var ox = (w - cols * g) / 2;
                var oy = (h - rows * g) / 2;
                using var canvas = new SKCanvas(_backBuffer);
                using var paint = new SKPaint { IsAntialias = false };

                if (_backgroundColor.Alpha == 0) canvas.Clear(SKColors.Transparent);
                else
                {
                    var bg = _backgroundColor == SKColors.Black ? new SKColor(8, 12, 22) : _backgroundColor;
                    GameFx.VerticalGradient(canvas, w, h, GameFx.Shade(bg, 1.6f), GameFx.Shade(bg, 0.7f));
                }

                // Board: faint dots at cell corners and a soft frame.
                if (ShowGrid)
                {
                    paint.Color = new SKColor(40, 60, 90);
                    for (var x = 0; x <= cols; x++)
                    for (var y = 0; y <= rows; y++)
                        canvas.DrawRect(ox + x * g, oy + y * g, 1, 1, paint);
                }

                _fx.BeginShake(canvas);

                // Fruit: pulsing glow, a round body with a shine and a leaf.
                var fx = ox + _food.X * g + g / 2f;
                var fy = oy + _food.Y * g + g / 2f;
                var pulse = 0.5f + 0.5f * MathF.Sin(_fx.Time * 5);
                GameFx.GlowDisc(canvas, fx, fy, g * (1.1f + 0.3f * pulse), FoodColor, 0.35f + 0.25f * pulse);
                paint.Color = FoodColor;
                PixelArt.Disc(canvas, paint, (int)fx, (int)fy, Math.Max(1, g / 2 - 2));
                paint.Color = GameFx.Lerp(FoodColor, SKColors.White, 0.6f);
                canvas.DrawRect((int)fx - g / 5, (int)fy - g / 5, Math.Max(1, g / 6), Math.Max(1, g / 6), paint);
                paint.Color = new SKColor(80, 200, 70);
                canvas.DrawRect((int)fx, (int)fy - g / 2 + 1, Math.Max(1, g / 5), Math.Max(1, g / 5), paint);

                if (!(_gameOver && _wasOver && _fx.Time - _overAt > 0.15f))
                    DrawSnake(canvas, paint, ox, oy, g);

                _fx.DrawParticles(canvas);
                _fx.EndShake(canvas);

                // HUD
                _best = Math.Max(_best, Score);
                if (_shownScore < Score) _shownScore = Math.Min(Score, _shownScore + 2);
                if (_shownScore > Score) _shownScore = Score;
                var size = CanvasText.ResolveSize(FontSize, Math.Max(7f, h * 0.075f));
                Text(canvas, $"{_shownScore}", 3, size + 1, size, SKColors.White, SKTextAlign.Left);
                Text(canvas, $"HI {_best}", w - 3, size + 1, size, new SKColor(255, 200, 80), SKTextAlign.Right);

                if (_gameOver)
                {
                    var overSize = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.12f));
                    var blink = (int)(_fx.Time * 3) % 2 == 0;
                    Text(canvas, "GAME OVER", w / 2f, h / 2f, overSize, blink ? new SKColor(255, 70, 70) : new SKColor(255, 160, 60),
                        SKTextAlign.Center);
                }

                _fx.DrawOverlay(canvas, _canvas, w, h, Math.Max(7f, h * 0.07f));
                _canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render error: {ex.Message}");
            }
        }
    }

    private float _overAt;

    private void Text(SKCanvas c, string s, float x, float y, float size, SKColor color, SKTextAlign align)
    {
        CanvasText.Draw(c, _canvas, s, new SKColor(0, 0, 0, 180), x + 1, y + 1, size, align, UseBdfFont);
        CanvasText.Draw(c, _canvas, s, color, x, y, size, align, UseBdfFont);
    }

    private void DrawSnake(SKCanvas canvas, SKPaint paint, int ox, int oy, int g)
    {
        var n = _snake.Count;
        var inset = Math.Max(1, g / 6);
        for (var i = n - 1; i >= 0; i--)
        {
            var s = _snake[i];
            var t = n <= 1 ? 0 : i / (float)(n - 1);
            // Colour runs from the head colour to a deeper, hue-shifted tail; a slow shimmer travels down.
            var shimmer = 0.85f + 0.15f * MathF.Sin(_fx.Time * 6 - i * 0.6f);
            var body = GameFx.Shade(GameFx.Lerp(SnakeColor, Hue(SnakeColor, 60), t), shimmer * (1 - 0.35f * t));
            var x = ox + s.X * g;
            var y = oy + s.Y * g;
            // Same thickness all along; only the last three segments taper to a tail.
            var thin = inset + Math.Max(0, i - (n - 4)) * Math.Max(1, g / 10);
            paint.Color = body;
            canvas.DrawRect(x + thin, y + thin, g - 2 * thin, g - 2 * thin, paint);

            // Bridge to the next segment so the body is one piece (not across a wrap).
            if (i > 0)
            {
                var p = _snake[i - 1];
                var dx = p.X - s.X;
                var dy = p.Y - s.Y;
                var pthin = inset + Math.Max(0, i - 1 - (n - 4)) * Math.Max(1, g / 10);
                if (Math.Abs(dx) + Math.Abs(dy) == 1)
                {
                    thin = Math.Max(thin, pthin);
                    if (dx != 0)
                        canvas.DrawRect(dx > 0 ? x + g - thin : x - thin, y + thin, 2 * thin, g - 2 * thin, paint);
                    else
                        canvas.DrawRect(x + thin, dy > 0 ? y + g - thin : y - thin, g - 2 * thin, 2 * thin, paint);
                }
            }

            // Scales: a lighter dot on every other segment.
            if (i % 2 == 1 && g >= 6)
            {
                paint.Color = GameFx.Shade(body, 1.35f);
                canvas.DrawRect(x + g / 2 - 1, y + g / 2 - 1, Math.Max(1, g / 5), Math.Max(1, g / 5), paint);
            }
        }

        if (n == 0) return;
        var head = _snake[0];
        var hx = ox + head.X * g;
        var hy = oy + head.Y * g;
        GameFx.Glow(canvas, hx + inset, hy + inset, g - 2 * inset, g - 2 * inset, SnakeColor, 3, 0.35f);
        paint.Color = GameFx.Lerp(SnakeColor, SKColors.White, 0.25f);
        canvas.DrawRect(hx + inset - 1, hy + inset - 1, g - 2 * inset + 2, g - 2 * inset + 2, paint);

        // Eyes look where it is going; a forked tongue flicks now and then.
        var (ex, ey) = _direction switch
        {
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            Direction.Up => (0, -1),
            _ => (0, 1)
        };
        var c = g / 2f;
        var side = g * 0.22f;
        var eye = Math.Max(1, g / 5);
        foreach (var sgn in new[] { -1, 1 })
        {
            var px = hx + c + ex * g * 0.15f + (ey != 0 ? sgn * side : 0) - eye / 2f;
            var py = hy + c + ey * g * 0.15f + (ex != 0 ? sgn * side : 0) - eye / 2f;
            paint.Color = SKColors.White;
            canvas.DrawRect(MathF.Round(px), MathF.Round(py), eye, eye, paint);
            paint.Color = new SKColor(10, 10, 20);
            canvas.DrawRect(MathF.Round(px + ex * Math.Max(0, eye - 1) * 0.5f), MathF.Round(py + ey * Math.Max(0, eye - 1) * 0.5f),
                Math.Max(1, eye - 1), Math.Max(1, eye - 1), paint);
        }
        if ((int)(_fx.Time * 4) % 5 == 0)
        {
            paint.Color = new SKColor(255, 60, 90);
            var tx = hx + c + ex * (g * 0.5f + 1);
            var ty = hy + c + ey * (g * 0.5f + 1);
            canvas.DrawRect(MathF.Round(tx), MathF.Round(ty), 1, 1, paint);
            canvas.DrawRect(MathF.Round(tx + ex + ey), MathF.Round(ty + ey + ex), 1, 1, paint);
            canvas.DrawRect(MathF.Round(tx + ex - ey), MathF.Round(ty + ey - ex), 1, 1, paint);
        }
    }

    private static SKColor Hue(SKColor c, float degrees)
    {
        c.ToHsv(out var h, out var s, out var v);
        return SKColor.FromHsv((h + degrees) % 360, s, v);
    }

    /// <summary>Food eaten: sparks in the fruit colour and a "+10" rising from where it was.</summary>
    private void FxEat(Point food)
    {
        var g = _gridSize;
        var ox = (_canvas.Width - _canvas.Width / g * g) / 2;
        var oy = (_canvas.Height - _canvas.Height / g * g) / 2;
        var x = ox + food.X * g + g / 2f;
        var y = oy + food.Y * g + g / 2f;
        _fx.Burst(x, y, FoodColor, 16, 2f, 0.6f, 0.02f);
        _fx.Burst(x, y, SnakeColor, 6, 1.2f, 0.5f, 0.02f);
        _fx.Pop("+10", x, y - g * 0.5f, new SKColor(255, 240, 120));
    }

    /// <summary>Crash: every segment bursts, the board flashes red and shakes.</summary>
    private void FxCrash()
    {
        var g = _gridSize;
        var ox = (_canvas.Width - _canvas.Width / g * g) / 2;
        var oy = (_canvas.Height - _canvas.Height / g * g) / 2;
        for (var i = 0; i < _snake.Count; i++)
        {
            var s = _snake[i];
            _fx.Burst(ox + s.X * g + g / 2f, oy + s.Y * g + g / 2f, GameFx.Lerp(SnakeColor, Hue(SnakeColor, 60), i / (float)Math.Max(1, _snake.Count)),
                4, 2.5f, 1.0f, 0.06f, true);
        }
        _fx.Flash(new SKColor(255, 40, 40), 0.7f);
        _fx.Shake(5);
        _wasOver = true;
        _overAt = _fx.Time;
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

    private GameSound? _snd;
    private GameSound _sound => _snd ??= new GameSound(_canvas, GameSong.Chill);

    [ExtensionParameter("Sound", "Sound effects on the audio board", DefaultValue = true, Order = 20)]
    public bool Sound { get => _sound.Effects; set => _sound.Effects = value; }

    [ExtensionParameter("Music", "Background music", DefaultValue = true, Order = 21)]
    public bool Music { get => _sound.Music; set => _sound.Music = value; }

    [ExtensionParameter("Volume", "How loud this game is on the audio board", DefaultValue = 70, MinValue = 0,
        MaxValue = 100, Order = 22)]
    public int Volume { get => (int)MathF.Round(_sound.Volume * 100); set => _sound.Volume = Math.Clamp(value, 0, 100) / 100f; }

    [ExtensionParameter("Music Volume", "Music level relative to the effects", DefaultValue = 50, MinValue = 0,
        MaxValue = 100, Order = 23)]
    public int MusicVolume
    {
        get => (int)MathF.Round(_sound.MusicVolume * 100);
        set => _sound.MusicVolume = Math.Clamp(value, 0, 100) / 100f;
    }

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

    [ExtensionParameter("Use BDF Font", "Render score text with the crisp bitmap (BDF) font", DefaultValue = true)]
    public bool UseBdfFont { get; set; } = true;

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