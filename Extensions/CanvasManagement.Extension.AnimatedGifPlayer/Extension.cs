using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.AnimatedGifPlayer
{
    /// <summary>
    /// Extension methods for creating AnimatedGifPlayer instances
    /// </summary>
    public static class Extension
    {
        /// <summary>
        /// Creates a new AnimatedGifPlayer extension for this canvas
        /// </summary>
        /// <param name="canvas">The canvas to play GIFs on</param>
        /// <returns>A new AnimatedGifPlayer instance</returns>
        public static AnimatedGifPlayerExtension GetAnimatedGifPlayer(this ICanvas canvas)
        {
            return new AnimatedGifPlayerExtension(canvas);
        }
    }
}
