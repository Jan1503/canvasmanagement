using SkiaSharp;
using System.Threading;
//using CanvasManagement.Canvas.Extension.TetrisClock;

namespace CanvasManagement.Console.Demo
{
    internal class Program
    {
        private static Color[] _imageBuffer;
        private static CanvasManager _cm;
        private static Canvas _primeCanvas;
        private static CancellationTokenSource _cancellationTokenSource = null;

        static void Main(string[] args)
        {
            _imageBuffer = GC.AllocateArray<Color>(384 * 192, true);
            _cm = new CanvasManager(384, 192);
            _cm.RenderCompleted += CanvasManagerRenderCompleted;
            _cm.Run();

            _primeCanvas = _cm.GetCanvas(0, 0, 384, 192, 1);

            

            System.Console.WriteLine("Press ENTER to exit...");
            System.Console.ReadLine();

            _cancellationTokenSource.Cancel();
            _cm.Stop();

        }

        private static void CanvasManagerRenderCompleted(object sender, SKBitmap e)
        {
            try
            {
                DrawBitmapPixelsToCanvas(e);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[RENDER ERROR] {ex.Message}");
            }
        }

        private static unsafe void DrawBitmapPixelsToCanvas(SKBitmap bitmap)
        {
            var ptr = (int*)bitmap.GetPixels();
            for (var col = 0; col < bitmap.Height; col++)
            for (var row = 0; row < bitmap.Width; row++)
            {
                _imageBuffer[row + col * bitmap.Width] = *(Color*)ptr;
                ptr++;
            }

            //canvas.SetPixels(0, 0, bitmap.Width, bitmap.Height, _imageBuffer);
            //ledMatrix.SwapOnVsync(canvas);
        }

        
    }
}
