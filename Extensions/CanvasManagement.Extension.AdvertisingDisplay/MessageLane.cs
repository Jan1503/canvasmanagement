using System.Globalization;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.AdvertisingDisplay;

/// <summary>
///     One independent line/lane of the advertising display. Owns its own messages, effect,
///     direction, colour, font and animation state, and renders into a band of the panel. The owning
///     extension stacks several of these and draws the shared background/border/decorations/confetti.
/// </summary>
internal sealed class MessageLane : IDisposable
{
    private readonly ICanvas _canvas; // only used for BDF render/measure (pure helpers)
    private readonly Random _random;

    // ── Configuration (set by the owner) ──
    public string[] Messages = { "" };
    public ScrollDirection Direction = ScrollDirection.Left;
    public TextEffect Effect = TextEffect.FlyIn;
    public int ScrollSpeed = 4;
    public int MessageDurationSeconds = 5;
    public bool MultiColor = true;
    public SKColor TextColor = SKColors.White;
    public bool Blink;
    public int BlinkIntervalMs = 500;
    public bool Fade = true;
    public int FontSize;
    public bool UseBdfFont = true;
    public string BdfFontName = "";
    public string FontFamily = "Arial";
    public bool Emojis;
    public int CharStagger = 60;
    public bool Sparkle = true;
    public bool Glow = true;
    public bool Twinkle;
    public float Weight = 1f;

    /// <summary>
    ///     Global timing multiplier for entrance animations (per-char + narrative).
    ///     Values &lt; 1.0 slow the animation down; values &gt; 1.0 speed it up. Applied to the
    ///     entranceMs budget for every effect so users can dial in the pace regardless of message
    ///     length.
    /// </summary>
    public float EffectSpeed = 1f;

    // ── Per-frame inputs (set at the start of Render) ──
    private int _frame;
    private double _nowMs;
    private int _bandW;
    private int _bandH;
    private float _scale = 1f;

    // ── State ──
    private int _messageIndex;
    private float _scrollPos;
    private bool _scrollInitialized;
    private double _messageStartMs;

    private string _currentText = "";
    private SKBitmap? _mask;
    private SKImage? _maskImage;
    private TextEffect _currentEffect;
    private ScrollDirection _currentDirection;
    private SKColor _currentColor;
    private bool _currentColorSet;

    private readonly List<CharCell> _cells = new();
    // Real-cell-indices of cells that contain lit pixels. Rebuilt whenever the message rebuilds
    // so narrative effects can iterate only visible characters (skipping spaces / whitespace).
    private readonly List<int> _visibleCellIndices = new();
    private readonly List<SparkleParticle> _sparkles = new();
    private readonly List<SKPointI> _litPixels = new();
    private int[] _pixelOrder = Array.Empty<int>();

    public MessageLane(ICanvas canvas, Random random)
    {
        _canvas = canvas;
        _random = random;
    }

    private int Sc(float designValue)
    {
        return Math.Max(1, (int)Math.Round(designValue * _scale));
    }

    public void Reset(double nowMs)
    {
        _messageIndex = 0;
        _scrollInitialized = false;
        _messageStartMs = nowMs;
        RebuildCurrentMessage();
    }

    public void Rebuild()
    {
        _scrollInitialized = false;
        RebuildCurrentMessage();
    }

    /// <summary>Immediately skips to the next message in this lane.</summary>
    public void ForceNext(double nowMs)
    {
        if (Messages.Length == 0) return;
        _messageIndex = (_messageIndex + 1) % Messages.Length;
        _scrollInitialized = false;
        _messageStartMs = nowMs;
        RebuildCurrentMessage();
    }

    public void Dispose()
    {
        _mask?.Dispose();
        _mask = null;
        _maskImage?.Dispose();
        _maskImage = null;
        _sparkles.Clear();
        _cells.Clear();
        _litPixels.Clear();
    }

    /// <summary>Renders one frame into the band (canvas is already translated/clipped so that the
    /// band's top-left is (0,0)). Returns true if the lane advanced to a new message this frame.</summary>
    public bool Render(SKCanvas canvas, int bandW, int bandH, int frame, double nowMs, float scale)
    {
        _frame = frame;
        _nowMs = nowMs;
        _bandW = bandW;
        _bandH = bandH;
        _scale = scale;

        if (_mask == null || _mask.Width == 0 || _mask.Height == 0)
            return false;

        // Blink: hide on the "off" half of the interval.
        if (Blink)
        {
            var interval = Math.Max(50, BlinkIntervalMs);
            if ((long)(nowMs / interval) % 2 == 1)
                return false;
        }

        // Reveal/entrance effects (Typewriter, ZoomIn, Paint, FlyIn, …) are one-shot animations that only
        // make sense on a stationary line. If one is selected, present the line statically so the chosen
        // effect actually plays (and loops); Direction then only matters for plain/Wave scrolling.
        if (_currentDirection == ScrollDirection.None || !IsScrollEffect(_currentEffect))
            return DrawStatic(canvas);

        // Scrolling modes: move the whole text block.
        if (!_scrollInitialized) InitScroll();
        var step = Math.Max(1, ScrollSpeed * _scale);
        float x, y;
        var advanced = false;

        if (_currentDirection is ScrollDirection.Up or ScrollDirection.Down)
        {
            x = (bandW - _mask.Width) / 2f;
            y = _scrollPos;
            if (_currentDirection == ScrollDirection.Up)
            {
                _scrollPos -= step;
                if (_scrollPos <= -_mask.Height) advanced = AdvanceMessage();
            }
            else
            {
                _scrollPos += step;
                if (_scrollPos >= bandH) advanced = AdvanceMessage();
            }
        }
        else
        {
            x = _scrollPos;
            y = (bandH - _mask.Height) / 2f;
            if (_currentDirection == ScrollDirection.Left)
            {
                _scrollPos -= step;
                if (_scrollPos <= -_mask.Width) advanced = AdvanceMessage();
            }
            else
            {
                _scrollPos += step;
                if (_scrollPos >= bandW) advanced = AdvanceMessage();
            }
        }

        if (advanced) return true; // don't draw on the advance frame (avoids a flash)

        // Per-character path is needed for rainbow colour or for the Wave effect; otherwise the whole
        // block is drawn at once (a plain flat scroll).
        if ((MultiColor && !_currentColorSet) || _currentEffect == TextEffect.Wave)
            DrawScrollingPerChar(canvas, x, y);
        else
            DrawMask(canvas, x, y, 1f);

        return false;
    }

    /// <summary>Effects compatible with continuous scrolling. Everything else is a one-shot reveal that
    /// is rendered statically instead.</summary>
    private static bool IsScrollEffect(TextEffect fx)
    {
        return fx is TextEffect.None or TextEffect.Wave;
    }

    // ───────────────────────────────────────── static (per-char) ─────────────

    private bool DrawStatic(SKCanvas canvas)
    {
        var maskH = _mask!.Height;
        var blockX = (_bandW - _mask.Width) / 2f;
        var blockY = (_bandH - maskH) / 2f;
        var elapsed = _nowMs - _messageStartMs;

        if (_currentEffect is TextEffect.PixelPaint or TextEffect.Dissolve)
            return DrawPixelReveal(canvas, blockX, blockY, maskH, elapsed);

        if (IsNarrative(_currentEffect))
            return DrawNarrative(canvas, blockX, blockY, maskH, elapsed);

        // EffectSpeed acts as a rate multiplier: <1 slows the animation down. We apply it to the
        // stagger and entrance windows so the whole thing dilates uniformly.
        var speed = Math.Max(0.05f, EffectSpeed);
        var speedInv = 1f / speed;
        var stagger = Math.Max(0, (int)Math.Round(CharStagger * speedInv));
        var entranceMs = 450.0 * speedInv;
        var allInMs = (_cells.Count - 1) * (double)stagger + entranceMs;
        var holdMs = MessageDurationSeconds * 1000.0;
        var totalMs = allInMs + holdMs;

        var globalAlpha = 1f;
        if (Fade && elapsed > totalMs - 400)
            globalAlpha = (float)Math.Max(0, (totalMs - elapsed) / 400.0);

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (cell.W <= 0) continue;

            var ct = stagger > 0 ? (elapsed - i * stagger) / entranceMs : elapsed / entranceMs;
            ct = (float)Math.Clamp(ct, 0, 1);

            var g = ComputeEffect(_currentEffect, (float)ct, cell, i, blockY, maskH);
            var a = g.Alpha * globalAlpha;
            if (Twinkle) a *= 0.55f + 0.45f * (float)Math.Sin(_nowMs / 180.0 + cell.Phase * 6.28);
            if (a <= 0.01f || g.Scale <= 0.01f) continue;

            var hue = _frame * 2f + i * 20f + (Twinkle ? cell.Phase * 140f : 0f);
            var color = MultiColor && !_currentColorSet ? SignageFx.Hue(hue) : _currentColor;
            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, a);

            if (Sparkle && ct >= 1f && _random.NextDouble() < 0.04)
                SpawnSparkle(blockX + cell.X + cell.W / 2f, blockY + maskH / 2f, maskH);
        }

        if (_currentEffect == TextEffect.Typewriter)
            DrawTypewriterCursor(canvas, blockX, blockY, maskH, elapsed, stagger, entranceMs, globalAlpha);

        UpdateAndDrawSparkles(canvas, globalAlpha);

        if (elapsed >= totalMs)
        {
            _sparkles.Clear();
            return AdvanceMessage();
        }

        return false;
    }

    private bool DrawPixelReveal(SKCanvas canvas, float blockX, float blockY, int maskH, double elapsed)
    {
        var holdMs = MessageDurationSeconds * 1000.0;
        if (_litPixels.Count == 0)
            return elapsed >= holdMs && AdvanceMessage();

        var revealMs = Math.Clamp(_litPixels.Count * 3.0, 800, 4000);
        var totalMs = revealMs + holdMs;
        var progress = (float)Math.Clamp(elapsed / revealMs, 0, 1);

        var globalAlpha = 1f;
        if (Fade && elapsed > totalMs - 400)
            globalAlpha = (float)Math.Max(0, (totalMs - elapsed) / 400.0);

        var useMulti = MultiColor && !_currentColorSet;
        var count = (int)(progress * _litPixels.Count);
        var dissolve = _currentEffect == TextEffect.Dissolve;

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        for (var k = 0; k < count; k++)
        {
            var p = _litPixels[dissolve ? _pixelOrder[k] : k];
            var color = useMulti ? SignageFx.Hue(p.X * 4f + p.Y * 4f + _frame * 3f) : _currentColor;
            var pa = globalAlpha;
            if (Twinkle) pa *= 0.55f + 0.45f * (float)Math.Sin(_nowMs / 180.0 + (p.X + p.Y) * 0.35);
            paint.Color = color.WithAlpha((byte)Math.Clamp(pa * 255f, 0, 255));
            canvas.DrawRect(blockX + p.X, blockY + p.Y, 1, 1, paint);
        }

        if (Sparkle && progress >= 1f && _random.NextDouble() < 0.08)
            SpawnSparkle(blockX + _mask!.Width / 2f, blockY + maskH / 2f, maskH);
        UpdateAndDrawSparkles(canvas, globalAlpha);

        if (elapsed >= totalMs)
        {
            _sparkles.Clear();
            return AdvanceMessage();
        }

        return false;
    }

    private void DrawTypewriterCursor(SKCanvas canvas, float blockX, float blockY, int maskH, double elapsed,
        int stagger, double entranceMs, float globalAlpha)
    {
        var revealed = 0;
        for (var i = 0; i < _cells.Count; i++)
        {
            var ct = stagger > 0 ? (elapsed - i * stagger) / entranceMs : 1.0;
            if (ct > 0) revealed = i + 1;
        }

        var typing = revealed < _cells.Count;
        var blinkOn = (long)(_nowMs / 400) % 2 == 0;
        if (!typing && !blinkOn) return;

        float curX;
        if (revealed == 0) curX = blockX;
        else
        {
            var last = _cells[revealed - 1];
            curX = blockX + last.X + last.W;
        }

        var cw = Sc(3);
        var color = MultiColor && !_currentColorSet ? SignageFx.Hue(_frame * 2f) : _currentColor;
        using var paint = new SKPaint
        {
            Color = color.WithAlpha((byte)Math.Clamp(255 * globalAlpha, 0, 255)),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(curX, blockY, cw, maskH, paint);
    }

    private void DrawScrollingPerChar(SKCanvas canvas, float blockX, float blockY)
    {
        var maskH = _mask!.Height;
        var multi = MultiColor && !_currentColorSet;
        var wave = _currentEffect == TextEffect.Wave;
        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (cell.W <= 0) continue;

            var g = GlyphAnim.Default;
            if (wave)
                g.Dy = (float)Math.Sin(_nowMs / 250.0 + cell.X * 0.05) * (maskH * 0.18f);

            var color = multi ? SignageFx.Hue(_frame * 3f + cell.X * 1.2f) : _currentColor;
            var alpha = Twinkle ? 0.55f + 0.45f * (float)Math.Sin(_nowMs / 180.0 + cell.X * 0.1) : 1f;
            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, alpha);
        }
    }

    private GlyphAnim ComputeEffect(TextEffect fx, float t, CharCell cell, int i, float blockY, int maskH)
    {
        var g = GlyphAnim.Default;
        var maxDim = Math.Max(_bandW, _bandH);

        switch (fx)
        {
            case TextEffect.None:
                break;
            case TextEffect.Typewriter:
                g.Alpha = t > 0 ? 1 : 0;
                break;
            case TextEffect.FlyIn:
            {
                var e = SignageFx.EaseOutCubic(t);
                g.Dx = cell.DirX * maxDim * (1 - e);
                g.Dy = cell.DirY * maxDim * (1 - e);
                g.Alpha = Math.Min(1, t * 2);
                break;
            }
            case TextEffect.Drop:
            {
                var e = SignageFx.EaseOutBounce(t);
                g.Dy = -(blockY + maskH) * (1 - e);
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.Rain:
            {
                var e = SignageFx.EaseOutCubic(t);
                g.Dy = -(blockY + maskH) * (1 + cell.Phase * 1.5f) * (1 - e);
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.Bounce:
                g.Scale = SignageFx.EaseOutBack(t);
                g.Alpha = Math.Min(1, t * 2);
                break;
            case TextEffect.ZoomIn:
                g.Scale = t;
                g.Alpha = t;
                break;
            case TextEffect.Spiral:
            {
                var e = SignageFx.EaseOutCubic(t);
                var ang = (1 - e) * (float)Math.PI * 4 + cell.Phase * 6.28f;
                var r = (1 - e) * maxDim * 0.5f;
                g.Dx = (float)Math.Cos(ang) * r;
                g.Dy = (float)Math.Sin(ang) * r;
                g.Scale = Math.Max(0.05f, e);
                g.Alpha = t;
                break;
            }
            case TextEffect.Wave:
                g.Alpha = Math.Min(1, t * 3);
                g.Dy = (float)Math.Sin(_nowMs / 300.0 + i * 0.6) * (maskH * 0.4f);
                break;
            case TextEffect.Paint:
                g.Reveal = t;
                break;
            case TextEffect.Roll:
            {
                var e = SignageFx.EaseOutCubic(t);
                g.Dy = -(blockY + maskH) * (1 - e) * 0.6f;
                g.Rotation = (1 - e) * 720f * (cell.DirX >= 0 ? 1 : -1);
                g.Scale = Math.Max(0.1f, e);
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.Flip:
            {
                var e = SignageFx.EaseOutCubic(t);
                g.ScaleX = (float)Math.Cos((1 - e) * Math.PI * 4);
                if (Math.Abs(g.ScaleX) < 0.05f) g.ScaleX = 0.05f * Math.Sign(g.ScaleX == 0 ? 1 : g.ScaleX);
                g.Alpha = Math.Min(1, t * 2);
                break;
            }
            case TextEffect.Slot:
            {
                if (t < 1f && _cells.Count > 0)
                {
                    g.SlotIndex = Math.Abs(i * 7 + _frame) % _cells.Count;
                    g.Dy = (1 - t) * -4f;
                }

                break;
            }
            // ─── New Hollywood Effects ───────────────────────────────────────────
            case TextEffect.MatrixRain:
            {
                // Digital rain cascading effect with random trails
                var cascade = (float)Math.Sin((_nowMs - i * 200) / 500.0);
                if (cascade < 0) cascade = 0;
                var e = SignageFx.EaseOutCubic(Math.Min(t * 1.5f, 1f));
                g.Dy = -(blockY + maskH * 2) * (1 - e) + cascade * maskH * 0.3f;
                g.Alpha = Math.Min(1, t * 4) * (0.5f + cascade * 0.5f);
                g.ScaleY = 0.8f + cascade * 0.4f;
                break;
            }
            case TextEffect.Glitch:
            {
                // Digital glitch with random horizontal displacement and RGB split
                var e = SignageFx.EaseOutCubic(t);
                var glitchPhase = (_nowMs + i * 50) % 300 < 50 && t > 0.3f;
                g.Dx = glitchPhase ? ((i % 3) - 1) * cell.W * 0.3f : 0;
                g.Dy = glitchPhase ? ((i % 2) - 0.5f) * maskH * 0.15f : 0;
                g.ScaleX = glitchPhase ? 1.0f + ((i % 3) - 1) * 0.1f : 1.0f;
                g.Alpha = Math.Min(1, t * 3) * (glitchPhase ? 0.85f : 1f);
                break;
            }
            case TextEffect.Shatter:
            {
                // Glass shatter with pieces flying outward
                var e = SignageFx.EaseOutCubic(t);
                var shardAngle = cell.Phase * (float)Math.PI * 2;
                var explosionDist = (1 - e) * maxDim * 0.8f;
                g.Dx = (float)Math.Cos(shardAngle) * explosionDist;
                g.Dy = (float)Math.Sin(shardAngle) * explosionDist + (1 - e) * maskH * 0.5f;
                g.Rotation = (1 - e) * (360f + cell.DirX * 180f);
                g.Scale = e;
                g.Alpha = Math.Min(1, t * 2) * e;
                break;
            }
            case TextEffect.Vortex:
            {
                // Spinning vortex pulling characters in from periphery
                var e = SignageFx.EaseOutCubic(t);
                var spiralTurns = 3f;
                var angle = (1 - e) * spiralTurns * (float)Math.PI * 2 + i * 0.8f;
                var radius = (1 - e) * maxDim * 0.7f;
                g.Dx = (float)Math.Cos(angle) * radius;
                g.Dy = (float)Math.Sin(angle) * radius;
                g.Rotation = (1 - e) * 720f;
                g.Scale = Math.Max(0.1f, e);
                g.Alpha = Math.Min(1, t * 2.5f);
                break;
            }
            case TextEffect.FadeReveal:
            {
                // Cinematic fade with directional wipe
                var e = SignageFx.EaseOutCubic(t);
                var wipeProgress = t * (_cells.Count + 5) - i;
                g.Alpha = Math.Clamp(wipeProgress, 0, 1);
                g.Scale = 0.8f + e * 0.2f;
                g.Dy = (1 - e) * maskH * 0.2f;
                break;
            }
            case TextEffect.Neon:
            {
                // Neon sign flicker effect
                var flicker = (_nowMs + i * 100) % 600 < 50 ? 0.3f : 1f;
                var warmup = Math.Min(t * 3f, 1f);
                g.Alpha = warmup * flicker;
                g.Scale = 0.95f + warmup * 0.05f + (flicker < 1 ? 0.05f : 0);
                break;
            }
            case TextEffect.Hologram:
            {
                // Sci-fi hologram materialization with scan lines
                var e = SignageFx.EaseOutCubic(t);
                var scanLine = (float)Math.Sin(_nowMs / 200.0 + i * 1.5);
                g.Alpha = Math.Min(1, t * 2) * (0.7f + scanLine * 0.3f);
                g.ScaleY = e * (0.95f + scanLine * 0.05f);
                g.Dy = (1 - e) * maskH * 0.1f + scanLine * 2f;
                break;
            }
            case TextEffect.Fire:
            {
                // Burning/flame effect with upward wave distortion
                var e = SignageFx.EaseOutCubic(t);
                var flameWave = (float)Math.Sin(_nowMs / 150.0 + i * 0.8 + cell.Phase * 3);
                g.Dy = -(1 - e) * maskH * 0.4f + flameWave * maskH * 0.15f * e;
                g.Dx = flameWave * cell.W * 0.2f * e;
                g.ScaleY = e * (0.9f + flameWave * 0.1f);
                g.Alpha = Math.Min(1, t * 2) * (1 - (1 - e) * 0.5f);
                break;
            }
            case TextEffect.Ripple:
            {
                // Water ripple distortion emanating from center
                var e = SignageFx.EaseOutCubic(t);
                var centerX = _bandW * 0.5f;
                var distFromCenter = Math.Abs(cell.X + cell.W * 0.5f - centerX);
                var ripplePhase = t * 10 - distFromCenter / (maxDim * 0.1f);
                var rippleAmp = Math.Max(0, 1 - t) * maskH * 0.15f;
                g.Dy = ripplePhase > 0 ? (float)Math.Sin(ripplePhase * Math.PI * 2) * rippleAmp : 0;
                g.Scale = 1 + (ripplePhase > 0 && ripplePhase < 1 ? Math.Abs((float)Math.Sin(ripplePhase * Math.PI)) * 0.15f : 0);
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.Explode:
            {
                // Particle explosion from center
                var e = SignageFx.EaseOutCubic(t);
                var centerX = _bandW * 0.5f;
                var dx = cell.X + cell.W * 0.5f - centerX;
                var explosionPower = (1 - e) * 2f;
                g.Dx = dx * explosionPower;
                g.Dy = ((i % 3) - 1) * maskH * explosionPower * 0.4f;
                g.Rotation = (1 - e) * (cell.Phase * 360f);
                g.Scale = Math.Max(0.1f, e) * (1 + (1 - e) * 0.3f);
                g.Alpha = Math.Min(1, t * 2) * e;
                break;
            }
            case TextEffect.Assemble:
            {
                // Puzzle pieces sliding into place
                var e = SignageFx.EaseOutBack(t);
                var assembleAngle = i * 0.7f + cell.Phase;
                var assembleDist = (1 - e) * maxDim * 0.5f;
                g.Dx = (float)Math.Cos(assembleAngle) * assembleDist;
                g.Dy = (float)Math.Sin(assembleAngle) * assembleDist;
                g.Rotation = (1 - e) * (180f * cell.DirX);
                g.Alpha = Math.Min(1, t * 2.5f);
                break;
            }
            case TextEffect.Lightning:
            {
                // Electric arc with jittery movement
                var e = SignageFx.EaseOutCubic(t);
                var electric = (_nowMs + i * 80) % 200 < 100;
                var jitterX = electric ? ((i % 5) - 2) * 2f : 0;
                var jitterY = electric ? ((i % 3) - 1) * 2f : 0;
                g.Dx = (cell.DirX * maxDim * (1 - e) * 0.3f) + jitterX;
                g.Dy = (cell.DirY * maxDim * (1 - e) * 0.3f) + jitterY;
                g.Alpha = Math.Min(1, t * 3) * (electric ? 1f : 0.8f);
                g.Scale = e * (electric ? 1.05f : 1f);
                break;
            }
            // ─── Blockbuster Movie Intros ────────────────────────────────────────
            case TextEffect.StarWars:
            {
                // Vanishing-point rush: characters start tiny at the CENTER of the band and
                // fly outward to their reading positions while growing to full size.
                var e = SignageFx.EaseOutCubic(t);
                var centerX = _bandW * 0.5f;
                var centerY = maskH * 0.5f;
                var targetX = cell.X + cell.W * 0.5f;
                var targetY = maskH * 0.5f;
                // Offset from target: at t=0 the character is drawn AT the centre; at t=1 no offset.
                g.Dx = (centerX - targetX) * (1 - e);
                g.Dy = (centerY - targetY) * (1 - e);
                // Grow from a distant dot to full readable size.
                g.Scale = 0.15f + e * 0.85f;
                g.Alpha = Math.Min(1, t * 3f);
                break;
            }
            case TextEffect.Pixar:
            {
                // Character-by-character bouncy landing with squash + stretch, like a Pixar
                // logo intro. Extra squash on impact then springs upright.
                var overshoot = SignageFx.EaseOutBack(Math.Min(t * 1.3f, 1f));
                var landing = SignageFx.EaseOutBounce(t);
                var squash = t < 0.5f ? (1f - t * 0.6f) : (1f + (1 - t) * 0.3f);
                g.Dy = -(blockY + maskH) * (1 - landing);
                g.ScaleX = overshoot * (2f - squash);   // stretch tall on descent, squish on impact
                g.ScaleY = overshoot * squash;
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.Minion:
            {
                // Goofy wobble: characters shake, tilt back and forth, wiggle in from below with
                // an over-eager overshoot. Feels distinctly Despicable-Me / cartoon.
                var e = SignageFx.EaseOutBack(t);
                var wobble = (float)Math.Sin(_nowMs / 90.0 + cell.Phase * 6f) * (1 - e);
                var giggle = (float)Math.Sin(_nowMs / 60.0 + i * 1.7f) * 0.15f * e;
                g.Dy = (1 - e) * maskH * 0.9f + wobble * maskH * 0.18f;
                g.Dx = wobble * cell.W * 0.25f;
                g.Rotation = wobble * 22f + giggle * 30f * (1 - t);
                g.Scale = e * (1f + giggle * 0.2f);
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.MarvelFlash:
            {
                // Comic-book impact: huge scale, transparent, snapping down to size with a bright
                // over-scaled flash at the front, like a Marvel/DC logo hit.
                var e = SignageFx.EaseOutCubic(t);
                var punch = t < 0.35f ? (1f - t / 0.35f) : 0f;   // hot flash in the first third
                g.Scale = 4f - e * 3f + punch * 0.5f;             // slam from 4x to 1x
                g.Alpha = Math.Min(1, (1 - punch) * (t < 0.05f ? 0f : 1f) + punch * 0.8f);
                g.Rotation = (1 - e) * (cell.DirX >= 0 ? 8f : -8f); // slight comic tilt on entry
                break;
            }
            case TextEffect.LightSpeed:
            {
                // Streaks from a vanishing point (screen centre) into position, with elongated
                // horizontal stretch during flight — the classic hyperspace jump.
                var e = SignageFx.EaseOutCubic(t);
                var centerX = _bandW * 0.5f;
                var centerY = _bandH * 0.5f;
                var targetX = cell.X + cell.W * 0.5f;
                var targetY = maskH * 0.5f;
                g.Dx = (centerX - targetX) * (1 - e);
                g.Dy = (centerY - targetY) * (1 - e);
                // Stretch heavily along direction of travel, then snap normal at arrival.
                var stretch = (1 - e);
                g.ScaleX = 1f + stretch * 6f * (targetX > centerX ? 1f : 1f);
                g.ScaleY = 1f - stretch * 0.4f;
                g.Alpha = Math.Min(1, t * 4);
                break;
            }
            case TextEffect.Portal:
            {
                // Doctor-Strange-style: characters spiral in from a swirling portal, growing and
                // rotating. Multiple full turns during arrival for that magical vibe.
                var e = SignageFx.EaseOutCubic(t);
                var swirlRadius = (1 - e) * maxDim * 0.35f;
                var swirlAngle = t * 6.28f * 2.5f + cell.Phase * 3f;
                g.Dx = MathF.Cos(swirlAngle) * swirlRadius;
                g.Dy = MathF.Sin(swirlAngle) * swirlRadius;
                g.Rotation = (1 - e) * 540f;              // 1.5 turns
                g.Scale = 0.1f + e * 0.9f;
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.Domino:
            {
                // Each character topples forward around its base like a falling domino, in
                // sequence — great chain-reaction feel.
                var e = SignageFx.EaseOutBounce(t);
                // Rotation pivots at the "base" (bottom-centre): we simulate by combining
                // rotation with a vertical anchor offset so the top drops forward.
                var fallAngle = (1 - e) * 90f;
                g.Rotation = -fallAngle;
                g.Dy = MathF.Sin(fallAngle * MathF.PI / 180f) * maskH * 0.35f;
                g.Dx = -(1 - MathF.Cos(fallAngle * MathF.PI / 180f)) * cell.W * 0.5f;
                g.Alpha = Math.Min(1, t * 3);
                break;
            }
            case TextEffect.CameraShake:
            {
                // Earthquake reveal — every character trembles violently and settles as the
                // shake dampens. Use pseudo-random per-character offsets seeded by position.
                var e = SignageFx.EaseOutCubic(t);
                var shakeAmp = (1 - e) * maskH * 0.35f;
                // Cheap deterministic jitter per frame per character.
                var s1 = MathF.Sin((float)_nowMs * 0.041f + i * 4.7f);
                var s2 = MathF.Sin((float)_nowMs * 0.037f + i * 2.9f);
                g.Dx = s1 * shakeAmp;
                g.Dy = s2 * shakeAmp;
                g.Rotation = s1 * 4f * (1 - e);
                g.Alpha = Math.Min(1, t * 4);
                break;
            }
            case TextEffect.FilmReel:
            {
                // Old film projector: flicker, weave, occasional dropped frame. Sepia-ish vibe
                // is left to the user's colour choice; we handle motion + flicker + a single
                // ghost-jump.
                var e = SignageFx.EaseOutCubic(t);
                var flicker = ((_nowMs + i * 20) % 90) < 8 ? 0.55f : 1f;
                var weaveY = MathF.Sin((float)_nowMs * 0.02f + i * 0.3f) * 1.6f;
                var missedFrame = ((int)(_nowMs / 220) + i) % 11 == 0;
                g.Dx = missedFrame ? cell.W * 0.15f : 0;
                g.Dy = weaveY + (missedFrame ? -1f : 0f);
                g.Alpha = Math.Min(1, t * 3) * flicker * (missedFrame ? 0.7f : 1f);
                g.Scale = 0.95f + e * 0.05f;
                break;
            }
            case TextEffect.Bubble:
            {
                // Elastic soap-bubble swell: over-inflates, wobbles, settles. Uses independent
                // X/Y scale wobble like surface tension.
                var e = SignageFx.EaseOutBack(t);
                var wobbleX = MathF.Sin(t * 12f + cell.Phase * 4f) * (1 - t) * 0.3f;
                var wobbleY = MathF.Cos(t * 12f + cell.Phase * 4f) * (1 - t) * 0.3f;
                g.ScaleX = e * (1f + wobbleX);
                g.ScaleY = e * (1f + wobbleY);
                g.Alpha = Math.Min(1, t * 4);
                break;
            }
            case TextEffect.HeroLanding:
            {
                // Superhero smash — character falls from way above with growing scale, cracks
                // down at impact (mid-anim), then a tiny recoil. Classic MCU hero landing.
                var impact = 0.55f;
                if (t < impact)
                {
                    // Descent phase
                    var td = t / impact;
                    var e = SignageFx.EaseOutCubic(td);
                    g.Dy = -(blockY + maskH * 3f) * (1 - e);
                    g.Scale = 1f + (1 - e) * 0.8f;   // slightly bigger while falling
                    g.Alpha = Math.Min(1, td * 3);
                }
                else
                {
                    // Impact & recoil phase — squash then unstick.
                    var tr = (t - impact) / (1 - impact);
                    var recoil = SignageFx.EaseOutBounce(tr);
                    g.ScaleY = 0.6f + recoil * 0.4f;   // squashed then normal
                    g.ScaleX = 1.4f - recoil * 0.4f;
                    g.Alpha = 1f;
                }
                break;
            }
            case TextEffect.JumpCut:
            {
                // Rapid MTV/action-trailer jump-cuts: character teleports between random offsets
                // several times before locking on. Zero interpolation between snaps.
                var cutIndex = (int)(t * 5); // 5 cuts across the animation
                var seed = i * 977 + cutIndex * 31;
                if (cutIndex < 4)
                {
                    // Deterministic pseudo-random offsets, one snapped position per cut.
                    g.Dx = ((seed % 17) - 8) * (cell.W * 0.3f);
                    g.Dy = (((seed / 17) % 13) - 6) * (maskH * 0.25f);
                    g.Rotation = ((seed % 7) - 3) * 6f;
                    g.Scale = 0.9f + (seed % 5) * 0.05f;
                    g.Alpha = Math.Min(1, t * 4);
                }
                else
                {
                    // Final "settle" cut — snap to place.
                    g.Alpha = 1f;
                }
                break;
            }
        }

        return g;
    }

    private void DrawGlyphCell(SKCanvas canvas, CharCell cell, float blockX, float blockY, GlyphAnim g,
        SKColor color, float alpha)
    {
        if (_maskImage == null) return;
        var a = (byte)Math.Clamp(alpha * 255f, 0, 255);
        if (a == 0) return;

        var maskH = _mask!.Height;
        var srcCell = g.SlotIndex >= 0 && g.SlotIndex < _cells.Count ? _cells[g.SlotIndex] : cell;
        var cx = blockX + cell.X + cell.W / 2f + g.Dx;
        var cy = blockY + maskH / 2f + g.Dy;

        canvas.Save();
        canvas.Translate(cx, cy);
        if (Math.Abs(g.Rotation) > 0.01f) canvas.RotateDegrees(g.Rotation);
        var sx = g.Scale * g.ScaleX;
        var sy = g.Scale * g.ScaleY;
        if (Math.Abs(sx - 1f) > 0.001f || Math.Abs(sy - 1f) > 0.001f) canvas.Scale(sx, sy);

        var src = new SKRect(srcCell.X, 0, srcCell.X + srcCell.W, maskH);
        var dst = new SKRect(-cell.W / 2f, -maskH / 2f, cell.W / 2f, maskH / 2f);

        if (g.Reveal < 0.999f)
        {
            var revW = cell.W * Math.Clamp(g.Reveal, 0f, 1f);
            canvas.ClipRect(new SKRect(-cell.W / 2f, -maskH / 2f, -cell.W / 2f + revW, maskH / 2f));
        }

        if (Glow)
        {
            var r = Sc(3);
            using var glowPaint = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateBlendMode(color.WithAlpha((byte)(a * 0.85f)), SKBlendMode.SrcIn),
                ImageFilter = SKImageFilter.CreateBlur(r, r),
                IsAntialias = true
            };
            canvas.DrawImage(_maskImage, src, dst, SignageFx.Nearest, glowPaint);
        }

        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(color.WithAlpha(a), SKBlendMode.SrcIn),
            IsAntialias = false
        };
        canvas.DrawImage(_maskImage, src, dst, SignageFx.Nearest, paint);
        canvas.Restore();
    }

    private void DrawMask(SKCanvas canvas, float x, float y, float alpha)
    {
        if (_maskImage == null || alpha <= 0.01f) return;
        var a = (byte)Math.Clamp(alpha * 255f, 0, 255);

        if (Glow)
        {
            var r = Sc(3);
            using var gp = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateBlendMode(_currentColor.WithAlpha((byte)(a * 0.8f)),
                    SKBlendMode.SrcIn),
                ImageFilter = SKImageFilter.CreateBlur(r, r)
            };
            canvas.DrawImage(_maskImage, x, y, SignageFx.Nearest, gp);
        }

        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(_currentColor.WithAlpha(a), SKBlendMode.SrcIn)
        };
        canvas.DrawImage(_maskImage, x, y, SignageFx.Nearest, paint);
    }

    private void SpawnSparkle(float x, float y, int spread)
    {
        if (_sparkles.Count >= 120) return;
        _sparkles.Add(new SparkleParticle
        {
            X = x + (_random.NextSingle() - 0.5f) * spread,
            Y = y + (_random.NextSingle() - 0.5f) * spread,
            Life = 0,
            Max = 6 + _random.Next(12),
            Color = SignageFx.Hue(_random.Next(360))
        });
    }

    private void UpdateAndDrawSparkles(SKCanvas canvas, float globalAlpha)
    {
        if (_sparkles.Count == 0) return;
        var sz = Sc(2);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        for (var i = _sparkles.Count - 1; i >= 0; i--)
        {
            var s = _sparkles[i];
            s.Life++;
            if (s.Life >= s.Max)
            {
                _sparkles.RemoveAt(i);
                continue;
            }

            var f = 1f - s.Life / s.Max;
            var a = (byte)Math.Clamp(255 * f * globalAlpha, 0, 255);
            paint.Color = s.Color.WithAlpha(a);
            canvas.DrawRect(s.X - sz / 2f, s.Y - sz / 2f, sz, sz, paint);
            paint.Color = SKColors.White.WithAlpha((byte)(a * 0.7f));
            canvas.DrawRect(s.X - sz, s.Y, sz * 2, 1, paint);
            canvas.DrawRect(s.X, s.Y - sz, 1, sz * 2, paint);
            _sparkles[i] = s;
        }
    }

    private void InitScroll()
    {
        _scrollInitialized = true;
        _scrollPos = _currentDirection switch
        {
            ScrollDirection.Left => _bandW,
            ScrollDirection.Right => -_mask!.Width,
            ScrollDirection.Up => _bandH,
            ScrollDirection.Down => -_mask!.Height,
            _ => 0
        };
    }

    private bool AdvanceMessage()
    {
        if (Messages.Length == 0) return false;
        _messageIndex = (_messageIndex + 1) % Messages.Length;
        _scrollInitialized = false;
        _messageStartMs = _nowMs;
        RebuildCurrentMessage();
        return true;
    }

    // ───────────────────────────────────────── narrative effects ─────────────

    private static bool IsNarrative(TextEffect fx) => fx is
        TextEffect.StickBuild or TextEffect.StickKick or TextEffect.DominoPush or
        TextEffect.Conveyor or TextEffect.Magnet or TextEffect.PoolBreak or
        TextEffect.Builder or TextEffect.DogWalk or TextEffect.PacManEat or TextEffect.NeoVision;

    /// <summary>
    ///     Render path for effects where characters interact with each other or with drawn "actors"
    ///     (stick figures, conveyor belt, magnet, cue ball). Uses a single global timeline instead
    ///     of per-character stagger so the storyline reads correctly.
    /// </summary>
    private bool DrawNarrative(SKCanvas canvas, float blockX, float blockY, int maskH, double elapsed)
    {
        var count = _cells.Count;
        if (count == 0)
        {
            var holdEmpty = MessageDurationSeconds * 1000.0;
            return elapsed >= holdEmpty && AdvanceMessage();
        }

        // Timing model — each effect has two phases:
        //   BUILD (0 .. buildMs)   — characters progressively assembled into their final positions.
        //   EXIT  (buildMs .. buildMs+exitMs) — trailing actor (walker/dog/pacman/scan) leaves.
        //
        // MessageDuration is honoured as "fully-built hold time" — the message stays on-screen for
        // exactly this many seconds *after* the last character is placed. The trailing actor's
        // exit animation runs concurrently during that hold, so it never delays the advance.
        //
        // buildMs scales with visCount, not raw _cells.Count, so whitespace doesn't inflate it.
        var speed = Math.Max(0.05f, EffectSpeed);
        var speedInv = 1f / speed;
        var visCount = Math.Max(1, _visibleCellIndices.Count);

        var (baseBuildMs, baseExitMs) = _currentEffect switch
        {
            //                        build (place all chars),  exit (actor leaves)
            TextEffect.StickBuild => (500.0 + visCount * 500.0, 1200.0),  // last walker walks off
            TextEffect.StickKick  => (350.0 + visCount * 700.0,    0.0),   // no trailing actor
            TextEffect.DominoPush => (500.0 + visCount * 500.0,    0.0),
            TextEffect.Conveyor   => (1500.0 + visCount * 250.0,  800.0),  // belt slides out left
            TextEffect.Magnet     => (400.0 + visCount * 700.0,    0.0),
            TextEffect.PoolBreak  => (2500.0,                     500.0),  // cue ball recoils off-screen
            TextEffect.Builder    => (400.0 + visCount * 900.0,    0.0),
            TextEffect.DogWalk    => (500.0 + visCount * 500.0,  1200.0),  // dog walks off right
            TextEffect.PacManEat  => (600.0 + visCount * 350.0,   900.0),  // pacman + ghosts exit
            TextEffect.NeoVision  => (300.0 + visCount * 250.0,   800.0),  // scan bar exits right
            _ => (1500.0, 0.0)
        };
        var buildMs = baseBuildMs * speedInv;
        var exitMs = baseExitMs * speedInv;
        var holdMs = MessageDurationSeconds * 1000.0;
        // Total on-screen time = build + max(hold, exit). Exit runs concurrently with hold.
        var totalMs = buildMs + Math.Max(holdMs, exitMs);

        var globalAlpha = 1f;
        if (Fade && elapsed > totalMs - 400)
            globalAlpha = (float)Math.Max(0, (totalMs - elapsed) / 400.0);

        // Build progress: 0..1 during 0..buildMs, saturates at 1 during the hold+exit phase.
        var t = (float)Math.Clamp(elapsed / buildMs, 0, 1);
        // Exit progress: 0 during build, 0..1 during exitMs, saturates at 1 after. Passed as a
        // separate parameter so renderers can animate trailing actors AFTER placement without
        // affecting character positions.
        var xt = exitMs > 0
            ? (float)Math.Clamp((elapsed - buildMs) / exitMs, 0, 1)
            : 1f;

        switch (_currentEffect)
        {
            case TextEffect.StickBuild: DrawStickBuild(canvas, blockX, blockY, maskH, t, xt, globalAlpha); break;
            case TextEffect.StickKick:  DrawStickKick(canvas, blockX, blockY, maskH, t, globalAlpha); break;
            case TextEffect.DominoPush: DrawDominoPush(canvas, blockX, blockY, maskH, t, globalAlpha); break;
            case TextEffect.Conveyor:   DrawConveyor(canvas, blockX, blockY, maskH, t, xt, globalAlpha); break;
            case TextEffect.Magnet:     DrawMagnet(canvas, blockX, blockY, maskH, t, globalAlpha); break;
            case TextEffect.PoolBreak:  DrawPoolBreak(canvas, blockX, blockY, maskH, t, xt, globalAlpha); break;
            case TextEffect.Builder:    DrawBuilder(canvas, blockX, blockY, maskH, t, globalAlpha); break;
            case TextEffect.DogWalk:    DrawDogWalk(canvas, blockX, blockY, maskH, t, xt, globalAlpha); break;
            case TextEffect.PacManEat:  DrawPacManEat(canvas, blockX, blockY, maskH, t, xt, globalAlpha); break;
            case TextEffect.NeoVision:  DrawNeoVision(canvas, blockX, blockY, maskH, t, xt, globalAlpha); break;
        }

        UpdateAndDrawSparkles(canvas, globalAlpha);

        if (elapsed >= totalMs)
        {
            _sparkles.Clear();
            return AdvanceMessage();
        }
        return false;
    }

    // Utility: pick the base render colour for a character.
    private SKColor NarrativeColor(int i)
    {
        var hue = _frame * 2f + i * 25f;
        return MultiColor && !_currentColorSet ? SignageFx.Hue(hue) : _currentColor;
    }

    // ─────────── StickBuild ────────────────────────────────────────────────
    // Stick figures walk on from the sides carrying their character on their head, walk to the
    // target column, drop the character, then continue walking off the opposite side. Multiple
    // walkers can be on-screen at once (overlapping schedules).
    //
    // Timeline per walker s ∈ [0, visCount):
    //   phase = t' / phaseLen  where t' = tBuild - s * stepLen
    //     0.00 .. 0.65   → walk in (character carried on head)
    //     0.65 .. 1.00   → drop (walker stationary next to target column)
    //     1.00 .. 1.40   → walk off toward the opposite side
    //   Steps overlap: stepLen = 0.7 * phaseLen so a new walker arrives before the previous
    //   walker has finished walking off.
    //
    // We choose phaseLen so the LAST walker's DROP completes exactly at tBuild = 1 (message
    // fully placed at the end of the build phase). The last walker's walk-off then extends into
    // the exit phase and uses xt to keep advancing beyond tBuild = 1.

    private void DrawStickBuild(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float xt, float globalAlpha)
    {
        var visible = _visibleCellIndices;
        var visCount = visible.Count;
        if (visCount == 0) return;

        // Walker phase geometry (as fractions of the per-walker window).
        const float walkInEnd = 0.65f;
        const float dropEnd = 1.00f;
        const float walkOffEnd = 1.40f;
        const float overlap = 0.30f;   // fraction of a window consumed before next walker starts

        // stepLen and phaseLen chosen so that the LAST walker's drop (dropEnd) ends at build-time 1.
        // Walker s starts at s*stepLen. Its drop-end is s*stepLen + dropEnd*phaseLen.
        // For s = visCount-1 we want that == 1.0f.
        // With stepLen = overlap*phaseLen: (visCount-1)*overlap*phaseLen + dropEnd*phaseLen = 1
        var phaseLen = 1f / ((visCount - 1) * overlap + dropEnd);
        var stepLen = overlap * phaseLen;

        var walkerFootY = blockY + maskH + Math.Max(2, Sc(6));

        using var floorPaint = new SKPaint
        {
            Color = NarrativeColor(0).WithAlpha((byte)(120 * globalAlpha)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, Sc(1))
        };
        canvas.DrawLine(0, walkerFootY, _bandW, walkerFootY, floorPaint);

        // Effective build time — extends past 1 for the last walker's walk-off during the exit
        // phase. Non-last walkers have already finished walking off before tBuild = 1.
        var tBuild = t + (t >= 1f ? xt * (walkOffEnd - dropEnd) : 0f);

        for (var s = 0; s < visCount; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var startT = s * stepLen;
            var phaseT = (tBuild - startT) / phaseLen;     // 0..walkOffEnd once fully done

            if (phaseT <= 0f) continue;                    // walker hasn't started yet
            if (phaseT >= walkOffEnd) continue;            // walker has left the display

            var targetX = blockX + cell.X + cell.W / 2f;
            var fromLeft = (s % 2 == 0);
            var startX = fromLeft ? -Sc(12) : _bandW + Sc(12);
            var exitX = fromLeft ? _bandW + Sc(12) : -Sc(12);
            var charColor = NarrativeColor(s);
            var g = GlyphAnim.Default;

            if (phaseT < walkInEnd)
            {
                // Walk-in: walker carries character on head.
                var wT = SignageFx.EaseOutCubic(phaseT / walkInEnd);
                var figX = startX + (targetX - startX) * wT;
                DrawStickFigure(canvas, figX, walkerFootY, maskH, phaseT, charColor, globalAlpha, carrying: true);
                g.Dx = figX - targetX;
                g.Dy = -(maskH * 0.9f);
                g.Alpha = globalAlpha;
                DrawGlyphCell(canvas, cell, blockX, blockY, g, charColor, globalAlpha);
            }
            else if (phaseT < dropEnd)
            {
                // Drop: char falls into place; walker stationary next to target.
                var dT = (phaseT - walkInEnd) / (dropEnd - walkInEnd);
                var fall = SignageFx.EaseOutBounce(dT);
                var startY = -(maskH * 0.9f);
                DrawStickFigure(canvas, targetX, walkerFootY, maskH, phaseT, charColor, globalAlpha, carrying: false);
                g.Dy = startY * (1 - fall);
                if (dT > 0.7f && dT < 1f)
                {
                    var sT = (dT - 0.7f) / 0.3f;
                    var squash = MathF.Sin(sT * MathF.PI);
                    g.ScaleX = 1f + 0.15f * squash;
                    g.ScaleY = 1f - 0.15f * squash;
                }
                g.Alpha = globalAlpha;
                DrawGlyphCell(canvas, cell, blockX, blockY, g, charColor, globalAlpha);
            }
            else
            {
                // Walk-off: character is placed and stays; walker heads to the opposite side.
                var oT = SignageFx.EaseOutCubic((phaseT - dropEnd) / (walkOffEnd - dropEnd));
                var figX = targetX + (exitX - targetX) * oT;
                DrawStickFigure(canvas, figX, walkerFootY, maskH, phaseT, charColor, globalAlpha, carrying: false);
                g.Alpha = globalAlpha;
                DrawGlyphCell(canvas, cell, blockX, blockY, g, charColor, globalAlpha);
            }
        }

        // Already-placed characters whose walkers have long since gone: they'd be culled by the
        // early-continue above (phaseT >= walkOffEnd), so we must still render them here.
        for (var s = 0; s < visCount; s++)
        {
            var startT = s * stepLen;
            var phaseT = (tBuild - startT) / phaseLen;
            if (phaseT < walkOffEnd) continue;   // handled in the main loop above

            var i = visible[s];
            var cell = _cells[i];
            var charColor = NarrativeColor(s);
            var g = GlyphAnim.Default;
            g.Alpha = globalAlpha;
            DrawGlyphCell(canvas, cell, blockX, blockY, g, charColor, globalAlpha);
        }
    }

    /// <summary>Simple 5-line stick figure. Head, body, 2 legs (walk-cycled), 2 arms.</summary>
    private void DrawStickFigure(SKCanvas canvas, float footX, float footY, int maskH, float phase,
        SKColor tint, float globalAlpha, bool carrying)
    {
        // Sized relative to text height so it always looks proportional.
        var bodyH = maskH * 0.7f;
        var headR = maskH * 0.16f;
        var strokeW = Math.Max(1, Sc(1));

        using var p = new SKPaint
        {
            Color = tint.WithAlpha((byte)Math.Clamp(220 * globalAlpha, 0, 255)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeW,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        using var fillP = new SKPaint
        {
            Color = tint.WithAlpha((byte)Math.Clamp(220 * globalAlpha, 0, 255)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        var hipY = footY - bodyH * 0.5f;
        var neckY = footY - bodyH;
        var headY = neckY - headR;

        // Legs — simple walk cycle based on phase.
        var swing = MathF.Sin(phase * MathF.PI * 8f) * (bodyH * 0.18f);
        canvas.DrawLine(footX, hipY, footX - swing, footY, p);
        canvas.DrawLine(footX, hipY, footX + swing, footY, p);

        // Body.
        canvas.DrawLine(footX, hipY, footX, neckY, p);

        // Arms — up when carrying, swinging when not.
        if (carrying)
        {
            var armY = neckY + bodyH * 0.1f;
            canvas.DrawLine(footX, armY, footX - bodyH * 0.3f, headY, p);
            canvas.DrawLine(footX, armY, footX + bodyH * 0.3f, headY, p);
        }
        else
        {
            var armY = neckY + bodyH * 0.15f;
            var armSwing = MathF.Sin(phase * MathF.PI * 8f) * (bodyH * 0.22f);
            canvas.DrawLine(footX, armY, footX - bodyH * 0.28f, armY + bodyH * 0.35f + armSwing, p);
            canvas.DrawLine(footX, armY, footX + bodyH * 0.28f, armY + bodyH * 0.35f - armSwing, p);
        }

        // Head.
        canvas.DrawCircle(footX, headY, headR, fillP);
    }

    // ─────────── DominoPush ────────────────────────────────────────────────
    // First character slams in from the left. As it reaches each subsequent character it "kicks"
    // it out of its resting cluster (all cells piled at the far-right of the band) toward its
    // target column. Chain-reaction feel with a bounce on arrival.

    private void DrawDominoPush(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float globalAlpha)
    {
        var count = _cells.Count;
        var perStep = 1f / count;

        for (var i = 0; i < count; i++)
        {
            var cell = _cells[i];
            if (cell.W <= 0) continue;

            // Each character gets kicked at t = i * perStep and settles by (i+1) * perStep.
            var launch = i * perStep;
            var settle = (i + 1) * perStep;
            var localT = Math.Clamp((t - launch) / (settle - launch), 0f, 1f);

            var color = NarrativeColor(i);
            var g = GlyphAnim.Default;

            if (t < launch)
            {
                // Still parked off-screen to the LEFT (waiting to be kicked).
                g.Dx = -blockX - cell.X - cell.W - Sc(20);
                g.Alpha = 0f;
            }
            else
            {
                var e = SignageFx.EaseOutBounce(localT);
                var startOffset = -blockX - cell.X - cell.W - Sc(20);
                g.Dx = startOffset * (1 - e);
                // Little vertical bounce.
                g.Dy = -MathF.Sin(localT * MathF.PI) * maskH * 0.25f;
                // Transient landing squash — releases fully at localT == 1 so characters aren't
                // stuck at ScaleY < 1 during the hold phase.
                if (localT > 0.85f && localT < 1f)
                {
                    var sT = (localT - 0.85f) / 0.15f;
                    var squash = MathF.Sin(sT * MathF.PI);
                    g.ScaleY = 1f - 0.3f * squash;
                    g.ScaleX = 1f + 0.2f * squash;
                }
                g.Alpha = Math.Min(1, localT * 4f) * globalAlpha;
            }

            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);

            // Draw impact-shock lines when this character has just landed.
            if (t >= launch && t < settle && localT > 0.8f && localT < 0.98f)
            {
                DrawImpactBurst(canvas, blockX + cell.X + cell.W / 2f, blockY + maskH * 0.5f,
                    maskH * 0.4f, color, globalAlpha * (1 - (localT - 0.8f) / 0.18f));
            }
        }
    }

    private void DrawImpactBurst(SKCanvas canvas, float cx, float cy, float len, SKColor tint, float alpha)
    {
        using var p = new SKPaint
        {
            Color = tint.WithAlpha((byte)Math.Clamp(255 * alpha, 0, 255)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, Sc(1)),
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        for (var k = 0; k < 6; k++)
        {
            var ang = k * MathF.PI / 3f;
            var x1 = cx + MathF.Cos(ang) * len * 0.5f;
            var y1 = cy + MathF.Sin(ang) * len * 0.5f;
            var x2 = cx + MathF.Cos(ang) * len;
            var y2 = cy + MathF.Sin(ang) * len;
            canvas.DrawLine(x1, y1, x2, y2, p);
        }
    }

    // ─────────── Conveyor ─────────────────────────────────────────────────
    // A conveyor belt slides in from the right carrying all characters. Each character rides
    // above its belt segment and hops off when it reaches its target column. Belt exits left.

    private void DrawConveyor(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float xt, float globalAlpha)
    {
        var visible = _visibleCellIndices;
        if (visible.Count == 0) return;

        var floorY = blockY + maskH + Math.Max(2, Sc(3));
        var beltH = Math.Max(3, Sc(4));

        // Belt slides in over first 25% of BUILD, holds full-width, then slides out during EXIT.
        float beltOffset;
        if (t < 1f)
        {
            beltOffset = t < 0.25f ? (1 - t / 0.25f) * _bandW : 0f;
        }
        else
        {
            // Exit: slide left off-screen.
            beltOffset = -xt * _bandW;
        }

        using var beltPaint = new SKPaint
        {
            Color = new SKColor(80, 80, 80, (byte)Math.Clamp(200 * globalAlpha, 0, 255)),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(beltOffset, floorY, _bandW, beltH, beltPaint);

        using var treadPaint = new SKPaint
        {
            Color = new SKColor(200, 200, 200, (byte)Math.Clamp(200 * globalAlpha, 0, 255)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, Sc(1))
        };
        var treadStep = Math.Max(3, Sc(6));
        var treadShift = ((int)(_nowMs / 30) % treadStep);
        for (var x = -treadStep + treadShift + (int)beltOffset; x < _bandW; x += treadStep)
        {
            canvas.DrawLine(x, floorY, x + treadStep / 2f, floorY + beltH, treadPaint);
        }

        // Character hop schedule spans t=0.4..0.9 of BUILD so the last char is placed BEFORE t=1.
        var hopStep = 0.5f / visible.Count;

        for (var s = 0; s < visible.Count; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var hopStart = 0.4f + s * hopStep;

            var color = NarrativeColor(s);
            var g = GlyphAnim.Default;

            if (t < hopStart)
            {
                // Riding the belt: drift in from the right edge to target column.
                var beltT = Math.Clamp(t / 0.4f, 0f, 1f);
                var startX = _bandW + Sc(10);
                var currentX = startX + (blockX + cell.X + cell.W / 2f - startX) * SignageFx.EaseOutCubic(beltT);
                g.Dx = currentX - (blockX + cell.X + cell.W / 2f);
                g.Alpha = Math.Min(1, beltT * 3) * globalAlpha;
            }
            else
            {
                // Hop-off arc, then rest.
                var hopT = Math.Clamp((t - hopStart) / (hopStep * 2f), 0f, 1f);
                var arc = MathF.Sin(hopT * MathF.PI) * maskH * 0.4f;
                g.Dy = -arc;
                g.Alpha = globalAlpha;
            }

            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }
    }

    // ─────────── Magnet ───────────────────────────────────────────────────
    // A cartoon horseshoe magnet flies in from off-screen and, one character at a time, "attracts"
    // each character from its scrambled starting position to the target column. Character wobbles
    // as it drags along.

    private void DrawMagnet(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float globalAlpha)
    {
        var count = _cells.Count;
        var perStep = 1f / count;

        // Where's the magnet right now? It floats above the target column of the currently-dragged
        // character and moves rightward through the word.
        var currentIdx = Math.Min(count - 1, (int)(t / perStep));
        var stepT = (t / perStep) - currentIdx;

        for (var i = 0; i < count; i++)
        {
            var cell = _cells[i];
            if (cell.W <= 0) continue;

            var launchT = i * perStep;
            var settleT = (i + 1) * perStep;
            var localT = Math.Clamp((t - launchT) / (settleT - launchT), 0f, 1f);

            var color = NarrativeColor(i);
            var g = GlyphAnim.Default;
            var targetX = blockX + cell.X + cell.W / 2f;

            if (t < launchT)
            {
                // Wait scrambled off-screen (top for even, bottom for odd — visually varied).
                var startY = (i % 2 == 0) ? -_bandH : _bandH;
                var startX = (i * 71) % _bandW;
                g.Dx = startX - targetX;
                g.Dy = startY;
                g.Alpha = 0f;
            }
            else
            {
                var e = SignageFx.EaseOutCubic(localT);
                var startY = (i % 2 == 0) ? -_bandH : _bandH;
                var startX = (i * 71) % _bandW;
                // Drag toward target column.
                g.Dx = (startX - targetX) * (1 - e);
                g.Dy = startY * (1 - e);
                // Wobble while being dragged.
                g.Rotation = MathF.Sin(localT * 30f) * 12f * (1 - e);
                g.Alpha = Math.Min(1, localT * 4) * globalAlpha;
            }

            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }

        // Draw the magnet above the currently-attracting character (or hover at end).
        var magnetX = blockX + _cells[currentIdx].X + _cells[currentIdx].W / 2f;
        var magnetY = blockY - maskH * 0.4f - MathF.Abs(MathF.Sin((float)_nowMs / 250f)) * maskH * 0.1f;
        DrawHorseshoeMagnet(canvas, magnetX, magnetY, maskH * 0.5f, globalAlpha);
    }

    private void DrawHorseshoeMagnet(SKCanvas canvas, float cx, float cy, float size, float alpha)
    {
        var red = new SKColor(220, 40, 40, (byte)Math.Clamp(230 * alpha, 0, 255));
        var silver = new SKColor(210, 210, 220, (byte)Math.Clamp(230 * alpha, 0, 255));
        var strokeW = Math.Max(1, Sc(1));

        using var redPaint = new SKPaint { Color = red, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var silverPaint = new SKPaint { Color = silver, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var edgePaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha((byte)Math.Clamp(200 * alpha, 0, 255)),
            Style = SKPaintStyle.Stroke, StrokeWidth = strokeW, IsAntialias = true
        };

        // U-shape: two vertical rects joined by a curve at the top, silver tips at the bottom.
        var w = size;
        var h = size * 0.9f;
        var barW = w * 0.28f;

        // Left prong (red main body + silver tip).
        canvas.DrawRect(cx - w / 2f, cy - h / 2f, barW, h - barW * 0.7f, redPaint);
        canvas.DrawRect(cx - w / 2f, cy + h / 2f - barW * 0.7f, barW, barW * 0.7f, silverPaint);
        canvas.DrawRect(cx - w / 2f, cy - h / 2f, barW, h, edgePaint);

        // Right prong.
        canvas.DrawRect(cx + w / 2f - barW, cy - h / 2f, barW, h - barW * 0.7f, redPaint);
        canvas.DrawRect(cx + w / 2f - barW, cy + h / 2f - barW * 0.7f, barW, barW * 0.7f, silverPaint);
        canvas.DrawRect(cx + w / 2f - barW, cy - h / 2f, barW, h, edgePaint);

        // Top connecting arc (rectangle for simplicity at LED resolution).
        canvas.DrawRect(cx - w / 2f, cy - h / 2f, w, barW * 0.9f, redPaint);
        canvas.DrawRect(cx - w / 2f, cy - h / 2f, w, barW * 0.9f, edgePaint);

        // A couple of "attraction" lines emitting from the tips downward.
        using var linePaint = new SKPaint
        {
            Color = red.WithAlpha((byte)Math.Clamp(160 * alpha, 0, 255)),
            Style = SKPaintStyle.Stroke, StrokeWidth = strokeW
        };
        var flash = ((int)(_nowMs / 100) % 2 == 0) ? 1f : 0.3f;
        linePaint.Color = red.WithAlpha((byte)Math.Clamp(160 * alpha * flash, 0, 255));
        canvas.DrawLine(cx - w / 2f + barW / 2f, cy + h / 2f, cx - w / 2f + barW / 2f, cy + h, linePaint);
        canvas.DrawLine(cx + w / 2f - barW / 2f, cy + h / 2f, cx + w / 2f - barW / 2f, cy + h, linePaint);
    }

    // ─────────── PoolBreak ────────────────────────────────────────────────
    // Characters start racked in a triangle at the centre. A white cue ball rockets in from the
    // left, hits the rack, and each character rolls to its final position with damped motion.
    // After impact the cue ball recoils fully off the left edge so it never occludes the text.

    private void DrawPoolBreak(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float xt, float globalAlpha)
    {
        var visible = _visibleCellIndices;
        var visCount = visible.Count;
        if (visCount == 0) return;

        var impactT = 0.35f;
        var rackX = _bandW * 0.5f;
        var rackY = blockY + maskH * 0.5f;
        var ballR = maskH * 0.28f;

        // Cue ball. BUILD 0..impactT: flies in from left. BUILD impactT..1: at impact position.
        // EXIT: recoils LEFT and exits the display.
        float cueX;
        if (t < impactT)
        {
            var bt = t / impactT;
            var e = SignageFx.EaseOutCubic(bt);
            cueX = -ballR + (rackX - ballR * 2 - -ballR) * e;
        }
        else if (t < 1f)
        {
            cueX = rackX - ballR * 2;   // parked just left of the rack
        }
        else
        {
            var impactPos = rackX - ballR * 2;
            var exitPos = -ballR * 3;
            cueX = impactPos + (exitPos - impactPos) * SignageFx.EaseOutCubic(xt);
        }
        if (cueX + ballR > 0) DrawCueBall(canvas, cueX, rackY, ballR, globalAlpha);

        for (var s = 0; s < visCount; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var color = NarrativeColor(s);
            var g = GlyphAnim.Default;
            var targetX = blockX + cell.X + cell.W / 2f;

            var racked = new SKPoint(
                rackX + ((s % 3) - 1) * cell.W * 0.4f,
                rackY + ((s / 3) - 1) * cell.W * 0.4f);

            if (t < impactT)
            {
                g.Dx = racked.X - targetX;
                g.Dy = racked.Y - (blockY + maskH * 0.5f);
                g.Alpha = Math.Min(1, t / impactT * 2) * globalAlpha;
            }
            else
            {
                var rt = (t - impactT) / (1f - impactT);
                var e = SignageFx.EaseOutCubic(rt);
                g.Dx = (racked.X - targetX) * (1 - e);
                g.Dy = (racked.Y - (blockY + maskH * 0.5f)) * (1 - e);
                g.Rotation = MathF.Sin(rt * 15f + s) * 8f * (1 - e);
                g.Alpha = globalAlpha;
            }

            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }
    }

    private void DrawCueBall(SKCanvas canvas, float cx, float cy, float r, float alpha)
    {
        using var body = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Clamp(240 * alpha, 0, 255)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var edge = new SKPaint
        {
            Color = SKColors.Black.WithAlpha((byte)Math.Clamp(200 * alpha, 0, 255)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, Sc(1)),
            IsAntialias = true
        };
        canvas.DrawCircle(cx, cy, r, body);
        canvas.DrawCircle(cx, cy, r, edge);
        // A tiny highlight for pool-ball feel.
        using var hi = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Clamp(255 * alpha, 0, 255)),
            Style = SKPaintStyle.Fill, IsAntialias = true
        };
        canvas.DrawCircle(cx - r * 0.35f, cy - r * 0.35f, r * 0.22f, hi);
    }

    // ─────────── StickKick ───────────────────────────────────────────────
    // All (visible) characters start in a jumbled pile on the floor near the LEFT edge, rotated
    // at random angles. A stick figure stands beside the pile and boots one character at a time
    // out of the pile toward its target column. The actor does NOT move to each column — it stays
    // planted at the pile edge, winds up, and kicks each character across the display.
    // Whitespace is skipped.

    private void DrawStickKick(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float globalAlpha)
    {
        var visible = _visibleCellIndices;
        var visCount = visible.Count;
        if (visCount == 0) return;

        var floorY = blockY + maskH + Math.Max(2, Sc(3));

        // Floor.
        using var floorPaint = new SKPaint
        {
            Color = NarrativeColor(0).WithAlpha((byte)(120 * globalAlpha)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, Sc(1))
        };
        canvas.DrawLine(0, floorY, _bandW, floorY, floorPaint);

        // Pile position — small cluster near the left edge, at floor level.
        var pileCX = Sc(14);
        var pileCY = floorY - maskH * 0.35f;
        // Actor stands just to the LEFT of the pile so its kicking leg swings rightward INTO the
        // pile as it launches each character. Fixed position for the entire animation.
        var actorX = pileCX - Sc(10);

        var perStep = 1f / visCount;
        var currentSlot = Math.Min(visCount - 1, (int)(t / perStep));
        var stepT = (t / perStep) - currentSlot;

        // Draw all characters. Three states: still in pile, being kicked, or already placed.
        for (var s = 0; s < visCount; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var color = NarrativeColor(s);
            var g = GlyphAnim.Default;
            var targetX = blockX + cell.X + cell.W / 2f;
            var targetY = blockY + maskH * 0.5f;

            // Deterministic pile offsets.
            var pileOffsetX = ((s * 37) % 11) - 5;
            var pileOffsetY = ((s * 71) % 5);
            var pileX = pileCX + pileOffsetX;
            var pileY = pileCY + pileOffsetY;
            var pileRot = ((s * 53) % 90) - 45f;

            if (s > currentSlot)
            {
                // Still in the pile.
                g.Dx = pileX - targetX;
                g.Dy = pileY - targetY;
                g.Rotation = pileRot;
                g.Alpha = 0.85f * globalAlpha;
                DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
                continue;
            }
            if (s == currentSlot)
            {
                // Wind-up first [0..0.4], then kick + arc [0.4..1.0]. Actor stays fixed, so we
                // don't need a "run to target" phase.
                if (stepT < 0.4f)
                {
                    g.Dx = pileX - targetX;
                    g.Dy = pileY - targetY;
                    g.Rotation = pileRot;
                    g.Alpha = 0.85f * globalAlpha;
                    DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
                }
                else
                {
                    var kT = (stepT - 0.4f) / 0.6f;
                    var e = SignageFx.EaseOutCubic(kT);
                    var xOff = (pileX - targetX) * (1 - e);
                    var yOff = (pileY - targetY) * (1 - e);
                    var arc = -MathF.Sin(kT * MathF.PI) * maskH * 0.9f;
                    g.Dx = xOff;
                    g.Dy = yOff + arc;
                    g.Rotation = pileRot * (1 - e) + (1 - e) * 720f;
                    // Transient landing squash — releases fully at kT == 1.
                    if (kT > 0.9f && kT < 1f)
                    {
                        var sT = (kT - 0.9f) / 0.1f;
                        var squash = MathF.Sin(sT * MathF.PI);
                        g.ScaleY = 1f - 0.15f * squash;
                        g.ScaleX = 1f + 0.15f * squash;
                    }
                    g.Alpha = globalAlpha;
                    DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
                }
                continue;
            }
            // s < currentSlot — already placed.
            g.Alpha = globalAlpha;
            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }

        // Actor animation — always at (actorX, floorY). Wind-up during first 40% of stepT, kick
        // release during the remaining 60%. Leg swings back for wind-up, forward for release.
        float legWind;
        bool kicking;
        if (stepT < 0.4f)
        {
            legWind = -stepT / 0.4f;    // 0 → -1 (pulled back)
            kicking = false;
        }
        else
        {
            legWind = 1f - (stepT - 0.4f) / 0.6f;    // 1 → 0 (kick + follow-through)
            kicking = true;
        }
        DrawStickFigureKicker(canvas, actorX, floorY, maskH, stepT, legWind, kicking,
            NarrativeColor(currentSlot), globalAlpha);
    }

    private void DrawStickFigureKicker(SKCanvas canvas, float footX, float footY, int maskH, float phase,
        float kickWind, bool kicking, SKColor tint, float globalAlpha)
    {
        var bodyH = maskH * 0.7f;
        var headR = maskH * 0.16f;
        var strokeW = Math.Max(1, Sc(1));

        using var p = new SKPaint
        {
            Color = tint.WithAlpha((byte)Math.Clamp(230 * globalAlpha, 0, 255)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeW,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        using var fillP = new SKPaint
        {
            Color = tint.WithAlpha((byte)Math.Clamp(230 * globalAlpha, 0, 255)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        var hipY = footY - bodyH * 0.5f;
        var neckY = footY - bodyH;
        var headY = neckY - headR;
        var runSwing = kicking ? 0f : MathF.Sin(phase * MathF.PI * 8f) * bodyH * 0.18f;

        // Standing leg (back leg while kicking).
        canvas.DrawLine(footX, hipY, footX - runSwing, footY, p);

        // Kicking leg — swings forward on wind-up = negative, then positive on release.
        var kickLegEnd = footX + kickWind * bodyH * 0.6f;
        var kickLegY = footY - MathF.Abs(kickWind) * bodyH * 0.15f;
        canvas.DrawLine(footX, hipY, kickLegEnd, kickLegY, p);

        // Body — leans forward on kick.
        var leanX = kicking ? bodyH * 0.15f : 0f;
        canvas.DrawLine(footX, hipY, footX + leanX, neckY, p);

        // Arms — swing counter to run legs; brace during wind-up/kick.
        if (kicking)
        {
            canvas.DrawLine(footX + leanX, neckY + bodyH * 0.1f, footX - bodyH * 0.3f, neckY + bodyH * 0.05f, p);
            canvas.DrawLine(footX + leanX, neckY + bodyH * 0.1f, footX + bodyH * 0.2f, neckY - bodyH * 0.1f, p);
        }
        else
        {
            canvas.DrawLine(footX, neckY + bodyH * 0.1f, footX - bodyH * 0.28f, neckY + bodyH * 0.35f + runSwing, p);
            canvas.DrawLine(footX, neckY + bodyH * 0.1f, footX + bodyH * 0.28f, neckY + bodyH * 0.35f - runSwing, p);
        }

        canvas.DrawCircle(footX + leanX, headY, headR, fillP);
    }

    // ─────────── Builder ───────────────────────────────────────────────
    // For each visible character we take its lit-pixel set and reveal a small random subset per
    // frame while a construction worker (stick figure with a hard hat and hammer) hovers over it.
    // When that character is fully built we move on to the next. Every "brick" placed emits a
    // spark. Whitespace characters are skipped — no build phase, no worker visit.

    private void DrawBuilder(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float globalAlpha)
    {
        var visible = _visibleCellIndices;
        var visCount = visible.Count;
        if (visCount == 0) return;

        var perStep = 1f / visCount;
        var currentSlot = Math.Min(visCount - 1, (int)(t / perStep));
        var stepT = (t / perStep) - currentSlot;
        var currentIdx = visible[currentSlot];

        // Fully render already-built characters.
        for (var s = 0; s < currentSlot; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var color = NarrativeColor(s);
            var g = GlyphAnim.Default;
            g.Alpha = globalAlpha;
            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }

        // Build the current character pixel-by-pixel from its mask (only within its column band).
        if (_mask != null)
        {
            var cur = _cells[currentIdx];
            var color = NarrativeColor(currentSlot);
            using var paint = new SKPaint { Color = color.WithAlpha((byte)(255 * globalAlpha)), Style = SKPaintStyle.Fill };

            var glyphStartX = cur.X;
            var glyphEndX = cur.X + cur.W;
            var maskW = _mask.Width;
            var maskHi = _mask.Height;

            // Count lit pixels in this glyph.
            var lit = new List<SKPointI>();
            unsafe
            {
                var px = (uint*)_mask.GetPixels().ToPointer();
                for (var y = 0; y < maskHi; y++)
                for (var x = glyphStartX; x < glyphEndX && x < maskW; x++)
                    if (px[y * maskW + x] != 0) lit.Add(new SKPointI(x, y));
            }

            if (lit.Count > 0)
            {
                var toShow = (int)MathF.Ceiling(stepT * lit.Count);
                for (var k = 0; k < toShow; k++)
                {
                    var idx = (k * 7 + 3) % lit.Count;
                    var p = lit[idx];
                    canvas.DrawRect(blockX + p.X, blockY + p.Y, 1, 1, paint);
                }

                if (toShow > 0 && Sparkle && _random.NextDouble() < 0.4)
                {
                    var lastIdx = ((toShow - 1) * 7 + 3) % lit.Count;
                    var pl = lit[lastIdx];
                    SpawnSparkle(blockX + pl.X, blockY + pl.Y, maskH);
                }
            }
        }

        // Builder figure hovering above the current character with a hammer.
        var actorX = blockX + _cells[currentIdx].X + _cells[currentIdx].W / 2f;
        var actorFootY = blockY - maskH * 0.25f;
        DrawBuilderFigure(canvas, actorX, actorFootY, maskH, (float)_nowMs,
            NarrativeColor(currentSlot), globalAlpha);
    }

    private void DrawBuilderFigure(SKCanvas canvas, float footX, float footY, int maskH, float phase,
        SKColor tint, float globalAlpha)
    {
        var bodyH = maskH * 0.55f;
        var headR = maskH * 0.14f;
        var strokeW = Math.Max(1, Sc(1));
        var alphaB = (byte)Math.Clamp(230 * globalAlpha, 0, 255);

        using var line = new SKPaint
        {
            Color = tint.WithAlpha(alphaB),
            Style = SKPaintStyle.Stroke, StrokeWidth = strokeW, IsAntialias = true, StrokeCap = SKStrokeCap.Round
        };
        using var fill = new SKPaint { Color = tint.WithAlpha(alphaB), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var hatFill = new SKPaint
        {
            Color = new SKColor(255, 200, 0, alphaB),
            Style = SKPaintStyle.Fill, IsAntialias = true
        };

        var hipY = footY - bodyH * 0.4f;
        var neckY = footY - bodyH;
        var headY = neckY - headR;

        canvas.DrawLine(footX - headR * 0.3f, hipY, footX - headR * 0.3f, footY, line);
        canvas.DrawLine(footX + headR * 0.3f, hipY, footX + headR * 0.3f, footY, line);
        canvas.DrawLine(footX, hipY, footX, neckY, line);
        canvas.DrawCircle(footX, headY, headR, fill);

        // Hard hat: yellow half-circle on top of head.
        canvas.DrawArc(new SKRect(footX - headR, headY - headR, footX + headR, headY + headR),
            180, 180, true, hatFill);

        // Hammer arm — swings up/down.
        var swing = MathF.Sin(phase * 0.02f) * 0.5f + 0.5f; // 0..1
        var handY = neckY + bodyH * 0.1f - swing * bodyH * 0.4f;
        var handX = footX + headR * 1.1f + swing * headR * 0.5f;
        canvas.DrawLine(footX, neckY + bodyH * 0.1f, handX, handY, line);

        // Hammer head — small rect at hand end.
        using var hammerHead = new SKPaint { Color = new SKColor(120, 120, 130, alphaB), Style = SKPaintStyle.Fill };
        canvas.DrawRect(handX - headR * 0.15f, handY - headR * 0.4f, headR * 0.5f, headR * 0.4f, hammerHead);
    }

    // ─────────── DogWalk ───────────────────────────────────────────────
    // A dog walks left-to-right along the floor. During BUILD (t=0..1) it walks from off-screen
    // left just past the last character's column, dropping each visible character as its rear
    // passes. During EXIT (xt=0..1) it walks the rest of the way and fully exits the right edge.

    private void DrawDogWalk(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float xt, float globalAlpha)
    {
        var visible = _visibleCellIndices;
        if (visible.Count == 0) return;

        var floorY = blockY + maskH + Math.Max(2, Sc(4));

        using var floorPaint = new SKPaint
        {
            Color = NarrativeColor(0).WithAlpha((byte)(120 * globalAlpha)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, Sc(1))
        };
        canvas.DrawLine(0, floorY, _bandW, floorY, floorPaint);

        // Dog geometry — must match DrawDog for accurate rear-position math.
        var dogLen = maskH * 1.05f + maskH * 0.55f;
        var dropAnimSpan = maskH * 1.2f;

        // Build target: dog's rear reaches the last visible column at exactly t=1, and has
        // travelled dropAnimSpan past it so the last char is fully placed.
        var lastCell = _cells[visible[^1]];
        var lastTarget = blockX + lastCell.X + lastCell.W / 2f;
        var startNoseX = -Sc(20) + dogLen * 0.7f;                    // rear starts off-screen left
        var buildEndNoseX = lastTarget + dogLen * 0.7f + dropAnimSpan; // last char fully placed
        var exitEndNoseX = _bandW + dogLen + Sc(20);                  // fully off right edge

        float dogNoseX;
        if (t < 1f)
            dogNoseX = startNoseX + (buildEndNoseX - startNoseX) * t;
        else
            dogNoseX = buildEndNoseX + (exitEndNoseX - buildEndNoseX) * xt;

        // Draw the dog FIRST so characters render on top and are never cropped.
        DrawDog(canvas, dogNoseX, floorY, maskH, (float)_nowMs, NarrativeColor(0), globalAlpha);

        var rearX = dogNoseX - dogLen * 0.7f;
        for (var s = 0; s < visible.Count; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var targetCentre = blockX + cell.X + cell.W / 2f;
            if (rearX < targetCentre) continue;

            var color = NarrativeColor(s);
            var g = GlyphAnim.Default;
            var distSinceDrop = rearX - targetCentre;
            var dropT = Math.Clamp(distSinceDrop / dropAnimSpan, 0f, 1f);
            var e = SignageFx.EaseOutBounce(dropT);
            g.Dy = -(maskH * 0.7f) * (1 - e);

            if (dropT > 0.85f && dropT < 1f)
            {
                var sT = (dropT - 0.85f) / 0.15f;
                var squashPhase = MathF.Sin(sT * MathF.PI);
                g.ScaleY = 1f - 0.1f * squashPhase;
                g.ScaleX = 1f + 0.1f * squashPhase;
            }

            g.Alpha = Math.Min(1, dropT * 4) * globalAlpha;
            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }
    }

    private void DrawDog(SKCanvas canvas, float noseX, float footY, int maskH, float phase,
        SKColor tint, float globalAlpha)
    {
        // Body: elongated ellipse; 4 legs; head with ear + nose; tail wags.
        var bodyH = maskH * 0.35f;
        var bodyW = maskH * 1.0f;
        var legLen = maskH * 0.35f;
        var strokeW = Math.Max(1, Sc(1));
        var alphaB = (byte)Math.Clamp(230 * globalAlpha, 0, 255);
        var tintA = tint.WithAlpha(alphaB);

        using var line = new SKPaint
        {
            Color = tintA, Style = SKPaintStyle.Stroke, StrokeWidth = strokeW,
            IsAntialias = true, StrokeCap = SKStrokeCap.Round
        };
        using var fill = new SKPaint { Color = tintA, Style = SKPaintStyle.Fill, IsAntialias = true };

        var bodyCX = noseX - bodyW * 0.35f;
        var bodyCY = footY - legLen - bodyH * 0.5f;

        // Body ellipse.
        canvas.DrawOval(new SKRect(bodyCX - bodyW * 0.4f, bodyCY - bodyH * 0.5f,
            bodyCX + bodyW * 0.4f, bodyCY + bodyH * 0.5f), fill);

        // 4 legs with walk-cycle offsets.
        var s1 = MathF.Sin(phase * 0.02f);
        var s2 = MathF.Cos(phase * 0.02f);
        DrawDogLeg(canvas, line, bodyCX - bodyW * 0.3f, bodyCY + bodyH * 0.4f, legLen,  s1 * legLen * 0.25f);
        DrawDogLeg(canvas, line, bodyCX - bodyW * 0.15f, bodyCY + bodyH * 0.4f, legLen, s2 * legLen * 0.25f);
        DrawDogLeg(canvas, line, bodyCX + bodyW * 0.15f, bodyCY + bodyH * 0.4f, legLen, -s1 * legLen * 0.25f);
        DrawDogLeg(canvas, line, bodyCX + bodyW * 0.3f, bodyCY + bodyH * 0.4f, legLen,  -s2 * legLen * 0.25f);

        // Head: circle at nose end with a smaller nose dot.
        var headR = bodyH * 0.55f;
        var headCX = bodyCX + bodyW * 0.4f;
        var headCY = bodyCY - bodyH * 0.15f;
        canvas.DrawCircle(headCX, headCY, headR, fill);
        // Nose.
        using var noseP = new SKPaint { Color = SKColors.Black.WithAlpha(alphaB), Style = SKPaintStyle.Fill };
        canvas.DrawCircle(headCX + headR * 0.7f, headCY, headR * 0.2f, noseP);
        // Floppy ear.
        canvas.DrawLine(headCX - headR * 0.5f, headCY - headR * 0.5f,
            headCX - headR * 0.9f, headCY + headR * 0.3f, line);

        // Wagging tail.
        var tailSwing = MathF.Sin(phase * 0.03f) * bodyH * 0.4f;
        canvas.DrawLine(bodyCX - bodyW * 0.4f, bodyCY,
            bodyCX - bodyW * 0.7f, bodyCY - bodyH * 0.4f + tailSwing, line);
    }

    private void DrawDogLeg(SKCanvas canvas, SKPaint line, float hipX, float hipY, float legLen, float swing)
    {
        canvas.DrawLine(hipX, hipY, hipX + swing, hipY + legLen, line);
    }

    // ─────────── PacManEat ─────────────────────────────────────────────
    // Characters start scrambled/jumbled. Pac-Man mows left-to-right chomping. During BUILD
    // (t=0..1) it reaches the last character. During EXIT (xt=0..1) it continues past the panel
    // edge with its trailing ghosts.

    private void DrawPacManEat(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float xt, float globalAlpha)
    {
        var visible = _visibleCellIndices;
        if (visible.Count == 0) return;

        var pacR = maskH * 0.45f;
        var pacY = blockY + maskH * 0.5f;

        var lastCell = _cells[visible[^1]];
        var lastTarget = blockX + lastCell.X + lastCell.W / 2f;
        var buildStartX = -pacR * 2;                     // pac-man off-screen left
        var buildEndX = lastTarget + pacR * 0.5f;        // just past the last char (eaten)
        var exitEndX = _bandW + pacR * 8;                // fully past the right edge (ghosts too)

        float pacX;
        if (t < 1f)
            pacX = buildStartX + (buildEndX - buildStartX) * t;
        else
            pacX = buildEndX + (exitEndX - buildEndX) * xt;

        var mouthPhase = MathF.Sin((float)_nowMs * 0.02f) * 0.5f + 0.5f;
        var mouthAngle = 15f + mouthPhase * 55f;

        // Ghosts drawn first (behind), then pac-man (behind chars).
        var ghost1X = pacX - pacR * 4;
        var ghost2X = pacX - pacR * 6;
        if (ghost2X < _bandW + pacR)
            DrawGhost(canvas, ghost2X, pacY, pacR * 0.9f, new SKColor(60, 200, 255), (float)_nowMs, globalAlpha);
        if (ghost1X < _bandW + pacR)
            DrawGhost(canvas, ghost1X, pacY, pacR * 0.9f, new SKColor(255, 60, 60), (float)_nowMs, globalAlpha);
        if (pacX - pacR < _bandW)
            DrawPacMan(canvas, pacX, pacY, pacR, mouthAngle, globalAlpha);

        for (var s = 0; s < visible.Count; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var color = NarrativeColor(s);
            var g = GlyphAnim.Default;
            var targetX = blockX + cell.X + cell.W / 2f;
            var eaten = pacX >= targetX;

            if (!eaten)
            {
                var jitter = MathF.Sin((float)_nowMs * 0.04f + s * 2.7f);
                g.Dx = jitter * cell.W * 0.4f;
                g.Dy = MathF.Cos((float)_nowMs * 0.045f + s * 3.1f) * maskH * 0.15f;
                g.Rotation = jitter * 15f;
                g.Alpha = 0.5f * globalAlpha;
            }
            else
            {
                g.Alpha = globalAlpha;
            }
            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }
    }

    private void DrawPacMan(SKCanvas canvas, float cx, float cy, float r, float mouthAngle, float alpha)
    {
        var alphaB = (byte)Math.Clamp(240 * alpha, 0, 255);
        using var body = new SKPaint
        {
            Color = new SKColor(255, 220, 0, alphaB),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        // Pie shape: full circle minus mouth wedge.
        using var path = new SKPath();
        path.MoveTo(cx, cy);
        path.ArcTo(new SKRect(cx - r, cy - r, cx + r, cy + r), mouthAngle, 360 - 2 * mouthAngle, false);
        path.Close();
        canvas.DrawPath(path, body);

        // Eye.
        using var eye = new SKPaint { Color = SKColors.Black.WithAlpha(alphaB), Style = SKPaintStyle.Fill };
        canvas.DrawCircle(cx + r * 0.1f, cy - r * 0.5f, r * 0.12f, eye);
    }

    private void DrawGhost(SKCanvas canvas, float cx, float cy, float r, SKColor color, float phase, float alpha)
    {
        var alphaB = (byte)Math.Clamp(220 * alpha, 0, 255);
        using var body = new SKPaint { Color = color.WithAlpha(alphaB), Style = SKPaintStyle.Fill, IsAntialias = true };
        // Rounded top + jagged bottom.
        using var path = new SKPath();
        path.MoveTo(cx - r, cy + r);
        path.LineTo(cx - r, cy);
        path.ArcTo(new SKRect(cx - r, cy - r, cx + r, cy + r), 180, 180, false);
        path.LineTo(cx + r, cy + r);
        // Little zigzag skirt (3 humps).
        path.LineTo(cx + r * 0.5f, cy + r * 0.7f);
        path.LineTo(cx, cy + r);
        path.LineTo(cx - r * 0.5f, cy + r * 0.7f);
        path.Close();
        canvas.DrawPath(path, body);

        // Two eyes.
        using var white = new SKPaint { Color = SKColors.White.WithAlpha(alphaB), Style = SKPaintStyle.Fill };
        using var pupil = new SKPaint { Color = new SKColor(30, 30, 130, alphaB), Style = SKPaintStyle.Fill };
        canvas.DrawCircle(cx - r * 0.35f, cy - r * 0.2f, r * 0.22f, white);
        canvas.DrawCircle(cx + r * 0.25f, cy - r * 0.2f, r * 0.22f, white);
        canvas.DrawCircle(cx - r * 0.30f, cy - r * 0.2f, r * 0.10f, pupil);
        canvas.DrawCircle(cx + r * 0.30f, cy - r * 0.2f, r * 0.10f, pupil);
    }

    // ─────────── NeoVision ──────────────────────────────────────────────
    // Full-band matrix code rains DOWN in green columns. As Neo's "vision" scan sweeps left→right,
    // random glyphs settle into readable characters as the scan passes over each column.

    private void DrawNeoVision(SKCanvas canvas, float blockX, float blockY, int maskH, float t, float xt, float globalAlpha)
    {
        var green = new SKColor(0, 255, 70);
        var brightGreen = new SKColor(180, 255, 180);

        // 1) Background rain columns of random katakana-ish glyphs across the WHOLE band.
        var colW = Math.Max(2, Sc(4));
        var rowH = Math.Max(3, Sc(5));

        using var rainPaint = new SKPaint
        {
            Color = green.WithAlpha((byte)(100 * globalAlpha)),
            Style = SKPaintStyle.Fill
        };

        for (var x = 0; x < _bandW; x += colW)
        {
            // Column-based fall speed & offset — deterministic per column so it doesn't strobe.
            var colHash = (x * 7919) & 0xFFF;
            var fallSpeed = 0.5f + (colHash % 5) * 0.15f;
            var fallOffset = ((float)_nowMs * fallSpeed * 0.1f + colHash) % (_bandH + rowH * 4);

            for (var y = -rowH * 3; y < _bandH; y += rowH)
            {
                var trail = ((y + fallOffset) % (_bandH + rowH * 3)) - rowH * 3;
                if (trail < -rowH) continue;
                // Head of trail is brighter.
                var isHead = Math.Abs(trail - _bandH * 0.5f) < rowH * 0.5f;
                rainPaint.Color = (isHead ? brightGreen : green).WithAlpha((byte)(isHead ? 220 * globalAlpha : 90 * globalAlpha));
                // Random-ish pixel pattern at (x, trail).
                var pattern = ((x + (int)trail) * 31) & 7;
                for (var b = 0; b < 3; b++)
                {
                    if (((pattern >> b) & 1) == 1)
                        canvas.DrawRect(x + b, trail, 1, 1, rainPaint);
                }
            }
        }

        // 2) Scan line — during BUILD it sweeps from left edge to just past the last visible
        // character (so all chars have been "scanned" at t=1). During EXIT it continues past the
        // right edge and disappears.
        var visible = _visibleCellIndices;
        float scanX;
        if (visible.Count == 0) { scanX = t * _bandW; }
        else
        {
            var lastCell = _cells[visible[^1]];
            var buildEnd = blockX + lastCell.X + lastCell.W + Sc(4);   // just past last char
            var exitEnd = _bandW + Sc(20);
            if (t < 1f) scanX = t * buildEnd;
            else scanX = buildEnd + (exitEnd - buildEnd) * xt;
        }
        var scanW = Math.Max(1, Sc(2));
        if (scanX - scanW / 2f < _bandW)
        {
            using var scanPaint = new SKPaint
            {
                Color = brightGreen.WithAlpha((byte)(180 * globalAlpha)),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(scanX - scanW / 2f, 0, scanW, _bandH, scanPaint);
            using var scanGlow = new SKPaint
            {
                Color = green.WithAlpha((byte)(60 * globalAlpha)),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(scanX - Sc(12), 0, Sc(10), _bandH, scanGlow);
        }

        // 3) Characters — before scan passes, visible chars show as scrambled glyph fragments in
        //    dim green. After the scan passes, they lock into the real character in matrix green.
        //    Whitespace cells contribute nothing (no junk pixels for them).
        for (var s = 0; s < visible.Count; s++)
        {
            var i = visible[s];
            var cell = _cells[i];
            var cellCentre = blockX + cell.X + cell.W / 2f;

            var g = GlyphAnim.Default;
            if (scanX < cellCentre)
            {
                var jitterSeed = (i + (int)(_nowMs / 80)) & 0xF;
                using var junkPaint = new SKPaint
                {
                    Color = green.WithAlpha((byte)(180 * globalAlpha)),
                    Style = SKPaintStyle.Fill
                };
                for (var k = 0; k < 6; k++)
                {
                    var jx = (jitterSeed * 13 + k * 7) % Math.Max(1, cell.W);
                    var jy = (jitterSeed * 17 + k * 11) % Math.Max(1, maskH);
                    canvas.DrawRect(blockX + cell.X + jx, blockY + jy, 1, 1, junkPaint);
                }
                continue;
            }

            var color = _currentColorSet ? _currentColor : green;
            g.Alpha = globalAlpha;
            DrawGlyphCell(canvas, cell, blockX, blockY, g, color, g.Alpha);
        }
    }


    // ───────────────────────────────────────── mask building ────────────────

    private void RebuildCurrentMessage()
    {
        var raw = Messages.Length > 0 ? Messages[_messageIndex % Messages.Length] : "";
        ApplyMessageDirectives(ref raw);
        _currentText = raw;

        _mask?.Dispose();
        _maskImage?.Dispose();
        _maskImage = null;
        _cells.Clear();

        _mask = BuildMask(_currentText, out var advances);
        if (_mask != null)
        {
            BuildCells(advances, _mask.Width);
            BuildLitPixels(_mask);
            MarkCellContent(_mask);
            _maskImage = SKImage.FromBitmap(_mask);
        }
    }

    /// <summary>
    ///     Walks the mask to determine which cells actually contain rendered pixels. Cells with
    ///     no lit pixels are whitespace and should be skipped by narrative effects (so no stick
    ///     figure carries an invisible space, pac-man doesn't stop to eat nothing, etc.).
    /// </summary>
    private void MarkCellContent(SKBitmap mask)
    {
        _visibleCellIndices.Clear();
        var w = mask.Width;
        var h = mask.Height;
        var rowBytes = mask.RowBytes;

        unsafe
        {
            var ptr = (byte*)mask.GetPixels().ToPointer();
            for (var i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var hasContent = false;
                var xEnd = Math.Min(w, cell.X + cell.W);
                for (var y = 0; y < h && !hasContent; y++)
                for (var x = cell.X; x < xEnd; x++)
                {
                    // Alpha channel is the 4th byte in Bgra8888.
                    if (ptr[y * rowBytes + x * 4 + 3] > 128) { hasContent = true; break; }
                }
                cell.HasContent = hasContent;
                _cells[i] = cell;
                if (hasContent) _visibleCellIndices.Add(i);
            }
        }
    }

    private void ApplyMessageDirectives(ref string text)
    {
        _currentEffect = Effect;
        _currentDirection = Direction;
        _currentColor = TextColor;
        _currentColorSet = false;

        if (string.IsNullOrEmpty(text) || text[0] != '[') return;
        var end = text.IndexOf(']');
        if (end <= 0) return;

        var directive = text.Substring(1, end - 1);
        text = text[(end + 1)..].TrimStart();

        foreach (var part in directive.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            var val = kv[1].Trim();

            switch (key)
            {
                case "fx":
                case "effect":
                    if (Enum.TryParse<TextEffect>(val, true, out var fx)) _currentEffect = fx;
                    break;
                case "dir":
                case "scroll":
                case "direction":
                    if (Enum.TryParse<ScrollDirection>(val, true, out var d)) _currentDirection = d;
                    break;
                case "color":
                case "colour":
                    if (SKColor.TryParse(val, out var c))
                    {
                        _currentColor = c;
                        _currentColorSet = true;
                    }

                    break;
            }
        }
    }

    private SKBitmap? BuildMask(string text, out List<int> advances)
    {
        advances = new List<int>();
        if (string.IsNullOrEmpty(text)) return null;

        if (UseBdfFont && !Emojis && IsAscii(text))
            try
            {
                string? fontName;
                if (!string.IsNullOrWhiteSpace(BdfFontName))
                    fontName = BdfFontName;
                else if (FontSize > 0)
                    fontName = BdfFontRegistry.GetBestFontForHeight(FontSize);
                else
                    fontName = null;

                var bmp = _canvas.RenderBdfTextToBitmap(text, SKColors.White, fontName);
                if (bmp is { Width: > 0, Height: > 0 })
                {
                    foreach (var el in EnumerateElements(text))
                        advances.Add(Math.Max(1, (int)Math.Round(_canvas.MeasureBdfText(el, fontName).Width)));
                    return bmp;
                }
            }
            catch
            {
                // fall through to Skia
            }

        return BuildSkiaMask(text, advances);
    }

    private void BuildCells(List<int> advances, int maskWidth)
    {
        var x = 0;
        foreach (var adv in advances)
        {
            if (x >= maskWidth) break;
            var cw = Math.Min(adv, maskWidth - x);
            if (cw <= 0) break;

            var ang = (float)(_random.NextDouble() * Math.PI * 2);
            _cells.Add(new CharCell
            {
                X = x, W = cw,
                DirX = (float)Math.Cos(ang),
                DirY = (float)Math.Sin(ang),
                Phase = (float)_random.NextDouble()
            });
            x += adv;
        }
    }

    private void BuildLitPixels(SKBitmap mask)
    {
        _litPixels.Clear();
        var w = mask.Width;
        var h = mask.Height;
        var rowBytes = mask.RowBytes;

        unsafe
        {
            var ptr = (byte*)mask.GetPixels().ToPointer();
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (ptr[y * rowBytes + x * 4 + 3] > 128)
                    _litPixels.Add(new SKPointI(x, y));
        }

        _litPixels.Sort((a, b) => a.X != b.X ? a.X - b.X : a.Y - b.Y);

        _pixelOrder = new int[_litPixels.Count];
        for (var i = 0; i < _pixelOrder.Length; i++) _pixelOrder[i] = i;
        for (var i = _pixelOrder.Length - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_pixelOrder[i], _pixelOrder[j]) = (_pixelOrder[j], _pixelOrder[i]);
        }
    }

    private static IEnumerable<string> EnumerateElements(string text)
    {
        var en = StringInfo.GetTextElementEnumerator(text);
        while (en.MoveNext()) yield return (string)en.Current;
    }

    private SKBitmap BuildSkiaMask(string text, List<int> advances)
    {
        var size = ResolveFontSize();
        var baseTf = SKTypeface.FromFamilyName(string.IsNullOrWhiteSpace(FontFamily) ? "Arial" : FontFamily,
            SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        var fm = SKFontManager.Default;

        var runs = new List<(string Text, SKTypeface Typeface)>();
        var en = StringInfo.GetTextElementEnumerator(text);
        while (en.MoveNext())
        {
            var el = (string)en.Current;
            var tf = baseTf;
            var cp = char.ConvertToUtf32(el, 0);
            if (cp > 127)
            {
                var match = fm.MatchCharacter(cp);
                if (match != null) tf = match;
            }

            runs.Add((el, tf));
        }

        float totalW = 0, maxAsc = 0, maxDesc = 0;
        foreach (var (s, tf) in runs)
        {
            using var f = new SKFont(tf, size);
            totalW += f.MeasureText(s);
            var m = f.Metrics;
            maxAsc = Math.Max(maxAsc, -m.Ascent);
            maxDesc = Math.Max(maxDesc, m.Descent);
        }

        var w = Math.Max(1, (int)Math.Ceiling(totalW) + 2);
        var hgt = Math.Max(1, (int)Math.Ceiling(maxAsc + maxDesc) + 2);

        var mask = new SKBitmap(w, hgt);
        using var canvas = new SKCanvas(mask);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = size >= 12 };
        var x = 1f;
        var baseline = maxAsc + 1;
        foreach (var (s, tf) in runs)
        {
            using var f = new SKFont(tf, size);
            canvas.DrawText(s, x, baseline, SKTextAlign.Left, f, paint);
            var adv = f.MeasureText(s);
            advances.Add(Math.Max(1, (int)Math.Round(adv)));
            x += adv;
        }

        return mask;
    }

    private float ResolveFontSize()
    {
        if (FontSize > 0) return FontSize;
        return Math.Max(6f, _bandH * 0.6f);
    }

    private static bool IsAscii(string s)
    {
        foreach (var c in s)
            if (c > 126)
                return false;
        return true;
    }
}
