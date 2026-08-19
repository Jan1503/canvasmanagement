using System.Runtime.InteropServices;

namespace CanvasManagement.Extension.LAV1StreamPlayer;

internal static class AlsaNative
{
    private const string Lib = "libasound.so.2";

    // Error helpers
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr snd_strerror(int errnum);

    internal static string GetError(int err)
    {
        var ptr = snd_strerror(err);
        return ptr == IntPtr.Zero ? $"ALSA error {err}" : Marshal.PtrToStringUTF8(ptr) ?? $"ALSA error {err}";
    }

    // PCM
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int snd_pcm_open(out IntPtr pcm, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        snd_pcm_stream_t stream, int mode);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int snd_pcm_close(IntPtr pcm);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int snd_pcm_prepare(IntPtr pcm);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern snd_pcm_state_t snd_pcm_state(IntPtr pcm);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong size);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int snd_pcm_drop(IntPtr pcm);

    // Convenience: configure hw/sw params with sane defaults in one call.
    // ALSA docs: snd_pcm_set_params exists and is much simpler than manual hw/sw param objects.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int snd_pcm_set_params(
        IntPtr pcm,
        snd_pcm_format_t format,
        snd_pcm_access_t access,
        uint channels,
        uint rate,
        int soft_resample,
        uint latency);

    internal enum snd_pcm_stream_t
    {
        SND_PCM_STREAM_PLAYBACK = 0,
        SND_PCM_STREAM_CAPTURE = 1
    }

    internal enum snd_pcm_access_t
    {
        SND_PCM_ACCESS_RW_INTERLEAVED = 3
    }

    internal enum snd_pcm_format_t
    {
        SND_PCM_FORMAT_S16_LE = 2
    }

    internal enum snd_pcm_state_t
    {
        SND_PCM_STATE_OPEN = 0,
        SND_PCM_STATE_SETUP = 2,
        SND_PCM_STATE_RUNNING = 3,
        SND_PCM_STATE_XRUN = 4,
        SND_PCM_STATE_DRAINING = 5,
        SND_PCM_STATE_PAUSED = 6,
        SND_PCM_STATE_SUSPENDED = 7,
        SND_PCM_STATE_DISCONNECTED = 8
    }
}