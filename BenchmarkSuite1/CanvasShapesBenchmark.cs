using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasShapesBenchmark
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
    public void DrawCircle()
    {
        _canvas.DrawCircle(50, 50, 20, SKColors.Red);
    }

    [Benchmark]
    public void DrawMultipleCircles()
    {
        _canvas.DrawCircle(30, 30, 10, SKColors.Red);
        _canvas.DrawCircle(60, 30, 10, SKColors.Green);
        _canvas.DrawCircle(90, 30, 10, SKColors.Blue);
        _canvas.DrawCircle(120, 30, 10, SKColors.Yellow);
    }

    [Benchmark]
    public void DrawLine()
    {
        _canvas.DrawLine(0, 0, 100, 100, SKColors.White);
    }

    [Benchmark]
    public void DrawMultipleLines()
    {
        _canvas.DrawLine(0, 10, 100, 10, SKColors.Red);
        _canvas.DrawLine(0, 20, 100, 20, SKColors.Green);
        _canvas.DrawLine(0, 30, 100, 30, SKColors.Blue);
        _canvas.DrawLine(0, 40, 100, 40, SKColors.Yellow);
    }

    [Benchmark]
    public void DrawRectFilled()
    {
        _canvas.DrawRect(10, 10, 50, 30, SKColors.Blue, SKPaintStyle.Fill);
    }

    [Benchmark]
    public void DrawRectStroke()
    {
        _canvas.DrawRect(10, 10, 50, 30, SKColors.Blue, SKPaintStyle.Stroke);
    }

    [Benchmark]
    public void DrawMultipleRects()
    {
        _canvas.DrawRect(10, 10, 30, 20, SKColors.Red, SKPaintStyle.Fill);
        _canvas.DrawRect(50, 10, 30, 20, SKColors.Green, SKPaintStyle.Fill);
        _canvas.DrawRect(90, 10, 30, 20, SKColors.Blue, SKPaintStyle.Fill);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _canvasManager?.Stop();
    }
}
