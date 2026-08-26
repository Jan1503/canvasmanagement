using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.SpaceInvaders;

/// <summary>
///     Self-playing Space Invaders: a marching invader formation, eroding shields, a bonus UFO and an AI
///     cannon that auto-aims and dodges incoming bombs. Scales to any panel. The input is funnelled through
///     a tiny action struct so a real controller can drive it later instead of the AI.
/// </summary>
[ExtensionInfo("Space Invaders",
    "Space Invaders — autopilot, or play with arrows / Space in Studio",
    "Games",
    IconResourceName = "space-invaders.svg")]
public class SpaceInvadersExtension : ICanvasExtension, IDisposable
{
    private static readonly string[] InvaderA =
    {
        "00100000100",
        "00010001000",
        "00111111100",
        "01101110110",
        "11111111111",
        "10111111101",
        "10100000101",
        "00011011000"
    };

    private static readonly string[] InvaderB =
    {
        "00100000100",
        "10010001001",
        "10111111101",
        "11101110111",
        "11111111111",
        "01111111110",
        "00100000100",
        "01000000010"
    };

    private static readonly string[] Cannon =
    {
        "00000100000",
        "00001110000",
        "01111111110",
        "11111111111"
    };

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Bomb> _bombs = new();
    private readonly List<Shield> _shields = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _frame;

    // Layout (pixels)
    private int _px;          // sprite block size
    private int _invW, _invH; // invader sprite size
    private int _cols, _rows;
    private int _cellW, _cellH;
    private float _formX, _formY; // top-left of the formation grid
    private int _dir = 1;
    private int _stepTimer;

    private bool[,] _alive = new bool[1, 1];
    private bool _frameB;

    private float _shipX;
    private int _shipY;
    private int _shipW, _shipH;
    private float _shipSpeed;

    private bool _bulletActive;
    private float _bulletX, _bulletY;
    private float _bulletSpeed;
    private float _bombSpeed;

    private Ufo? _ufo;
    private int _ufoTimer;

    private int _gameOverTimer;
    private int _respawnTimer;
    private bool _human;
    private bool _leftHeld;
    private bool _rightHeld;

    internal SpaceInvadersExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 33,
        MinValue = 16, MaxValue = 100, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 33;

    [ExtensionParameter("Difficulty", "Bomb frequency & speed", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Background Color", "Background colour", DefaultValue = "#000000", Order = 3)]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    [ExtensionParameter("Show HUD", "Show score / lives / wave", DefaultValue = true, Order = 4)]
    public bool ShowHud { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD text with the crisp bitmap (BDF) font", DefaultValue = false,
        Order = 5)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "HUD text height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 48, Unit = "px", Order = 6)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI shoots until you press a key in Studio", DefaultValue = true, Order = 7)]
    public bool AutoPilot { get; set; } = true;

    [ExtensionParameter("Score", "Current score", ReadOnly = true, Order = 10)]
    public int Score { get; private set; }

    [ExtensionParameter("Lives", "Remaining lives", ReadOnly = true, Order = 11)]
    public int Lives { get; private set; }

    [ExtensionParameter("Wave", "Current wave", ReadOnly = true, Order = 12)]
    public int Wave { get; private set; }

    public string Name => "Space Invaders";
    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _human = false;
            _leftHeld = false;
            _rightHeld = false;
            Layout();
            NewGame();

            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

            _timer = new Timer(GameSpeed) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
            Console.WriteLine($"[SpaceInvaders] Started {_cols}x{_rows} invaders, px {_px}");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); }
            catch { }
        }
    }

    // ── Setup ───────────────────────────────────────────────────────────────

    private void Layout()
    {
        var scale = DisplayScale.GetScale(_canvas.Width, _canvas.Height);
        var w = _canvas.Width;
        var h = _canvas.Height;

        _px = Math.Max(1, (int)Math.Round(scale * 2));
        _invW = 11 * _px;
        _invH = 8 * _px;
        _cellW = _invW + _px * 3;
        _cellH = _invH + _px * 2;

        _cols = Math.Clamp((int)((w * 0.92) / _cellW), 5, 11);
        _rows = Math.Clamp((h / 2) / _cellH, 3, 5);

        _shipW = 11 * _px;
        _shipH = 4 * _px;
        _shipY = h - _shipH - _px * 2;
        _shipSpeed = Math.Max(1f, Math.Min(2.4f * scale, _px * 1.4f));

        _bulletSpeed = Math.Max(2f, 3.5f * scale * _px);
        _bombSpeed = Math.Max(1f, 1.6f * scale * _px);
    }

    private void NewGame()
    {
        Score = 0;
        Lives = 3;
        Wave = 1;
        _gameOverTimer = 0;
        StartWave();
    }

    private void StartWave()
    {
        _alive = new bool[_cols, _rows];
        for (var c = 0; c < _cols; c++)
        for (var r = 0; r < _rows; r++)
            _alive[c, r] = true;

        var totalW = _cols * _cellW;
        _formX = (_canvas.Width - totalW) / 2f;
        _formY = _canvas.Height * 0.12f + Math.Min(Wave - 1, 4) * _cellH * 0.5f;
        _dir = 1;
        _stepTimer = 0;
        _frameB = false;

        _bombs.Clear();
        _bulletActive = false;
        _ufo = null;
        _ufoTimer = _random.Next(300, 700);

        _shipX = (_canvas.Width - _shipW) / 2f;
        _respawnTimer = 0;

        BuildShields();
    }

    private void BuildShields()
    {
        _shields.Clear();
        const int count = 4;
        var blockSize = Math.Max(1, _px);
        var sw = 8 * blockSize;
        var sh = 6 * blockSize;
        var y = _shipY - sh - _px * 3;
        if (y < _formY + _rows * _cellH) return; // no room on tiny panels

        for (var i = 0; i < count; i++)
        {
            var x = (int)((i + 0.5) / count * _canvas.Width - sw / 2.0);
            var blocks = new bool[8, 6];
            for (var bx = 0; bx < 8; bx++)
            for (var by = 0; by < 6; by++)
                blocks[bx, by] = !(by >= 4 && bx >= 3 && bx <= 4); // notch at the bottom centre
            _shields.Add(new Shield { X = x, Y = y, Block = blockSize, Blocks = blocks });
        }
    }

    // ── Loop ────────────────────────────────────────────────────────────────

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try
            {
                if (_timer != null && Math.Abs(_timer.Interval - GameSpeed) > 0.5) _timer.Interval = GameSpeed;

                if (_gameOverTimer > 0)
                {
                    _gameOverTimer--;
                    if (_gameOverTimer == 0) NewGame();
                }
                else
                {
                    Update();
                }

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpaceInvaders] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        if (_respawnTimer > 0) _respawnTimer--;

        UpdateFormation();
        UpdateBullet();
        UpdateBombs();
        UpdateUfo();
        if (_respawnTimer == 0)
        {
            if (AutoPilot && !_human) RunAi();
            else
            {
                var hold = (_rightHeld ? 1 : 0) - (_leftHeld ? 1 : 0);
                if (hold != 0) MoveShip(hold);
            }
        }

        // Wave cleared?
        if (AliveCount() == 0) { Wave++; StartWave(); }
    }

    private void UpdateFormation()
    {
        var alive = AliveCount();
        if (alive == 0) return;

        // Faster as fewer invaders remain.
        var interval = Math.Max(2, 2 + alive * 18 / Math.Max(1, _cols * _rows));
        if (++_stepTimer < interval) return;
        _stepTimer = 0;
        _frameB = !_frameB;

        GetFormationBounds(out var minX, out var maxX, out var maxY);
        var step = _px * 2;

        if (_dir > 0 && maxX + step > _canvas.Width - _px || _dir < 0 && minX - step < _px)
        {
            _dir = -_dir;
            _formY += _invH * 0.5f;
        }
        else
        {
            _formX += _dir * step;
        }

        // Invaders reached the cannon line → lose a life and reset positions.
        if (maxY >= _shipY - _invH * 0.5f)
        {
            LoseLife();
            _formY = _canvas.Height * 0.12f;
            _formX = (_canvas.Width - _cols * _cellW) / 2f;
        }

        // Occasionally drop a bomb from the lowest invader of a random column.
        var bombChance = 0.05 + Difficulty * 0.03;
        if (_random.NextDouble() < bombChance)
        {
            var c = _random.Next(_cols);
            for (var r = _rows - 1; r >= 0; r--)
                if (_alive[c, r])
                {
                    var (ix, iy) = InvaderPos(c, r);
                    _bombs.Add(new Bomb { X = ix + _invW / 2f, Y = iy + _invH });
                    break;
                }
        }
    }

    private void UpdateBullet()
    {
        if (!_bulletActive) return;
        _bulletY -= _bulletSpeed;
        if (_bulletY < 0) { _bulletActive = false; return; }

        // UFO hit
        if (_ufo != null && _bulletX >= _ufo.X && _bulletX <= _ufo.X + _invW && _bulletY <= _invH * 1.2f)
        {
            Score += 100 + _random.Next(5) * 50;
            _ufo = null;
            _bulletActive = false;
            return;
        }

        // Invader hit
        for (var c = 0; c < _cols; c++)
        for (var r = 0; r < _rows; r++)
        {
            if (!_alive[c, r]) continue;
            var (ix, iy) = InvaderPos(c, r);
            if (_bulletX >= ix && _bulletX <= ix + _invW && _bulletY >= iy && _bulletY <= iy + _invH)
            {
                _alive[c, r] = false;
                Score += (_rows - r) * 10;
                _bulletActive = false;
                return;
            }
        }

        if (HitShield((int)_bulletX, (int)_bulletY)) _bulletActive = false;
    }

    private void UpdateBombs()
    {
        for (var i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            b.Y += _bombSpeed + Difficulty * 0.15f;
            _bombs[i] = b;

            if (b.Y > _canvas.Height) { _bombs.RemoveAt(i); continue; }
            if (HitShield((int)b.X, (int)b.Y)) { _bombs.RemoveAt(i); continue; }

            // Ship hit
            if (_respawnTimer == 0 && b.Y >= _shipY && b.X >= _shipX && b.X <= _shipX + _shipW)
            {
                _bombs.RemoveAt(i);
                LoseLife();
            }
        }
    }

    private void UpdateUfo()
    {
        if (_ufo == null)
        {
            if (--_ufoTimer <= 0)
            {
                var fromLeft = _random.Next(2) == 0;
                _ufo = new Ufo { X = fromLeft ? -_invW : _canvas.Width, Dir = fromLeft ? 1 : -1 };
                _ufoTimer = _random.Next(400, 900);
            }

            return;
        }

        _ufo.X += _ufo.Dir * Math.Max(1f, _px * 0.8f);
        if (_ufo.X < -_invW * 2 || _ufo.X > _canvas.Width + _invW) _ufo = null;
    }

    // ── AI cannon ─────────────────────────────────────────────────────────────

    private void RunAi()
    {
        var shipCenter = _shipX + _shipW / 2f;

        // 1) Dodge bombs that are actually on a collision course, not merely nearby.
        var dodge = 0;
        var closestThreat = float.MaxValue;
        foreach (var b in _bombs)
        {
            if (b.Y > _shipY) continue;
            var frames = (_shipY - b.Y) / Math.Max(0.4f, _bombSpeed + Difficulty * 0.15f);
            if (frames > 22) continue;
            var hit = b.X >= _shipX - _px && b.X <= _shipX + _shipW + _px;
            if (!hit) continue;
            if (frames < closestThreat)
            {
                closestThreat = frames;
                var roomLeft = _shipX;
                var roomRight = _canvas.Width - (_shipX + _shipW);
                dodge = roomLeft >= roomRight ? -1 : 1;
                if (b.X < shipCenter && roomRight > _shipW * 0.3f) dodge = 1;
                if (b.X > shipCenter && roomLeft > _shipW * 0.3f) dodge = -1;
            }
        }

        if (dodge != 0)
        {
            MoveShip(dodge);
            TryShoot(shipCenter);
            return;
        }

        var targetX = PickTargetX(shipCenter);
        var err = targetX - shipCenter;
        var align = Math.Max(_invW * 0.4f, _shipSpeed);
        if (Math.Abs(err) > align)
            MoveShip(err > 0 ? 1 : -1);
        TryShoot(shipCenter + Math.Clamp(err, -_shipSpeed, _shipSpeed));
    }

    private float PickTargetX(float shipCenter)
    {
        if (_ufo != null)
        {
            var ufoX = _ufo.X + _invW / 2f;
            if (ufoX > _px && ufoX < _canvas.Width - _px)
                return ufoX;
        }

        var bestX = shipCenter;
        var bestScore = float.MaxValue;
        for (var c = 0; c < _cols; c++)
        {
            var front = -1;
            for (var r = _rows - 1; r >= 0; r--)
                if (_alive[c, r]) { front = r; break; }
            if (front < 0) continue;

            var (ix, iy) = InvaderPos(c, front);
            var cx = ix + _invW / 2f;
            // Prefer the lowest (nearest) invaders, then the closest column.
            var score = (_rows - 1 - front) * _canvas.Width + Math.Abs(cx - shipCenter);
            if (score < bestScore)
            {
                bestScore = score;
                bestX = cx;
            }
        }

        return bestX;
    }

    private void TryShoot(float aimX)
    {
        if (_bulletActive || _respawnTimer > 0) return;
        var shipCenter = _shipX + _shipW / 2f;
        if (Math.Abs(aimX - shipCenter) > _invW * 0.55f) return;
        Fire();
    }

    private void MoveShip(int dir)
    {
        _shipX = Math.Clamp(_shipX + dir * _shipSpeed, 0, _canvas.Width - _shipW);
    }

    private void Fire()
    {
        _bulletActive = true;
        _bulletX = _shipX + _shipW / 2f;
        _bulletY = _shipY;
    }

    [ExtensionMethod("Move Left", "Hold left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 1)]
    public void MoveLeft()
    {
        lock (_lock) { _human = true; _leftHeld = true; }
    }

    [ExtensionMethod("Move Right", "Hold right — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 2)]
    public void MoveRight()
    {
        lock (_lock) { _human = true; _rightHeld = true; }
    }

    [ExtensionMethod("Release Left", "Release left",
        Category = "Controls", KeyboardShortcut = "Left:up|A:up", Order = 3)]
    public void ReleaseLeft()
    {
        lock (_lock) _leftHeld = false;
    }

    [ExtensionMethod("Release Right", "Release right",
        Category = "Controls", KeyboardShortcut = "Right:up|D:up", Order = 4)]
    public void ReleaseRight()
    {
        lock (_lock) _rightHeld = false;
    }

    [ExtensionMethod("Shoot", "Fire — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Space|Up", Order = 5)]
    public void Shoot()
    {
        lock (_lock)
        {
            _human = true;
            if (!_bulletActive && _respawnTimer == 0) Fire();
        }
    }

    private void LoseLife()
    {
        Lives--;
        _bombs.Clear();
        _bulletActive = false;
        _respawnTimer = 40;
        _shipX = (_canvas.Width - _shipW) / 2f;
        if (Lives <= 0) _gameOverTimer = 90;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (float x, float y) InvaderPos(int col, int row)
    {
        return (_formX + col * _cellW, _formY + row * _cellH);
    }

    private void GetFormationBounds(out float minX, out float maxX, out float maxY)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        maxY = float.MinValue;
        for (var c = 0; c < _cols; c++)
        for (var r = 0; r < _rows; r++)
        {
            if (!_alive[c, r]) continue;
            var (ix, iy) = InvaderPos(c, r);
            minX = Math.Min(minX, ix);
            maxX = Math.Max(maxX, ix + _invW);
            maxY = Math.Max(maxY, iy + _invH);
        }

        if (minX == float.MaxValue) { minX = maxX = _formX; maxY = _formY; }
    }

    private int AliveCount()
    {
        var n = 0;
        foreach (var a in _alive) if (a) n++;
        return n;
    }

    // Authentic arcade: player shots and invader bombs both nibble the bunkers.
    private bool HitShield(int x, int y)
    {
        foreach (var s in _shields)
        {
            var bx = (x - s.X) / s.Block;
            var by = (y - s.Y) / s.Block;
            if (bx < 0 || bx >= 8 || by < 0 || by >= 6) continue;
            if (!s.Blocks[bx, by]) continue;

            // Erode a small splash around the impact.
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = 0; dy <= 1; dy++)
            {
                int nx = bx + dx, ny = by + dy;
                if (nx >= 0 && nx < 8 && ny >= 0 && ny < 6) s.Blocks[nx, ny] = false;
            }

            return true;
        }

        return false;
    }

    // ── Render ────────────────────────────────────────────────────────────────

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var canvas = new SKCanvas(bb);
        canvas.Clear(BackgroundColor);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        // Invaders (colour by row band).
        var frame = _frameB ? InvaderB : InvaderA;
        for (var c = 0; c < _cols; c++)
        for (var r = 0; r < _rows; r++)
        {
            if (!_alive[c, r]) continue;
            var (ix, iy) = InvaderPos(c, r);
            paint.Color = r == 0 ? new SKColor(120, 220, 255)
                : r <= 2 ? new SKColor(120, 255, 140)
                : new SKColor(255, 200, 120);
            DrawSprite(canvas, frame, ix, iy, paint);
        }

        if (_ufo != null)
        {
            paint.Color = new SKColor(255, 80, 200);
            DrawSprite(canvas, InvaderB, _ufo.X, _invH * 0.2f, paint);
        }

        // Shields.
        paint.Color = new SKColor(0, 230, 90);
        foreach (var s in _shields)
            for (var bx = 0; bx < 8; bx++)
            for (var by = 0; by < 6; by++)
                if (s.Blocks[bx, by])
                    canvas.DrawRect(s.X + bx * s.Block, s.Y + by * s.Block, s.Block, s.Block, paint);

        // Ship (blinks while respawning).
        if (_respawnTimer == 0 || _frame % 6 < 3)
        {
            paint.Color = new SKColor(220, 255, 220);
            DrawSprite(canvas, Cannon, _shipX, _shipY, paint);
        }

        // Bullet + bombs.
        if (_bulletActive)
        {
            paint.Color = SKColors.White;
            canvas.DrawRect(_bulletX, _bulletY, _px, _px * 3, paint);
        }

        paint.Color = new SKColor(255, 120, 120);
        foreach (var b in _bombs)
            canvas.DrawRect(b.X, b.Y, _px, _px * 2, paint);

        if (ShowHud) DrawHud(canvas);

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawSprite(SKCanvas canvas, string[] rows, float x, float y, SKPaint paint)
    {
        for (var ry = 0; ry < rows.Length; ry++)
        {
            var row = rows[ry];
            for (var rx = 0; rx < row.Length; rx++)
                if (row[rx] == '1')
                    canvas.DrawRect(x + rx * _px, y + ry * _px, _px, _px, paint);
        }
    }

    private void DrawHud(SKCanvas canvas)
    {
        var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, Math.Min(14f, _canvas.Height * 0.09f)));
        CanvasText.Draw(canvas, _canvas, $"SCORE {Score}", SKColors.White, 3, size, size, SKTextAlign.Left,
            UseBdfFont);

        if (_gameOverTimer > 0)
        {
            CanvasText.Draw(canvas, _canvas, "GAME OVER", SKColors.Red, _canvas.Width / 2f, _canvas.Height / 2f,
                CanvasText.ResolveSize(FontSize, Math.Max(14f, _canvas.Height * 0.16f)), SKTextAlign.Center, UseBdfFont);
        }

        // Lives as little cannons.
        using var lp = new SKPaint { Color = new SKColor(120, 255, 140), IsAntialias = false };
        for (var i = 0; i < Lives; i++)
            DrawSprite(canvas, Cannon, _canvas.Width - (i + 1) * (_shipW + _px * 2), 2, lp);
    }

    private struct Bomb
    {
        public float X;
        public float Y;
    }

    private sealed class Ufo
    {
        public float X;
        public int Dir;
    }

    private sealed class Shield
    {
        public int X;
        public int Y;
        public int Block;
        public bool[,] Blocks = new bool[8, 6];
    }
}
