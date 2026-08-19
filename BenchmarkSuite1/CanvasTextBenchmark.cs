using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasTextBenchmark
{
    private CanvasManager _canvasManager;
    private Canvas _canvas;
    private SKPaint _textPaint;

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(384, 192);
        _canvas = _canvasManager.GetCanvas(0, 0, 384, 192, 1);
        
        var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        
        _textPaint = new SKPaint
        {
            TextSize = 24,
            Color = SKColors.White,
            Typeface = typeface,
            IsAntialias = true
        };
    }

    [Benchmark]
    public void DrawShortText()
    {
        _canvas.DrawText("Hello", 10, 10, 100, 50, _textPaint);
    }

    [Benchmark]
    public void DrawLongText()
    {
        _canvas.DrawText("This is a longer text string for benchmarking", 10, 10, 300, 50, _textPaint);
    }

    [Benchmark]
    public void DrawCenteredText()
    {
        _canvas.DrawText("Centered", 10, 10, 200, 50, _textPaint, centered: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _textPaint?.Dispose();
        _canvasManager?.Stop();
    }
}
