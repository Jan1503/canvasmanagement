using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasManagerBenchmark
{
    private CanvasManager _canvasManager;
    private Canvas _canvas1;
    private Canvas _canvas2;
    private Canvas _canvas3;
    private Canvas _canvas4;

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(384, 192);
        
        _canvas1 = _canvasManager.GetCanvas(0, 0, 96, 96, 1);
        _canvas2 = _canvasManager.GetCanvas(96, 0, 96, 96, 2);
        _canvas3 = _canvasManager.GetCanvas(192, 0, 96, 96, 3);
        _canvas4 = _canvasManager.GetCanvas(288, 0, 96, 96, 4);
        
        // Fill canvases with content
        _canvas1.DrawRect(10, 10, 50, 50, SKColors.Red, SKPaintStyle.Fill);
        _canvas2.DrawRect(10, 10, 50, 50, SKColors.Green, SKPaintStyle.Fill);
        _canvas3.DrawRect(10, 10, 50, 50, SKColors.Blue, SKPaintStyle.Fill);
        _canvas4.DrawRect(10, 10, 50, 50, SKColors.Yellow, SKPaintStyle.Fill);
    }

    [Benchmark]
    public void CreateAndGetCanvas()
    {
        var canvas = _canvasManager.GetCanvas(0, 0, 100, 100, 10);
    }

    //[Benchmark]
    //public void SwapAllCanvases()
    //{
    //    _canvasManager.SwapCanvases();
    //}

    [GlobalCleanup]
    public void Cleanup()
    {
        _canvasManager?.Stop();
    }
}
