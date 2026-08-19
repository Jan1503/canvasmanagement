using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasRenderingBenchmark
{
    private CanvasManager _canvasManager;
    private Canvas _canvas1;
    private Canvas _canvas2;
    private Canvas _canvas3;
    private SKBitmap _testBitmap;

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(384, 192);
        
        _canvas1 = _canvasManager.GetCanvas(0, 0, 128, 64, 1);
        _canvas2 = _canvasManager.GetCanvas(128, 0, 128, 64, 2);
        _canvas3 = _canvasManager.GetCanvas(256, 0, 128, 64, 3);
        
        // Create test bitmap with some content
        _testBitmap = new SKBitmap(128, 64);
        using (var canvas = new SKCanvas(_testBitmap))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(10, 10, 50, 30, new SKPaint { Color = SKColors.Red });
        }
        
        // Draw some content to canvases to simulate typical usage
        _canvas1.DrawBitmap(_testBitmap, 0, 0, fitToCanvas: false);
        _canvas2.DrawBitmap(_testBitmap, 0, 0, fitToCanvas: false);
        _canvas3.DrawBitmap(_testBitmap, 0, 0, fitToCanvas: false);
    }

    [Benchmark]
    public void DrawOperationsOnCanvas()
    {
        // Simulate typical drawing operations that happen per frame
        _canvas1.DrawRect(10, 10, 50, 30, SKColors.Red, SKPaintStyle.Fill);
        _canvas2.DrawRect(20, 20, 40, 20, SKColors.Green, SKPaintStyle.Fill);
        _canvas3.DrawRect(5, 5, 60, 40, SKColors.Blue, SKPaintStyle.Fill);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _testBitmap?.Dispose();
        _canvasManager?.Stop();
    }
}