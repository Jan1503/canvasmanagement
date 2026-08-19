using System.Text;
using System.Text.Json;
using Windows.Media.Control;
using Windows.Storage.Streams;

// Companion agent: reads the Windows "system media transport controls" session (Apple Music app,
// YouTube/Music in Edge/Chrome, Groove, VLC, most players) and pushes the current track + album art to
// the verpixeld display's /api/nowplaying endpoint. No Spotify required.
//
// Usage:  NowPlayingAgent --host http://raspberrypi.local:8080 [--interval 1500]

var host = GetArg("--host") ?? "http://localhost:8080";
var intervalMs = int.TryParse(GetArg("--interval"), out var iv) ? Math.Max(500, iv) : 1500;
var endpoint = host.TrimEnd('/') + "/api/nowplaying";

// verpixeld redirects HTTP→HTTPS and uses a self-signed certificate, so accept the display's cert
// (this is a trusted device on your own LAN).
using var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
};
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
Console.WriteLine($"Now-Playing agent → {endpoint}  (every {intervalMs} ms). Ctrl+C to stop.");

var lastTrackKey = "";

while (true)
{
    try
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetCurrentSession();

        if (session != null)
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var isPlaying = playback.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var trackKey = $"{props.Artist}|{props.Title}|{props.AlbumTitle}";

            // Only (re)send the (potentially large) album art when the track actually changes.
            string? artBase64 = null;
            if (trackKey != lastTrackKey && props.Thumbnail != null)
                artBase64 = await ReadThumbnailBase64(props.Thumbnail);

            lastTrackKey = trackKey;

            var payload = new
            {
                title = props.Title ?? "",
                artist = props.Artist ?? "",
                album = props.AlbumTitle ?? "",
                isPlaying,
                positionSeconds = timeline.Position.TotalSeconds,
                durationSeconds = timeline.EndTime.TotalSeconds,
                artBase64
            };

            var json = JsonSerializer.Serialize(payload);
            await http.PostAsync(endpoint, new StringContent(json, Encoding.UTF8, "application/json"));
            Console.WriteLine($"{(isPlaying ? "▶" : "⏸")} {props.Artist} — {props.Title}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"(retry) {ex.Message}");
    }

    await Task.Delay(intervalMs);
}

static async Task<string?> ReadThumbnailBase64(IRandomAccessStreamReference thumbRef)
{
    try
    {
        using var stream = await thumbRef.OpenReadAsync();
        var bytes = new byte[stream.Size];
        using var dr = new DataReader(stream);
        await dr.LoadAsync((uint)stream.Size);
        dr.ReadBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
    catch
    {
        return null;
    }
}

static string? GetArg(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 1; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}
