using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Renders the Pac-Man game with high-quality graphics and animations.
/// </summary>
public class GameRenderer : IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _renderLock = new();
    private SKBitmap? _backBuffer;
    private int _frameCount;

    public GameRenderer(ICanvas canvas)
    {
        _canvas = canvas;
    }

    public bool ShowDebugInfo { get; set; }
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    public bool UseBdfFont { get; set; }
    public int FontSize { get; set; }

    private float TextH(float auto) => CanvasText.ResolveSize(FontSize, auto);

    public void Dispose()
    {
        lock (_renderLock)
        {
            _backBuffer?.Dispose();
            _backBuffer = null;
        }
    }

    public void Render(GameState state)
    {
        if (state?.Maze == null || state.PacMan == null) return;

        var maze = state.Maze;
        var width = _canvas.Width;
        var height = _canvas.Height;

        lock (_renderLock)
        {
            if (_backBuffer == null || _backBuffer.Width != width || _backBuffer.Height != height)
            {
                _backBuffer?.Dispose();
                _backBuffer = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            }

            using var canvas = new SKCanvas(_backBuffer);

            // Clear with background color
            canvas.Clear(BackgroundColor);

            // Render maze
            RenderMaze(canvas, maze);

            // Handle special animations
            if (state.IsDeathAnimation)
            {
                RenderDeathAnimation(canvas, state, width, height);
            }
            else if (state.IsLevelStartAnimation)
            {
                RenderLevelStartAnimation(canvas, state, width, height);
            }
            else if (state.GameOver)
            {
                RenderGameOver(canvas, state, width, height);
            }
            else if (state.LevelComplete)
            {
                RenderLevelComplete(canvas, state, width, height);
            }
            else
            {
                // Normal gameplay rendering
                foreach (var ghost in state.Ghosts.Where(g => g.State == GhostState.Dead))
                    RenderGhost(canvas, ghost, maze);
                foreach (var ghost in state.Ghosts.Where(g => g.State != GhostState.Dead))
                    RenderGhost(canvas, ghost, maze);

                RenderPacMan(canvas, state.PacMan, maze);
            }

            // Always render HUD
            RenderHUD(canvas, state, width, height);

            if (ShowDebugInfo)
                RenderDebugInfo(canvas, state);

            _frameCount++;

            canvas.Flush();
        }

        // Submit completed frame - canvas opacity is applied during compositing
        if (_backBuffer != null)
        {
            _canvas.SubmitCompletedFrame(_backBuffer);
        }
    }

    private void RenderDeathAnimation(SKCanvas canvas, GameState state, int width, int height)
    {
        var progress = (float)state.AnimationFrame / GameState.DeathAnimationFrames;
        var maze = state.Maze;
        var cellSize = maze.CellSize;
        var offsetX = maze.OffsetX;
        var offsetY = maze.OffsetY;

        // Render ghosts fading out
        foreach (var ghost in state.Ghosts)
            if (progress < 0.3f)
                RenderGhost(canvas, ghost, maze);

        // Pac-Man death animation (shrinking spiral)
        var x = offsetX + state.PacMan.Position.X * cellSize + cellSize / 2f;
        var y = offsetY + state.PacMan.Position.Y * cellSize + cellSize / 2f;
        var baseRadius = cellSize * 0.42f;

        if (progress < 0.7f)
        {
            // Phase 1: Pac-Man spins and shrinks
            var phase1Progress = progress / 0.7f;
            var radius = baseRadius * (1f - phase1Progress * 0.5f);
            var rotation = phase1Progress * 720; // Two full rotations

            using var paint = new SKPaint
            {
                Color = SKColors.Yellow,
                IsAntialias = true
            };

            // Opening mouth wider
            var mouthAngle = 5f + phase1Progress * 170f; // Opens to almost full circle

            using var path = new SKPath();
            path.MoveTo(x, y);
            path.ArcTo(new SKRect(x - radius, y - radius, x + radius, y + radius),
                rotation + mouthAngle, 360 - mouthAngle * 2, false);
            path.Close();
            canvas.DrawPath(path, paint);
        }
        else
        {
            // Phase 2: Explosion particles
            var phase2Progress = (progress - 0.7f) / 0.3f;

            var particleCount = 12;
            for (var i = 0; i < particleCount; i++)
            {
                var angle = (float)(i * Math.PI * 2 / particleCount);
                var distance = baseRadius * 2f * phase2Progress;
                var px = x + (float)Math.Cos(angle) * distance;
                var py = y + (float)Math.Sin(angle) * distance;
                var size = baseRadius * 0.3f * (1f - phase2Progress);

                var alpha = (byte)(255 * (1f - phase2Progress));
                using var paint = new SKPaint
                {
                    Color = new SKColor(255, 255, 0, alpha),
                    IsAntialias = true
                };
                canvas.DrawCircle(px, py, size, paint);
            }
        }

        // Screen flash effect at the moment of death
        if (progress < 0.1f)
        {
            var alpha = (byte)(200 * (1f - progress / 0.1f));
            using var flashPaint = new SKPaint
            {
                Color = new SKColor(255, 0, 0, alpha)
            };
            canvas.DrawRect(0, 0, width, height, flashPaint);
        }

        // "OUCH!" text
        if (progress > 0.2f && progress < 0.8f)
        {
            var textAlpha = progress < 0.5f ? (progress - 0.2f) / 0.3f : (0.8f - progress) / 0.3f;
            CanvasText.Draw(canvas, _canvas, "OUCH!", new SKColor(255, 50, 50, (byte)(255 * textAlpha)),
                width / 2f, height / 2f - cellSize, TextH(Math.Max(16, cellSize * 2)), SKTextAlign.Center, UseBdfFont);
        }
    }

    private void RenderLevelStartAnimation(SKCanvas canvas, GameState state, int width, int height)
    {
        var progress = (float)state.AnimationFrame / GameState.LevelStartAnimationFrames;
        var maze = state.Maze;
        var cellSize = maze.CellSize;

        // Fade in ghosts
        var ghostAlpha = Math.Min(1f, progress * 2f);
        foreach (var ghost in state.Ghosts) RenderGhostWithAlpha(canvas, ghost, maze, ghostAlpha);

        // Pac-Man appears with a growing animation
        var pacmanScale = Math.Min(1f, progress * 2f);
        RenderPacManWithScale(canvas, state.PacMan, maze, pacmanScale);

        // "READY!" text
        if (progress < 0.8f)
        {
            var textScale = 1f + 0.1f * (float)Math.Sin(progress * Math.PI * 4);
            CanvasText.Draw(canvas, _canvas, "READY!", SKColors.Yellow, width / 2f, height / 2f,
                TextH(Math.Max(14, cellSize * 1.5f) * (UseBdfFont ? 1f : textScale)), SKTextAlign.Center, UseBdfFont);
        }

        CanvasText.Draw(canvas, _canvas, $"LEVEL {state.Level}", SKColors.Cyan, width / 2f,
            height / 2f + cellSize * 2, TextH(Math.Max(12, cellSize * 1.2f)), SKTextAlign.Center, UseBdfFont);
    }

    private void RenderGameOver(SKCanvas canvas, GameState state, int width, int height)
    {
        var cellSize = state.Maze.CellSize;

        // Dark overlay
        using var overlayPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 200)
        };
        canvas.DrawRect(0, 0, width, height, overlayPaint);

        // Pulsing "GAME OVER" text
        var pulse = 1f + 0.1f * (float)Math.Sin(_frameCount * 0.1);
        CanvasText.Draw(canvas, _canvas, "GAME OVER", SKColors.Red, width / 2f, height / 2f - cellSize,
            TextH(Math.Max(20, cellSize * 2.5f) * (UseBdfFont ? 1f : pulse)), SKTextAlign.Center, UseBdfFont);

        CanvasText.Draw(canvas, _canvas, $"FINAL SCORE: {state.Score}", SKColors.White, width / 2f,
            height / 2f + cellSize, TextH(Math.Max(12, cellSize * 1.2f)), SKTextAlign.Center, UseBdfFont);

        var hintAlpha = 0.5f + 0.5f * (float)Math.Sin(_frameCount * 0.05);
        CanvasText.Draw(canvas, _canvas, "Restarting...", new SKColor(200, 200, 200, (byte)(255 * hintAlpha)),
            width / 2f, height / 2f + cellSize * 3, TextH(Math.Max(10, cellSize)), SKTextAlign.Center, UseBdfFont);
    }

    private void RenderLevelComplete(SKCanvas canvas, GameState state, int width, int height)
    {
        var cellSize = state.Maze.CellSize;

        // Flashing background
        var flashValue = (byte)(100 + 50 * Math.Sin(_frameCount * 0.3));
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0, flashValue, 0, 150)
        };
        canvas.DrawRect(0, 0, width, height, bgPaint);

        // Still show Pac-Man celebrating
        RenderPacMan(canvas, state.PacMan, state.Maze);

        // "LEVEL COMPLETE!" text with rainbow effect
        float hue = _frameCount * 5 % 360;
        var textColor = SKColor.FromHsl(hue, 100, 50);

        CanvasText.Draw(canvas, _canvas, "LEVEL COMPLETE!", textColor, width / 2f, height / 2f - cellSize,
            TextH(Math.Max(16, cellSize * 2)), SKTextAlign.Center, UseBdfFont);

        var bonus = state.Level * 1000;
        CanvasText.Draw(canvas, _canvas, $"BONUS: {bonus} pts", SKColors.Yellow, width / 2f,
            height / 2f + cellSize, TextH(Math.Max(12, cellSize * 1.2f)), SKTextAlign.Center, UseBdfFont);

        // Firework particles
        RenderCelebrationParticles(canvas, width, height);
    }

    private void RenderCelebrationParticles(SKCanvas canvas, int width, int height)
    {
        var random = new Random(_frameCount / 5);
        var particleCount = 20;

        for (var i = 0; i < particleCount; i++)
        {
            float x = random.Next(width);
            float baseY = random.Next(height);
            var y = (baseY + _frameCount * 2) % (height + 50) - 25;
            float size = 2 + random.Next(4);

            float hue = (i * 30 + _frameCount * 3) % 360;
            var color = SKColor.FromHsl(hue, 100, 60);

            using var paint = new SKPaint
            {
                Color = color,
                IsAntialias = true
            };
            canvas.DrawCircle(x, y, size, paint);
        }
    }

    private void RenderMaze(SKCanvas canvas, Maze maze)
    {
        var cellSize = maze.CellSize;
        var offsetX = maze.OffsetX;
        var offsetY = maze.OffsetY;

        using var wallPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 139),
            IsAntialias = false
        };
        using var wallBorderPaint = new SKPaint
        {
            Color = new SKColor(50, 50, 255),
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        using var pelletPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 200),
            IsAntialias = true
        };

        var pulseScale = 1f + 0.2f * (float)Math.Sin(_frameCount * 0.15);
        using var powerPelletPaint = new SKPaint
        {
            Color = SKColors.Yellow,
            IsAntialias = true
        };
        using var powerGlowPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 0, 100),
            IsAntialias = true
        };

        var pelletRadius = cellSize * 0.12f;
        var powerRadius = cellSize * 0.3f * pulseScale;

        for (var x = 0; x < maze.GridWidth; x++)
        for (var y = 0; y < maze.GridHeight; y++)
        {
            float px = offsetX + x * cellSize;
            float py = offsetY + y * cellSize;
            var centerX = px + cellSize / 2f;
            var centerY = py + cellSize / 2f;

            if (maze.IsWall(x, y))
            {
                canvas.DrawRect(px + 1, py + 1, cellSize - 2, cellSize - 2, wallPaint);
                canvas.DrawRect(px + 1, py + 1, cellSize - 2, cellSize - 2, wallBorderPaint);
            }
            else
            {
                if (maze.HasPowerPellet(x, y))
                {
                    canvas.DrawCircle(centerX, centerY, powerRadius * 1.3f, powerGlowPaint);
                    canvas.DrawCircle(centerX, centerY, powerRadius, powerPelletPaint);
                }
                else if (maze.HasPellet(x, y))
                {
                    canvas.DrawCircle(centerX, centerY, pelletRadius, pelletPaint);
                }
            }
        }
    }

    private void RenderPacMan(SKCanvas canvas, PacManCharacter pacman, Maze maze)
    {
        RenderPacManWithScale(canvas, pacman, maze, 1f);
    }

    private void RenderPacManWithScale(SKCanvas canvas, PacManCharacter pacman, Maze maze, float scale)
    {
        var cellSize = maze.CellSize;
        var x = maze.OffsetX + pacman.Position.X * cellSize + cellSize / 2f;
        var y = maze.OffsetY + pacman.Position.Y * cellSize + cellSize / 2f;
        var radius = cellSize * 0.42f * scale;

        var mouthAngle = 5f + 40f * (float)Math.Abs(Math.Sin(_frameCount * 0.3));

        var startAngle = pacman.Direction switch
        {
            Direction.Right => mouthAngle,
            Direction.Left => 180 + mouthAngle,
            Direction.Up => 270 + mouthAngle,
            Direction.Down => 90 + mouthAngle,
            _ => mouthAngle
        };
        var sweepAngle = 360 - mouthAngle * 2;

        using var bodyPaint = new SKPaint
        {
            Color = SKColors.Yellow,
            IsAntialias = true
        };

        using var path = new SKPath();
        path.MoveTo(x, y);
        path.ArcTo(new SKRect(x - radius, y - radius, x + radius, y + radius), startAngle, sweepAngle, false);
        path.Close();
        canvas.DrawPath(path, bodyPaint);

        // Highlight
        using var highlightPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 150, 150),
            IsAntialias = true
        };
        var highlightRadius = radius * 0.3f;
        canvas.DrawCircle(x - radius * 0.2f, y - radius * 0.3f, highlightRadius, highlightPaint);

        // Eye
        var eyeRadius = radius * 0.15f;
        var eyeOffsetY = -radius * 0.25f;
        using var eyePaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawCircle(x, y + eyeOffsetY, eyeRadius, eyePaint);
    }

    private void RenderGhost(SKCanvas canvas, Ghost ghost, Maze maze)
    {
        RenderGhostWithAlpha(canvas, ghost, maze, 1f);
    }

    private void RenderGhostWithAlpha(SKCanvas canvas, Ghost ghost, Maze maze, float alpha)
    {
        var cellSize = maze.CellSize;
        var x = maze.OffsetX + ghost.Position.X * cellSize + cellSize / 2f;
        var y = maze.OffsetY + ghost.Position.Y * cellSize + cellSize / 2f;
        var radius = cellSize * 0.4f;
        var bodyHeight = radius * 1.2f;

        var baseColor = ghost.Color;
        var color = new SKColor(baseColor.Red, baseColor.Green, baseColor.Blue, (byte)(baseColor.Alpha * alpha));

        using var bodyPaint = new SKPaint
        {
            Color = color,
            IsAntialias = true
        };

        using var path = new SKPath();
        path.AddArc(new SKRect(x - radius, y - radius, x + radius, y + radius * 0.2f), 180, 180);
        path.LineTo(x + radius, y + bodyHeight * 0.4f);

        var waves = 4;
        var waveWidth = radius * 2 / waves;
        var waveHeight = radius * 0.2f;

        for (var i = 0; i < waves; i++)
        {
            var wx1 = x + radius - i * waveWidth - waveWidth * 0.5f;
            var wx2 = x + radius - (i + 1) * waveWidth;
            var wy = y + bodyHeight * 0.4f + (i % 2 == 0 ? waveHeight : 0);
            var wyNext = y + bodyHeight * 0.4f + (i % 2 == 1 ? waveHeight : 0);
            path.QuadTo(wx1, wy, wx2, wyNext);
        }

        path.LineTo(x - radius, y + radius * 0.1f);
        path.Close();
        canvas.DrawPath(path, bodyPaint);

        // Highlight
        using var highlightPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(60 * alpha)),
            IsAntialias = true
        };
        canvas.DrawCircle(x - radius * 0.3f, y - radius * 0.3f, radius * 0.25f, highlightPaint);

        // Eyes
        if (ghost.State != GhostState.Dead || alpha < 1f)
            RenderGhostEyes(canvas, x, y, radius, ghost.Direction, ghost.State, alpha);
    }

    private void RenderGhostEyes(SKCanvas canvas, float x, float y, float radius, Direction direction, GhostState state,
        float alpha)
    {
        if (state == GhostState.Frightened)
        {
            using var scaredEyePaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, (byte)(255 * alpha)),
                IsAntialias = true
            };
            var eyeY = y - radius * 0.1f;
            canvas.DrawCircle(x - radius * 0.3f, eyeY, radius * 0.15f, scaredEyePaint);
            canvas.DrawCircle(x + radius * 0.3f, eyeY, radius * 0.15f, scaredEyePaint);

            using var mouthPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, (byte)(255 * alpha)),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, radius * 0.08f)
            };
            using var mouthPath = new SKPath();
            mouthPath.MoveTo(x - radius * 0.4f, y + radius * 0.3f);
            for (var i = 0; i < 4; i++)
            {
                var mx = x - radius * 0.4f + i * radius * 0.2f + radius * 0.1f;
                var my = y + radius * 0.3f + (i % 2 == 0 ? radius * 0.1f : -radius * 0.1f);
                mouthPath.LineTo(mx, my);
            }

            canvas.DrawPath(mouthPath, mouthPaint);
        }
        else
        {
            using var eyeWhitePaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, (byte)(255 * alpha)),
                IsAntialias = true
            };
            using var pupilPaint = new SKPaint
            {
                Color = new SKColor(30, 30, 180, (byte)(255 * alpha)),
                IsAntialias = true
            };

            var eyeRadius = radius * 0.22f;
            var pupilRadius = radius * 0.12f;
            var eyeY = y - radius * 0.1f;
            var eyeSpacing = radius * 0.35f;

            var pupilOffsetX = direction switch
            {
                Direction.Left => -eyeRadius * 0.4f,
                Direction.Right => eyeRadius * 0.4f,
                _ => 0
            };
            var pupilOffsetY = direction switch
            {
                Direction.Up => -eyeRadius * 0.4f,
                Direction.Down => eyeRadius * 0.4f,
                _ => 0
            };

            canvas.DrawCircle(x - eyeSpacing, eyeY, eyeRadius, eyeWhitePaint);
            canvas.DrawCircle(x - eyeSpacing + pupilOffsetX, eyeY + pupilOffsetY, pupilRadius, pupilPaint);

            canvas.DrawCircle(x + eyeSpacing, eyeY, eyeRadius, eyeWhitePaint);
            canvas.DrawCircle(x + eyeSpacing + pupilOffsetX, eyeY + pupilOffsetY, pupilRadius, pupilPaint);
        }
    }

    private void RenderHUD(SKCanvas canvas, GameState state, int width, int height)
    {
        var fontSize = TextH(Math.Max(8, Math.Min(14, state.Maze.CellSize * 0.9f)));
        var scoreText = $"SCORE: {state.Score}";
        CanvasText.Draw(canvas, _canvas, scoreText, new SKColor(0, 0, 0, 150), 4, fontSize + 1, fontSize,
            SKTextAlign.Left, UseBdfFont);
        CanvasText.Draw(canvas, _canvas, scoreText, SKColors.White, 3, fontSize, fontSize,
            SKTextAlign.Left, UseBdfFont);

        var livesText = $"LIVES: {state.Lives}";
        var livesWidth = CanvasText.Measure(_canvas, livesText, fontSize, UseBdfFont);
        CanvasText.Draw(canvas, _canvas, livesText, new SKColor(0, 0, 0, 150), width - livesWidth - 2, fontSize + 1,
            fontSize, SKTextAlign.Left, UseBdfFont);
        CanvasText.Draw(canvas, _canvas, livesText, SKColors.White, width - livesWidth - 3, fontSize, fontSize,
            SKTextAlign.Left, UseBdfFont);

        var levelText = $"LEVEL {state.Level}";
        CanvasText.Draw(canvas, _canvas, levelText, new SKColor(0, 0, 0, 150), 4, height - 3, fontSize,
            SKTextAlign.Left, UseBdfFont);
        CanvasText.Draw(canvas, _canvas, levelText, SKColors.White, 3, height - 4, fontSize,
            SKTextAlign.Left, UseBdfFont);
    }

    private void RenderDebugInfo(SKCanvas canvas, GameState state)
    {
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 200)
        };
        canvas.DrawRect(0, 25, 200, 50, bgPaint);

        var pacman = state.PacMan;
        var y = 36;
        CanvasText.Draw(canvas, _canvas,
            $"Pos: ({pacman.Position.X:F1}, {pacman.Position.Y:F1}) Dir: {pacman.Direction}",
            SKColors.Lime, 5, y, 10, SKTextAlign.Left, UseBdfFont);

        y += 12;
        var activeGhosts = state.Ghosts.Count(g => g.State != GhostState.Dead);
        CanvasText.Draw(canvas, _canvas, $"Ghosts: {activeGhosts}/{state.Ghosts.Count} | Frame: {_frameCount}",
            SKColors.Lime, 5, y, 10, SKTextAlign.Left, UseBdfFont);

        y += 12;
        CanvasText.Draw(canvas, _canvas, $"Pellets: {state.Maze.CountPellets()}", SKColors.Lime, 5, y, 10,
            SKTextAlign.Left, UseBdfFont);
    }
}