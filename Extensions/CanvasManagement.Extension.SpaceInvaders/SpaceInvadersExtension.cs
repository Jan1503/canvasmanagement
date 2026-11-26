using CanvasManagement.Interfaces;
using SkiaSharp;

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
public class SpaceInvadersExtension : GameExtension
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

    private readonly Random _random = new();
    private readonly List<Bomb> _bombs = new();
    private readonly List<Shield> _shields = new();

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

    internal SpaceInvadersExtension(ICanvas canvas) : base(canvas, GameSong.Space)
    {
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

    [ExtensionParameter("Use BDF Font", "Render HUD text with the crisp bitmap (BDF) font", DefaultValue = true,
        Order = 5)]
    public bool UseBdfFont { get; set; } = true;

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

    public override string Name => "Space Invaders";
    protected override double FrameIntervalMs => GameSpeed;

    protected override void OnStart()
    {
        _human = false;
        _leftHeld = false;
        _rightHeld = false;
        Layout();
        NewGame();

        Console.WriteLine($"[SpaceInvaders] Started {_cols}x{_rows} invaders, px {_px}");
    }

    // ── Setup ───────────────────────────────────────────────────────────────

    private void Layout()
    {
        var scale = DisplayScale.GetScale(Canvas.Width, Canvas.Height);
        var w = Canvas.Width;
        var h = Canvas.Height;

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
        _formX = (Canvas.Width - totalW) / 2f;
        _formY = Canvas.Height * 0.12f + Math.Min(Wave - 1, 4) * _cellH * 0.5f;
        _dir = 1;
        _stepTimer = 0;
        _frameB = false;

        _bombs.Clear();
        _bulletActive = false;
        _ufo = null;
        _ufoTimer = _random.Next(300, 700);

        _shipX = (Canvas.Width - _shipW) / 2f;
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
            var x = (int)((i + 0.5) / count * Canvas.Width - sw / 2.0);
            var blocks = new bool[8, 6];
            for (var bx = 0; bx < 8; bx++)
            for (var by = 0; by < 6; by++)
                blocks[bx, by] = !(by >= 4 && bx >= 3 && bx <= 4); // notch at the bottom centre
            _shields.Add(new Shield { X = x, Y = y, Block = blockSize, Blocks = blocks });
        }
    }

    // ── Loop ────────────────────────────────────────────────────────────────

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        if (_gameOverTimer > 0)
        {
            _gameOverTimer--;
            if (_gameOverTimer == 0) NewGame();
        }
        else
        {
            Update();
        }

        Draw(canvas);
        Audio.Update();
        _frame++;
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
        if (AliveCount() == 0)
        {
            Wave++;
            Audio.Play(GameSfx.LevelUp, 0.7f);
            _fx.Flash(new SKColor(120, 200, 255), 0.4f);
            _fx.Confetti(Canvas.Width / 2f, Canvas.Height * 0.4f, 50, 3f);
            _fx.Pop($"WAVE {Wave}", Canvas.Width / 2f, Canvas.Height * 0.45f, new SKColor(120, 220, 255));
            StartWave();
        }
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

        if (_dir > 0 && maxX + step > Canvas.Width - _px || _dir < 0 && minX - step < _px)
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
            _formY = Canvas.Height * 0.12f;
            _formX = (Canvas.Width - _cols * _cellW) / 2f;
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
                    Audio.Play(GameSfx.Drop, 0.25f, 0, 1.6f, 120);
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
            var bonus = 100 + _random.Next(5) * 50;
            Score += bonus;
            _fx.Burst(_ufo.X + _invW / 2f, _invH * 0.6f, new SKColor(255, 80, 200), 30, 3f, 0.9f, 0.03f);
            _fx.Pop(bonus.ToString(), _ufo.X + _invW / 2f, _invH * 1.6f, new SKColor(255, 160, 230));
            Audio.Play(GameSfx.Score, 0.7f);
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
                FxAlien(ix, iy, r);
                Audio.Play(GameSfx.Explode, 0.45f, (_bulletX / Canvas.Width) * 2 - 1, 1.6f);
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

            if (b.Y > Canvas.Height) { _bombs.RemoveAt(i); continue; }
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
                _ufo = new Ufo { X = fromLeft ? -_invW : Canvas.Width, Dir = fromLeft ? 1 : -1 };
                _ufoTimer = _random.Next(400, 900);
            }

            return;
        }

        _ufo.X += _ufo.Dir * Math.Max(1f, _px * 0.8f);
        if (_ufo.X < -_invW * 2 || _ufo.X > Canvas.Width + _invW) _ufo = null;
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
                var roomRight = Canvas.Width - (_shipX + _shipW);
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
            if (ufoX > _px && ufoX < Canvas.Width - _px)
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
            var score = (_rows - 1 - front) * Canvas.Width + Math.Abs(cx - shipCenter);
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
        _shipX = Math.Clamp(_shipX + dir * _shipSpeed, 0, Canvas.Width - _shipW);
    }

    private void Fire()
    {
        _bulletActive = true;
        Audio.Play(GameSfx.Shoot, 0.4f, (_shipX / Canvas.Width) * 2 - 1);
        _bulletX = _shipX + _shipW / 2f;
        _bulletY = _shipY;
    }

    [ExtensionMethod("Move Left", "Hold left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 1)]
    public void MoveLeft()
    {
        lock (Sync) { _human = true; _leftHeld = true; }
    }

    [ExtensionMethod("Move Right", "Hold right — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 2)]
    public void MoveRight()
    {
        lock (Sync) { _human = true; _rightHeld = true; }
    }

    [ExtensionMethod("Release Left", "Release left",
        Category = "Controls", KeyboardShortcut = "Left:up|A:up", Order = 3)]
    public void ReleaseLeft()
    {
        lock (Sync) _leftHeld = false;
    }

    [ExtensionMethod("Release Right", "Release right",
        Category = "Controls", KeyboardShortcut = "Right:up|D:up", Order = 4)]
    public void ReleaseRight()
    {
        lock (Sync) _rightHeld = false;
    }

    [ExtensionMethod("Shoot", "Fire — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Space|Up", Order = 5)]
    public void Shoot()
    {
        lock (Sync)
        {
            _human = true;
            if (!_bulletActive && _respawnTimer == 0) Fire();
        }
    }

    private void LoseLife()
    {
        FxShip();
        Lives--;
        Audio.Play(GameSfx.Explode, 0.8f);
        if (Lives <= 0) Audio.Play(GameSfx.GameOver, 0.6f);
        _bombs.Clear();
        _bulletActive = false;
        _respawnTimer = 40;
        _shipX = (Canvas.Width - _shipW) / 2f;
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

            _fx.Spray(x, y, y > s.Y + s.Block * 3 ? MathF.PI / 2 : -MathF.PI / 2, 1.2f, new SKColor(80, 255, 140), 6, 1.4f, 0.4f, 0.08f);
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

    // Two more alien kinds (original designs) so each band of rows looks different; same 11×8 box.
    private static readonly string[] JellyA =
    {
        "00011111000",
        "00111111100",
        "01101110110",
        "01111111110",
        "00111111100",
        "01010101010",
        "10100100101",
        "01000000010"
    };

    private static readonly string[] JellyB =
    {
        "00011111000",
        "00111111100",
        "01101110110",
        "01111111110",
        "00111111100",
        "00101010100",
        "01010101010",
        "10001000100"
    };

    private static readonly string[] DroneA =
    {
        "00001110000",
        "00111111100",
        "01111111110",
        "11011011011",
        "11111111111",
        "00110001100",
        "01101110110",
        "11000000011"
    };

    private static readonly string[] DroneB =
    {
        "00001110000",
        "00111111100",
        "01111111110",
        "11011011011",
        "11111111111",
        "00110001100",
        "00011011000",
        "00110001100"
    };

    private readonly GameFx _fx = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastRender;
    private readonly Queue<(float X, float Y)> _shotTrail = new();

    private static SKColor RowColor(int r) => r == 0 ? new SKColor(120, 220, 255)
        : r <= 2 ? new SKColor(120, 255, 140)
        : new SKColor(255, 190, 110);

    /// <summary>
    ///     Deep-space look: a drifting starfield with a faint nebula, aliens of three kinds that glow in their row
    ///     colour, a cannon with an engine glow, glowing shots with a trail, debris when shields are hit, alien
    ///     explosions, a shaking, flashing screen when the cannon is hit.
    /// </summary>
    private void Draw(SKCanvas canvas)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastRender, 0.001, 0.1);
        _lastRender = now;
        _fx.Update(dt);

        var w = Canvas.Width;
        var h = Canvas.Height;
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        if (BackgroundColor == SKColors.Black)
        {
            GameFx.VerticalGradient(canvas, w, h, new SKColor(4, 2, 16), new SKColor(10, 4, 26));
            GameFx.GlowDisc(canvas, w * 0.75f, h * 0.35f, h * 0.45f, new SKColor(90, 20, 120), 0.18f);
            GameFx.GlowDisc(canvas, w * 0.2f, h * 0.6f, h * 0.35f, new SKColor(20, 40, 110), 0.15f);
            GameFx.Stars(canvas, w, h, _fx.Time, 0, -_fx.Time * 6, 220);
        }
        else canvas.Clear(BackgroundColor);

        _fx.BeginShake(canvas);

        // Aliens: kind and colour by row band, a soft glow behind each.
        var b = _frameB;
        for (var c = 0; c < _cols; c++)
        for (var r = 0; r < _rows; r++)
        {
            if (!_alive[c, r]) continue;
            var (ix, iy) = InvaderPos(c, r);
            var col = RowColor(r);
            var sprite = r == 0 ? b ? JellyB : JellyA : r <= 2 ? b ? InvaderB : InvaderA : b ? DroneB : DroneA;
            paint.Color = col;
            DrawSprite(canvas, sprite, ix, iy, paint);
            // Eyes light up.
            paint.Color = SKColors.White;
            canvas.DrawRect(ix + 3 * _px, iy + 3 * _px, _px, _px, paint);
            canvas.DrawRect(ix + 7 * _px, iy + 3 * _px, _px, _px, paint);
        }

        if (_ufo != null)
        {
            var uy = _invH * 0.2f;
            GameFx.Glow(canvas, _ufo.X, uy, _invW, _invH, new SKColor(255, 60, 200), 4, 0.4f);
            paint.Color = new SKColor(255, 80, 200);
            DrawSprite(canvas, InvaderB, _ufo.X, uy, paint);
            // Blinking running lights.
            paint.Color = (int)(_fx.Time * 8) % 2 == 0 ? SKColors.Yellow : SKColors.Cyan;
            canvas.DrawRect(_ufo.X + _px, uy + 4 * _px, _px, _px, paint);
            canvas.DrawRect(_ufo.X + 9 * _px, uy + 4 * _px, _px, _px, paint);
        }

        // Shields: shaded blocks, brighter on top.
        foreach (var s in _shields)
            for (var bx = 0; bx < 8; bx++)
            for (var by = 0; by < 6; by++)
                if (s.Blocks[bx, by])
                {
                    paint.Color = by == 0 || !s.Blocks[bx, by - 1] ? new SKColor(120, 255, 170) : new SKColor(0, (byte)(200 - by * 18), (byte)(90 - by * 8));
                    canvas.DrawRect(s.X + bx * s.Block, s.Y + by * s.Block, s.Block, s.Block, paint);
                }

        // Cannon with a flickering engine glow (blinks while respawning).
        if (_respawnTimer == 0 || _frame % 6 < 3)
        {
            var flick = 0.3f + 0.15f * MathF.Sin(_fx.Time * 30);
            GameFx.GlowDisc(canvas, _shipX + _shipW / 2f, _shipY + 4 * _px, _shipW * 0.6f, new SKColor(80, 200, 255), flick);
            paint.Color = new SKColor(220, 255, 230);
            DrawSprite(canvas, Cannon, _shipX, _shipY, paint);
            paint.Color = new SKColor(80, 220, 255);
            canvas.DrawRect(_shipX + 5 * _px, _shipY, _px, _px, paint);
        }

        // Player shot: glowing bolt with a short trail.
        if (_bulletActive)
        {
            _shotTrail.Enqueue((_bulletX, _bulletY));
            while (_shotTrail.Count > 5) _shotTrail.Dequeue();
            var i = 0;
            foreach (var (tx, ty) in _shotTrail)
            {
                paint.Color = new SKColor(120, 220, 255, (byte)(40 * ++i));
                canvas.DrawRect(tx, ty, _px, _px * 3, paint);
            }
            GameFx.Glow(canvas, _bulletX, _bulletY, _px, _px * 3, new SKColor(120, 220, 255), 2, 0.5f);
            paint.Color = SKColors.White;
            canvas.DrawRect(_bulletX, _bulletY, _px, _px * 3, paint);
        }
        else _shotTrail.Clear();

        // Alien bombs: wiggling red sparks.
        foreach (var bomb in _bombs)
        {
            var wig = ((int)(bomb.Y / (2 * _px)) % 2 == 0) ? 0 : _px;
            GameFx.Glow(canvas, bomb.X, bomb.Y, _px, _px * 2, new SKColor(255, 60, 40), 2, 0.4f);
            paint.Color = new SKColor(255, 140, 120);
            canvas.DrawRect(bomb.X + wig - _px * 0.5f, bomb.Y, _px, _px, paint);
            canvas.DrawRect(bomb.X - wig + _px * 0.5f, bomb.Y + _px, _px, _px, paint);
        }

        _fx.DrawParticles(canvas);
        _fx.EndShake(canvas);

        if (ShowHud) DrawHud(canvas);
        _fx.DrawOverlay(canvas, Canvas, w, h, Math.Max(7f, h * 0.07f));
    }

    private void FxAlien(float x, float y, int row)
    {
        var col = RowColor(row);
        _fx.Burst(x + _invW / 2f, y + _invH / 2f, col, 16, 2.2f, 0.55f, 0.02f, true, 1, SKColors.White);
        _fx.Burst(x + _invW / 2f, y + _invH / 2f, SKColors.White, 4, 1f, 0.25f, 0, true);
    }

    private void FxShip()
    {
        var cx = _shipX + _shipW / 2f;
        _fx.Burst(cx, _shipY, new SKColor(255, 200, 80), 40, 3.2f, 1.1f, 0.05f, true, 1, new SKColor(255, 40, 0));
        _fx.Flash(new SKColor(255, 60, 30), 0.7f);
        _fx.Shake(6);
    }

    /// <summary>A one-colour sprite ('1' = lit) in the paint's colour, cached as an image by PixelArt.</summary>
    private void DrawSprite(SKCanvas canvas, string[] rows, float x, float y, SKPaint paint)
    {
        var col = paint.Color;
        PixelArt.Blit(canvas, rows, x, y, ch => ch == '1' ? col : SKColors.Transparent, _px);
    }

    private void DrawHud(SKCanvas canvas)
    {
        var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, Math.Min(14f, Canvas.Height * 0.09f)));
        CanvasText.Draw(canvas, Canvas, $"SCORE {Score}", SKColors.White, 3, size, size, SKTextAlign.Left,
            UseBdfFont);

        if (_gameOverTimer > 0)
        {
            CanvasText.Draw(canvas, Canvas, "GAME OVER", SKColors.Red, Canvas.Width / 2f, Canvas.Height / 2f,
                CanvasText.ResolveSize(FontSize, Math.Max(14f, Canvas.Height * 0.16f)), SKTextAlign.Center, UseBdfFont);
        }

        // Lives as little cannons.
        using var lp = new SKPaint { Color = new SKColor(120, 255, 140), IsAntialias = false };
        for (var i = 0; i < Lives; i++)
            DrawSprite(canvas, Cannon, Canvas.Width - (i + 1) * (_shipW + _px * 2), 2, lp);
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
