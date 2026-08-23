using BenchmarkDotNet.Attributes;
using CanvasManagement;
using SkiaSharp;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class CanvasTextBenchmark
{
    private CanvasManager _canvasManager = null!;
    private Canvas _canvas = null!;

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(384, 192);
        _canvas = _canvasManager.GetCanvas(0, 0, 384, 192, 1);
    }

    [Benchmark]
    public void DrawShortText()
    {
        _canvas.DrawText("Hello", 10, 10, SKColors.White, 24);
    }

    [Benchmark]
    public void DrawLongText()
    {
        _canvas.DrawText("This is a longer text string for benchmarking", 10, 10, SKColors.White, 24);
    }

    [Benchmark]
    public void DrawCenteredText()
    {
        _canvas.DrawTextAligned("Centered", 10, 10, 200, 50, SKColors.White, 24, SKTextAlign.Center);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _canvasManager.Stop();
    }
}
