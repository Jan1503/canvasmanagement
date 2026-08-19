using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.Aquarium;

[ExtensionInfo("Aquarium",
    "Animated aquarium with colorful swimming fish and bubbles",
    "Nature",
    IconResourceName = "aquarium.svg")]
public class AquariumExtension : IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _renderLock = new();

    // Double buffering to prevent flicker
    private SKBitmap? _backBuffer;
    private readonly List<Bubble> _bubbles = new();
    private readonly List<Crab> _crabs = new();
    private bool _disposed;

    private readonly List<Fish> _fish = new();
    private readonly List<Plant> _plants = new();
    private readonly Random _random = new();
    private readonly List<Seahorse> _seahorses = new();
    private readonly List<Shark> _sharks = new();
    private readonly List<Shell> _shells = new();
    private readonly List<Snail> _snails = new();
    private readonly List<Starfish> _starfishes = new();
    private float _time;
    private Timer? _updateTimer;

    // Scale factor relative to the 384x192 design; creature sizes and vertical bands are
    // multiplied by this so the aquarium fits any panel (and avoids negative random ranges).
    private float _scale = 1f;

    internal AquariumExtension(ICanvas canvas)
    {
        _canvas = canvas;
        _scale = DisplayScale.GetScale(canvas.Width, canvas.Height);
    }

    /// <summary>Scales a design pixel value to the current panel (min 1).</summary>
    private int Sc(int designValue)
    {
        return Math.Max(1, (int)Math.Round(designValue * _scale));
    }

    /// <summary>Random value in a design range, scaled to the panel (always min &lt; max).</summary>
    private int ScRange(int designMin, int designMax)
    {
        var a = Sc(designMin);
        var b = Sc(designMax);
        return b > a ? _random.Next(a, b) : a;
    }

    public bool IsRunning { get; private set; }

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

        // Create back buffer
        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);

        InitializeAquarium();

        _updateTimer = new Timer(33); // ~30 FPS
        _updateTimer.Elapsed += OnUpdate;
        _updateTimer.AutoReset = true;
        _updateTimer.Start();

        IsRunning = true;
        Console.WriteLine("Aquarium started");
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _updateTimer?.Stop();
        _updateTimer?.Dispose();
        _updateTimer = null;

        _backBuffer?.Dispose();
        _backBuffer = null;

        try
        {
            _canvas.Clear(WaterColor);
        }
        catch
        {
        }

        IsRunning = false;
        Console.WriteLine("Aquarium stopped");
    }

    private void InitializeAquarium()
    {
        _fish.Clear();
        _bubbles.Clear();
        _plants.Clear();
        _sharks.Clear();
        _crabs.Clear();
        _snails.Clear();
        _shells.Clear();
        _seahorses.Clear();
        _starfishes.Clear();

        // Create fish with random properties and types
        var fishColors = new[]
        {
            new SKColor(255, 140, 0), // Orange
            new SKColor(255, 215, 0), // Gold
            new SKColor(255, 69, 0), // Red-Orange
            new SKColor(0, 191, 255), // Deep Sky Blue
            new SKColor(138, 43, 226), // Blue-Violet
            new SKColor(50, 205, 50), // Lime Green
            new SKColor(255, 20, 147), // Deep Pink
            new SKColor(0, 255, 255) // Cyan
        };

        // Vertical bands are derived from the panel height so nothing goes off-screen or
        // produces an inverted random range on small displays.
        var h = _canvas.Height;
        for (var i = 0; i < FishCount; i++)
            _fish.Add(new Fish
            {
                X = _random.Next(_canvas.Width),
                Y = RandY(Sc(50), h - Sc(100)),
                Size = ScRange(15, 40),
                Speed = 0.5f + (float)_random.NextDouble() * 1.5f,
                Color = fishColors[_random.Next(fishColors.Length)],
                SwimPhase = (float)(_random.NextDouble() * Math.PI * 2),
                DirectionRight = _random.Next(2) == 0,
                Type = (FishType)_random.Next(4)
            });

        // Create sharks
        for (var i = 0; i < SharkCount; i++)
            _sharks.Add(new Shark
            {
                X = _random.Next(_canvas.Width),
                Y = RandY(Sc(30), h / 2),
                Size = ScRange(60, 100),
                Speed = 0.3f + (float)_random.NextDouble() * 0.5f,
                SwimPhase = (float)(_random.NextDouble() * Math.PI * 2),
                DirectionRight = _random.Next(2) == 0
            });

        // Create crabs
        for (var i = 0; i < CrabCount; i++)
            _crabs.Add(new Crab
            {
                X = _random.Next(_canvas.Width),
                Y = h - Sc(35),
                Size = ScRange(10, 20),
                Speed = 0.2f + (float)_random.NextDouble() * 0.3f,
                DirectionRight = _random.Next(2) == 0,
                WalkPhase = (float)(_random.NextDouble() * Math.PI * 2)
            });

        // Create seahorses
        for (var i = 0; i < SeahorseCount; i++)
            _seahorses.Add(new Seahorse
            {
                X = _random.Next(_canvas.Width),
                Y = RandY(h / 2, h - Sc(60)),
                Size = ScRange(20, 35),
                SwayPhase = (float)(_random.NextDouble() * Math.PI * 2),
                BobPhase = (float)(_random.NextDouble() * Math.PI * 2)
            });

        // Create snails
        for (var i = 0; i < SnailCount; i++)
            _snails.Add(new Snail
            {
                X = _random.Next(_canvas.Width),
                Y = RandY(h / 2, h - Sc(40)),
                Size = ScRange(8, 15),
                Speed = 0.05f + (float)_random.NextDouble() * 0.1f,
                DirectionRight = _random.Next(2) == 0
            });

        // Create starfish
        for (var i = 0; i < StarfishCount; i++)
            _starfishes.Add(new Starfish
            {
                X = _random.Next(_canvas.Width),
                Y = h - Sc(35) - _random.Next(Sc(10)),
                Size = ScRange(10, 20),
                Rotation = (float)(_random.NextDouble() * Math.PI * 2),
                Color = new[]
                {
                    new SKColor(255, 140, 0),
                    new SKColor(255, 69, 0),
                    new SKColor(255, 20, 147)
                }[_random.Next(3)]
            });

        // Create shells
        for (var i = 0; i < ShellCount; i++)
            _shells.Add(new Shell
            {
                X = _random.Next(_canvas.Width),
                Y = h - Sc(30) - _random.Next(Sc(15)),
                Size = ScRange(6, 15),
                Rotation = (float)(_random.NextDouble() * Math.PI),
                Type = _random.Next(3)
            });

        // Create bubbles
        for (var i = 0; i < BubbleCount; i++)
            _bubbles.Add(new Bubble
            {
                X = _random.Next(_canvas.Width),
                Y = _random.Next(_canvas.Height),
                Size = ScRange(3, 10),
                Speed = 0.2f + (float)_random.NextDouble() * 0.5f,
                WobblePhase = (float)(_random.NextDouble() * Math.PI * 2)
            });

        // Create plants with more variation
        for (var i = 0; i < PlantCount; i++)
            _plants.Add(new Plant
            {
                X = _random.Next(_canvas.Width),
                Height = ScRange(80, 150),
                SwayPhase = (float)(_random.NextDouble() * Math.PI * 2),
                Segments = _random.Next(8, 15),
                Width = Sc(3) + _random.Next(Math.Max(1, Sc(4))),
                Type = _random.Next(2) // 0 = kelp, 1 = seagrass
            });
    }

    /// <summary>Random Y within [min,max], guarding against inverted ranges on small panels.</summary>
    private int RandY(int min, int max)
    {
        if (min < 0) min = 0;
        if (max <= min) max = min + 1;
        return _random.Next(min, max);
    }

    private void OnUpdate(object? sender, ElapsedEventArgs e)
    {
        if (!IsRunning) return;

        try
        {
            _updateTimer?.Stop();

            _time += 0.03f * (float)AnimationSpeed;

            // Update fish
            foreach (var fish in _fish)
            {
                fish.SwimPhase += 0.1f * (float)AnimationSpeed;

                if (fish.DirectionRight)
                {
                    fish.X += fish.Speed * (float)AnimationSpeed;
                    if (fish.X > _canvas.Width + fish.Size)
                        fish.X = -fish.Size;
                }
                else
                {
                    fish.X -= fish.Speed * (float)AnimationSpeed;
                    if (fish.X < -fish.Size)
                        fish.X = _canvas.Width + fish.Size;
                }

                fish.Y += (float)Math.Sin(fish.SwimPhase) * 0.3f * (float)AnimationSpeed;
                // Proportional swim band (raw 50/100px margins go negative on short panels and crash Clamp).
                fish.Y = Math.Clamp(fish.Y, _canvas.Height * 0.12f, _canvas.Height * 0.88f);
            }

            // Update sharks
            foreach (var shark in _sharks)
            {
                shark.SwimPhase += 0.05f * (float)AnimationSpeed;

                if (shark.DirectionRight)
                {
                    shark.X += shark.Speed * (float)AnimationSpeed;
                    if (shark.X > _canvas.Width + shark.Size * 2)
                        shark.X = -shark.Size * 2;
                }
                else
                {
                    shark.X -= shark.Speed * (float)AnimationSpeed;
                    if (shark.X < -shark.Size * 2)
                        shark.X = _canvas.Width + shark.Size * 2;
                }

                shark.Y += (float)Math.Sin(shark.SwimPhase) * 0.2f * (float)AnimationSpeed;
                shark.Y = Math.Clamp(shark.Y, _canvas.Height * 0.08f, _canvas.Height * 0.55f);
            }

            // Update crabs
            foreach (var crab in _crabs)
            {
                crab.WalkPhase += 0.15f * (float)AnimationSpeed;

                if (crab.DirectionRight)
                {
                    crab.X += crab.Speed * (float)AnimationSpeed;
                    if (crab.X > _canvas.Width + 30)
                        crab.X = -30;
                }
                else
                {
                    crab.X -= crab.Speed * (float)AnimationSpeed;
                    if (crab.X < -30)
                        crab.X = _canvas.Width + 30;
                }
            }

            // Update snails
            foreach (var snail in _snails)
                if (snail.DirectionRight)
                {
                    snail.X += snail.Speed * (float)AnimationSpeed;
                    if (snail.X > _canvas.Width + 20)
                        snail.X = -20;
                }
                else
                {
                    snail.X -= snail.Speed * (float)AnimationSpeed;
                    if (snail.X < -20)
                        snail.X = _canvas.Width + 20;
                }

            // Update seahorses
            foreach (var seahorse in _seahorses)
            {
                seahorse.SwayPhase += 0.05f * (float)AnimationSpeed;
                seahorse.BobPhase += 0.08f * (float)AnimationSpeed;
            }

            // Update bubbles
            foreach (var bubble in _bubbles)
            {
                bubble.Y -= bubble.Speed * (float)AnimationSpeed;
                bubble.WobblePhase += 0.05f * (float)AnimationSpeed;
                bubble.X += (float)Math.Sin(bubble.WobblePhase) * 0.5f;

                if (bubble.Y < -bubble.Size)
                {
                    bubble.Y = _canvas.Height + bubble.Size;
                    bubble.X = _random.Next(_canvas.Width);
                }
            }

            Render();

            if (IsRunning && _updateTimer != null) _updateTimer.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Aquarium update error: {ex.Message}");
            try
            {
                _updateTimer?.Start();
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

        lock (_renderLock)
        {
            try
            {
                using var canvas = new SKCanvas(_backBuffer);

                // Clear with background color
                canvas.Clear(BackgroundColor);

                // Draw water gradient background
                using var waterGradient = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, _canvas.Height),
                    new[]
                    {
                        new SKColor(0, 40, 80),
                        WaterColor,
                        new SKColor(0, 10, 30)
                    },
                    new[] { 0f, 0.5f, 1f },
                    SKShaderTileMode.Clamp);

                using var waterPaint = new SKPaint { Shader = waterGradient };
                canvas.DrawRect(0, 0, _canvas.Width, _canvas.Height, waterPaint);

                // Draw light rays
                if (ShowLightRays) DrawLightRays(canvas);

                // Draw sand at bottom (before creatures)
                DrawSand(canvas);

                // Draw shells on sand
                foreach (var shell in _shells) DrawShell(canvas, shell);

                // Draw starfish on sand
                foreach (var starfish in _starfishes) DrawStarfish(canvas, starfish);

                // Draw plants (background)
                foreach (var plant in _plants) DrawPlant(canvas, plant);

                // Draw snails on plants
                foreach (var snail in _snails) DrawSnail(canvas, snail);

                // Draw crabs on sand
                foreach (var crab in _crabs) DrawCrab(canvas, crab);

                // Draw seahorses
                foreach (var seahorse in _seahorses) DrawSeahorse(canvas, seahorse);

                // Draw fish (mid-layer)
                foreach (var fish in _fish) DrawFish(canvas, fish);

                // Draw sharks (top layer with fish)
                foreach (var shark in _sharks) DrawShark(canvas, shark);

                // Draw bubbles (foreground)
                foreach (var bubble in _bubbles) DrawBubble(canvas, bubble);

                canvas.Flush();_canvas.SubmitCompletedFrame(_backBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render error: {ex.Message}");
            }
        }
    }

    private void DrawFish(SKCanvas canvas, Fish fish)
    {
        canvas.Save();
        canvas.Translate(fish.X, fish.Y);

        if (!fish.DirectionRight) canvas.Scale(-1, 1);

        var bodyWave = (float)Math.Sin(fish.SwimPhase) * 0.15f;

        // Draw different fish types
        switch (fish.Type)
        {
            case FishType.Normal:
                DrawNormalFish(canvas, fish, bodyWave);
                break;
            case FishType.Angelfish:
                DrawAngelfish(canvas, fish, bodyWave);
                break;
            case FishType.Pufferfish:
                DrawPufferfish(canvas, fish, bodyWave);
                break;
            case FishType.Clownfish:
                DrawClownfish(canvas, fish, bodyWave);
                break;
        }

        canvas.Restore();
    }

    private void DrawNormalFish(SKCanvas canvas, Fish fish, float bodyWave)
    {
        var bodyLength = fish.Size;
        var bodyHeight = fish.Size * 0.6f;

        using var path = new SKPath();
        path.AddOval(new SKRect(-bodyLength / 2, -bodyHeight / 2, bodyLength / 2, bodyHeight / 2));

        using var bodyGradient = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            fish.Size / 2,
            new[]
            {
                fish.Color,
                new SKColor(
                    (byte)(fish.Color.Red * 0.7),
                    (byte)(fish.Color.Green * 0.7),
                    (byte)(fish.Color.Blue * 0.7))
            },
            null,
            SKShaderTileMode.Clamp);

        using var bodyPaint = new SKPaint { Shader = bodyGradient, IsAntialias = true };
        canvas.DrawPath(path, bodyPaint);

        using var outlinePaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 100),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawPath(path, outlinePaint);

        // Tail
        using var tailPath = new SKPath();
        tailPath.MoveTo(-bodyLength / 2, 0);
        tailPath.CubicTo(
            -bodyLength / 2 - fish.Size * 0.3f, -fish.Size * 0.3f + bodyWave * fish.Size,
            -bodyLength / 2 - fish.Size * 0.4f, -fish.Size * 0.4f + bodyWave * fish.Size,
            -bodyLength / 2 - fish.Size * 0.5f, bodyWave * fish.Size);
        tailPath.CubicTo(
            -bodyLength / 2 - fish.Size * 0.4f, fish.Size * 0.4f + bodyWave * fish.Size,
            -bodyLength / 2 - fish.Size * 0.3f, fish.Size * 0.3f + bodyWave * fish.Size,
            -bodyLength / 2, 0);

        using var tailPaint = new SKPaint { Color = fish.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawPath(tailPath, tailPaint);
        canvas.DrawPath(tailPath, outlinePaint);

        // Eye
        using var eyePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(bodyLength * 0.2f, -bodyHeight * 0.2f, fish.Size * 0.1f, eyePaint);

        using var pupilPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(bodyLength * 0.25f, -bodyHeight * 0.2f, fish.Size * 0.05f, pupilPaint);

        // Fins
        using var finPaint = new SKPaint
        {
            Color = new SKColor(
                (byte)(fish.Color.Red * 0.8),
                (byte)(fish.Color.Green * 0.8),
                (byte)(fish.Color.Blue * 0.8)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var topFin = new SKPath();
        topFin.MoveTo(0, -bodyHeight / 2);
        topFin.LineTo(-fish.Size * 0.2f + bodyWave * fish.Size * 0.5f, -bodyHeight / 2 - fish.Size * 0.3f);
        topFin.LineTo(fish.Size * 0.1f, -bodyHeight / 2);
        canvas.DrawPath(topFin, finPaint);

        canvas.DrawOval(-fish.Size * 0.1f, bodyHeight * 0.3f, fish.Size * 0.2f, fish.Size * 0.15f, finPaint);
    }

    private void DrawAngelfish(SKCanvas canvas, Fish fish, float bodyWave)
    {
        var bodySize = fish.Size;

        // Triangular body shape
        using var bodyPath = new SKPath();
        bodyPath.MoveTo(bodySize * 0.3f, 0); // Front point
        bodyPath.QuadTo(0, -bodySize * 0.7f, -bodySize * 0.4f, -bodySize * 0.2f); // Top curve
        bodyPath.QuadTo(-bodySize * 0.5f, 0, -bodySize * 0.4f, bodySize * 0.2f); // Back
        bodyPath.QuadTo(0, bodySize * 0.7f, bodySize * 0.3f, 0); // Bottom curve
        bodyPath.Close();

        using var bodyGradient = SKShader.CreateLinearGradient(
            new SKPoint(-bodySize * 0.5f, 0),
            new SKPoint(bodySize * 0.3f, 0),
            new[]
            {
                new SKColor(
                    (byte)(fish.Color.Red * 0.6),
                    (byte)(fish.Color.Green * 0.6),
                    (byte)(fish.Color.Blue * 0.6)),
                fish.Color
            },
            null,
            SKShaderTileMode.Clamp);

        using var bodyPaint = new SKPaint { Shader = bodyGradient, IsAntialias = true };
        canvas.DrawPath(bodyPath, bodyPaint);

        using var outlinePaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 100),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawPath(bodyPath, outlinePaint);

        // Stripes
        using var stripePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 80),
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawLine(0, -bodySize * 0.5f, 0, bodySize * 0.5f, stripePaint);
        canvas.DrawLine(-bodySize * 0.2f, -bodySize * 0.4f, -bodySize * 0.2f, bodySize * 0.4f, stripePaint);

        // Eye
        using var eyePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(bodySize * 0.15f, -bodySize * 0.15f, fish.Size * 0.1f, eyePaint);

        using var pupilPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(bodySize * 0.18f, -bodySize * 0.15f, fish.Size * 0.05f, pupilPaint);
    }

    private void DrawPufferfish(SKCanvas canvas, Fish fish, float bodyWave)
    {
        var radius = fish.Size * 0.5f;

        // Round body
        using var bodyGradient = SKShader.CreateRadialGradient(
            new SKPoint(-radius * 0.2f, -radius * 0.2f),
            radius,
            new[]
            {
                fish.Color,
                new SKColor(
                    (byte)(fish.Color.Red * 0.6),
                    (byte)(fish.Color.Green * 0.6),
                    (byte)(fish.Color.Blue * 0.6))
            },
            null,
            SKShaderTileMode.Clamp);

        using var bodyPaint = new SKPaint { Shader = bodyGradient, IsAntialias = true };
        canvas.DrawCircle(0, 0, radius, bodyPaint);

        // Spikes
        using var spikePaint = new SKPaint
        {
            Color = new SKColor(
                (byte)(fish.Color.Red * 0.8),
                (byte)(fish.Color.Green * 0.8),
                (byte)(fish.Color.Blue * 0.8)),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        for (var i = 0; i < 12; i++)
        {
            var angle = (float)(i * Math.PI * 2 / 12);
            var spikeLength = radius * 0.3f;
            var x1 = (float)Math.Cos(angle) * radius;
            var y1 = (float)Math.Sin(angle) * radius;
            var x2 = (float)Math.Cos(angle) * (radius + spikeLength);
            var y2 = (float)Math.Sin(angle) * (radius + spikeLength);
            canvas.DrawLine(x1, y1, x2, y2, spikePaint);
        }

        // Spots
        using var spotPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 100),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(radius * 0.3f, -radius * 0.3f, radius * 0.15f, spotPaint);
        canvas.DrawCircle(-radius * 0.3f, radius * 0.2f, radius * 0.12f, spotPaint);
        canvas.DrawCircle(radius * 0.1f, radius * 0.4f, radius * 0.1f, spotPaint);

        // Eye
        using var eyePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(radius * 0.4f, -radius * 0.2f, fish.Size * 0.12f, eyePaint);

        using var pupilPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(radius * 0.45f, -radius * 0.2f, fish.Size * 0.06f, pupilPaint);

        // Small tail fins
        using var finPaint = new SKPaint { Color = fish.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var tailFin = new SKPath();
        tailFin.MoveTo(-radius, 0);
        tailFin.LineTo(-radius - fish.Size * 0.2f, -fish.Size * 0.15f);
        tailFin.LineTo(-radius - fish.Size * 0.2f, fish.Size * 0.15f);
        tailFin.Close();
        canvas.DrawPath(tailFin, finPaint);
    }

    private void DrawClownfish(SKCanvas canvas, Fish fish, float bodyWave)
    {
        var bodyLength = fish.Size;
        var bodyHeight = fish.Size * 0.7f;

        // Rounder body
        using var bodyPath = new SKPath();
        bodyPath.AddOval(new SKRect(-bodyLength / 2, -bodyHeight / 2, bodyLength / 2, bodyHeight / 2));

        using var bodyGradient = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            fish.Size / 2,
            new[]
            {
                fish.Color,
                new SKColor(
                    (byte)(fish.Color.Red * 0.8),
                    (byte)(fish.Color.Green * 0.8),
                    (byte)(fish.Color.Blue * 0.8))
            },
            null,
            SKShaderTileMode.Clamp);

        using var bodyPaint = new SKPaint { Shader = bodyGradient, IsAntialias = true };
        canvas.DrawPath(bodyPath, bodyPaint);

        // White stripes (characteristic of clownfish)
        using var stripePaint = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = fish.Size * 0.15f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(bodyLength * 0.1f, -bodyHeight * 0.6f, bodyLength * 0.1f, bodyHeight * 0.6f, stripePaint);
        canvas.DrawLine(-bodyLength * 0.2f, -bodyHeight * 0.5f, -bodyLength * 0.2f, bodyHeight * 0.5f, stripePaint);

        // Black outline on stripes
        using var blackOutline = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = fish.Size * 0.18f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(bodyLength * 0.1f, -bodyHeight * 0.6f, bodyLength * 0.1f, bodyHeight * 0.6f, blackOutline);
        canvas.DrawLine(-bodyLength * 0.2f, -bodyHeight * 0.5f, -bodyLength * 0.2f, bodyHeight * 0.5f, blackOutline);

        // Draw white on top
        canvas.DrawLine(bodyLength * 0.1f, -bodyHeight * 0.6f, bodyLength * 0.1f, bodyHeight * 0.6f, stripePaint);
        canvas.DrawLine(-bodyLength * 0.2f, -bodyHeight * 0.5f, -bodyLength * 0.2f, bodyHeight * 0.5f, stripePaint);

        // Rounded tail
        using var tailPath = new SKPath();
        tailPath.MoveTo(-bodyLength / 2, 0);
        tailPath.QuadTo(-bodyLength * 0.7f, -bodyHeight * 0.4f, -bodyLength * 0.8f, 0);
        tailPath.QuadTo(-bodyLength * 0.7f, bodyHeight * 0.4f, -bodyLength / 2, 0);

        using var tailPaint = new SKPaint { Color = fish.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawPath(tailPath, tailPaint);

        // Eye
        using var eyePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(bodyLength * 0.3f, -bodyHeight * 0.2f, fish.Size * 0.1f, eyePaint);

        using var pupilPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(bodyLength * 0.33f, -bodyHeight * 0.2f, fish.Size * 0.05f, pupilPaint);

        // Fins
        using var finPaint = new SKPaint
        {
            Color = new SKColor(
                (byte)(fish.Color.Red * 0.9),
                (byte)(fish.Color.Green * 0.9),
                (byte)(fish.Color.Blue * 0.9)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        // Top fin (more rounded)
        using var topFin = new SKPath();
        topFin.MoveTo(0, -bodyHeight / 2);
        topFin.QuadTo(-fish.Size * 0.1f + bodyWave * fish.Size * 0.5f, -bodyHeight / 2 - fish.Size * 0.4f,
            fish.Size * 0.1f, -bodyHeight / 2);
        canvas.DrawPath(topFin, finPaint);

        // Side fins
        canvas.DrawOval(-fish.Size * 0.1f, bodyHeight * 0.3f, fish.Size * 0.25f, fish.Size * 0.2f, finPaint);
    }

    private void DrawBubble(SKCanvas canvas, Bubble bubble)
    {
        using var bubblePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 100),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        // Draw bubble with highlight
        canvas.DrawCircle(bubble.X, bubble.Y, bubble.Size, bubblePaint);

        // Highlight
        using var highlightPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 180),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(
            bubble.X - bubble.Size * 0.3f,
            bubble.Y - bubble.Size * 0.3f,
            bubble.Size * 0.4f,
            highlightPaint);
    }

    private void DrawPlant(SKCanvas canvas, Plant plant)
    {
        if (plant.Type == 0)
            DrawKelp(canvas, plant);
        else
            DrawSeagrass(canvas, plant);
    }

    private void DrawKelp(SKCanvas canvas, Plant plant)
    {
        // Realistic kelp with smooth curves and organic leaves
        var stemColor = new SKColor(40, 80, 40);
        var leafColor = new SKColor(60, 120, 60);

        var baseX = plant.X;
        float baseY = _canvas.Height - 30;

        // Main stem using smooth bezier curves
        using var stemPath = new SKPath();
        stemPath.MoveTo(baseX, baseY);

        var segmentHeight = plant.Height / plant.Segments;
        var points = new List<SKPoint>();
        points.Add(new SKPoint(baseX, baseY));

        for (var i = 1; i <= plant.Segments; i++)
        {
            var progress = i / (float)plant.Segments;
            var sway = (float)Math.Sin(_time * 2 + plant.SwayPhase + progress * Math.PI) * (progress * 15f);
            var x = baseX + sway;
            var y = baseY - i * segmentHeight;
            points.Add(new SKPoint(x, y));
        }

        // Draw smooth curve through points using quadratic bezier
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            var control = new SKPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
            stemPath.QuadTo(control.X, control.Y, p2.X, p2.Y);
        }

        using var stemPaint = new SKPaint
        {
            Color = stemColor,
            StrokeWidth = plant.Width,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawPath(stemPath, stemPaint);

        // Draw organic leaves along stem
        for (var i = 2; i < points.Count; i += 2)
        {
            float leafDirection = i % 4 == 0 ? 1 : -1;
            DrawKelpLeaf(canvas, points[i].X, points[i].Y, plant.Width * 2, leafDirection, leafColor);
        }
    }

    private void DrawKelpLeaf(SKCanvas canvas, float x, float y, float size, float direction, SKColor color)
    {
        using var leafPath = new SKPath();

        // Organic teardrop shape
        leafPath.MoveTo(x, y);
        leafPath.CubicTo(
            x + direction * size * 0.3f, y - size * 0.5f,
            x + direction * size * 0.8f, y - size * 1.2f,
            x + direction * size * 0.2f, y - size * 2.0f);
        leafPath.CubicTo(
            x - direction * size * 0.1f, y - size * 1.5f,
            x - direction * size * 0.1f, y - size * 0.8f,
            x, y);
        leafPath.Close();

        using var leafPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawPath(leafPath, leafPaint);

        // Add vein detail
        using var veinPaint = new SKPaint
        {
            Color = new SKColor(
                (byte)(color.Red * 0.7),
                (byte)(color.Green * 0.7),
                (byte)(color.Blue * 0.7)),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawLine(x, y, x + direction * size * 0.2f, y - size * 2.0f, veinPaint);
    }

    private void DrawSeagrass(SKCanvas canvas, Plant plant)
    {
        // Thin flowing seagrass
        var grassColor = new SKColor(80, 150, 80);

        var baseX = plant.X;
        float baseY = _canvas.Height - 30;

        using var grassPath = new SKPath();
        grassPath.MoveTo(baseX, baseY);

        var segmentHeight = plant.Height / plant.Segments;

        for (var i = 1; i <= plant.Segments; i++)
        {
            var progress = i / (float)plant.Segments;
            var sway = (float)Math.Sin(_time * 3 + plant.SwayPhase + progress * Math.PI * 2) * (progress * 10f);
            var x = baseX + sway;
            var y = baseY - i * segmentHeight;

            if (i == 1)
            {
                grassPath.LineTo(x, y);
            }
            else
            {
                var prevPoint = grassPath.LastPoint;
                var control = new SKPoint((prevPoint.X + x) / 2 + sway * 0.3f, (prevPoint.Y + y) / 2);
                grassPath.QuadTo(control.X, control.Y, x, y);
            }
        }

        using var grassPaint = new SKPaint
        {
            Color = grassColor,
            StrokeWidth = plant.Width * 0.6f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawPath(grassPath, grassPaint);
    }

    private void DrawSand(SKCanvas canvas)
    {
        // Draw sand with better gradient
        using var sandGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, _canvas.Height - 35),
            new SKPoint(0, _canvas.Height),
            new[]
            {
                new SKColor(180, 160, 110),
                new SKColor(194, 178, 128)
            },
            null,
            SKShaderTileMode.Clamp);

        using var sandPaint = new SKPaint
        {
            Shader = sandGradient,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(0, _canvas.Height - 35, _canvas.Width, 35, sandPaint);

        // Add pebbles for texture
        using var pebblePaint = new SKPaint
        {
            Color = new SKColor(170, 150, 100, 80),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        for (var i = 0; i < 30; i++)
        {
            var x = _random.Next(_canvas.Width);
            var y = _canvas.Height - _random.Next(30);
            float size = 1 + _random.Next(3);
            canvas.DrawOval(x, y, size * 1.5f, size, pebblePaint);
        }
    }

    private void DrawShark(SKCanvas canvas, Shark shark)
    {
        canvas.Save();
        canvas.Translate(shark.X, shark.Y);

        if (!shark.DirectionRight) canvas.Scale(-1, 1);

        var bodyWave = (float)Math.Sin(shark.SwimPhase) * 0.1f;
        var length = shark.Size;
        var height = shark.Size * 0.3f;

        // Shark body - torpedo shape
        using var bodyPath = new SKPath();
        bodyPath.MoveTo(-length * 0.5f, 0);
        bodyPath.CubicTo(-length * 0.3f, -height, length * 0.2f, -height, length * 0.5f, -height * 0.3f);
        bodyPath.LineTo(length * 0.5f, height * 0.3f);
        bodyPath.CubicTo(length * 0.2f, height, -length * 0.3f, height, -length * 0.5f, 0);
        bodyPath.Close();

        using var bodyPaint = new SKPaint
        {
            Color = new SKColor(70, 80, 90),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawPath(bodyPath, bodyPaint);

        // Dorsal fin
        using var dorsalFin = new SKPath();
        dorsalFin.MoveTo(0, -height);
        dorsalFin.LineTo(-length * 0.15f, -height - shark.Size * 0.25f);
        dorsalFin.LineTo(length * 0.1f, -height);
        dorsalFin.Close();
        canvas.DrawPath(dorsalFin, bodyPaint);

        // Tail fin
        using var tailFin = new SKPath();
        tailFin.MoveTo(-length * 0.5f, 0);
        tailFin.CubicTo(
            -length * 0.7f + bodyWave * length * 0.5f, -height * 1.2f,
            -length * 0.8f + bodyWave * length * 0.5f, -height * 1.5f,
            -length * 0.9f + bodyWave * length * 0.5f, -height * 0.8f);
        tailFin.CubicTo(
            -length * 0.75f + bodyWave * length * 0.5f, height * 0.3f,
            -length * 0.65f + bodyWave * length * 0.5f, height * 0.5f,
            -length * 0.5f, 0);
        tailFin.Close();
        canvas.DrawPath(tailFin, bodyPaint);

        // Eye
        using var eyePaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(length * 0.3f, -height * 0.5f, shark.Size * 0.03f, eyePaint);

        // Gills
        using var gillPaint = new SKPaint
        {
            Color = new SKColor(40, 50, 60),
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        for (var i = 0; i < 3; i++)
        {
            var x = length * 0.1f - i * length * 0.08f;
            canvas.DrawLine(x, -height * 0.6f, x - length * 0.05f, -height * 0.3f, gillPaint);
        }

        canvas.Restore();
    }

    private void DrawCrab(SKCanvas canvas, Crab crab)
    {
        canvas.Save();
        canvas.Translate(crab.X, crab.Y);

        if (!crab.DirectionRight) canvas.Scale(-1, 1);

        // Body
        using var bodyPaint = new SKPaint
        {
            Color = new SKColor(200, 50, 40),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawOval(-crab.Size * 0.6f, -crab.Size * 0.4f, crab.Size * 1.2f, crab.Size * 0.8f, bodyPaint);

        // Claws
        var clawWave = (float)Math.Sin(crab.WalkPhase) * 5;

        using var clawPath1 = new SKPath();
        clawPath1.MoveTo(-crab.Size * 0.6f, 0);
        clawPath1.LineTo(-crab.Size * 1.0f, -crab.Size * 0.3f + clawWave);
        clawPath1.LineTo(-crab.Size * 1.2f, -crab.Size * 0.2f + clawWave);
        clawPath1.LineTo(-crab.Size * 1.1f, -crab.Size * 0.4f + clawWave);
        clawPath1.Close();
        canvas.DrawPath(clawPath1, bodyPaint);

        using var clawPath2 = new SKPath();
        clawPath2.MoveTo(crab.Size * 0.6f, 0);
        clawPath2.LineTo(crab.Size * 1.0f, -crab.Size * 0.3f - clawWave);
        clawPath2.LineTo(crab.Size * 1.2f, -crab.Size * 0.2f - clawWave);
        clawPath2.LineTo(crab.Size * 1.1f, -crab.Size * 0.4f - clawWave);
        clawPath2.Close();
        canvas.DrawPath(clawPath2, bodyPaint);

        // Legs
        using var legPaint = new SKPaint
        {
            Color = new SKColor(180, 40, 30),
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        var legWave = (float)Math.Sin(crab.WalkPhase);
        for (var i = -2; i <= 2; i++)
        {
            if (i == 0) continue;
            var x = i * crab.Size * 0.15f;
            var legAngle = i * 0.3f + legWave * 0.2f;
            canvas.DrawLine(x, crab.Size * 0.2f, x + crab.Size * 0.4f * Math.Sign(i), crab.Size * 0.6f + legAngle * 10,
                legPaint);
        }

        // Eyes on stalks
        using var eyeStalkPaint = new SKPaint
        {
            Color = new SKColor(220, 80, 70),
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(-crab.Size * 0.2f, -crab.Size * 0.4f, -crab.Size * 0.25f, -crab.Size * 0.7f, eyeStalkPaint);
        canvas.DrawLine(crab.Size * 0.2f, -crab.Size * 0.4f, crab.Size * 0.25f, -crab.Size * 0.7f, eyeStalkPaint);

        using var eyePaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(-crab.Size * 0.25f, -crab.Size * 0.7f, crab.Size * 0.1f, eyePaint);
        canvas.DrawCircle(crab.Size * 0.25f, -crab.Size * 0.7f, crab.Size * 0.1f, eyePaint);

        canvas.Restore();
    }

    private void DrawSnail(SKCanvas canvas, Snail snail)
    {
        canvas.Save();
        canvas.Translate(snail.X, snail.Y);

        if (!snail.DirectionRight) canvas.Scale(-1, 1);

        // Shell (spiral)
        using var shellPaint = new SKPaint
        {
            Color = new SKColor(139, 90, 43),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var shellPath = new SKPath();
        float centerX = 0;
        var centerY = -snail.Size * 0.5f;

        for (float angle = 0; angle < Math.PI * 4; angle += 0.3f)
        {
            var radius = angle * snail.Size * 0.15f;
            var x = centerX + (float)Math.Cos(angle) * radius;
            var y = centerY + (float)Math.Sin(angle) * radius;

            if (angle == 0)
                shellPath.MoveTo(x, y);
            else
                shellPath.LineTo(x, y);
        }

        canvas.DrawPath(shellPath, shellPaint);
        canvas.DrawCircle(centerX, centerY, snail.Size * 0.4f, shellPaint);

        // Body
        using var bodyPaint = new SKPaint
        {
            Color = new SKColor(160, 140, 100),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var bodyPath = new SKPath();
        bodyPath.MoveTo(-snail.Size * 0.3f, 0);
        bodyPath.LineTo(snail.Size * 0.5f, 0);
        bodyPath.LineTo(snail.Size * 0.4f, snail.Size * 0.2f);
        bodyPath.LineTo(-snail.Size * 0.2f, snail.Size * 0.2f);
        bodyPath.Close();
        canvas.DrawPath(bodyPath, bodyPaint);

        // Antennae
        using var antennaPaint = new SKPaint
        {
            Color = new SKColor(140, 120, 80),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(snail.Size * 0.4f, 0, snail.Size * 0.5f, -snail.Size * 0.3f, antennaPaint);
        canvas.DrawLine(snail.Size * 0.3f, 0, snail.Size * 0.4f, -snail.Size * 0.25f, antennaPaint);

        canvas.Restore();
    }

    private void DrawSeahorse(SKCanvas canvas, Seahorse seahorse)
    {
        canvas.Save();
        canvas.Translate(seahorse.X, seahorse.Y);

        var sway = (float)Math.Sin(seahorse.SwayPhase) * 5;
        var bob = (float)Math.Sin(seahorse.BobPhase) * 3;

        canvas.Translate(sway, bob);

        // Body
        using var bodyPaint = new SKPaint
        {
            Color = new SKColor(255, 200, 50),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var bodyPath = new SKPath();
        bodyPath.MoveTo(0, seahorse.Size * 0.5f); // Tail
        bodyPath.CubicTo(
            -seahorse.Size * 0.3f, seahorse.Size * 0.2f,
            -seahorse.Size * 0.3f, 0,
            0, -seahorse.Size * 0.3f); // Body curve
        bodyPath.CubicTo(
            seahorse.Size * 0.2f, -seahorse.Size * 0.5f,
            seahorse.Size * 0.3f, -seahorse.Size * 0.7f,
            seahorse.Size * 0.2f, -seahorse.Size * 0.9f); // Head

        using var strokePaint = new SKPaint
        {
            Color = new SKColor(200, 150, 30),
            StrokeWidth = seahorse.Size * 0.15f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawPath(bodyPath, strokePaint);

        // Snout
        using var snoutPaint = new SKPaint
        {
            Color = new SKColor(220, 170, 40),
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(seahorse.Size * 0.2f, -seahorse.Size * 0.9f, seahorse.Size * 0.5f, -seahorse.Size * 0.95f,
            snoutPaint);

        // Dorsal fin
        using var finPath = new SKPath();
        finPath.MoveTo(seahorse.Size * 0.05f, -seahorse.Size * 0.2f);
        for (var i = 0; i < 5; i++)
        {
            var y = -seahorse.Size * 0.2f - i * seahorse.Size * 0.1f;
            finPath.LineTo(seahorse.Size * 0.15f, y);
            finPath.LineTo(seahorse.Size * 0.05f, y - seahorse.Size * 0.05f);
        }

        using var finPaint = new SKPaint
        {
            Color = new SKColor(255, 220, 100, 150),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawPath(finPath, finPaint);

        // Eye
        using var eyePaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(seahorse.Size * 0.25f, -seahorse.Size * 0.85f, seahorse.Size * 0.05f, eyePaint);

        canvas.Restore();
    }

    private void DrawShell(SKCanvas canvas, Shell shell)
    {
        canvas.Save();
        canvas.Translate(shell.X, shell.Y);
        canvas.RotateRadians(shell.Rotation);

        using var shellPaint = new SKPaint
        {
            Color = new SKColor(220, 200, 180),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var stripePaint = new SKPaint
        {
            Color = new SKColor(180, 160, 140),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        if (shell.Type == 0) // Spiral shell
        {
            using var spiralPath = new SKPath();
            for (float angle = 0; angle < Math.PI * 3; angle += 0.2f)
            {
                var radius = angle * shell.Size * 0.1f;
                var x = (float)Math.Cos(angle) * radius;
                var y = (float)Math.Sin(angle) * radius;

                if (angle == 0)
                    spiralPath.MoveTo(x, y);
                else
                    spiralPath.LineTo(x, y);
            }

            canvas.DrawPath(spiralPath, shellPaint);
        }
        else if (shell.Type == 1) // Clam shell
        {
            canvas.DrawOval(-shell.Size * 0.6f, -shell.Size * 0.4f, shell.Size * 1.2f, shell.Size * 0.8f, shellPaint);
            for (var i = 0; i < 5; i++)
            {
                var angle = -0.3f + i * 0.15f;
                canvas.DrawLine(0, 0,
                    (float)Math.Cos(angle) * shell.Size * 0.6f,
                    (float)Math.Sin(angle) * shell.Size * 0.4f, stripePaint);
            }
        }
        else // Conch
        {
            using var conchPath = new SKPath();
            conchPath.MoveTo(0, 0);
            conchPath.CubicTo(shell.Size * 0.3f, -shell.Size * 0.2f, shell.Size * 0.5f, -shell.Size * 0.1f,
                shell.Size * 0.6f, 0);
            conchPath.CubicTo(shell.Size * 0.5f, shell.Size * 0.3f, shell.Size * 0.2f, shell.Size * 0.4f, 0,
                shell.Size * 0.3f);
            conchPath.Close();
            canvas.DrawPath(conchPath, shellPaint);
        }

        canvas.Restore();
    }

    private void DrawStarfish(SKCanvas canvas, Starfish starfish)
    {
        canvas.Save();
        canvas.Translate(starfish.X, starfish.Y);
        canvas.RotateRadians(starfish.Rotation);

        using var starPaint = new SKPaint
        {
            Color = starfish.Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var starPath = new SKPath();

        for (var i = 0; i < 5; i++)
        {
            var angle1 = i * (float)Math.PI * 2 / 5 - (float)Math.PI / 2;
            var angle2 = (i + 0.5f) * (float)Math.PI * 2 / 5 - (float)Math.PI / 2;

            var x1 = (float)Math.Cos(angle1) * starfish.Size;
            var y1 = (float)Math.Sin(angle1) * starfish.Size;
            var x2 = (float)Math.Cos(angle2) * starfish.Size * 0.4f;
            var y2 = (float)Math.Sin(angle2) * starfish.Size * 0.4f;

            if (i == 0)
                starPath.MoveTo(x1, y1);
            else
                starPath.LineTo(x1, y1);

            starPath.LineTo(x2, y2);
        }

        starPath.Close();

        canvas.DrawPath(starPath, starPaint);

        // Add spots
        using var spotPaint = new SKPaint
        {
            Color = new SKColor(
                (byte)(starfish.Color.Red * 0.7),
                (byte)(starfish.Color.Green * 0.7),
                (byte)(starfish.Color.Blue * 0.7)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        for (var i = 0; i < 5; i++)
        {
            var angle = i * (float)Math.PI * 2 / 5;
            var x = (float)Math.Cos(angle) * starfish.Size * 0.5f;
            var y = (float)Math.Sin(angle) * starfish.Size * 0.5f;
            canvas.DrawCircle(x, y, starfish.Size * 0.1f, spotPaint);
        }

        canvas.Restore();
    }

    private void DrawLightRays(SKCanvas canvas)
    {
        using var rayPaint = new SKPaint
        {
            Color = new SKColor(100, 150, 200, 30),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        for (var i = 0; i < 5; i++)
        {
            var angle = (float)Math.Sin(_time * 0.5f + i) * 0.3f;
            var x1 = _canvas.Width * (0.2f + i * 0.2f);
            var x2 = x1 + (float)Math.Tan(angle) * _canvas.Height;

            using var path = new SKPath();
            path.MoveTo(x1, 0);
            path.LineTo(x2, _canvas.Height);
            path.LineTo(x2 + 50, _canvas.Height);
            path.LineTo(x1 + 50, 0);
            path.Close();

            canvas.DrawPath(path, rayPaint);
        }
    }

    #region Parameters

    [ExtensionParameter("Fish Count", "Number of fish swimming",
        DefaultValue = 8, MinValue = 1, MaxValue = 30)]
    public int FishCount { get; set; } = 8;

    [ExtensionParameter("Bubble Count", "Number of bubbles rising",
        DefaultValue = 15, MinValue = 0, MaxValue = 50)]
    public int BubbleCount { get; set; } = 15;

    [ExtensionParameter("Plant Count", "Number of seaweed plants",
        DefaultValue = 5, MinValue = 0, MaxValue = 15)]
    public int PlantCount { get; set; } = 5;

    [ExtensionParameter("Shark Count", "Number of sharks swimming",
        DefaultValue = 1, MinValue = 0, MaxValue = 5)]
    public int SharkCount { get; set; } = 1;

    [ExtensionParameter("Crab Count", "Number of crabs on bottom",
        DefaultValue = 3, MinValue = 0, MaxValue = 10)]
    public int CrabCount { get; set; } = 3;

    [ExtensionParameter("Snail Count", "Number of snails on plants",
        DefaultValue = 2, MinValue = 0, MaxValue = 8)]
    public int SnailCount { get; set; } = 2;

    [ExtensionParameter("Shell Count", "Number of shells on sand",
        DefaultValue = 5, MinValue = 0, MaxValue = 20)]
    public int ShellCount { get; set; } = 5;

    [ExtensionParameter("Seahorse Count", "Number of seahorses",
        DefaultValue = 1, MinValue = 0, MaxValue = 5)]
    public int SeahorseCount { get; set; } = 1;

    [ExtensionParameter("Starfish Count", "Number of starfish",
        DefaultValue = 2, MinValue = 0, MaxValue = 8)]
    public int StarfishCount { get; set; } = 2;

    [ExtensionParameter("Water Color", "Color of water",
        DefaultValue = "#001a33")]
    public SKColor WaterColor { get; set; } = new(0, 26, 51);

    [ExtensionParameter("Background Color", "Background color for the aquarium",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Animation Speed", "Animation speed multiplier",
        DefaultValue = 0.7, MinValue = 0.1, MaxValue = 3.0)]
    public double AnimationSpeed { get; set; } = 0.7;

    [ExtensionParameter("Show Light Rays", "Display sun rays through water",
        DefaultValue = true)]
    public bool ShowLightRays { get; set; } = true;

    #endregion
}

// Fish data structure
public class Fish
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float Speed { get; set; }
    public SKColor Color { get; set; }
    public float SwimPhase { get; set; }
    public bool DirectionRight { get; set; }
    public FishType Type { get; set; }
}

// Fish type enumeration
public enum FishType
{
    Normal,
    Angelfish,
    Pufferfish,
    Clownfish
}

// Shark data structure
public class Shark
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float Speed { get; set; }
    public float SwimPhase { get; set; }
    public bool DirectionRight { get; set; }
}

// Crab data structure
public class Crab
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float Speed { get; set; }
    public bool DirectionRight { get; set; }
    public float WalkPhase { get; set; }
}

// Snail data structure
public class Snail
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float Speed { get; set; }
    public bool DirectionRight { get; set; }
}

// Seahorse data structure
public class Seahorse
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float SwayPhase { get; set; }
    public float BobPhase { get; set; }
}

// Shell data structure
public class Shell
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float Rotation { get; set; }
    public int Type { get; set; } // 0=spiral, 1=clam, 2=conch
}

// Starfish data structure
public class Starfish
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float Rotation { get; set; }
    public SKColor Color { get; set; }
}

// Bubble data structure
public class Bubble
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Size { get; set; }
    public float Speed { get; set; }
    public float WobblePhase { get; set; }
}

// Plant data structure
public class Plant
{
    public float X { get; set; }
    public float Height { get; set; }
    public float SwayPhase { get; set; }
    public int Segments { get; set; }
    public float Width { get; set; }
    public int Type { get; set; } // 0=kelp, 1=seagrass
}