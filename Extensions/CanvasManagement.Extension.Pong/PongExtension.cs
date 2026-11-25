using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.Pong;

[ExtensionInfo("Pong Game",
    "Classic Pong — right paddle AI, left paddle autopilot or arrows in Studio",
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
    private bool _human;
    private bool _upHeld;
    private bool _downHeld;

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
            _human = false;
            _upHeld = false;
            _downHeld = false;
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
                _sound.Play(GameSfx.Bounce, 0.35f, (_ballX / _canvas.Width) * 2 - 1, 0.8f);
                FxWall();
                _ballY = Math.Clamp(_ballY, BALL_SIZE / 2, _canvas.Height - BALL_SIZE / 2);
            }

            // Ball collision with paddles
            // Left paddle
            if (_ballX <= PADDLE_WIDTH + BALL_SIZE / 2 &&
                _ballY >= _paddle1Y && _ballY <= _paddle1Y + PADDLE_HEIGHT)
            {
                _ballVelocityX = Math.Min(Math.Abs(_ballVelocityX) * 1.05f, BallSpeed * 2.2f); // rallies speed up
                _sound.Play(GameSfx.Bounce, 0.5f, -0.7f);
                FxHit(true);
                _ballVelocityY += (_ballY - (_paddle1Y + PADDLE_HEIGHT / 2)) * 0.1f; // Add spin
                _ballX = PADDLE_WIDTH + BALL_SIZE / 2;
            }

            // Right paddle
            if (_ballX >= _canvas.Width - PADDLE_WIDTH - BALL_SIZE / 2 &&
                _ballY >= _paddle2Y && _ballY <= _paddle2Y + PADDLE_HEIGHT)
            {
                _ballVelocityX = -Math.Min(Math.Abs(_ballVelocityX) * 1.05f, BallSpeed * 2.2f);
                _sound.Play(GameSfx.Bounce, 0.5f, 0.7f, 1.2f);
                FxHit(false);
                _ballVelocityY += (_ballY - (_paddle2Y + PADDLE_HEIGHT / 2)) * 0.1f; // Add spin
                _ballX = _canvas.Width - PADDLE_WIDTH - BALL_SIZE / 2;
            }

            // Scoring
            if (_ballX < 0)
            {
                FxPoint(false, 0, _ballY);
                Score2++;
                _sound.Play(GameSfx.Score, 0.6f, 0.7f);
                ResetBall();
                if (MaxScore > 0 && Score2 >= MaxScore)
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        if (IsRunning) InitializeGame();
                    });
            }
            else if (_ballX > _canvas.Width)
            {
                FxPoint(true, _canvas.Width, _ballY);
                Score1++;
                _sound.Play(GameSfx.Score, 0.6f, -0.7f);
                ResetBall();
                if (MaxScore > 0 && Score1 >= MaxScore)
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        if (IsRunning) InitializeGame();
                    });
            }

            // Left paddle: player hold or AI
            if (_human || !AutoPilot)
            {
                var hold = (_downHeld ? 1 : 0) - (_upHeld ? 1 : 0);
                if (hold != 0)
                    _paddle1Y += hold * PaddleSpeed;
            }
            else
            {
                var target1 = AiTarget(true);
                var aiSpeed1 = PaddleSpeed * (AIDifficulty / 10f);
                if (_paddle1Y < target1 - 5)
                    _paddle1Y = Math.Min(_paddle1Y + aiSpeed1, target1);
                else if (_paddle1Y > target1 + 5)
                    _paddle1Y = Math.Max(_paddle1Y - aiSpeed1, target1);
            }

            // AI for paddle 2 (right)
            var target2 = AiTarget(false);
            var aiSpeed2 = PaddleSpeed * (AIDifficulty / 10f);
            if (_paddle2Y < target2 - 5)
                _paddle2Y = Math.Min(_paddle2Y + aiSpeed2, target2);
            else if (_paddle2Y > target2 + 5)
                _paddle2Y = Math.Max(_paddle2Y - aiSpeed2, target2);

            // Clamp paddles
            _paddle1Y = Math.Clamp(_paddle1Y, 0, _canvas.Height - PADDLE_HEIGHT);
            _paddle2Y = Math.Clamp(_paddle2Y, 0, _canvas.Height - PADDLE_HEIGHT);

            Render();
            _sound.Update();

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

    private float _aim1, _aim2;
    private int _lastDir;

    /// <summary>
    ///     Where an AI paddle heads: the predicted crossing point (with wall bounces) plus a per-rally aiming error,
    ///     larger at low difficulty, so rallies end and the score moves. Idle paddles drift back to the middle.
    /// </summary>
    private float AiTarget(bool left)
    {
        var dir = Math.Sign(_ballVelocityX);
        if (dir != _lastDir)
        {
            _lastDir = dir;
            // Usually aim somewhere on the paddle (that is what puts spin on the ball); now and then misjudge
            // it completely. The miss rate falls with difficulty: about 1 in 3 at 1, 1 in 8 at 10.
            var miss = _random.NextDouble() < 0.36 - Math.Clamp(AIDifficulty, 1, 10) * 0.024;
            var side = _random.Next(2) == 0 ? -1 : 1;
            var roll = miss
                ? side * PADDLE_HEIGHT * (0.75f + (float)_random.NextDouble() * 0.4f)
                : (float)(_random.NextDouble() * 2 - 1) * PADDLE_HEIGHT * 0.38f;
            if (dir < 0) _aim1 = roll;
            else _aim2 = roll;
        }

        // React only once the ball crosses into this half, like a person would.
        var coming = left ? _ballVelocityX < 0 && _ballX < _canvas.Width * 0.6f
                          : _ballVelocityX > 0 && _ballX > _canvas.Width * 0.4f;
        if (!coming) return _canvas.Height / 2f - PADDLE_HEIGHT / 2;

        var px = left ? PADDLE_WIDTH : _canvas.Width - PADDLE_WIDTH;
        var t = Math.Abs((px - _ballX) / Math.Max(0.01f, Math.Abs(_ballVelocityX)));
        var y = _ballY + _ballVelocityY * t;
        var top = BALL_SIZE / 2f;
        var span = Math.Max(1, _canvas.Height - BALL_SIZE);
        // Unfold the bounces.
        var u = (y - top) % (2 * span);
        if (u < 0) u += 2 * span;
        y = top + (u > span ? 2 * span - u : u);
        return y + (left ? _aim1 : _aim2) - PADDLE_HEIGHT / 2;
    }

    private readonly GameFx _fx = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Queue<(float X, float Y)> _trail = new();
    private double _lastRender;
    private float _pulse1, _pulse2;

    private SKColor LeftColor => PaddleColor == SKColors.White ? new SKColor(0, 230, 255) : PaddleColor;
    private SKColor RightColor => PaddleColor == SKColors.White ? new SKColor(255, 60, 200) : PaddleColor;

    /// <summary>Neon arcade: glowing paddles in two colours, a ball with a comet tail, sparks on every hit,
    /// a flash and shake when a point falls, and big scores that pulse when they change.</summary>
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
                _pulse1 = Math.Max(0, _pulse1 - dt * 2);
                _pulse2 = Math.Max(0, _pulse2 - dt * 2);

                var w = _canvas.Width;
                var h = _canvas.Height;
                using var canvas = new SKCanvas(_backBuffer);
                using var paint = new SKPaint { IsAntialias = false };

                if (BackgroundColor.Alpha == 0) canvas.Clear(SKColors.Transparent);
                else if (BackgroundColor == SKColors.Black)
                    GameFx.VerticalGradient(canvas, w, h, new SKColor(14, 6, 34), new SKColor(4, 2, 12));
                else canvas.Clear(BackgroundColor);

                // Court: frame lines and a glowing dashed centre line.
                paint.Color = new SKColor(60, 40, 120);
                canvas.DrawRect(0, 0, w, 1, paint);
                canvas.DrawRect(0, h - 1, w, 1, paint);
                if (ShowCenterLine)
                {
                    var dash = Math.Max(2, h / 24);
                    paint.Color = new SKColor(90, 70, 170);
                    for (var y = 0; y < h; y += dash * 2)
                        canvas.DrawRect(w / 2, y, 1, dash, paint);
                    GameFx.Glow(canvas, w / 2, 0, 1, h, new SKColor(80, 60, 200), 2, 0.25f);
                }

                // Scores behind the play.
                var scoreSize = CanvasText.ResolveSize(FontSize, Math.Max(10f, h * 0.2f));
                ScoreText(canvas, Score1.ToString(), w / 4f, h * 0.3f, scoreSize, LeftColor, _pulse1);
                ScoreText(canvas, Score2.ToString(), w * 3 / 4f, h * 0.3f, scoreSize, RightColor, _pulse2);

                _fx.BeginShake(canvas);

                Paddle(canvas, paint, _margin, _paddle1Y, LeftColor);
                Paddle(canvas, paint, w - _margin - PADDLE_WIDTH, _paddle2Y, RightColor);

                // Ball with a comet tail in the colour of the last paddle that hit it.
                _trail.Enqueue((_ballX, _ballY));
                while (_trail.Count > 10) _trail.Dequeue();
                var k = 0;
                var ballColor = BallColor == SKColors.White ? new SKColor(255, 250, 220) : BallColor;
                var tail = _ballVelocityX > 0 ? LeftColor : RightColor;
                foreach (var (tx, ty) in _trail)
                {
                    var a = ++k / (float)_trail.Count;
                    paint.Color = GameFx.Lerp(tail, ballColor, a).WithAlpha((byte)(200 * a * a));
                    var r = Math.Max(1, BALL_SIZE / 2 * a);
                    canvas.DrawRect(MathF.Round(tx - r), MathF.Round(ty - r), r * 2, r * 2, paint);
                }
                GameFx.GlowDisc(canvas, _ballX, _ballY, BALL_SIZE * 1.4f, ballColor, 0.45f);
                paint.Color = ballColor;
                PixelArt.Disc(canvas, paint, (int)MathF.Round(_ballX), (int)MathF.Round(_ballY), Math.Max(1, (int)(BALL_SIZE / 2)));

                _fx.DrawParticles(canvas);
                _fx.EndShake(canvas);

                if (MaxScore > 0 && (Score1 >= MaxScore || Score2 >= MaxScore))
                {
                    var winner = Score1 >= MaxScore ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
                    var col = Score1 >= MaxScore ? LeftColor : RightColor;
                    var size = CanvasText.ResolveSize(FontSize, Math.Max(9f, h * 0.12f));
                    CanvasText.Draw(canvas, _canvas, winner, new SKColor(0, 0, 0, 200), w / 2f + 1, h / 2f + 1, size, SKTextAlign.Center, UseBdfFont);
                    CanvasText.Draw(canvas, _canvas, winner, col, w / 2f, h / 2f, size, SKTextAlign.Center, UseBdfFont);
                    if ((int)(_fx.Time * 6) % 3 == 0) _fx.Confetti(_fx.Rand(0, w), 0, 3, 1.2f);
                }

                _fx.DrawOverlay(canvas, _canvas, w, h);
                canvas.Flush();
                _canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render error: {ex.Message}");
            }
        }
    }

    private void ScoreText(SKCanvas c, string text, float x, float y, float size, SKColor col, float pulse)
    {
        var color = GameFx.Lerp(GameFx.Shade(col, 0.55f), SKColors.White, pulse);
        CanvasText.Draw(c, _canvas, text, color, x, y, size, SKTextAlign.Center, UseBdfFont);
    }

    private void Paddle(SKCanvas c, SKPaint paint, float x, float y, SKColor col)
    {
        GameFx.Glow(c, x, y, PADDLE_WIDTH, PADDLE_HEIGHT, col, 4, 0.5f);
        paint.Color = col;
        c.DrawRect(MathF.Round(x), MathF.Round(y), PADDLE_WIDTH, PADDLE_HEIGHT, paint);
        paint.Color = GameFx.Lerp(col, SKColors.White, 0.6f);
        c.DrawRect(MathF.Round(x), MathF.Round(y), 1, PADDLE_HEIGHT, paint);
        paint.Color = GameFx.Shade(col, 0.6f);
        c.DrawRect(MathF.Round(x + PADDLE_WIDTH - 1), MathF.Round(y), 1, PADDLE_HEIGHT, paint);
    }

    private void FxHit(bool left)
    {
        var col = left ? LeftColor : RightColor;
        _fx.Spray(_ballX, _ballY, left ? 0 : MathF.PI, 0.9f, col, 12, 2.4f, 0.4f, 0, true);
    }

    private void FxWall() =>
        _fx.Spray(_ballX, _ballY, _ballY < _canvas.Height / 2f ? MathF.PI / 2 : -MathF.PI / 2, 1f,
            new SKColor(160, 140, 255), 6, 1.5f, 0.3f, 0, true);

    private void FxPoint(bool leftScored, float x, float y)
    {
        var col = leftScored ? LeftColor : RightColor;
        _fx.Burst(x, y, col, 30, 3.5f, 0.9f, 0.02f);
        _fx.Flash(col, 0.45f);
        _fx.Shake(4);
        if (leftScored) _pulse1 = 1;
        else _pulse2 = 1;
        _trail.Clear();
    }

    #region Parameters

    private GameSound? _snd;
    private GameSound _sound => _snd ??= new GameSound(_canvas, GameSong.Retro);

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

    [ExtensionParameter("Use BDF Font", "Render scores with the crisp bitmap (BDF) font", DefaultValue = true)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "Score height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 64, Unit = "px")]
    public int FontSize { get; set; }

    [ExtensionParameter("Trail Effect", "Show ball trail effect",
        DefaultValue = false)]
    public bool TrailEffect { get; set; } = false;

    [ExtensionParameter("Max Score", "Score to win (0 = no limit)",
        DefaultValue = 0, MinValue = 0, MaxValue = 99)]
    public int MaxScore { get; set; } = 0;

    [ExtensionParameter("Auto Pilot", "AI plays left paddle until you press a key in Studio", DefaultValue = true)]
    public bool AutoPilot { get; set; } = true;

    [ExtensionMethod("Paddle Up", "Move left paddle up — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Up|W", Order = 1)]
    public void PaddleUp()
    {
        lock (_gameLock) { _human = true; _upHeld = true; }
    }

    [ExtensionMethod("Paddle Down", "Move left paddle down — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Down|S", Order = 2)]
    public void PaddleDown()
    {
        lock (_gameLock) { _human = true; _downHeld = true; }
    }

    [ExtensionMethod("Release Up", "Release up",
        Category = "Controls", KeyboardShortcut = "Up:up|W:up", Order = 3)]
    public void ReleaseUp()
    {
        lock (_gameLock) _upHeld = false;
    }

    [ExtensionMethod("Release Down", "Release down",
        Category = "Controls", KeyboardShortcut = "Down:up|S:up", Order = 4)]
    public void ReleaseDown()
    {
        lock (_gameLock) _downHeld = false;
    }

    #endregion
}