using System.Reflection;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.Quake3Screensaver;

/// <summary>
/// Quake 3 Arena themed screensaver with rotating 3D logo and animated text
/// </summary>
[ExtensionInfo("Quake 3 Screensaver",
    "Iconic Quake 3 Arena style screensaver with rotating 3D logo, particles, and scrolling text",
    "Screensavers",
    IconResourceName = "quake3.svg")]
public class Quake3ScreensaverExtension : ICanvasExtension, IDisposable
{
    private const string FontName = "q3arena";
    
    private readonly ICanvas _canvas;
    private readonly Logo3DRenderer _logoRenderer;
    private readonly ParticleSystem _particles;
    private readonly Random _random = new();
    
    private Task? _animationTask;
    private SKBitmap? _backBuffer;
    private CancellationTokenSource? _cts;
    
    private int _currentQuoteIndex;
    private float _textOpacity = 1.0f;
    private float _textFadeDirection = -1;
    private bool _fontRegistered;

    // Famous-sounding combat-game lines. Original wording — not Quake 3 announcer quotes.
    private readonly string[] _quotes =
    {
        "WELCOME TO THE ARENA",
        "PREPARE TO FIGHT",
        "SYSTEM ONLINE",
        "GOOD HIT",
        "NICE SHOT",
        "KEEP MOVING",
        "FIGHT!",
        "ROUND OVER",
        "YOU ARE IN THE LEAD",
        "PERFECT!",
        "MATCH POINT"
    };

    internal Quake3ScreensaverExtension(ICanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        
        // Initialize components - constructors are designed not to throw
        _logoRenderer = new Logo3DRenderer();
        _particles = new ParticleSystem();
        
        // Don't register font in constructor - defer to Start()
    }

    #region Extension Parameters

    [ExtensionParameter("Rotation Speed", "Speed of logo rotation",
        DefaultValue = 0.02f, MinValue = 0.005f, MaxValue = 0.1f)]
    public float RotationSpeed
    {
        get => _logoRenderer?.RotationSpeed ?? 0.02f;
        set { if (_logoRenderer != null) _logoRenderer.RotationSpeed = value; }
    }

    [ExtensionParameter("Symbol Scale", "Size multiplier for the rotating logo symbol",
        DefaultValue = 0.6f, MinValue = 0.1f, MaxValue = 3.0f)]
    public float SymbolScale { get; set; } = 0.6f;

    [ExtensionParameter("Text Scale", "Size multiplier for the QUAKE III ARENA text",
        DefaultValue = 0.8f, MinValue = 0.1f, MaxValue = 3.0f)]
    public float TextScale { get; set; } = 0.8f;

    [ExtensionParameter("Glow Intensity", "Intensity of the logo glow effect",
        DefaultValue = 0.4f, MinValue = 0.0f, MaxValue = 1.0f)]
    public float GlowIntensity
    {
        get => _logoRenderer?.GlowIntensity ?? 0.4f;
        set { if (_logoRenderer != null) _logoRenderer.GlowIntensity = value; }
    }

    [ExtensionParameter("Render Mode", "How to render the logo: Solid, Wireframe, or Both",
        DefaultValue = LogoRenderMode.Both)]
    public LogoRenderMode RenderMode
    {
        get => _logoRenderer?.RenderMode ?? LogoRenderMode.Both;
        set { if (_logoRenderer != null) _logoRenderer.RenderMode = value; }
    }

    [ExtensionParameter("Wireframe Thickness", "Thickness of wireframe lines",
        DefaultValue = 1.0f, MinValue = 0.5f, MaxValue = 3.0f)]
    public float WireframeThickness
    {
        get => _logoRenderer?.WireframeThickness ?? 1.0f;
        set { if (_logoRenderer != null) _logoRenderer.WireframeThickness = value; }
    }

    [ExtensionParameter("Extrusion Depth", "Number of depth layers for 3D effect",
        DefaultValue = 8, MinValue = 0, MaxValue = 20)]
    public int ExtrusionDepth
    {
        get => _logoRenderer?.ExtrusionDepth ?? 8;
        set { if (_logoRenderer != null) _logoRenderer.ExtrusionDepth = value; }
    }

    [ExtensionParameter("Particle Count", "Number of background particles",
        DefaultValue = 100, MinValue = 0, MaxValue = 300)]
    public int ParticleCount
    {
        get => _particles?.ParticleCount ?? 100;
        set { if (_particles != null) _particles.ParticleCount = value; }
    }

    [ExtensionParameter("Show Particles", "Enable background particle effects",
        DefaultValue = true)]
    public bool ShowParticles
    {
        get => _particles?.Enabled ?? true;
        set { if (_particles != null) _particles.Enabled = value; }
    }

    [ExtensionParameter("Particle Style", "Style of background particles",
        DefaultValue = ParticleStyle.Smoke)]
    public ParticleStyle ParticleStyle
    {
        get => _particles?.Style ?? ParticleStyle.Smoke;
        set { if (_particles != null) _particles.Style = value; }
    }

    [ExtensionParameter("Show Text", "Display scrolling text quotes",
        DefaultValue = true)]
    public bool ShowText { get; set; } = true;

    [ExtensionParameter("Custom Text", "Custom text to display (empty for random quotes)",
        DefaultValue = "")]
    public string CustomText { get; set; } = string.Empty;

    [ExtensionParameter("Text Display Time", "Seconds to display each quote",
        DefaultValue = 4.0f, MinValue = 1.0f, MaxValue = 20.0f)]
    public float TextDisplayTime { get; set; } = 4.0f;

    [ExtensionParameter("Color Scheme", "Color theme for the screensaver",
        DefaultValue = ColorScheme.Classic)]
    public ColorScheme ColorScheme
    {
        get => _colorScheme;
        set
        {
            _colorScheme = value;
            ApplyColorScheme();
        }
    }
    private ColorScheme _colorScheme = ColorScheme.Classic;

    #endregion

    #region ICanvasExtension Implementation

    public string Name => "Quake 3 Screensaver";
    public string Description => "Iconic Q3A style screensaver";
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;

        // Lazy initialization on first Start()
        try
        {
            _logoRenderer?.EnsureLoaded();
            RegisterEmbeddedFont();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Q3Screensaver] Lazy init error: {ex.Message}");
        }

        // Initialize back buffer
        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        // Initialize particle system
        _particles?.Initialize(_canvas.Width, _canvas.Height);

        // Apply initial color scheme
        ApplyColorScheme();

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _animationTask = Task.Run(async () =>
        {
            var frameCount = 0;
            var lastQuoteChange = DateTime.Now;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_backBuffer == null) break;

                    // Update animations
                    _logoRenderer?.Update();
                    _particles?.Update();

                    // Update text
                    UpdateText(ref lastQuoteChange);

                    // Render frame
                    RenderFrame(frameCount);

                    frameCount++;
                    await Task.Delay(33, ct); // ~30 FPS
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Q3Screensaver] Animation error: {ex.Message}");
            }
            finally
            {
                _canvas.Clear();
                IsRunning = false;
            }
        }, ct);
    }

    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            _cts?.Cancel();
            _animationTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[Q3Screensaver] Error stopping: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _animationTask = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            _canvas.Clear();
            IsRunning = false;
        }
    }

    #endregion

    #region Extension Methods

    [ExtensionMethod("Next Quote", "Switch to the next quote",
        Category = "Text", IconName = "skip-forward", Order = 1)]
    public void NextQuote()
    {
        _currentQuoteIndex = (_currentQuoteIndex + 1) % _quotes.Length;
        _textOpacity = 1.0f;
        _textFadeDirection = -1;
    }

    [ExtensionMethod("Previous Quote", "Switch to the previous quote",
        Category = "Text", IconName = "skip-back", Order = 2)]
    public void PreviousQuote()
    {
        _currentQuoteIndex = (_currentQuoteIndex - 1 + _quotes.Length) % _quotes.Length;
        _textOpacity = 1.0f;
        _textFadeDirection = -1;
    }

    [ExtensionMethod("Random Quote", "Switch to a random quote",
        Category = "Text", IconName = "shuffle", Order = 3)]
    public void RandomQuote()
    {
        _currentQuoteIndex = _random.Next(_quotes.Length);
        _textOpacity = 1.0f;
        _textFadeDirection = -1;
    }

    [ExtensionMethod("Set Custom Text", "Set custom display text",
        Category = "Text", Order = 4)]
    public void SetCustomText(string text)
    {
        CustomText = text;
        _textOpacity = 1.0f;
        _textFadeDirection = -1;
    }

    [ExtensionMethod("Reset Animation", "Reset the animation to initial state",
        Category = "Control", IconName = "refresh-cw", Order = 10)]
    public void ResetAnimation()
    {
        _currentQuoteIndex = 0;
        _textOpacity = 1.0f;
        _textFadeDirection = -1;
        _particles?.Initialize(_canvas.Width, _canvas.Height);
    }

    [ExtensionMethod("Get Current Quote", "Returns the currently displayed quote",
        Category = "Info", ReturnsValue = true, Order = 20)]
    public string GetCurrentQuote()
    {
        return string.IsNullOrEmpty(CustomText) ? _quotes[_currentQuoteIndex] : CustomText;
    }

    #endregion

    #region Private Methods

    private void RegisterEmbeddedFont()
    {
        if (_fontRegistered) return;

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("q3arena.bdf", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    // Save to temp file and register
                    var tempPath = Path.Combine(Path.GetTempPath(), "q3arena.bdf");
                    using (var fileStream = File.Create(tempPath))
                    {
                        stream.CopyTo(fileStream);
                    }
                    
                    if (!BdfFontRegistry.IsFontRegistered(FontName))
                    {
                        BdfFontRegistry.RegisterFont(FontName, tempPath);
                        Console.WriteLine($"[Q3Screensaver] Registered Q3 Arena font");
                    }
                    _fontRegistered = true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Q3Screensaver] Failed to register font: {ex.Message}");
        }
    }

    private void ApplyColorScheme()
    {
        if (_logoRenderer == null || _particles == null) return;
        
        switch (_colorScheme)
        {
            case ColorScheme.Classic:
                // Original Q3 is red-based
                _logoRenderer.PrimaryColor = new SKColor(255, 0, 0);     // Red (matches SVG)
                _logoRenderer.SecondaryColor = new SKColor(255, 100, 50); // Orange highlight
                _particles.PrimaryColor = new SKColor(180, 0, 0);
                _particles.SecondaryColor = new SKColor(120, 0, 0);
                break;
                
            case ColorScheme.Blue:
                _logoRenderer.PrimaryColor = new SKColor(0, 150, 255);   // Blue
                _logoRenderer.SecondaryColor = new SKColor(100, 200, 255); // Light blue
                _particles.PrimaryColor = new SKColor(0, 100, 200);
                _particles.SecondaryColor = new SKColor(0, 50, 150);
                break;
                
            case ColorScheme.Green:
                _logoRenderer.PrimaryColor = new SKColor(0, 255, 100);   // Green
                _logoRenderer.SecondaryColor = new SKColor(150, 255, 150); // Light green
                _particles.PrimaryColor = new SKColor(0, 200, 50);
                _particles.SecondaryColor = new SKColor(0, 150, 30);
                break;
                
            case ColorScheme.Orange:
                _logoRenderer.PrimaryColor = new SKColor(255, 120, 0);   // Orange
                _logoRenderer.SecondaryColor = new SKColor(255, 200, 50); // Yellow
                _particles.PrimaryColor = new SKColor(255, 80, 0);
                _particles.SecondaryColor = new SKColor(255, 40, 0);
                break;
                
            case ColorScheme.Purple:
                _logoRenderer.PrimaryColor = new SKColor(180, 50, 255);  // Purple
                _logoRenderer.SecondaryColor = new SKColor(220, 150, 255); // Light purple
                _particles.PrimaryColor = new SKColor(150, 30, 200);
                _particles.SecondaryColor = new SKColor(100, 20, 150);
                break;
        }
    }

    private void UpdateText(ref DateTime lastQuoteChange)
    {
        if (!ShowText) return;

        var elapsed = (DateTime.Now - lastQuoteChange).TotalSeconds;
        
        // Fade in/out logic
        if (elapsed > TextDisplayTime - 0.5f && _textFadeDirection < 0)
        {
            _textFadeDirection = 1; // Start fading out
        }
        
        if (_textFadeDirection > 0)
        {
            _textOpacity -= 0.05f;
            if (_textOpacity <= 0)
            {
                _textOpacity = 0;
                _currentQuoteIndex = (_currentQuoteIndex + 1) % _quotes.Length;
                lastQuoteChange = DateTime.Now;
                _textFadeDirection = -1;
            }
        }
        else if (_textFadeDirection < 0 && _textOpacity < 1)
        {
            _textOpacity += 0.05f;
            if (_textOpacity >= 1)
            {
                _textOpacity = 1;
                _textFadeDirection = 0;
            }
        }
    }

    private void RenderFrame(int frameCount)
    {
        if (_backBuffer == null) return;

        using var canvas = new SKCanvas(_backBuffer);

        // Clear with dark background
        canvas.Clear(new SKColor(10, 5, 15)); // Very dark purple-ish black
        
        // Draw gradient background
        DrawBackground(canvas);

        // Draw particles (behind logo)
        _particles?.Render(canvas);

        // Calculate vertical layout to prevent overlap
        // Symbol takes upper portion, text below it with spacing
        var symbolBounds = _logoRenderer?.SymbolBounds;
        var textBounds = _logoRenderer?.TextBounds;

        // Auto-fit the logo to the panel width: never wider than ~70% (symbol) / ~90% (text)
        // of the display, while never exceeding the user's configured scale.
        var symbolSrcWidth = symbolBounds?.Width ?? 100f;
        var textSrcWidth = textBounds?.Width ?? 150f;
        var effectiveSymbolScale = Math.Min(SymbolScale, _canvas.Width * 0.7f / Math.Max(1f, symbolSrcWidth));
        var effectiveTextScale = Math.Min(TextScale, _canvas.Width * 0.9f / Math.Max(1f, textSrcWidth));

        var symbolHeight = (symbolBounds?.Height ?? 60) * effectiveSymbolScale;
        var textHeight = (textBounds?.Height ?? 30) * effectiveTextScale;
        var spacing = _canvas.Height * 0.05f; // 5% spacing between symbol and text
        
        // Total content height
        var totalHeight = symbolHeight + spacing + textHeight;
        
        // Center the combined content vertically (slightly above center for aesthetic)
        var contentStartY = (_canvas.Height - totalHeight) / 2 - _canvas.Height * 0.05f;
        
        // Draw 3D rotating logo symbol
        var symbolY = (int)(contentStartY + symbolHeight / 2);
        _logoRenderer?.RenderSymbol(canvas, _canvas.Width / 2, symbolY, effectiveSymbolScale);

        // Draw static "QUAKE III ARENA" text from original SVG (below symbol)
        var textY = (int)(contentStartY + symbolHeight + spacing + textHeight / 2);
        _logoRenderer?.RenderText(canvas, _canvas.Width / 2, textY, effectiveTextScale);

        // Draw scrolling quotes at the bottom
        if (ShowText)
        {
            DrawText(canvas);
        }

        // Draw scanline effect (subtle)
        DrawScanlines(canvas, frameCount);

        // Submit frame
        canvas.Flush();
        _canvas.SubmitCompletedFrame(_backBuffer);
    }

    private void DrawBackground(SKCanvas canvas)
    {
        canvas.Clear(SKColors.Black);
    }

    private void DrawText(SKCanvas canvas)
    {
        var text = string.IsNullOrEmpty(CustomText) ? _quotes[_currentQuoteIndex] : CustomText;
        if (string.IsNullOrEmpty(text)) return;
        
        // Calculate text position (bottom third of screen)
        var textY = (int)(_canvas.Height * 0.75f);

        // Get current color based on scheme
        var textColor = _logoRenderer?.PrimaryColor ?? new SKColor(255, 0, 0);
        var alpha = (byte)(_textOpacity * _canvas.Opacity * 255);
        textColor = new SKColor(textColor.Red, textColor.Green, textColor.Blue, alpha);

        try
        {
            // Always use SKCanvas text rendering for the back buffer
            // BDF text would draw to the ICanvas internal buffer, not our back buffer
            using var typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);
            using var font = new SKFont(typeface ?? SKTypeface.Default, Math.Max(5f, _canvas.ScaleSizeF(10)));
            using var paint = new SKPaint
            {
                Color = textColor,
                IsAntialias = false
            };
            
            var textWidth = font.MeasureText(text);
            var textX = (_canvas.Width - textWidth) / 2;
            canvas.DrawText(text, textX, textY, SKTextAlign.Left, font, paint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Q3Screensaver] Text render error: {ex.Message}");
        }
    }

    private void DrawScanlines(SKCanvas canvas, int frameCount)
    {
        // Subtle scanline effect
        using var scanPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 30),
            Style = SKPaintStyle.Fill
        };

        for (int y = 0; y < _canvas.Height; y += 3)
        {
            canvas.DrawRect(0, y, _canvas.Width, 1, scanPaint);
        }
    }

    public void Dispose()
    {
        Stop();
        _logoRenderer?.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// Available color schemes for the screensaver
/// </summary>
public enum ColorScheme
{
    Classic,  // Red (original Q3 style from SVG)
    Blue,     // Blue/Cyan
    Green,    // Green (Matrix-like)
    Orange,   // Orange/Yellow
    Purple    // Purple/Pink
}
