# Now-Playing Agent (Windows)

A tiny console app that reads whatever is playing on your PC via the Windows System Media Transport
Controls (Apple Music app, YouTube / YouTube Music in Edge/Chrome, Groove, VLC, foobar, …) and pushes the
title, artist, album and **album art** to the verpixeld display.

It feeds the **Now Playing** display extension — no Spotify needed, and it works for your Apple Music and
YouTube subscriptions because it reads the OS media session, not a specific service API.

## Build

Requires the Windows 10 SDK (10.0.19041) — already present with Visual Studio or the .NET desktop workload.

```powershell
dotnet build tools/NowPlayingAgent/NowPlayingAgent.csproj -c Release
```

## Run

```powershell
# point it at your display (the verpixeld web server)
NowPlayingAgent --host http://raspberrypi.local:8080 --interval 1500
```

- `--host`     base URL of the display's web server (default `http://localhost:8080`)
- `--interval` poll interval in ms (default 1500)

Then add the **Now Playing** extension to a canvas in the verpixeld GUI. It reads the snapshot the agent
posts (via `/api/nowplaying`) and renders art + track + progress.

## Autostart (optional)

Create a shortcut to the published exe in `shell:startup`, or register a Scheduled Task at logon, passing
your `--host`.
