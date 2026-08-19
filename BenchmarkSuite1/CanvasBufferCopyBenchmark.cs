using BenchmarkDotNet.Attributes;
using CanvasManagement;
using Microsoft.VSDiagnostics;
using SkiaSharp;

namespace BenchmarkSuite1;
[CPUUsageDiagnoser]
public class CanvasBufferCopyBenchmark
{
    private CanvasManager _canvasManager;
    private Canvas _sourceCanvas;
    private SKBitmap _testBitmap;
    [Params(64, 192, 384)]
    public int Width { get; set; }

    [Params(64, 192)]
    public int Height { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _canvasManager = new CanvasManager(Width, Height);
        _sourceCanvas = _canvasManager.GetCanvas(0, 0, Width, Height, 1);
        _testBitmap = new SKBitmap(Width, Height);
        using (var canvas = new SKCanvas(_testBitmap))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(10, 10, 50, 30, new SKPaint { Color = SKColors.Red });
        }

        _sourceCanvas.DrawBitmap(_testBitmap, 0, 0, fitToCanvas: false);
    }

    [Benchmark]
    public void CopyBackgroundToForeground()
    {
        _sourceCanvas.CopyBackgroundToForeground();
    }

    [Benchmark]
    public void CompositeCopyToMain()
    {
        _sourceCanvas.PrepareNextFrame();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _testBitmap?.Dispose();
        _canvasManager?.Stop();
    }
}