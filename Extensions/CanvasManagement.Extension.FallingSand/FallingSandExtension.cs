using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.FallingSand;

/// <summary>
///     A cellular falling-sand simulation: colourful sand and water pour from the top, pile up, flow around
///     stone obstacles and drain away at the bottom. Runs itself (no input), and the cell size auto-scales
///     so the grid stays sensible on any panel.
/// </summary>
[ExtensionInfo("Falling Sand",
    "Mesmerising cellular sand & water physics simulation",
    "Visual Effects",
    IconResourceName = "falling-sand.svg")]
public class FallingSandExtension : ICanvasExtension, IDisposable
{
    private const byte Empty = 0;
    private const byte Sand = 1;
    private const byte Water = 2;
    private const byte Stone = 3;

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();

    private SKBitmap? _backBuffer;
    private int _cell = 3;
    private int[] _emitters = Array.Empty<int>();
    private int _frame;
    private Timer? _timer;
    private int _gw;
    private int _gh;
    private byte[,] _mat = new byte[1, 1];
    private byte[,] _hue = new byte[1, 1];
    private bool[,] _moved = new bool[1, 1];
    private int _offsetX;
    private int _offsetY;
    private bool _scanLtr;
    private bool _draining;
    private int _lastObstacles = -1;

    internal FallingSandExtension(ICanvas canvas)
    {
        _canvas = canvas;
    }

    public enum SandMaterial
    {
        Rainbow,
        Sand,
        Water,
        Mixed
    }

    [ExtensionParameter("Material", "What pours from the top", DefaultValue = "Rainbow", Order = 1)]
    public SandMaterial Material { get; set; } = SandMaterial.Rainbow;

    [ExtensionParameter("Flow Rate", "How much material pours in", DefaultValue = 5, MinValue = 1, MaxValue = 10,
        Order = 2)]
    public int FlowRate { get; set; } = 5;

    [ExtensionParameter("Sim Speed", "Physics sub-steps per frame (higher = faster flow)", DefaultValue = 2,
        MinValue = 1, MaxValue = 5, Order = 3)]
    public int SimSpeed { get; set; } = 2;

    [ExtensionParameter("Emitters", "Number of pour points across the top", DefaultValue = 3, MinValue = 1,
        MaxValue = 8, Order = 4)]
    public int EmitterCount { get; set; } = 3;

    [ExtensionParameter("Obstacles", "Number of static stone pegs for the sand to pile on", DefaultValue = 4,
        MinValue = 0, MaxValue = 20, Order = 5)]
    public int Obstacles { get; set; } = 4;

    [ExtensionParameter("Frame Delay", "Frame interval in milliseconds", DefaultValue = 33, MinValue = 16,
        MaxValue = 100, Unit = "ms", Order = 6)]
    public int FrameDelay { get; set; } = 33;

    [ExtensionParameter("Background Color", "Background colour", DefaultValue = "#000000", Order = 7)]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    public string Name => "Falling Sand";
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

            var scale = DisplayScale.GetScale(_canvas.Width, _canvas.Height);
            _cell = Math.Clamp((int)Math.Round(4 * scale), 2, 6);
            _gw = Math.Max(8, _canvas.Width / _cell);
            _gh = Math.Max(8, _canvas.Height / _cell);
            _offsetX = (_canvas.Width - _gw * _cell) / 2;
            _offsetY = (_canvas.Height - _gh * _cell) / 2;

            _mat = new byte[_gw, _gh];
            _hue = new byte[_gw, _gh];
            _moved = new bool[_gw, _gh];

            PlaceObstacles();
            _lastObstacles = Obstacles;
            BuildEmitters();

            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

            _timer = new Timer(FrameDelay) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
            Console.WriteLine($"[FallingSand] Started {_gw}x{_gh} (cell {_cell})");
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

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;

            try
            {
                if (_timer != null && Math.Abs(_timer.Interval - FrameDelay) > 0.5) _timer.Interval = FrameDelay;

                BuildEmitters(); // cheap; honours live EmitterCount changes
                if (Obstacles != _lastObstacles)
                {
                    ReplaceObstacles();
                    _lastObstacles = Obstacles;
                }

                for (var s = 0; s < SimSpeed; s++) Step();

                Render();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FallingSand] {ex.Message}");
            }
        }
    }

    // ── Simulation ──────────────────────────────────────────────────────────

    private void Step()
    {
        Array.Clear(_moved, 0, _moved.Length);
        _scanLtr = !_scanLtr;

        var filled = 0;
        for (var y = _gh - 1; y >= 0; y--)
        for (var k = 0; k < _gw; k++)
        {
            var x = _scanLtr ? k : _gw - 1 - k;
            var m = _mat[x, y];
            if (m != Empty) filled++;

            if (y == _gh - 1) continue; // bottom row: nothing below to fall into
            if (_moved[x, y]) continue;

            switch (m)
            {
                case Sand:
                    StepSand(x, y);
                    break;
                case Water:
                    StepWater(x, y);
                    break;
            }
        }

        // Let dunes build up; once the field is nearly full, open the bottom drain so it slowly sinks and
        // avalanches, then close it again - giving perpetual, lifelike motion instead of straight-through fall.
        var ratio = filled / (float)(_gw * _gh);
        if (ratio > 0.62f) _draining = true;
        else if (ratio < 0.4f) _draining = false;

        if (_draining)
            for (var x = 0; x < _gw; x++)
                if (_mat[x, _gh - 1] != Stone)
                    _mat[x, _gh - 1] = Empty;

        Emit();
    }

    private void StepSand(int x, int y)
    {
        var below = y + 1;

        if (_mat[x, below] == Empty)
        {
            MoveCell(x, y, x, below);
            return;
        }

        if (_mat[x, below] == Water)
        {
            SwapCells(x, y, x, below); // sand sinks through water
            return;
        }

        // Diagonal slide (angle of repose).
        var first = _random.Next(2) == 0 ? -1 : 1;
        foreach (var d in new[] { first, -first })
        {
            var nx = x + d;
            if (nx < 0 || nx >= _gw) continue;
            if (_mat[nx, below] is Empty or Water)
            {
                if (_mat[nx, below] == Water) SwapCells(x, y, nx, below);
                else MoveCell(x, y, nx, below);
                return;
            }
        }
    }

    private void StepWater(int x, int y)
    {
        var below = y + 1;

        if (_mat[x, below] == Empty)
        {
            MoveCell(x, y, x, below);
            return;
        }

        var first = _random.Next(2) == 0 ? -1 : 1;
        foreach (var d in new[] { first, -first })
        {
            var nx = x + d;
            if (nx >= 0 && nx < _gw && _mat[nx, below] == Empty)
            {
                MoveCell(x, y, nx, below);
                return;
            }
        }

        // Spread sideways so water finds its level.
        foreach (var d in new[] { first, -first })
        {
            var nx = x + d;
            if (nx >= 0 && nx < _gw && _mat[nx, y] == Empty)
            {
                MoveCell(x, y, nx, y);
                return;
            }
        }
    }

    private void MoveCell(int sx, int sy, int dx, int dy)
    {
        _mat[dx, dy] = _mat[sx, sy];
        _hue[dx, dy] = _hue[sx, sy];
        _mat[sx, sy] = Empty;
        _moved[dx, dy] = true;
    }

    private void SwapCells(int ax, int ay, int bx, int by)
    {
        (_mat[ax, ay], _mat[bx, by]) = (_mat[bx, by], _mat[ax, ay]);
        (_hue[ax, ay], _hue[bx, by]) = (_hue[bx, by], _hue[ax, ay]);
        _moved[ax, ay] = true;
        _moved[bx, by] = true;
    }

    private void Emit()
    {
        var chance = FlowRate / 10.0;
        for (var i = 0; i < _emitters.Length; i++)
        {
            if (_random.NextDouble() > chance) continue;
            var x = _emitters[i];
            if (x < 0 || x >= _gw) continue;
            if (_mat[x, 0] != Empty) continue;

            var mat = PickMaterial(i);
            _mat[x, 0] = mat;
            _hue[x, 0] = mat == Sand ? (byte)((_frame * 2 + i * 47) & 0xFF) : (byte)0;
        }
    }

    private byte PickMaterial(int emitterIndex)
    {
        return Material switch
        {
            SandMaterial.Sand => Sand,
            SandMaterial.Water => Water,
            SandMaterial.Mixed => emitterIndex % 3 == 0 ? Water : Sand,
            _ => Sand // Rainbow = sand with cycling hue
        };
    }

    private void BuildEmitters()
    {
        var count = Math.Clamp(EmitterCount, 1, 8);
        if (_emitters.Length == count) return;
        _emitters = new int[count];
        for (var i = 0; i < count; i++)
            _emitters[i] = (int)((i + 0.5) / count * _gw);
    }

    private void ReplaceObstacles()
    {
        // Clear existing stone (keep sand/water), then place the new count.
        for (var x = 0; x < _gw; x++)
        for (var y = 0; y < _gh; y++)
            if (_mat[x, y] == Stone)
                _mat[x, y] = Empty;
        PlaceObstacles();
    }

    private void PlaceObstacles()
    {
        // A few static stone pegs in the middle band for the sand to pile on and flow around.
        var count = Math.Clamp(Obstacles, 0, 20);
        for (var i = 0; i < count; i++)
        {
            var cx = _random.Next(2, _gw - 2);
            var cy = _random.Next(_gh / 3, _gh * 3 / 4);
            var r = 1 + _random.Next(Math.Max(1, _gw / 24));
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < _gw && y >= 0 && y < _gh - 1) _mat[x, y] = Stone;
            }
        }
    }

    // ── Render ──────────────────────────────────────────────────────────────

    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var canvas = new SKCanvas(bb);
        canvas.Clear(BackgroundColor);

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        for (var y = 0; y < _gh; y++)
        for (var x = 0; x < _gw; x++)
        {
            var m = _mat[x, y];
            if (m == Empty) continue;

            paint.Color = m switch
            {
                Sand => SKColor.FromHsl(_hue[x, y] / 255f * 360f, 85, 58),
                Water => new SKColor(40, 110, (byte)(220 - (y * 30 / _gh)), 230),
                _ => new SKColor(95, 95, 110) // stone
            };

            canvas.DrawRect(_offsetX + x * _cell, _offsetY + y * _cell, _cell, _cell, paint);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }
}
