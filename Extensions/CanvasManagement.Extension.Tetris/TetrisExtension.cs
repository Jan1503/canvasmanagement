using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.Tetris;

/// <summary>
///     Full Tetris for the LED wall: 7-bag, hold, next queue, ghost piece, SRS-ish kicks,
///     line-clear scoring and a decent autopilot. 1:1 pixels, no anti-alias.
/// </summary>
[ExtensionInfo("Tetris",
    "Classic Tetris — arrows / WASD, Space hard-drop, C hold, or autopilot",
    "Games",
    IconResourceName = "tetris.svg")]
public class TetrisExtension : ICanvasExtension, IDisposable
{
    private const int Cols = 10;
    private const int Rows = 20;
    private const int Hidden = 2;

    private static readonly SKColor[] Palette =
    {
        new(0, 240, 240),   // I cyan
        new(40, 80, 255),   // J blue
        new(255, 160, 20),  // L orange
        new(255, 220, 40),  // O yellow
        new(80, 220, 70),   // S green
        new(180, 70, 255),  // T purple
        new(255, 60, 70)    // Z red
    };

    // 4 rotations × 4 cells. Origin is the bounding 4×4.
    private static readonly (int x, int y)[][][] Shapes =
    {
        // I
        new[]
        {
            new[] { (0, 1), (1, 1), (2, 1), (3, 1) },
            new[] { (2, 0), (2, 1), (2, 2), (2, 3) },
            new[] { (0, 2), (1, 2), (2, 2), (3, 2) },
            new[] { (1, 0), (1, 1), (1, 2), (1, 3) }
        },
        // J
        new[]
        {
            new[] { (0, 0), (0, 1), (1, 1), (2, 1) },
            new[] { (1, 0), (2, 0), (1, 1), (1, 2) },
            new[] { (0, 1), (1, 1), (2, 1), (2, 2) },
            new[] { (1, 0), (1, 1), (0, 2), (1, 2) }
        },
        // L
        new[]
        {
            new[] { (2, 0), (0, 1), (1, 1), (2, 1) },
            new[] { (1, 0), (1, 1), (1, 2), (2, 2) },
            new[] { (0, 1), (1, 1), (2, 1), (0, 2) },
            new[] { (0, 0), (1, 0), (1, 1), (1, 2) }
        },
        // O
        new[]
        {
            new[] { (1, 0), (2, 0), (1, 1), (2, 1) },
            new[] { (1, 0), (2, 0), (1, 1), (2, 1) },
            new[] { (1, 0), (2, 0), (1, 1), (2, 1) },
            new[] { (1, 0), (2, 0), (1, 1), (2, 1) }
        },
        // S
        new[]
        {
            new[] { (1, 0), (2, 0), (0, 1), (1, 1) },
            new[] { (1, 0), (1, 1), (2, 1), (2, 2) },
            new[] { (1, 1), (2, 1), (0, 2), (1, 2) },
            new[] { (0, 0), (0, 1), (1, 1), (1, 2) }
        },
        // T
        new[]
        {
            new[] { (1, 0), (0, 1), (1, 1), (2, 1) },
            new[] { (1, 0), (1, 1), (2, 1), (1, 2) },
            new[] { (0, 1), (1, 1), (2, 1), (1, 2) },
            new[] { (1, 0), (0, 1), (1, 1), (1, 2) }
        },
        // Z
        new[]
        {
            new[] { (0, 0), (1, 0), (1, 1), (2, 1) },
            new[] { (2, 0), (1, 1), (2, 1), (1, 2) },
            new[] { (0, 1), (1, 1), (1, 2), (2, 2) },
            new[] { (1, 0), (0, 1), (1, 1), (0, 2) }
        }
    };

    private readonly ICanvas _canvas;
    private readonly object _sync = new();
    private readonly Random _random = new();
    private readonly Queue<int> _bag = new();
    private readonly Queue<int> _next = new();
    private readonly int[,] _grid = new int[Cols, Rows + Hidden];

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _cell, _ox, _oy;
    private int _kind, _rot, _x, _y;
    private int? _hold;
    private bool _heldThisPiece;
    private int _holdX, _soft;
    private int _das, _softDas;
    private int _rotCool, _dropCool;
    private const int DasDelay = 18;   // ~400ms at 22ms/tick before auto-shift
    private const int DasRepeat = 6;   // ~130ms per extra step while held
    private const int SoftDelay = 10;
    private const int SoftRepeat = 4;
    private bool _human;
    private int _grav, _lockTicks, _lockMoves;
    private int _score, _lines, _level, _best;
    private int _clearFlash, _crashTimer, _frame;
    private int[] _clearing = Array.Empty<int>();
    private int _aiRot = -1, _aiX;

    internal TetrisExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 22,
        MinValue = 14, MaxValue = 50, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 22;

    [ExtensionParameter("Difficulty", "Starting level (gravity)", DefaultValue = 1, MinValue = 1, MaxValue = 15,
        Order = 2)]
    public int Difficulty { get; set; } = 1;

    [ExtensionParameter("Show Score", "Show score, level and next/hold", DefaultValue = true, Order = 3)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = false, Order = 4)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI plays until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    public string Name => "Tetris";
    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning) return;
            _human = false;
            NewGame();
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            _timer = new Timer(GameSpeed) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); } catch { }
        }
    }

    private void NewGame()
    {
        Array.Clear(_grid);
        _bag.Clear();
        _next.Clear();
        _hold = null;
        _heldThisPiece = false;
        _score = 0;
        _lines = 0;
        _level = Math.Max(1, Difficulty);
        _crashTimer = 0;
        _clearFlash = 0;
        _clearing = Array.Empty<int>();
        _holdX = 0;
        _soft = 0;
        _das = 0;
        _softDas = 0;
        _lockTicks = 0;
        _lockMoves = 0;
        Layout();
        while (_next.Count < 3) _next.Enqueue(DrawBag());
        Spawn();
    }

    private void Layout()
    {
        var hud = Math.Max(2, _canvas.Height / 32);
        _cell = Math.Max(4, (_canvas.Height - hud) / Rows);
        var boardW = Cols * _cell;
        var side = Math.Max(18, _canvas.Width - boardW - 8);
        if (side > boardW * 0.9f)
            _ox = 6;
        else
            _ox = Math.Max(2, (_canvas.Width - boardW - side) / 2);
        _oy = Math.Max(1, (_canvas.Height - Rows * _cell) / 2);
    }

    private int DrawBag()
    {
        if (_bag.Count == 0)
        {
            var ids = new[] { 0, 1, 2, 3, 4, 5, 6 };
            for (var i = ids.Length - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            foreach (var id in ids) _bag.Enqueue(id);
        }

        return _bag.Dequeue();
    }

    private void Spawn()
    {
        _kind = _next.Dequeue();
        _next.Enqueue(DrawBag());
        _rot = 0;
        _x = 3;
        _y = 0;
        _grav = 0;
        _lockTicks = 0;
        _lockMoves = 0;
        _heldThisPiece = false;
        _aiRot = -1;
        if (Hits(_kind, _rot, _x, _y))
        {
            _crashTimer = 80;
            _best = Math.Max(_best, _score);
        }
        else
            PlanAi();
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_sync)
        {
            if (!IsRunning || _backBuffer == null) return;
            try
            {
                if (_timer != null && Math.Abs(_timer.Interval - GameSpeed) > 0.5) _timer.Interval = GameSpeed;
                if (_crashTimer > 0)
                {
                    _crashTimer--;
                    if (_crashTimer == 0) NewGame();
                }
                else if (_clearFlash > 0)
                {
                    _clearFlash--;
                    if (_clearFlash == 0) FinishClear();
                }
                else
                    Update();

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tetris] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        if (_rotCool > 0) _rotCool--;
        if (_dropCool > 0) _dropCool--;

        if (AutoPilot && !_human) StepAi();
        else
        {
            if (_holdX != 0)
            {
                _das++;
                if (_das >= DasDelay && (_das - DasDelay) % DasRepeat == 0)
                    TryMove(_holdX, 0);
            }

            if (_soft != 0)
            {
                _softDas++;
                if (_softDas >= SoftDelay && (_softDas - SoftDelay) % SoftRepeat == 0)
                    TryMove(0, 1);
            }
        }

        var delay = GravityDelay();
        _grav++;
        var falling = !Resting();
        if (falling)
        {
            _lockTicks = 0;
            if (_grav >= delay)
            {
                _grav = 0;
                if (!TryMove(0, 1) && Resting()) _lockTicks = 1;
            }
        }
        else
        {
            _lockTicks++;
            if (_lockTicks >= 18) LockPiece();
        }
    }

    private int GravityDelay()
    {
        // Frames per row. Level 1 is leisurely, high levels still readable on a 22ms tick.
        var t = Math.Max(2, 18 - _level);
        return t;
    }

    private bool Resting() => Hits(_kind, _rot, _x, _y + 1);

    private bool TryMove(int dx, int dy)
    {
        if (_clearFlash > 0 || _crashTimer > 0) return false;
        if (Hits(_kind, _rot, _x + dx, _y + dy)) return false;
        _x += dx;
        _y += dy;
        if (dy == 0 && Resting() && _lockMoves < 12)
        {
            _lockTicks = 0;
            _lockMoves++;
        }

        return true;
    }

    private bool TryRotate(int dir)
    {
        if (_clearFlash > 0 || _crashTimer > 0) return false;
        var nr = (_rot + dir + 4) % 4;
        foreach (var (kx, ky) in Kicks(_kind, _rot, nr))
        {
            if (Hits(_kind, nr, _x + kx, _y + ky)) continue;
            _rot = nr;
            _x += kx;
            _y += ky;
            if (Resting() && _lockMoves < 12)
            {
                _lockTicks = 0;
                _lockMoves++;
            }

            return true;
        }

        return false;
    }

    private static (int x, int y)[] Kicks(int kind, int from, int to)
    {
        _ = from;
        _ = to;
        if (kind == 3) return new[] { (0, 0) }; // O
        if (kind == 0) // I
            return new[] { (0, 0), (-2, 0), (1, 0), (-2, 1), (1, -2), (2, 0), (-1, 0) };
        return new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (-1, -1), (1, -1), (0, 1), (-2, 0), (2, 0) };
    }

    private void HardDrop()
    {
        var n = 0;
        while (!Hits(_kind, _rot, _x, _y + 1))
        {
            _y++;
            n++;
        }

        _score += n * 2;
        LockPiece();
    }

    private void Hold()
    {
        if (_heldThisPiece || _crashTimer > 0) return;
        _heldThisPiece = true;
        if (_hold == null)
        {
            _hold = _kind;
            Spawn();
            _heldThisPiece = true;
        }
        else
        {
            var swap = _hold.Value;
            _hold = _kind;
            _kind = swap;
            _rot = 0;
            _x = 3;
            _y = 0;
            _lockTicks = 0;
            if (Hits(_kind, _rot, _x, _y)) _crashTimer = 80;
            else PlanAi();
        }
    }

    private void LockPiece()
    {
        foreach (var (px, py) in Cells(_kind, _rot, _x, _y))
        {
            if (py < 0) continue;
            if (py >= Rows + Hidden || px < 0 || px >= Cols)
            {
                _crashTimer = 80;
                return;
            }

            _grid[px, py] = _kind + 1;
        }

        var full = new List<int>();
        for (var r = 0; r < Rows + Hidden; r++)
        {
            var ok = true;
            for (var c = 0; c < Cols; c++)
                if (_grid[c, r] == 0) { ok = false; break; }
            if (ok) full.Add(r);
        }

        if (full.Count > 0)
        {
            _clearing = full.ToArray();
            _clearFlash = 8;
            var n = full.Count;
            var pts = n switch { 1 => 100, 2 => 300, 3 => 500, _ => 800 };
            _score += pts * _level;
            _lines += n;
            _level = Math.Max(_level, 1 + _lines / 10);
            _best = Math.Max(_best, _score);
            return;
        }

        Spawn();
    }

    private void FinishClear()
    {
        var skip = new HashSet<int>(_clearing);
        var dest = Rows + Hidden - 1;
        for (var r = Rows + Hidden - 1; r >= 0; r--)
        {
            if (skip.Contains(r)) continue;
            if (dest != r)
                for (var c = 0; c < Cols; c++)
                    _grid[c, dest] = _grid[c, r];
            dest--;
        }

        while (dest >= 0)
        {
            for (var c = 0; c < Cols; c++) _grid[c, dest] = 0;
            dest--;
        }

        _clearing = Array.Empty<int>();
        Spawn();
    }

    private bool Hits(int kind, int rot, int x, int y)
    {
        foreach (var (px, py) in Cells(kind, rot, x, y))
        {
            if (px < 0 || px >= Cols || py >= Rows + Hidden) return true;
            if (py < 0) continue;
            if (_grid[px, py] != 0) return true;
        }

        return false;
    }

    private static IEnumerable<(int x, int y)> Cells(int kind, int rot, int x, int y)
    {
        foreach (var (cx, cy) in Shapes[kind][rot])
            yield return (x + cx, y + cy);
    }

    private int GhostY()
    {
        var gy = _y;
        while (!Hits(_kind, _rot, _x, gy + 1)) gy++;
        return gy;
    }

    private void PlanAi()
    {
        var best = float.MinValue;
        _aiRot = 0;
        _aiX = _x;
        for (var rot = 0; rot < 4; rot++)
        for (var col = -2; col <= Cols; col++)
        {
            if (Hits(_kind, rot, col, 0) && Hits(_kind, rot, col, 1)) continue;
            var y = 0;
            if (Hits(_kind, rot, col, y)) continue;
            while (!Hits(_kind, rot, col, y + 1)) y++;
            var score = RateDrop(kind: _kind, rot, col, y);
            if (score > best)
            {
                best = score;
                _aiRot = rot;
                _aiX = col;
            }
        }
    }

    private float RateDrop(int kind, int rot, int x, int y)
    {
        var bak = (int[,])_grid.Clone();
        foreach (var (px, py) in Cells(kind, rot, x, y))
            if (py >= 0 && py < Rows + Hidden && px >= 0 && px < Cols)
                _grid[px, py] = kind + 1;

        var lines = 0;
        var holes = 0;
        var heights = new int[Cols];
        var bump = 0;
        var maxH = 0;
        for (var c = 0; c < Cols; c++)
        {
            var blocked = false;
            var h = 0;
            for (var r = 0; r < Rows + Hidden; r++)
            {
                if (_grid[c, r] != 0)
                {
                    if (h == 0) h = Rows + Hidden - r;
                    blocked = true;
                }
                else if (blocked) holes++;
            }

            heights[c] = h;
            maxH = Math.Max(maxH, h);
        }

        for (var r = 0; r < Rows + Hidden; r++)
        {
            var full = true;
            for (var c = 0; c < Cols; c++)
                if (_grid[c, r] == 0) { full = false; break; }
            if (full) lines++;
        }

        for (var c = 0; c < Cols - 1; c++)
            bump += Math.Abs(heights[c] - heights[c + 1]);

        Array.Copy(bak, _grid, _grid.Length);

        var tetris = lines >= 4 ? 40 : 0;
        return lines * 90f + tetris - holes * 48f - bump * 3.2f - maxH * 2.4f - y * 0.15f;
    }

    private void StepAi()
    {
        if (_aiRot < 0) PlanAi();
        if (_rot != _aiRot)
        {
            TryRotate(1);
            return;
        }

        if (_x < _aiX) TryMove(1, 0);
        else if (_x > _aiX) TryMove(-1, 0);
        else if (_frame % 2 == 0) TryMove(0, 1);
    }

    [ExtensionMethod("Move Left", "Shift left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 1)]
    public void MoveLeft()
    {
        lock (_sync)
        {
            _human = true;
            if (_holdX == -1) return;
            _holdX = -1;
            _das = 0;
            TryMove(-1, 0);
        }
    }

    [ExtensionMethod("Move Right", "Shift right",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 2)]
    public void MoveRight()
    {
        lock (_sync)
        {
            _human = true;
            if (_holdX == 1) return;
            _holdX = 1;
            _das = 0;
            TryMove(1, 0);
        }
    }

    [ExtensionMethod("Release Left", "Release left",
        Category = "Controls", KeyboardShortcut = "Left:up|A:up", Order = 3)]
    public void ReleaseLeft()
    {
        lock (_sync) { if (_holdX < 0) { _holdX = 0; _das = 0; } }
    }

    [ExtensionMethod("Release Right", "Release right",
        Category = "Controls", KeyboardShortcut = "Right:up|D:up", Order = 4)]
    public void ReleaseRight()
    {
        lock (_sync) { if (_holdX > 0) { _holdX = 0; _das = 0; } }
    }

    [ExtensionMethod("Soft Drop", "Hold to drop faster",
        Category = "Controls", KeyboardShortcut = "Down|S", Order = 5)]
    public void SoftDrop()
    {
        lock (_sync)
        {
            _human = true;
            if (_soft == 1) return;
            _soft = 1;
            _softDas = 0;
            TryMove(0, 1);
        }
    }

    [ExtensionMethod("Release Soft Drop", "Release down",
        Category = "Controls", KeyboardShortcut = "Down:up|S:up", Order = 6)]
    public void ReleaseSoft()
    {
        lock (_sync) { _soft = 0; _softDas = 0; }
    }

    [ExtensionMethod("Rotate", "Rotate clockwise",
        Category = "Controls", KeyboardShortcut = "Up|W|X", Order = 7)]
    public void Rotate()
    {
        lock (_sync)
        {
            _human = true;
            if (_rotCool > 0) return;
            _rotCool = 8;
            TryRotate(1);
        }
    }

    [ExtensionMethod("Rotate CCW", "Rotate counter-clockwise",
        Category = "Controls", KeyboardShortcut = "Z", Order = 8)]
    public void RotateCcw()
    {
        lock (_sync)
        {
            _human = true;
            if (_rotCool > 0) return;
            _rotCool = 8;
            TryRotate(-1);
        }
    }

    [ExtensionMethod("Hard Drop", "Slam the piece down",
        Category = "Controls", KeyboardShortcut = "Space", Order = 9)]
    public void Drop()
    {
        lock (_sync)
        {
            _human = true;
            if (_crashTimer > 0)
            {
                _crashTimer = 0;
                NewGame();
                return;
            }

            if (_dropCool > 0) return;
            _dropCool = 12;
            HardDrop();
        }
    }

    [ExtensionMethod("Hold", "Swap with the held piece",
        Category = "Controls", KeyboardShortcut = "C", Order = 10)]
    public void DoHold()
    {
        lock (_sync) { _human = true; Hold(); }
    }

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        using var canvas = new SKCanvas(bb);
        canvas.Clear(new SKColor(10, 8, 22));
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        var wellW = Cols * _cell;
        var wellH = Rows * _cell;
        paint.Color = new SKColor(18, 14, 36);
        canvas.DrawRect(_ox - 2, _oy - 2, wellW + 4, wellH + 4, paint);
        paint.Color = new SKColor(8, 6, 16);
        canvas.DrawRect(_ox, _oy, wellW, wellH, paint);

        paint.Color = new SKColor(28, 22, 48);
        for (var c = 1; c < Cols; c++)
            canvas.DrawRect(_ox + c * _cell, _oy, 1, wellH, paint);
        for (var r = 1; r < Rows; r++)
            canvas.DrawRect(_ox, _oy + r * _cell, wellW, 1, paint);

        var skip = new HashSet<int>(_clearing);
        for (var r = Hidden; r < Rows + Hidden; r++)
        {
            var flash = skip.Contains(r) && _clearFlash / 2 % 2 == 0;
            for (var c = 0; c < Cols; c++)
            {
                var v = _grid[c, r];
                if (v == 0 && !flash) continue;
                DrawMino(canvas, paint, c, r - Hidden, flash ? SKColors.White : Palette[v - 1], solid: true);
            }
        }

        if (_crashTimer == 0 && _clearFlash == 0)
        {
            var gy = GhostY();
            if (gy != _y)
                foreach (var (px, py) in Cells(_kind, _rot, _x, gy))
                    if (py >= Hidden)
                        DrawMino(canvas, paint, px, py - Hidden, Palette[_kind], solid: false);

            foreach (var (px, py) in Cells(_kind, _rot, _x, _y))
                if (py >= Hidden)
                    DrawMino(canvas, paint, px, py - Hidden, Palette[_kind], solid: true);
        }

        var sideX = _ox + wellW + 6;
        var box = Math.Max(4, _cell);
        DrawPreview(canvas, paint, sideX, _oy, "NEXT", _next.ToArray(), box);
        DrawHoldBox(canvas, paint, sideX, _oy + box * 8, box);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(7f, _canvas.Height * 0.07f));
            var tx = sideX;
            var ty = _oy + box * 14;
            if (ty > _canvas.Height - size * 4)
            {
                tx = 4;
                ty = 4;
            }

            CanvasText.Draw(canvas, _canvas, $"{_score}", SKColors.White, tx, ty + size, size, SKTextAlign.Left, UseBdfFont);
            CanvasText.Draw(canvas, _canvas, $"LV {_level}  {_lines}", new SKColor(180, 200, 255),
                tx, ty + size * 2.1f, Math.Max(6f, size * 0.8f), SKTextAlign.Left, UseBdfFont);
            if (_best > 0)
                CanvasText.Draw(canvas, _canvas, $"HI {_best}", new SKColor(255, 210, 80),
                    tx, ty + size * 3.1f, Math.Max(6f, size * 0.75f), SKTextAlign.Left, UseBdfFont);
        }

        if (_crashTimer > 0)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(12f, _canvas.Height * 0.14f));
            CanvasText.Draw(canvas, _canvas, "GAME OVER", SKColors.White,
                _ox + wellW / 2f, _oy + wellH * 0.45f, size, SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawPreview(SKCanvas canvas, SKPaint paint, float x, float y, string label, int[] kinds, int box)
    {
        var size = CanvasText.ResolveSize(FontSize, Math.Max(6f, box * 1.1f));
        CanvasText.Draw(canvas, _canvas, label, new SKColor(200, 210, 255),
            x, y + size, size, SKTextAlign.Left, UseBdfFont);
        var py = y + size + 4;
        var mini = Math.Max(3, box - 1);
        foreach (var k in kinds.Take(3))
        {
            DrawMini(canvas, paint, x, py, k, mini);
            py += mini * 5 + 2;
        }
    }

    private void DrawHoldBox(SKCanvas canvas, SKPaint paint, float x, float y, int box)
    {
        var size = CanvasText.ResolveSize(FontSize, Math.Max(6f, box * 1.1f));
        CanvasText.Draw(canvas, _canvas, "HOLD", new SKColor(200, 210, 255),
            x, y + size, size, SKTextAlign.Left, UseBdfFont);
        if (_hold != null)
            DrawMini(canvas, paint, x, y + size + 4, _hold.Value, Math.Max(3, box - 1));
    }

    private void DrawMini(SKCanvas canvas, SKPaint paint, float x, float y, int kind, int mini)
    {
        foreach (var (cx, cy) in Shapes[kind][0])
            FillMino(canvas, paint, x + cx * mini, y + cy * mini, mini, Palette[kind], solid: true);
    }

    private void DrawMino(SKCanvas canvas, SKPaint paint, int c, int r, SKColor col, bool solid)
    {
        FillMino(canvas, paint, _ox + c * _cell, _oy + r * _cell, _cell, col, solid);
    }

    private static void FillMino(SKCanvas canvas, SKPaint paint, float x, float y, int cell, SKColor col, bool solid)
    {
        if (cell < 3)
        {
            paint.Color = col;
            canvas.DrawRect(x, y, cell, cell, paint);
            return;
        }

        paint.Color = new SKColor(12, 8, 18);
        canvas.DrawRect(x, y, cell, cell, paint);
        if (!solid)
        {
            paint.Color = col.WithAlpha(90);
            canvas.DrawRect(x + 1, y + 1, cell - 2, cell - 2, paint);
            return;
        }

        paint.Color = col;
        canvas.DrawRect(x + 1, y + 1, cell - 2, cell - 2, paint);
        paint.Color = Bright(col);
        canvas.DrawRect(x + 1, y + 1, cell - 2, 1, paint);
        canvas.DrawRect(x + 1, y + 1, 1, cell - 2, paint);
        paint.Color = Dark(col);
        canvas.DrawRect(x + 1, y + cell - 2, cell - 2, 1, paint);
        canvas.DrawRect(x + cell - 2, y + 1, 1, cell - 2, paint);
        if (cell >= 6)
        {
            paint.Color = SKColors.White.WithAlpha(160);
            canvas.DrawRect(x + 2, y + 2, Math.Max(1, cell / 4), Math.Max(1, cell / 4), paint);
        }
    }

    private static SKColor Bright(SKColor c) =>
        new((byte)Math.Min(255, c.Red + 50), (byte)Math.Min(255, c.Green + 50), (byte)Math.Min(255, c.Blue + 50));

    private static SKColor Dark(SKColor c) =>
        new((byte)(c.Red * 0.45f), (byte)(c.Green * 0.45f), (byte)(c.Blue * 0.45f));
}
