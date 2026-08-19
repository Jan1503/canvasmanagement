namespace CanvasManagement.Interfaces;

/// <summary>
///     Base interface for canvas extensions (animations, effects, modes)
/// </summary>
public interface ICanvasExtension
{
    /// <summary>
    ///     Display name of the extension
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Gets whether the extension is currently running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     Start the extension/animation
    /// </summary>
    void Start();

    /// <summary>
    ///     Stop the extension/animation
    /// </summary>
    void Stop();
}