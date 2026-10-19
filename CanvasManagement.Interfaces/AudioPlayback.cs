namespace CanvasManagement.Interfaces;

/// <summary>
///     Shared playback-device probe for FFmpeg, VLC, and the visualizer.
///     Docker / no ALSA card → never open <c>default</c> or a Pulse monitor.
/// </summary>
public static class AudioPlayback
{
    public static bool RunningInContainer()
    {
        if (File.Exists("/.dockerenv")) return true;
        return string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasPlaybackDevice()
    {
        if (RunningInContainer()) return false;
        if (!OperatingSystem.IsLinux()) return true;

        try
        {
            const string snd = "/dev/snd";
            if (!Directory.Exists(snd)) return false;
            return Directory.EnumerateFileSystemEntries(snd, "pcmC*D*p").Any();
        }
        catch
        {
            return false;
        }
    }
}
