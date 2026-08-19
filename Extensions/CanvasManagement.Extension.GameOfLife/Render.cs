using SkiaSharp;

namespace CanvasManagement.Canvas.Extension.GameOfLife;

public static class Render
{
    /// <summary>
    ///     Draw entire board (global mode)
    /// </summary>
    public static void DrawBoard(Board board, SKBitmap target, SKColor cellColor, SKColor backgroundColor)
    {
        target.Erase(backgroundColor);

        // OPTIMIZATION: Use unsafe pointer access for faster pixel manipulation
        unsafe
        {
            var pixels = (uint*)target.GetPixels().ToPointer();
            var width = board.Columns;
            var cellColorValue = (uint)cellColor;

            for (var y = 0; y < board.Rows; y++)
            {
                var rowOffset = y * width;
                for (var x = 0; x < board.Columns; x++)
                    if (board.Cells[y, x] > 0)
                        pixels[rowOffset + x] = cellColorValue;
            }
        }
    }
}