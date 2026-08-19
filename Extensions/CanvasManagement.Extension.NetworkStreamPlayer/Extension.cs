using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.NetworkStreamPlayer
{
    /// <summary>
    /// Extension methods for creating NetworkStreamPlayer instances
    /// </summary>
    public static class Extension
    {
        /// <summary>
        /// Creates a new NetworkStreamPlayer extension for this canvas
        /// </summary>
        /// <param name="canvas">The canvas to stream to</param>
        /// <returns>A new NetworkStreamPlayerExtension instance</returns>
        public static NetworkStreamPlayerExtension GetNetworkStreamPlayer(this ICanvas canvas)
        {
            return new NetworkStreamPlayerExtension(canvas);
        }
    }
}
