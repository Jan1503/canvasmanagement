using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasPixelOperationsBenchmark
{
    private CanvasManager _canvasManager;
    private Canvas _canvas;

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(384, 192);
        _canvas = _canvasManager.GetCanvas(0, 0, 384, 192, 1);
    }

    [Benchmark]
    public void SetSinglePixel()
    {
        _canvas.SetPixel(100, 50, SKColors.Red);
    }

    [Benchmark]
    public void SetMultiplePixels()
    {
        for (int i = 0; i < 10; i++)
        {
            _canvas.SetPixel(i * 10, i * 5, SKColors.Red);
        }
    }

    [Benchmark]
    public void GetSinglePixel()
    {
        var color = _canvas.GetPixel(100, 50);
    }

    [Benchmark]
    public void DrawCircleLegacy()
    {
        _canvas.DrawCircleLegacy(100, 50, 20, SKColors.Green);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _canvasManager?.Stop();
    }
}
