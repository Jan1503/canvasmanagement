using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.LavaLamp;

/// <summary>Blob movement and physics behavior style.</summary>
public enum BlobPhysics
{
    Float, // Smooth floating with gentle collisions
    Bounce, // Bouncy rubber balls
    Gravity, // Rising with realistic physics
    Wander, // Slow drifting
    Chaos // Erratic high-speed motion
}

/// <summary>Visual rendering style for the lava lamp effect.</summary>
public enum LampStyle
{
    Classic, // Soft metaballs
    Sharp, // High-contrast edges
    Neon, // Glowing bright blobs
    Plasma, // Color-shifting gradient
    Liquid, // Watery translucent
    Metallic // Reflective chrome-like
}

/// <summary>
///     Mesmerizing lava lamp extension featuring realistic metaball physics, smooth blob merging,
///     customizable colors, gravity simulation, and multiple visual styles. Perfect for ambient displays
///     with rich parameter control over blob count, size, speed, viscosity, and visual appearance.
/// </summary>
[ExtensionInfo("Lava Lamp",
    "Hypnotic metaball physics: floating blobs with smooth merging, customizable colors, gravity, and multiple visual styles",
    "Visual Effects",
    IconResourceName = "lava-lamp.svg")]
public class LavaLampExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly float _scale;

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private readonly List<Blob> _blobs = new();
    private float _time;
    private int _lastBlobCount;
    private int _lastMinSize;
    private int _lastMaxSize;

    internal LavaLampExtension(ICanvas canvas)
    {
        _canvas = canvas;
        _scale = DisplayScale.GetScale(canvas.Width, canvas.Height);
    }

    // ── Core Parameters ──────────────────────────────────────────────────────
    [ExtensionParameter("Blob Count", "Number of lava blobs", DefaultValue = 8, MinValue = 2, MaxValue = 30)]
    public int BlobCount { get; set; } = 8;

    [ExtensionParameter("Min Blob Size", "Minimum blob radius", DefaultValue = 15, MinValue = 5, MaxValue = 100,
        Unit = "px")]
    public int MinBlobSize { get; set; } = 15;

    [ExtensionParameter("Max Blob Size", "Maximum blob radius", DefaultValue = 40, MinValue = 10, MaxValue = 150,
        Unit = "px")]
    public int MaxBlobSize { get; set; } = 40;

    [ExtensionParameter("Speed", "Blob movement speed multiplier", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0)]
    public double Speed { get; set; } = 1.0;

    [ExtensionParameter("Physics", "Blob behavior physics style", DefaultValue = BlobPhysics.Float)]
    public BlobPhysics Physics { get; set; } = BlobPhysics.Float;

    // ── Visual Style ─────────────────────────────────────────────────────────
    [ExtensionParameter("Style", "Visual rendering style", DefaultValue = LampStyle.Classic)]
    public LampStyle Style { get; set; } = LampStyle.Classic;

    [ExtensionParameter("Metaball Threshold", "Blob merging sensitivity (higher = more merging)",
        DefaultValue = 1.0, MinValue = 0.3, MaxValue = 3.0)]
    public double MetaballThreshold { get; set; } = 1.0;

    [ExtensionParameter("Glow Strength", "Glow/bloom intensity", DefaultValue = 2, MinValue = 0, MaxValue = 10)]
    public int GlowStrength { get; set; } = 2;

    [ExtensionParameter("Smoothness", "Edge smoothness/anti-aliasing", DefaultValue = 3, MinValue = 1, MaxValue = 10)]
    public int Smoothness { get; set; } = 3;

    [ExtensionParameter("Resolution", "Pixel block size for metaball/plasma styles (1 = full quality, higher = faster). " +
                                      "Increase on slow hardware (e.g. Pi Zero) to keep the frame rate up.",
        DefaultValue = 2, MinValue = 1, MaxValue = 8)]
    public int Resolution { get; set; } = 2;

    // ── Color ────────────────────────────────────────────────────────────────
    [ExtensionParameter("Color Mode", "Single color or multi-color blobs", DefaultValue = "Multi")]
    public string ColorMode { get; set; } = "Multi";

    [ExtensionParameter("Primary Color", "Main blob color (Single mode) or base hue (Multi mode)",
        DefaultValue = "#FF4500")]
    public SKColor PrimaryColor { get; set; } = new(255, 69, 0);

    [ExtensionParameter("Secondary Color", "Gradient target color", DefaultValue = "#FFD700")]
    public SKColor SecondaryColor { get; set; } = new(255, 215, 0);

    [ExtensionParameter("Color Cycle", "Slowly shift colors through hue spectrum", DefaultValue = true)]
    public bool ColorCycle { get; set; } = true;

    [ExtensionParameter("Color Cycle Speed", "Color animation speed", DefaultValue = 20, MinValue = 1, MaxValue = 100)]
    public int ColorCycleSpeed { get; set; } = 20;

    [ExtensionParameter("Background Color", "Lamp background", DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    // ── Advanced ─────────────────────────────────────────────────────────────
    [ExtensionParameter("Gravity", "Buoyancy strength. In Float/Bubble modes, blobs are pulled UP (warm lamp physics). " +
                                    "In Gravity/Bounce modes, blobs are pulled DOWN.",
        DefaultValue = 0.5, MinValue = 0, MaxValue = 2.0)]
    public double Gravity { get; set; } = 0.5;

    [ExtensionParameter("Viscosity", "Blob resistance/drag", DefaultValue = 0.98, MinValue = 0.85, MaxValue = 1.0)]
    public double Viscosity { get; set; } = 0.98;

    [ExtensionParameter("Wall Bounce", "Energy retained when hitting walls", DefaultValue = 0.8, MinValue = 0.2,
        MaxValue = 1.0)]
    public double WallBounce { get; set; } = 0.8;

    [ExtensionParameter("Blob Collision", "Enable blob-to-blob physics", DefaultValue = true)]
    public bool BlobCollision { get; set; } = true;

    [ExtensionParameter("Pulse", "Gently pulse blob sizes", DefaultValue = true)]
    public bool Pulse { get; set; } = true;

    [ExtensionParameter("Shimmer", "Add subtle shimmer to blobs", DefaultValue = false)]
    public bool Shimmer { get; set; }

    public string Name => "Lava Lamp";
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

            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

            InitializeBlobs();

            _timer = new Timer(33) { AutoReset = true }; // ~30 FPS
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
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
            _blobs.Clear();
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); }
            catch { /* Canvas might be disposed */ }
        }
    }

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try
            {
                _time += 0.033f * (float)Speed;

                if (_lastBlobCount != BlobCount)
                {
                    InitializeBlobs();
                }
                else if (_lastMinSize != MinBlobSize || _lastMaxSize != MaxBlobSize)
                {
                    ApplyBlobSizeChanges();
                }

                UpdatePhysics();
                Render();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LavaLamp] render: {ex.Message}");
            }
        }
    }

    private void InitializeBlobs()
    {
        _blobs.Clear();
        _lastBlobCount = BlobCount;
        _lastMinSize = MinBlobSize;
        _lastMaxSize = MaxBlobSize;

        var w = _canvas.Width;
        var h = _canvas.Height;
        var min = Math.Min(MinBlobSize, MaxBlobSize);
        var max = Math.Max(MinBlobSize, MaxBlobSize);

        for (var i = 0; i < BlobCount; i++)
        {
            var size = min + (float)_random.NextDouble() * (max - min);
            // Ensure the blob actually fits in the canvas (avoids being spawned outside on tiny panels).
            size = Math.Min(size, Math.Max(2f, Math.Min(w, h) * 0.45f));

            _blobs.Add(new Blob
            {
                X = size + (float)_random.NextDouble() * Math.Max(0f, w - size * 2),
                Y = size + (float)_random.NextDouble() * Math.Max(0f, h - size * 2),
                // Start with modest velocities so the initial state is calm.
                VX = ((float)_random.NextDouble() - 0.5f) * 0.6f,
                VY = ((float)_random.NextDouble() - 0.5f) * 0.6f,
                Radius = size,
                BaseRadius = size,
                Hue = (float)_random.NextDouble() * 360f,
                Phase = (float)_random.NextDouble() * (float)Math.PI * 2
            });
        }
    }

    /// <summary>
    ///     Rescales existing blobs in place when Min/Max Blob Size change at runtime, so the user sees
    ///     the effect immediately without regenerating positions (which would be visually jarring).
    /// </summary>
    private void ApplyBlobSizeChanges()
    {
        if (_blobs.Count == 0) return;

        var w = _canvas.Width;
        var h = _canvas.Height;
        var newMin = Math.Min(MinBlobSize, MaxBlobSize);
        var newMax = Math.Max(MinBlobSize, MaxBlobSize);
        var oldMin = Math.Min(_lastMinSize, _lastMaxSize);
        var oldMax = Math.Max(_lastMinSize, _lastMaxSize);
        var oldRange = Math.Max(1f, oldMax - oldMin);
        var maxFit = Math.Max(2f, Math.Min(w, h) * 0.45f);

        foreach (var blob in _blobs)
        {
            // Preserve each blob's relative size within the old range so their variety is preserved.
            var t = Math.Clamp((blob.BaseRadius - oldMin) / oldRange, 0f, 1f);
            blob.BaseRadius = Math.Min(maxFit, newMin + t * (newMax - newMin));
            // If the new radius pushes the blob out of bounds, snap it back inside.
            blob.X = Math.Clamp(blob.X, blob.BaseRadius, Math.Max(blob.BaseRadius, w - blob.BaseRadius));
            blob.Y = Math.Clamp(blob.Y, blob.BaseRadius, Math.Max(blob.BaseRadius, h - blob.BaseRadius));
        }

        _lastMinSize = MinBlobSize;
        _lastMaxSize = MaxBlobSize;
    }

    private void UpdatePhysics()
    {
        var w = _canvas.Width;
        var h = _canvas.Height;

        // Speed is a playback multiplier — it scales how fast blobs *travel*, but does NOT scale
        // per-frame force magnitudes (which caused runaway "table tennis" velocities: Speed=3 meant
        // 9x stronger forces on top of 3x faster integration, and viscosity couldn't drain it fast
        // enough).
        var speed = Math.Clamp((float)Speed, 0.05f, 5f);
        var g = (float)Gravity;
        var visc = Math.Clamp((float)Viscosity, 0.85f, 1f);
        var bounce = Math.Clamp((float)WallBounce, 0f, 1f);

        // Rest threshold: any wall-perpendicular velocity below this snaps to 0 after a bounce. This
        // prevents the perpetual jitter you'd otherwise get from gravity constantly re-accelerating a
        // resting blob into the floor.
        var restThreshold = 0.15f + g * 0.4f;

        foreach (var blob in _blobs)
        {
            // Apply forces — constant per-frame magnitudes, NOT scaled by Speed or dt.
            switch (Physics)
            {
                case BlobPhysics.Float:
                    // Classic lava lamp: warm blobs rise gently, tiny random horizontal drift.
                    blob.VY -= g * 0.03f;
                    blob.VX += ((float)_random.NextDouble() - 0.5f) * 0.04f;
                    break;
                case BlobPhysics.Bounce:
                    // Pulled down; wall bounce gives that rubber-ball feel.
                    blob.VY += g * 0.06f;
                    break;
                case BlobPhysics.Gravity:
                    // Stronger downward pull, minimal randomness.
                    blob.VY += g * 0.1f;
                    break;
                case BlobPhysics.Wander:
                    blob.VX += ((float)_random.NextDouble() - 0.5f) * 0.02f;
                    blob.VY += ((float)_random.NextDouble() - 0.5f) * 0.02f;
                    break;
                case BlobPhysics.Chaos:
                    // Was 0.3f — insanely large. Even 0.1f with viscosity 0.98 gives a random walk
                    // with std-dev up to ~2.2 per axis, still very energetic without exploding.
                    blob.VX += ((float)_random.NextDouble() - 0.5f) * 0.1f;
                    blob.VY += ((float)_random.NextDouble() - 0.5f) * 0.1f;
                    break;
            }

            // Damping (drag).
            blob.VX *= visc;
            blob.VY *= visc;

            // Hard velocity clamp — a blob can never move more than 40% of its radius per frame.
            // Guarantees no wall tunneling and prevents any runaway velocity feedback, no matter
            // what params the user picks.
            var maxV = Math.Max(1.5f, blob.BaseRadius * 0.4f);
            var spMag = MathF.Sqrt(blob.VX * blob.VX + blob.VY * blob.VY);
            if (spMag > maxV)
            {
                var s = maxV / spMag;
                blob.VX *= s;
                blob.VY *= s;
            }

            // Integrate position — this is where Speed comes in, as a playback multiplier only.
            blob.X += blob.VX * speed;
            blob.Y += blob.VY * speed;

            // Wall collisions with rest state.
            if (blob.X - blob.Radius < 0)
            {
                blob.X = blob.Radius;
                blob.VX = -blob.VX * bounce;
                if (Math.Abs(blob.VX) < restThreshold) blob.VX = 0;
            }
            else if (blob.X + blob.Radius > w)
            {
                blob.X = w - blob.Radius;
                blob.VX = -blob.VX * bounce;
                if (Math.Abs(blob.VX) < restThreshold) blob.VX = 0;
            }

            if (blob.Y - blob.Radius < 0)
            {
                blob.Y = blob.Radius;
                blob.VY = -blob.VY * bounce;
                if (Math.Abs(blob.VY) < restThreshold) blob.VY = 0;
            }
            else if (blob.Y + blob.Radius > h)
            {
                blob.Y = h - blob.Radius;
                blob.VY = -blob.VY * bounce;
                if (Math.Abs(blob.VY) < restThreshold) blob.VY = 0;
            }

            // Visual: pulse radius (unchanged).
            blob.Radius = Pulse
                ? blob.BaseRadius * (1 + (float)Math.Sin(_time * 2 + blob.Phase) * 0.1f)
                : blob.BaseRadius;

            // Color cycle (frame-based, independent of Speed so colors don't wobble with playback rate).
            if (ColorCycle) blob.Hue = (blob.Hue + ColorCycleSpeed * 0.05f) % 360f;
        }

        // Blob-to-blob collision — positional separation + velocity reflection with damping.
        if (BlobCollision)
        {
            for (var i = 0; i < _blobs.Count; i++)
            for (var j = i + 1; j < _blobs.Count; j++)
            {
                var a = _blobs[i];
                var b = _blobs[j];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var distSq = dx * dx + dy * dy;
                var minDist = a.Radius + b.Radius;
                if (distSq >= minDist * minDist || distSq < 0.01f) continue;

                var dist = MathF.Sqrt(distSq);
                var nx = dx / dist;
                var ny = dy / dist;
                var overlap = minDist - dist;

                a.X -= nx * overlap * 0.5f;
                a.Y -= ny * overlap * 0.5f;
                b.X += nx * overlap * 0.5f;
                b.Y += ny * overlap * 0.5f;

                var relVX = b.VX - a.VX;
                var relVY = b.VY - a.VY;
                var velDot = relVX * nx + relVY * ny;
                if (velDot >= 0) continue;

                // Elastic-ish exchange (0.5 = perfectly inelastic on the normal, 1.0 = fully elastic).
                var restitution = 0.7f;
                var j2 = -(1 + restitution) * velDot * 0.5f;
                a.VX -= nx * j2;
                a.VY -= ny * j2;
                b.VX += nx * j2;
                b.VY += ny * j2;
            }
        }
    }

    private void Render()
    {
        if (_backBuffer == null) return;

        using var canvas = new SKCanvas(_backBuffer);
        canvas.Clear(BackgroundColor);

        var w = _canvas.Width;
        var h = _canvas.Height;

        // Render based on style
        switch (Style)
        {
            case LampStyle.Classic:
                RenderClassicMetaballs(canvas, w, h);
                break;
            case LampStyle.Sharp:
                RenderSharpBlobs(canvas);
                break;
            case LampStyle.Neon:
                RenderNeonBlobs(canvas);
                break;
            case LampStyle.Plasma:
                RenderPlasmaBlobs(canvas, w, h);
                break;
            case LampStyle.Liquid:
                RenderLiquidBlobs(canvas);
                break;
            case LampStyle.Metallic:
                RenderMetallicBlobs(canvas);
                break;
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(_backBuffer);
    }

    private void RenderClassicMetaballs(SKCanvas canvas, int w, int h)
    {
        if (_backBuffer == null) return;
        if (_blobs.Count == 0) return;

        var threshold = (float)MetaballThreshold;
        var smoothness = Smoothness;
        var res = Math.Max(1, Resolution);

        // Snapshot blobs once and cache their colors so the inner loop is allocation-free.
        var blobCount = _blobs.Count;
        Span<float> bx = stackalloc float[blobCount];
        Span<float> by = stackalloc float[blobCount];
        Span<float> br2 = stackalloc float[blobCount];
        Span<uint> bcol = stackalloc uint[blobCount];
        for (var k = 0; k < blobCount; k++)
        {
            var b = _blobs[k];
            bx[k] = b.X;
            by[k] = b.Y;
            br2[k] = b.Radius * b.Radius;
            var c = GetBlobColor(b);
            bcol[k] = (uint)(0xFF000000 | ((uint)c.Blue << 16) | ((uint)c.Green << 8) | c.Red);
        }

        // Read background as a premultiplied BGRA uint so we can splat it into holes without
        // re-clearing the canvas per row.
        var bg = (uint)(0xFF000000 | ((uint)BackgroundColor.Blue << 16) | ((uint)BackgroundColor.Green << 8) |
                        BackgroundColor.Red);
        var bgB = BackgroundColor.Blue;
        var bgG = BackgroundColor.Green;
        var bgR = BackgroundColor.Red;

        unsafe
        {
            var pixels = (uint*)_backBuffer.GetPixels().ToPointer();

            for (var y = 0; y < h; y += res)
            for (var x = 0; x < w; x += res)
            {
                var sum = 0f;
                var maxInfluence = 0f;
                var pickedColor = bcol[0];

                for (var k = 0; k < blobCount; k++)
                {
                    var dx = x - bx[k];
                    var dy = y - by[k];
                    var distSq = dx * dx + dy * dy;
                    if (distSq < 0.1f) distSq = 0.1f;
                    var influence = br2[k] / distSq;
                    sum += influence;
                    if (influence > maxInfluence)
                    {
                        maxInfluence = influence;
                        pickedColor = bcol[k];
                    }
                }

                var alpha = Math.Clamp((sum - threshold) * smoothness, 0, 1);
                uint px;
                if (alpha <= 0f)
                {
                    px = bg;
                }
                else
                {
                    var shimmerFactor = Shimmer
                        ? (1 + (float)Math.Sin(_time * 5 + x * 0.1f + y * 0.1f) * 0.1f)
                        : 1f;
                    var b = (byte)((pickedColor >> 16) & 0xFF);
                    var g = (byte)((pickedColor >> 8) & 0xFF);
                    var r = (byte)(pickedColor & 0xFF);
                    // Blend blob color over background using computed alpha.
                    var oneMinus = 1f - alpha;
                    var mixR = (byte)(r * alpha * shimmerFactor + bgR * oneMinus);
                    var mixG = (byte)(g * alpha * shimmerFactor + bgG * oneMinus);
                    var mixB = (byte)(b * alpha * shimmerFactor + bgB * oneMinus);
                    px = (uint)(0xFF000000 | ((uint)mixB << 16) | ((uint)mixG << 8) | mixR);
                }

                // Splat this sample across the resolution block.
                var yEnd = Math.Min(y + res, h);
                var xEnd = Math.Min(x + res, w);
                for (var yy = y; yy < yEnd; yy++)
                {
                    var row = yy * w;
                    for (var xx = x; xx < xEnd; xx++) pixels[row + xx] = px;
                }
            }
        }

        if (GlowStrength > 0)
        {
            // Soft additive glow on top of the just-written pixels.
            using var glowPaint = new SKPaint
            {
                ImageFilter = SKImageFilter.CreateBlur(GlowStrength * 2, GlowStrength * 2),
                BlendMode = SKBlendMode.Plus
            };
            using var snapshot = SKImage.FromBitmap(_backBuffer);
            canvas.DrawImage(snapshot, 0, 0, glowPaint);
        }
    }

    private void RenderSharpBlobs(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var blob in _blobs)
        {
            paint.Color = GetBlobColor(blob);
            canvas.DrawCircle(blob.X, blob.Y, blob.Radius, paint);

            if (GlowStrength > 0)
            {
                using var glowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = paint.Color.WithAlpha(60),
                    ImageFilter = SKImageFilter.CreateBlur(GlowStrength, GlowStrength)
                };
                canvas.DrawCircle(blob.X, blob.Y, blob.Radius, glowPaint);
            }
        }
    }

    private void RenderNeonBlobs(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var blob in _blobs)
        {
            var color = GetBlobColor(blob);

            // Outer glow
            for (var i = 5; i > 0; i--)
            {
                paint.Color = color.WithAlpha((byte)(30 * i));
                canvas.DrawCircle(blob.X, blob.Y, blob.Radius + i * 3, paint);
            }

            // Core
            paint.Color = SKColors.White;
            canvas.DrawCircle(blob.X, blob.Y, blob.Radius * 0.5f, paint);

            // Main blob
            paint.Color = color;
            canvas.DrawCircle(blob.X, blob.Y, blob.Radius * 0.8f, paint);
        }
    }

    private void RenderPlasmaBlobs(SKCanvas canvas, int w, int h)
    {
        if (_backBuffer == null) return;
        if (_blobs.Count == 0) return;

        var res = Math.Max(1, Resolution);
        var blobCount = _blobs.Count;
        Span<float> bx = stackalloc float[blobCount];
        Span<float> by = stackalloc float[blobCount];
        Span<float> br2x = stackalloc float[blobCount];
        Span<float> bhue = stackalloc float[blobCount];
        for (var k = 0; k < blobCount; k++)
        {
            var b = _blobs[k];
            bx[k] = b.X;
            by[k] = b.Y;
            br2x[k] = b.Radius * 2f;
            bhue[k] = b.Hue;
        }

        var bg = (uint)(0xFF000000 | ((uint)BackgroundColor.Blue << 16) | ((uint)BackgroundColor.Green << 8) |
                        BackgroundColor.Red);

        unsafe
        {
            var pixels = (uint*)_backBuffer.GetPixels().ToPointer();

            for (var y = 0; y < h; y += res)
            for (var x = 0; x < w; x += res)
            {
                var hue = 0f;
                var totalInfluence = 0f;

                for (var k = 0; k < blobCount; k++)
                {
                    var dx = x - bx[k];
                    var dy = y - by[k];
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    var influence = MathF.Max(0, 1 - dist / br2x[k]);
                    hue += bhue[k] * influence;
                    totalInfluence += influence;
                }

                uint px;
                if (totalInfluence > 0.1f)
                {
                    hue /= totalInfluence;
                    var color = HsvToRgb(hue, 1f, Math.Clamp(totalInfluence, 0, 1));
                    px = (uint)(0xFF000000 | ((uint)color.Blue << 16) | ((uint)color.Green << 8) | color.Red);
                }
                else
                {
                    px = bg;
                }

                var yEnd = Math.Min(y + res, h);
                var xEnd = Math.Min(x + res, w);
                for (var yy = y; yy < yEnd; yy++)
                {
                    var row = yy * w;
                    for (var xx = x; xx < xEnd; xx++) pixels[row + xx] = px;
                }
            }
        }
    }

    private void RenderLiquidBlobs(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var blob in _blobs)
        {
            var color = GetBlobColor(blob);
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(blob.X - blob.Radius * 0.3f, blob.Y - blob.Radius * 0.3f),
                blob.Radius * 1.5f,
                new[] { color.WithAlpha(180), color.WithAlpha(100), color.WithAlpha(20), SKColors.Transparent },
                new[] { 0f, 0.5f, 0.8f, 1f },
                SKShaderTileMode.Clamp);

            paint.Shader = shader;
            canvas.DrawCircle(blob.X, blob.Y, blob.Radius * 1.2f, paint);
        }
    }

    private void RenderMetallicBlobs(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var blob in _blobs)
        {
            var color = GetBlobColor(blob);

            // Metallic gradient
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(blob.X - blob.Radius * 0.4f, blob.Y - blob.Radius * 0.4f),
                blob.Radius,
                new[]
                {
                    SKColors.White, color.WithAlpha(220), Darken(color, 0.6f), Darken(color, 0.3f),
                    SKColors.Black.WithAlpha(100)
                },
                new[] { 0f, 0.3f, 0.6f, 0.85f, 1f },
                SKShaderTileMode.Clamp);

            paint.Shader = shader;
            canvas.DrawCircle(blob.X, blob.Y, blob.Radius, paint);

            // Specular highlight
            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White.WithAlpha(180)
            };
            canvas.DrawCircle(blob.X - blob.Radius * 0.35f, blob.Y - blob.Radius * 0.35f, blob.Radius * 0.25f,
                highlightPaint);
        }
    }

    private SKColor GetBlobColor(Blob blob)
    {
        if (ColorMode == "Single") return PrimaryColor;

        if (ColorMode == "Multi")
            return HsvToRgb(blob.Hue, 1f, 1f);

        // Gradient between primary and secondary
        var t = (float)Math.Sin(blob.Hue * Math.PI / 180) * 0.5f + 0.5f;
        return Lerp(PrimaryColor, SecondaryColor, t);
    }

    private static SKColor HsvToRgb(float h, float s, float v)
    {
        h = (h % 360 + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        float r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);

        return new SKColor((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static SKColor Lerp(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t),
            (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t));
    }

    private static SKColor Darken(SKColor c, float factor)
    {
        return new SKColor((byte)(c.Red * factor), (byte)(c.Green * factor), (byte)(c.Blue * factor));
    }

    // ── Presets ──────────────────────────────────────────────────────────────
    [ExtensionMethod("Preset: Classic Lava", "Traditional orange/red lava lamp", Category = "Presets", Order = 1)]
    public void PresetClassic()
    {
        Style = LampStyle.Classic;
        Physics = BlobPhysics.Float;
        ColorMode = "Single";
        PrimaryColor = new SKColor(255, 69, 0);
        SecondaryColor = new SKColor(255, 215, 0);
        ColorCycle = false;
        BlobCount = 6;
        Speed = 1.0;
        Gravity = 0.5;
        GlowStrength = 2;
        Pulse = true;
        Shimmer = false;
    }

    [ExtensionMethod("Preset: Neon Dreams", "Vibrant glowing blobs", Category = "Presets", Order = 2)]
    public void PresetNeon()
    {
        Style = LampStyle.Neon;
        Physics = BlobPhysics.Float;
        ColorMode = "Multi";
        ColorCycle = true;
        ColorCycleSpeed = 30;
        BlobCount = 10;
        Speed = 1.5;
        GlowStrength = 5;
        Pulse = true;
        Shimmer = true;
    }

    [ExtensionMethod("Preset: Liquid Metal", "Chrome-like metallic blobs", Category = "Presets", Order = 3)]
    public void PresetMetallic()
    {
        Style = LampStyle.Metallic;
        Physics = BlobPhysics.Bounce;
        ColorMode = "Multi";
        ColorCycle = true;
        ColorCycleSpeed = 15;
        BlobCount = 8;
        Speed = 2.0;
        WallBounce = 0.9;
        BlobCollision = true;
        GlowStrength = 3;
    }

    [ExtensionMethod("Preset: Plasma Storm", "Chaotic color-shifting blobs", Category = "Presets", Order = 4)]
    public void PresetPlasma()
    {
        Style = LampStyle.Plasma;
        Physics = BlobPhysics.Chaos;
        ColorMode = "Multi";
        ColorCycle = true;
        ColorCycleSpeed = 50;
        BlobCount = 15;
        Speed = 3.0;
        MetaballThreshold = 0.8;
        Viscosity = 0.95;
        BlobCollision = false;
    }

    [ExtensionMethod("Preset: Gentle Flow", "Slow, meditative movement", Category = "Presets", Order = 5)]
    public void PresetGentle()
    {
        Style = LampStyle.Liquid;
        Physics = BlobPhysics.Wander;
        ColorMode = "Gradient";
        PrimaryColor = new SKColor(100, 150, 255);
        SecondaryColor = new SKColor(255, 150, 200);
        ColorCycle = false;
        BlobCount = 5;
        Speed = 0.5;
        Gravity = 0.2;
        Viscosity = 0.99;
        GlowStrength = 1;
        Pulse = false;
        Shimmer = false;
    }

    private class Blob
    {
        public float X;
        public float Y;
        public float VX;
        public float VY;
        public float Radius;
        public float BaseRadius;
        public float Hue;
        public float Phase;
    }
}
