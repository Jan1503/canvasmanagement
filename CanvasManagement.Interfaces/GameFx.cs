using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     Juice for LED games, all 1:1 pixel friendly: particles (optionally glowing, added instead of painted),
///     screen shake, a full-screen flash, rising score pop-ups, glows and a few backdrops.
///     One instance per game; call <see cref="Update"/> once per tick and draw in this order:
///     backdrop, <see cref="BeginShake"/>, the game, <see cref="DrawParticles"/>, <see cref="EndShake"/>,
///     <see cref="DrawOverlay"/> (flash and pop-ups).
/// </summary>
public sealed class GameFx
{
    private readonly List<Particle> _parts = new();
    private readonly List<Popup> _pops = new();
    private readonly Random _rng = new();
    private float _shake, _flash;
    private SKColor _flashColor = SKColors.White;
    private int _saved = -1;

    public int MaxParticles { get; set; } = 600;
    public float Time { get; private set; }
    public int Count => _parts.Count;

    private struct Particle
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Size, Gravity, Drag;
        public SKColor Color, Color2;
        public bool Glow;
    }

    private struct Popup
    {
        public string Text;
        public float X, Y, Life;
        public SKColor Color;
    }

    public float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ emitters

    public void Spark(float x, float y, float vx, float vy, SKColor color, float life = 0.6f, float size = 1,
        bool glow = true, float gravity = 0, SKColor? fadeTo = null, float drag = 0.02f)
    {
        if (_parts.Count >= MaxParticles) _parts.RemoveAt(0);
        _parts.Add(new Particle
        {
            X = x, Y = y, Vx = vx, Vy = vy, Life = life, MaxLife = life, Size = size, Gravity = gravity, Drag = drag,
            Color = color, Color2 = fadeTo ?? color, Glow = glow
        });
    }

    /// <summary>A round burst: <paramref name="n"/> sparks flying out at up to <paramref name="speed"/> px/tick.</summary>
    public void Burst(float x, float y, SKColor color, int n = 14, float speed = 2.2f, float life = 0.7f,
        float gravity = 0.04f, bool glow = true, float size = 1, SKColor? fadeTo = null)
    {
        for (var i = 0; i < n; i++)
        {
            var a = Rand(0, MathF.Tau);
            var s = speed * MathF.Sqrt(Rand(0.1f, 1f));
            Spark(x, y, MathF.Cos(a) * s, MathF.Sin(a) * s, color, life * Rand(0.6f, 1.2f), size, glow, gravity,
                fadeTo);
        }
    }

    /// <summary>A cone of sparks (dust, splashes, exhaust) around <paramref name="angle"/> (radians).</summary>
    public void Spray(float x, float y, float angle, float spread, SKColor color, int n = 8, float speed = 1.6f,
        float life = 0.5f, float gravity = 0.06f, bool glow = false, float size = 1)
    {
        for (var i = 0; i < n; i++)
        {
            var a = angle + Rand(-spread, spread);
            var s = speed * Rand(0.4f, 1f);
            Spark(x, y, MathF.Cos(a) * s, MathF.Sin(a) * s, color, life * Rand(0.6f, 1.2f), size, glow, gravity);
        }
    }

    /// <summary>Confetti in many hues.</summary>
    public void Confetti(float x, float y, int n = 30, float speed = 2.5f)
    {
        for (var i = 0; i < n; i++)
        {
            var a = Rand(-MathF.PI, 0);
            var s = speed * Rand(0.4f, 1f);
            Spark(x, y, MathF.Cos(a) * s, MathF.Sin(a) * s, PixelArt.Hsv(_rng.Next(360), 0.8f, 1f), Rand(0.8f, 1.6f),
                1, false, 0.05f);
        }
    }

    public void Shake(float amount) => _shake = Math.Max(_shake, amount);

    public void Flash(SKColor color, float strength = 0.6f)
    {
        _flashColor = color;
        _flash = Math.Max(_flash, Math.Clamp(strength, 0, 1));
    }

    public void Pop(string text, float x, float y, SKColor color) =>
        _pops.Add(new Popup { Text = text, X = x, Y = y, Life = 1f, Color = color });

    public void Clear()
    {
        _parts.Clear();
        _pops.Clear();
        _shake = _flash = 0;
    }

    // ------------------------------------------------------------------ update / draw

    /// <param name="dt">Seconds since the last tick (games on a fixed timer pass their interval).</param>
    public void Update(float dt)
    {
        Time += dt;
        var ticks = dt * 30f; // particle speeds are "per tick at 30 fps"
        for (var i = _parts.Count - 1; i >= 0; i--)
        {
            var p = _parts[i];
            p.Life -= dt;
            if (p.Life <= 0)
            {
                _parts.RemoveAt(i);
                continue;
            }
            p.Vy += p.Gravity * ticks;
            var k = MathF.Pow(1 - p.Drag, ticks);
            p.Vx *= k;
            p.Vy *= k;
            p.X += p.Vx * ticks;
            p.Y += p.Vy * ticks;
            _parts[i] = p;
        }
        for (var i = _pops.Count - 1; i >= 0; i--)
        {
            var q = _pops[i];
            q.Life -= dt * 1.1f;
            q.Y -= 12 * dt;
            if (q.Life <= 0) _pops.RemoveAt(i);
            else _pops[i] = q;
        }
        _shake *= MathF.Pow(0.82f, ticks);
        if (_shake < 0.3f) _shake = 0;
        _flash = Math.Max(0, _flash - dt * 2.5f);
    }

    /// <summary>Translates the canvas by the current shake (whole pixels). Pair with <see cref="EndShake"/>.</summary>
    public void BeginShake(SKCanvas c)
    {
        _saved = c.Save();
        if (_shake <= 0) return;
        c.Translate(MathF.Round(Rand(-_shake, _shake)), MathF.Round(Rand(-_shake, _shake)));
    }

    public void EndShake(SKCanvas c)
    {
        if (_saved >= 0) c.RestoreToCount(_saved);
        _saved = -1;
    }

    public void DrawParticles(SKCanvas c)
    {
        using var paint = new SKPaint { IsAntialias = false };
        foreach (var p in _parts)
        {
            var t = 1 - p.Life / p.MaxLife;
            var col = Lerp(p.Color, p.Color2, t);
            var fade = p.Life / p.MaxLife;
            var x = MathF.Round(p.X);
            var y = MathF.Round(p.Y);
            var s = Math.Max(1, (int)MathF.Round(p.Size * (p.Glow ? 1 : 0.6f + 0.4f * fade)));
            if (p.Glow)
            {
                // Halo first (added), then the hot core.
                paint.BlendMode = SKBlendMode.Plus;
                paint.Color = col.WithAlpha((byte)(70 * fade));
                c.DrawRect(x - s, y - s, s * 3, s * 3, paint);
                paint.Color = col.WithAlpha((byte)(255 * fade));
                c.DrawRect(x, y, s, s, paint);
            }
            else
            {
                paint.BlendMode = SKBlendMode.SrcOver;
                paint.Color = col.WithAlpha((byte)(255 * Math.Min(1, fade * 1.5f)));
                // Big particles are blobs (falling bubbles, debris), small ones single pixels.
                if (s >= 3) PixelArt.Disc(c, paint, (int)x, (int)y, s / 2);
                else c.DrawRect(x, y, s, s, paint);
            }
        }
    }

    /// <summary>Flash and pop-ups, on top of everything (not shaken).</summary>
    public void DrawOverlay(SKCanvas c, ICanvas host, int w, int h, float popSize = 8)
    {
        if (_flash > 0.01f)
        {
            using var p = new SKPaint { Color = _flashColor.WithAlpha((byte)(_flash * 200)), BlendMode = SKBlendMode.Plus };
            c.DrawRect(0, 0, w, h, p);
        }
        foreach (var q in _pops)
        {
            var a = (byte)(255 * Math.Clamp(q.Life * 2, 0, 1));
            CanvasText.Draw(c, host, q.Text, new SKColor(0, 0, 0, (byte)(a * 0.7f)), q.X + 1, q.Y + 1, popSize,
                SKTextAlign.Center, true);
            CanvasText.Draw(c, host, q.Text, q.Color.WithAlpha(a), q.X, q.Y, popSize, SKTextAlign.Center, true);
        }
    }

    // ------------------------------------------------------------------ static helpers

    public static SKColor Lerp(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t), (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }

    public static SKColor Shade(SKColor c, float k) =>
        new((byte)Math.Clamp(c.Red * k, 0, 255), (byte)Math.Clamp(c.Green * k, 0, 255), (byte)Math.Clamp(c.Blue * k, 0, 255),
            c.Alpha);

    /// <summary>Soft additive glow: nested rectangles fading out, around (x,y,w,h).</summary>
    public static void Glow(SKCanvas c, float x, float y, float w, float h, SKColor color, int radius = 4,
        float strength = 0.5f)
    {
        using var p = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.Plus };
        for (var r = radius; r >= 1; r--)
        {
            var a = strength * (1 - r / (float)(radius + 1)) / radius * 1.6f;
            p.Color = color.WithAlpha((byte)Math.Clamp(a * 255, 0, 255));
            c.DrawRect(x - r, y - r, w + 2 * r, h + 2 * r, p);
        }
    }

    /// <summary>Soft additive round glow (pixel discs).</summary>
    public static void GlowDisc(SKCanvas c, float cx, float cy, float radius, SKColor color, float strength = 0.5f)
    {
        if (radius <= 0 || strength <= 0) return;
        var mask = PixelSprites.Glow(Math.Max(1, (int)MathF.Round(radius * 4)));
        var r = (mask.Width - 1) / 2;
        // The mask holds (1-d)²; the paint's alpha supplies the strength.
        using var p = new SKPaint
        {
            IsAntialias = false, BlendMode = SKBlendMode.Plus,
            Color = color.WithAlpha((byte)Math.Clamp(strength * 255, 0, 255))
        };
        c.DrawImage(mask, MathF.Round(cx) - r, MathF.Round(cy) - r, PixelArt.Nearest, p);
    }

    public static void VerticalGradient(SKCanvas c, int w, int h, SKColor top, SKColor bottom, int y0 = 0, int y1 = -1)
    {
        if (y1 < 0) y1 = h;
        using var p = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, y0), new SKPoint(0, y1), new[] { top, bottom },
                SKShaderTileMode.Clamp)
        };
        c.DrawRect(0, y0, w, y1 - y0, p);
    }

    /// <summary>Twinkling starfield; <paramref name="scroll"/> moves layers at different speeds (parallax).</summary>
    public static void Stars(SKCanvas c, int w, int h, float time, float scrollX = 0, float scrollY = 0, int density = 400,
        int seed = 7)
    {
        var rnd = new Random(seed);
        using var p = new SKPaint { IsAntialias = false };
        var n = w * h / density;
        for (var i = 0; i < n; i++)
        {
            var layer = 1 + i % 3;
            var x = (rnd.Next(w) - scrollX * layer * 0.3f) % w;
            if (x < 0) x += w;
            var y = (rnd.Next(h) - scrollY * layer * 0.3f) % h;
            if (y < 0) y += h;
            var tw = 0.55f + 0.45f * MathF.Sin(time * (0.8f + (i % 7) * 0.3f) + i);
            var v = (byte)(40 + layer * 40 * tw);
            p.Color = new SKColor((byte)(v * 0.85f), (byte)(v * 0.9f), v);
            c.DrawRect(MathF.Floor(x), MathF.Floor(y), 1, 1, p);
        }
    }

    /// <summary>Scanline dimming for a retro CRT feel (every other row slightly darker).</summary>
    public static void Scanlines(SKCanvas c, int w, int h, float strength = 0.12f)
    {
        using var p = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(255 * strength)) };
        for (var y = 1; y < h; y += 2) c.DrawRect(0, y, w, 1, p);
    }
}
