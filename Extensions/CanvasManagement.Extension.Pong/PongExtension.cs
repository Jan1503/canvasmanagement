using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.Pong;

[ExtensionInfo("Pong Game",
    "Classic Pong game with AI opponents",
    "Games",
    IconResourceName = "pong.svg")]
public class PongExtension : IDisposable
{
    // Sizes are scaled from the 384x192 design to the actual panel in the constructor.
    private readonly float PADDLE_WIDTH;
    private readonly float PADDLE_HEIGHT;
    private readonly float BALL_SIZE;
    private readonly int _margin;
    private readonly ICanvas _canvas;
    private readonly object _gameLock = new();

    // Double buffering to prevent flicker
    private SKBitmap? _backBuffer;
    private float _ballVelocityX, _ballVelocityY;

    // Game state
    private float _ballX, _ballY;
    private bool _disposed;
    private Timer? _gameTimer;
    private float _paddle1Y, _paddle2Y;
    private readonly Random _random = new();

    internal PongExtension(ICanvas canvas)
    {
        _canvas = canvas;

        // Scale paddle/ball/margin from the 384x192 design to the real panel size.
        var s = DisplayScale.GetScale(canvas.Width, canvas.Height);
        PADDLE_WIDTH = Math.Max(2, 10 * s);
        PADDLE_HEIGHT = Math.Max(6, 80 * s);
        BALL_SIZE = Math.Max(2, 10 * s);
        _margin = Math.Max(1, (int)Math.Round(5 * s));
    }

    public bool IsRunning { get; private set; }
    public int Score1 { get; private set; }

    public int Score2 { get; private set; }

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
            InitializeGame();

            // Create back buffer
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

            _gameTimer = new Timer(16.67 / GameSpeed); // ~60 FPS
            _gameTimer.Elapsed += OnGameTick;
            _gameTimer.AutoReset = true;
            _gameTimer.Start();

            IsRunning = true;
            Console.WriteLine($"Pong started - Speed: {GameSpeed}x, AI: {AIDifficulty}");
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
        Console.WriteLine($"Pong stopped - Score: {Score1}:{Score2}");
    }

    private void InitializeGame()
    {
        _ballX = _canvas.Width / 2f;
        _ballY = _canvas.Height / 2f;
        _ballVelocityX = BallSpeed * (_random.Next(2) == 0 ? 1 : -1);
        _ballVelocityY = BallSpeed * 0.5f * (_random.Next(2) == 0 ? 1 : -1);

        _paddle1Y = _canvas.Height / 2f - PADDLE_HEIGHT / 2;
        _paddle2Y = _canvas.Height / 2f - PADDLE_HEIGHT / 2;

        Score1 = 0;
        Score2 = 0;
    }

    private void ResetBall()
    {
        _ballX = _canvas.Width / 2f;
        _ballY = _canvas.Height / 2f;
        _ballVelocityX = BallSpeed * (_random.Next(2) == 0 ? 1 : -1);
        _ballVelocityY = BallSpeed * 0.5f * (float)(_random.NextDouble() - 0.5) * 2;
    }

    private void OnGameTick(object? sender, ElapsedEventArgs e)
    {
        if (!IsRunning) return;

        try
        {
            _gameTimer?.Stop();

            // Update speed if changed
            if (_gameTimer != null && Math.Abs(_gameTimer.Interval - 16.67 / GameSpeed) > 0.1)
                _gameTimer.Interval = 16.67 / GameSpeed;

            // Update ball position
            _ballX += _ballVelocityX;
            _ballY += _ballVelocityY;

            // Ball collision with top/bottom
            if (_ballY <= BALL_SIZE / 2 || _ballY >= _canvas.Height - BALL_SIZE / 2)
            {
                _ballVelocityY *= -1;
                _ballY = Math.Clamp(_ballY, BALL_SIZE / 2, _canvas.Height - BALL_SIZE / 2);
            }

            // Ball collision with paddles
            // Left paddle
            if (_ballX <= PADDLE_WIDTH + BALL_SIZE / 2 &&
                _ballY >= _paddle1Y && _ballY <= _paddle1Y + PADDLE_HEIGHT)
            {
                _ballVelocityX = Math.Abs(_ballVelocityX);
                _ballVelocityY += (_ballY - (_paddle1Y + PADDLE_HEIGHT / 2)) * 0.1f; // Add spin
                _ballX = PADDLE_WIDTH + BALL_SIZE / 2;
            }

            // Right paddle
            if (_ballX >= _canvas.Width - PADDLE_WIDTH - BALL_SIZE / 2 &&
                _ballY >= _paddle2Y && _ballY <= _paddle2Y + PADDLE_HEIGHT)
            {
                _ballVelocityX = -Math.Abs(_ballVelocityX);
                _ballVelocityY += (_ballY - (_paddle2Y + PADDLE_HEIGHT / 2)) * 0.1f; // Add spin
                _ballX = _canvas.Width - PADDLE_WIDTH - BALL_SIZE / 2;
            }

            // Scoring
            if (_ballX < 0)
            {
                Score2++;
                ResetBall();
                if (MaxScore > 0 && Score2 >= MaxScore)
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        if (IsRunning) InitializeGame();
                    });
            }
            else if (_ballX > _canvas.Width)
            {
                Score1++;
                ResetBall();
                if (MaxScore > 0 && Score1 >= MaxScore)
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        if (IsRunning) InitializeGame();
                    });
            }

            // AI for paddle 1 (left)
            var target1 = _ballY - PADDLE_HEIGHT / 2;
            var aiSpeed1 = PaddleSpeed * (AIDifficulty / 10f);
            if (_paddle1Y < target1 - 5)
                _paddle1Y = Math.Min(_paddle1Y + aiSpeed1, target1);
            else if (_paddle1Y > target1 + 5)
                _paddle1Y = Math.Max(_paddle1Y - aiSpeed1, target1);

            // AI for paddle 2 (right)
            var target2 = _ballY - PADDLE_HEIGHT / 2;
            var aiSpeed2 = PaddleSpeed * (AIDifficulty / 10f);
            if (_paddle2Y < target2 - 5)
                _paddle2Y = Math.Min(_paddle2Y + aiSpeed2, target2);
            else if (_paddle2Y > target2 + 5)
                _paddle2Y = Math.Max(_paddle2Y - aiSpeed2, target2);

            // Clamp paddles
            _paddle1Y = Math.Clamp(_paddle1Y, 0, _canvas.Height - PADDLE_HEIGHT);
            _paddle2Y = Math.Clamp(_paddle2Y, 0, _canvas.Height - PADDLE_HEIGHT);

            Render();

            if (IsRunning && _gameTimer != null) _gameTimer.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Pong tick error: {ex.Message}");
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

    private void Render()
    {
        if (_backBuffer == null) return;

        lock (_gameLock)
        {
            try
            {
                // Render to back buffer
                using var canvas = new SKCanvas(_backBuffer);

                // Clear with trail effect or solid background
                if (TrailEffect)
                {
                    using var overlayPaint = new SKPaint
                    {
                        Color = new SKColor(
                            BackgroundColor.Red,
                            BackgroundColor.Green,
                            BackgroundColor.Blue,
                            50), // Semi-transparent
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, overlayPaint);
                }
                else
                {
                    canvas.Clear(BackgroundColor);
                }

                // Draw center line
                if (ShowCenterLine)
                {
                    using var linePaint = new SKPaint
                    {
                        Color = new SKColor(100, 100, 100),
                        StrokeWidth = Math.Max(1, _canvas.ScaleSize(2)),
                        IsAntialias = false
                    };

                    var dashStep = _canvas.ScaleSize(20);
                    var dashLen = _canvas.ScaleSize(10);
                    for (var y = 0; y < _canvas.Height; y += dashStep)
                        canvas.DrawLine(_canvas.Width / 2, y, _canvas.Width / 2, y + dashLen, linePaint);
                }

                // Draw paddles
                using var paddlePaint = new SKPaint
                {
                    Color = PaddleColor,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = false
                };

                canvas.DrawRect(_margin, _paddle1Y, PADDLE_WIDTH, PADDLE_HEIGHT, paddlePaint);
                canvas.DrawRect(_canvas.Width - _margin - PADDLE_WIDTH, _paddle2Y, PADDLE_WIDTH, PADDLE_HEIGHT,
                    paddlePaint);

                // Draw ball
                using var ballPaint = new SKPaint
                {
                    Color = BallColor,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawCircle(_ballX, _ballY, BALL_SIZE / 2, ballPaint);

                // Draw scores
                using var scoreFont = new SKFont
                {
                    Size = _canvas.ScaleSizeF(32),
                    Typeface = SKTypeface.FromFamilyName("Arial")
                };
                using var scorePaint = new SKPaint
                {
                    Color = SKColors.White,
                    IsAntialias = true
                };
                var scoreY = _canvas.ScaleSize(40);
                canvas.DrawText(Score1.ToString(), _canvas.Width / 4, scoreY, SKTextAlign.Left, scoreFont, scorePaint);
                canvas.DrawText(Score2.ToString(), _canvas.Width * 3 / 4, scoreY, SKTextAlign.Left, scoreFont,
                    scorePaint);

                // Draw winner message
                if (MaxScore > 0 && (Score1 >= MaxScore || Score2 >= MaxScore))
                {
                    var winner = Score1 >= MaxScore ? "Player 1 Wins!" : "Player 2 Wins!";
                    using var winFont = new SKFont
                    {
                        Size = _canvas.ScaleSizeF(24),
                        Typeface = SKTypeface.FromFamilyName("Arial")
                    };
                    using var winPaint = new SKPaint
                    {
                        Color = SKColors.Yellow,
                        IsAntialias = true
                    };
                    canvas.DrawText(winner, _canvas.Width / 2, _canvas.Height / 2, SKTextAlign.Center, winFont,
                        winPaint);
                }

                canvas.Flush();// Blit to canvas in one operation
                _canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render error: {ex.Message}");
            }
        }
    }

    #region Parameters

    [ExtensionParameter("Game Speed", "Game speed multiplier",
        DefaultValue = 1.0, MinValue = 0.5, MaxValue = 3.0)]
    public double GameSpeed { get; set; } = 1.0;

    [ExtensionParameter("Ball Speed", "Ball movement speed",
        DefaultValue = 3, MinValue = 1, MaxValue = 15)]
    public int BallSpeed { get; set; } = 3;

    [ExtensionParameter("Paddle Speed", "Paddle movement speed",
        DefaultValue = 6, MinValue = 2, MaxValue = 20)]
    public int PaddleSpeed { get; set; } = 6;

    [ExtensionParameter("AI Difficulty", "AI reaction speed (1-10)",
        DefaultValue = 7, MinValue = 1, MaxValue = 10)]
    public int AIDifficulty { get; set; } = 7;

    [ExtensionParameter("Ball Color", "Color of the ball",
        DefaultValue = "#FFFFFF")]
    public SKColor BallColor { get; set; } = SKColors.White;

    [ExtensionParameter("Paddle Color", "Color of the paddles",
        DefaultValue = "#FFFFFF")]
    public SKColor PaddleColor { get; set; } = SKColors.White;

    [ExtensionParameter("Background Color", "Background color for the game",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Show Center Line", "Show dashed center line",
        DefaultValue = true)]
    public bool ShowCenterLine { get; set; } = true;

    [ExtensionParameter("Trail Effect", "Show ball trail effect",
        DefaultValue = false)]
    public bool TrailEffect { get; set; } = false;

    [ExtensionParameter("Max Score", "Score to win (0 = no limit)",
        DefaultValue = 0, MinValue = 0, MaxValue = 99)]
    public int MaxScore { get; set; } = 0;

    #endregion
}