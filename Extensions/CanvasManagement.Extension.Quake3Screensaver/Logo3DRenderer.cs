using System.Reflection;
using SkiaSharp;

namespace CanvasManagement.Extension.Quake3Screensaver;

/// <summary>
/// Render mode for the logo
/// </summary>
public enum LogoRenderMode
{
    Solid,
    Wireframe,
    Both
}

/// <summary>
/// Renders the Quake 3 Arena logo with 3D rotation and depth effect
/// Loads symbol and text separately for independent rendering
/// </summary>
public class Logo3DRenderer : IDisposable
{
    private Svg.Skia.SKSvg? _symbolSvg;
    private Svg.Skia.SKSvg? _textSvg;
    private float _rotationAngle;
    private float _pulsePhase;
    private bool _isLoaded;

    public Logo3DRenderer()
    {
        _isLoaded = false;
    }
    
    /// <summary>
    /// Ensures the logo assets are loaded (call from Start())
    /// </summary>
    public void EnsureLoaded()
    {
        if (_isLoaded) return;
        
        try
        {
            LoadAssets();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Q3Screensaver] Logo initialization error: {ex.Message}");
            _isLoaded = false;
        }
    }

    /// <summary>
    /// Render mode: Solid, Wireframe, or Both
    /// </summary>
    public LogoRenderMode RenderMode { get; set; } = LogoRenderMode.Both;

    /// <summary>
    /// Wireframe line thickness
    /// </summary>
    public float WireframeThickness { get; set; } = 1.0f;

    /// <summary>
    /// Glow intensity (0-1)
    /// </summary>
    public float GlowIntensity { get; set; } = 0.4f;

    /// <summary>
    /// Pulse speed for breathing effect
    /// </summary>
    public float PulseSpeed { get; set; } = 0.02f;

    /// <summary>
    /// Rotation speed in radians per frame
    /// </summary>
    public float RotationSpeed { get; set; } = 0.02f;

    /// <summary>
    /// Depth of 3D extrusion (number of layers)
    /// </summary>
    public int ExtrusionDepth { get; set; } = 8;

    /// <summary>
    /// Primary color (used for solid fill and glow)
    /// </summary>
    public SKColor PrimaryColor { get; set; } = new(255, 0, 0);

    /// <summary>
    /// Secondary color (used for depth shading)
    /// </summary>
    public SKColor SecondaryColor { get; set; } = new(100, 0, 0);

    /// <summary>
    /// Wireframe color
    /// </summary>
    public SKColor WireframeColor { get; set; } = new(255, 100, 50);

    /// <summary>
    /// Gets the symbol SVG bounds for external positioning calculations
    /// </summary>
    public SKRect? SymbolBounds => _symbolSvg?.Picture?.CullRect;

    /// <summary>
    /// Gets the text SVG bounds for external positioning calculations
    /// </summary>
    public SKRect? TextBounds => _textSvg?.Picture?.CullRect;

    private void LoadAssets()
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        // Prefer a private local Q3 copy when present; otherwise the original placeholders.
        var symbolResource = FirstResource(assembly, "q3symbol.svg", "arena-symbol.svg");
        if (symbolResource != null)
        {
            using var stream = assembly.GetManifestResourceStream(symbolResource);
            if (stream != null)
            {
                _symbolSvg = new Svg.Skia.SKSvg();
                _symbolSvg.Load(stream);
                Console.WriteLine($"[Q3Screensaver] Loaded symbol ({symbolResource})");
            }
        }

        var textResource = FirstResource(assembly, "q3text.svg", "arena-text.svg");
        if (textResource != null)
        {
            using var stream = assembly.GetManifestResourceStream(textResource);
            if (stream != null)
            {
                _textSvg = new Svg.Skia.SKSvg();
                _textSvg.Load(stream);
                Console.WriteLine($"[Q3Screensaver] Loaded text ({textResource})");
            }
        }
        
        _isLoaded = _symbolSvg?.Picture != null;
        
        if (!_isLoaded)
            Console.WriteLine("[Q3Screensaver] Warning: Failed to load logo assets");
    }

    private static string? FirstResource(Assembly assembly, params string[] fileNames)
    {
        var names = assembly.GetManifestResourceNames();
        foreach (var file in fileNames)
        {
            var match = names.FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    /// <summary>
    /// Updates the logo animation
    /// </summary>
    public void Update()
    {
        _rotationAngle += RotationSpeed;
        if (_rotationAngle >= MathF.PI * 2) _rotationAngle -= MathF.PI * 2;
        if (_rotationAngle < 0) _rotationAngle += MathF.PI * 2;
        
        _pulsePhase += PulseSpeed;
    }

    /// <summary>
    /// Renders the rotating 3D logo symbol
    /// </summary>
    public void RenderSymbol(SKCanvas canvas, int centerX, int centerY, float scale = 1.0f)
    {
        if (!_isLoaded || _symbolSvg?.Picture == null)
        {
            RenderFallback(canvas, centerX, centerY, scale);
            return;
        }

        var svgBounds = _symbolSvg.Picture.CullRect;
        if (svgBounds.IsEmpty) return;

        // Calculate 3D rotation
        var cosAngle = MathF.Cos(_rotationAngle);
        var sinAngle = MathF.Sin(_rotationAngle);
        var absScaleX = MathF.Abs(cosAngle);
        
        // Skip rendering when edge-on
        if (absScaleX < 0.02f) return;

        // Base dimensions
        var baseWidth = svgBounds.Width * scale;
        var baseHeight = svgBounds.Height * scale;
        
        // Subtle pulse
        var pulse = 1.0f + MathF.Sin(_pulsePhase) * 0.015f;
        baseWidth *= pulse;
        baseHeight *= pulse;

        // 3D perspective width
        var scaledWidth = baseWidth * absScaleX;

        canvas.Save();

        // Draw glow first (behind everything)
        if (GlowIntensity > 0 && absScaleX > 0.2f)
        {
            DrawGlow(canvas, centerX, centerY, scaledWidth, baseHeight, absScaleX);
        }

        // Draw based on render mode
        switch (RenderMode)
        {
            case LogoRenderMode.Solid:
                DrawExtrudedSymbol(canvas, centerX, centerY, svgBounds, 
                                  scaledWidth, baseHeight, cosAngle, sinAngle, false);
                break;
            case LogoRenderMode.Wireframe:
                DrawExtrudedSymbol(canvas, centerX, centerY, svgBounds, 
                                  scaledWidth, baseHeight, cosAngle, sinAngle, true);
                break;
            case LogoRenderMode.Both:
                // Draw solid first, then wireframe on top
                DrawExtrudedSymbol(canvas, centerX, centerY, svgBounds, 
                                  scaledWidth, baseHeight, cosAngle, sinAngle, false);
                DrawExtrudedSymbol(canvas, centerX, centerY, svgBounds, 
                                  scaledWidth, baseHeight, cosAngle, sinAngle, true);
                break;
        }

        canvas.Restore();
    }

    private void DrawExtrudedSymbol(SKCanvas canvas, float centerX, float centerY,
                                    SKRect svgBounds, float width, float height,
                                    float cosAngle, float sinAngle, bool wireframe)
    {
        if (_symbolSvg?.Picture == null) return;

        var isBackSide = cosAngle < 0;
        var absScaleX = MathF.Abs(cosAngle);
        
        // Calculate depth offset direction based on rotation
        var depthOffsetX = sinAngle * 0.5f;
        var depthOffsetY = 0.15f;
        
        // Draw depth layers (back to front)
        var depthLayers = wireframe ? 0 : Math.Max(1, (int)(ExtrusionDepth * absScaleX));
        
        for (int i = depthLayers; i >= 0; i--)
        {
            var layerOffset = i * 0.8f;
            var offsetX = depthOffsetX * layerOffset;
            var offsetY = depthOffsetY * layerOffset;
            
            // Calculate darkness for depth
            var depthFactor = i / (float)Math.Max(1, depthLayers);
            var brightness = isBackSide 
                ? 0.3f + (1 - depthFactor) * 0.3f
                : 0.4f + (1 - depthFactor) * 0.6f;
            
            canvas.Save();
            
            var layerX = centerX - width / 2 + offsetX;
            var layerY = centerY - height / 2 + offsetY;
            
            // Flip horizontally if viewing back
            canvas.Translate(centerX, centerY);
            canvas.Scale(cosAngle > 0 ? 1 : -1, 1);
            canvas.Translate(-centerX, -centerY);
            
            canvas.Translate(layerX, layerY);
            canvas.Scale(width / svgBounds.Width, height / svgBounds.Height);
            
            if (wireframe)
            {
                // Only draw wireframe on front layer
                if (i == 0)
                {
                    DrawWireframe(canvas, isBackSide);
                }
            }
            else
            {
                // Solid rendering
                if (i > 0)
                {
                    using var depthPaint = new SKPaint
                    {
                        ColorFilter = SKColorFilter.CreateBlendMode(
                            new SKColor(
                                (byte)(SecondaryColor.Red * brightness),
                                (byte)(SecondaryColor.Green * brightness),
                                (byte)(SecondaryColor.Blue * brightness),
                                255),
                            SKBlendMode.SrcIn)
                    };
                    canvas.DrawPicture(_symbolSvg.Picture, depthPaint);
                }
                else
                {
                    if (isBackSide)
                    {
                        using var backPaint = new SKPaint
                        {
                            ColorFilter = SKColorFilter.CreateBlendMode(
                                new SKColor(180, 180, 180, 255),
                                SKBlendMode.Modulate)
                        };
                        canvas.DrawPicture(_symbolSvg.Picture, backPaint);
                    }
                    else
                    {
                        canvas.DrawPicture(_symbolSvg.Picture);
                    }
                }
            }
            
            canvas.Restore();
        }
    }

    private void DrawWireframe(SKCanvas canvas, bool isBackSide)
    {
        if (_symbolSvg?.Picture == null) return;

        // Create a wireframe effect by drawing with stroke
        var wireColor = isBackSide
            ? new SKColor((byte)(WireframeColor.Red * 0.5f), 
                         (byte)(WireframeColor.Green * 0.5f), 
                         (byte)(WireframeColor.Blue * 0.5f))
            : WireframeColor;

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = WireframeThickness,
            Color = wireColor,
            IsAntialias = true,
            ColorFilter = SKColorFilter.CreateBlendMode(wireColor, SKBlendMode.SrcIn)
        };
        
        canvas.DrawPicture(_symbolSvg.Picture, strokePaint);
    }

    /// <summary>
    /// Renders the static text (does not rotate)
    /// </summary>
    public void RenderText(SKCanvas canvas, int centerX, int centerY, float scale = 1.0f)
    {
        if (_textSvg?.Picture == null) return;

        var svgBounds = _textSvg.Picture.CullRect;
        if (svgBounds.IsEmpty) return;

        var scaledWidth = svgBounds.Width * scale;
        var scaledHeight = svgBounds.Height * scale;
        
        var x = centerX - scaledWidth / 2;
        var y = centerY - scaledHeight / 2;

        canvas.Save();
        
        // Subtle glow for text
        if (GlowIntensity > 0)
        {
            var glowStrength = (MathF.Sin(_pulsePhase * 1.5f) + 1) * 0.5f;
            var alpha = (byte)(25 * GlowIntensity * (0.5f + glowStrength * 0.5f));
            
            using var glowPaint = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateBlendMode(
                    new SKColor(PrimaryColor.Red, PrimaryColor.Green, PrimaryColor.Blue, alpha),
                    SKBlendMode.SrcIn),
                ImageFilter = SKImageFilter.CreateBlur(2f, 2f),
                IsAntialias = true
            };
            
            canvas.Save();
            canvas.Translate(x, y);
            canvas.Scale(scaledWidth / svgBounds.Width, scaledHeight / svgBounds.Height);
            canvas.DrawPicture(_textSvg.Picture, glowPaint);
            canvas.Restore();
        }
        
        // Draw the text
        canvas.Translate(x, y);
        canvas.Scale(scaledWidth / svgBounds.Width, scaledHeight / svgBounds.Height);
        canvas.DrawPicture(_textSvg.Picture);
        
        canvas.Restore();
    }

    private void DrawGlow(SKCanvas canvas, float centerX, float centerY, 
                          float width, float height, float intensity)
    {
        if (_symbolSvg?.Picture == null) return;

        var glowStrength = (MathF.Sin(_pulsePhase * 2) + 1) * 0.5f;
        var alpha = (byte)(40 * GlowIntensity * glowStrength * intensity);
        
        if (alpha < 5) return;

        var svgBounds = _symbolSvg.Picture.CullRect;
        
        using var glowPaint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor(PrimaryColor.Red, PrimaryColor.Green, PrimaryColor.Blue, alpha),
                SKBlendMode.SrcIn),
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f),
            IsAntialias = true
        };

        canvas.Save();
        canvas.Translate(centerX - width / 2, centerY - height / 2);
        canvas.Scale(width / svgBounds.Width, height / svgBounds.Height);
        canvas.DrawPicture(_symbolSvg.Picture, glowPaint);
        canvas.Restore();
    }

    private void RenderFallback(SKCanvas canvas, int centerX, int centerY, float scale)
    {
        var cosAngle = MathF.Cos(_rotationAngle);
        var absScaleX = MathF.Abs(cosAngle);
        
        if (absScaleX < 0.05f) return;

        using var typeface = SKTypeface.FromFamilyName("Impact", SKFontStyle.Bold);
        using var font = new SKFont(typeface ?? SKTypeface.Default, 20 * scale);
        
        const string text = "III";
        var textWidth = font.MeasureText(text) * absScaleX;
        var x = centerX - textWidth / 2;
        var y = centerY + 20 * scale / 3;

        canvas.Save();
        canvas.Translate(centerX, centerY);
        canvas.Scale(cosAngle > 0 ? absScaleX : -absScaleX, 1);
        canvas.Translate(-centerX, -centerY);
        
        using var paint = new SKPaint
        {
            Color = PrimaryColor,
            IsAntialias = true
        };
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        
        canvas.Restore();
    }
    
    public void Dispose()
    {
        _symbolSvg?.Dispose();
        _textSvg?.Dispose();
        _symbolSvg = null;
        _textSvg = null;
        GC.SuppressFinalize(this);
    }
}
