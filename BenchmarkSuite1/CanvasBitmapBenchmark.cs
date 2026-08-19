using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasBitmapBenchmark
{
    private CanvasManager _canvasManager;
    private Canvas _canvas;
    private SKBitmap _smallBitmap;
    private SKBitmap _largeBitmap;

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(384, 192);
        _canvas = _canvasManager.GetCanvas(0, 0, 384, 192, 1);
        
        // Create small bitmap (32x32)
        _smallBitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(_smallBitmap))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(5, 5, 20, 20, new SKPaint { Color = SKColors.Red });
        }
        
        // Create large bitmap (128x64)
        _largeBitmap = new SKBitmap(128, 64);
        using (var canvas = new SKCanvas(_largeBitmap))
        {
            canvas.Clear(SKColors.Green);
            canvas.DrawRect(10, 10, 50, 30, new SKPaint { Color = SKColors.Yellow });
        }
    }

    [Benchmark]
    public void DrawSmallBitmap()
    {
        _canvas.DrawBitmap(_smallBitmap, 10, 10, 32, 32);
    }

    [Benchmark]
    public void DrawLargeBitmap()
    {
        _canvas.DrawBitmap(_largeBitmap, 0, 0, 128, 64);
    }

    [Benchmark]
    public void DrawBitmapWithRotation()
    {
        _canvas.DrawBitmap(_smallBitmap, 10, 10, 32, 32, rotateDegrees: 45);
    }

    [Benchmark]
    public void DrawBitmapWithScale()
    {
        _canvas.DrawBitmap(_smallBitmap, 10, 10, 32, 32, scale: 2.0f);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _smallBitmap?.Dispose();
        _largeBitmap?.Dispose();
        _canvasManager?.Stop();
    }
}
