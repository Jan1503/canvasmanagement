using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasClearBenchmark
{
    private CanvasManager _canvasManager;
    private Canvas _canvas;

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(384, 192);
        _canvas = _canvasManager.GetCanvas(0, 0, 384, 192, 1);
        
        // Fill canvas with some content
        for (int i = 0; i < 10; i++)
        {
            _canvas.DrawRect(i * 30, i * 15, 25, 10, SKColors.Red, SKPaintStyle.Fill);
        }
    }

    [Benchmark]
    public void ClearToBlack()
    {
        _canvas.Clear();
    }

    [Benchmark]
    public void ClearToColor()
    {
        _canvas.Clear(SKColors.DarkBlue);
    }

    [Benchmark]
    public void MakeTransparent()
    {
        _canvas.MakeTransparent();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _canvasManager?.Stop();
    }
}
