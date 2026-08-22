# CanvasManagement

.NET 10 canvas engine for LED walls and other low-resolution displays. It composites **multiple canvases**, loads **content extensions** and **post-processing filters** as plugins, and renders with SkiaSharp.

The host that talks to hardware, the web UI, media playback and voice lives in **[verpixeld](https://github.com/Jan1503/verpixeld)**. This repository is the shared framework those pieces sit on.

[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)

## What it does

```
  Extensions (clocks, games, weather, …)     each owns one ICanvas
                 │
                 ▼
        CanvasManager  ── z-order, opacity, transparent holes
                 │
                 ▼
        Filters (blur, scanline, overlays, …)   full-frame, after composite
                 │
                 ▼
        SKBitmap  ── verpixeld sends this to GPIO / PixPlane / HDMI / SPI / preview
```

- **Canvases** are independent bitmaps with position, size, z-order, opacity, optional per-pixel alpha, and `PanelColorBits` (8 or 14) for network LED walls.
- **Extensions** are plugins that draw into a canvas (animation loop, clocks, stream players).
- **Filters** run on the *composited* frame, not on a single canvas.
- **BDF fonts** give pixel-perfect text on a 256×128 or 384×192 wall.

If you want to add a new clock, game or visual effect: start with [docs/EXTENSIONS.md](docs/EXTENSIONS.md).  
If you want a look (CRT scanlines, blur, seasonal overlay): start with [docs/FILTERS.md](docs/FILTERS.md).

## What's new

Dated from the public GitHub history. Newest first.

### 2026-08-22 — HA Departures

New **HA Departures** tile: next trains/buses from a Home Assistant sensor (HVV, HAFAS, RMV, similar). Reads a `departures` / `next` JSON attribute list and draws coloured line badges (U-Bahn, S-Bahn, AKN, bus, ferry), destination, and countdown or clock time. The host stores array/object HA attributes as JSON strings so the list is available to the plugin.

Wall toasts now carry an optional **severity** on `HomeAssistantBridge.Notification` (`HaNotification`), so the host overlay can pick error/warning/success/info accents.

### 2026-08-22 — HA tiles, weather sky, plugin hot-reload

Home Assistant content plugins: **Sensor**, **Grid**, **Graph**, **Energy**, **Weather** (animated sky), **Now Playing**, **Climate**, **Waste**. Plugin DLLs load from a memory copy into a collectible context, so files on disk are not locked — copy a new DLL and **Reload plugins** in verpixeld (or `POST /api/plugins/reload`) without a process restart.

### 2026-08-21 / 2026-08-20 — Panel colour depth

`ICanvas.PanelColorBits` is 8 or 14 (default 14) for network LED walls. verpixeld takes the max of *visible* canvases and live-switches [verpixeld-panel](https://github.com/Jan1503/verpixeld-panel) firmware 1.7 with `livemode`. HDMI / SPI / GPIO / simulation ignore it.

### 2026-08-19 — YouTube 403 workaround, public engine

VLC and YouTube extensions pass yt-dlp android extractor-args so playback is less likely to 403. First public docs for the engine, extension API and filter API; clone the repo as **`CanvasManagement`** (PascalCase) so verpixeld’s project references resolve.

---

## Panel colour depth (network walls)

`ICanvas.PanelColorBits` is **8** (triple-buffer, high fps) or **14** (double-buffer, video quality). Default **14** so a video canvas is not silently quantized. HDMI, SPI, GPIO and simulation ignore it.

On a [verpixeld-panel](https://github.com/Jan1503/verpixeld-panel) the RP2350 cannot keep both buffer layouts in SRAM. verpixeld therefore takes the **maximum** of every *visible* canvas (hidden, or opacity below ~0.01, is ignored) and live-switches the panel with firmware 1.7 `livemode` — no reboot. One 14-bit canvas on the wall forces 14-bit for everyone; hide it and a clock-only layout can drop back to 8-bit.

This property is a **host/layout** control (web UI / persisted canvas JSON). Extensions should not override it unless they are a dedicated depth switch. See [verpixeld](https://github.com/Jan1503/verpixeld) for the switch sequence and [PixPlane](https://github.com/Jan1503/pixplane) `SetColorModeLiveAsync`.

## Layout

```
CanvasManagement/                 compositor (CanvasManager, Canvas)
CanvasManagement.Interfaces/      ICanvas, ICanvasExtension, ICanvasFilter, attributes
CanvasManagement.BdfFontManager/  bitmap font renderer
CanvasManagement.WinForms.Demo/   run extensions on a window (no Pi, no panel)
Extensions/                       one project per plugin
Filters/CanvasManagement.Filters/ all stock filters in one assembly
Fonts/                            redistributable BDF set — see Fonts/NOTICE.md
docs/EXTENSIONS.md
docs/FILTERS.md
deploy.ps1                        publish verpixeld + plugins + fonts
```

Expected siblings (same parent folder):

| Folder | Repo |
|--------|------|
| `verpixeld/` | [Jan1503/verpixeld](https://github.com/Jan1503/verpixeld) |
| `pixplane/` | [Jan1503/pixplane](https://github.com/Jan1503/pixplane) |

Clone this tree as **`CanvasManagement`** (PascalCase). verpixeld’s project reference is `../CanvasManagement/...`. On Linux:

```bash
git clone https://github.com/Jan1503/canvasmanagement.git CanvasManagement
```

## Build

```powershell
dotnet build CanvasManagement.sln -c Release
```

Target framework is **net10.0**. SkiaSharp is pinned to **3.116.1** in `Directory.Packages.props` (3.119.x is missing Raspberry Pi native assets). Do not bump it.

### Try an extension without hardware

```powershell
dotnet run --project CanvasManagement.WinForms.Demo -c Release
```

### Deploy with verpixeld (Raspberry Pi / panel host)

From this folder:

```powershell
./deploy.ps1 -Configuration Release -Rid linux-arm64
```

That publishes the host, copies every extension DLL into `deploy/Extensions/<name>/`, filters into `deploy/Filters/`, and BDFs into `deploy/Fonts/`. Copy `deploy/` to the device (keep the device’s `appsettings.json` and `server.pfx`).

Plugin load paths at runtime (next to `verpixeld.dll`):

- Extensions: `Extensions/**/*.dll`
- Filters: `Filters/**/*.dll`

Plugin DLLs are loaded from a memory copy into a collectible load context, so the files on disk are **not locked**. After copying a new `Extensions/` or `Filters/` DLL, Settings → Plugins → **Reload plugins** (or `POST /api/plugins/reload`) unloads the old types, loads the new ones, and restores running canvases/filters. A process restart is still required if `CanvasManagement.Interfaces` / SkiaSharp / the host itself changed, or if a native plugin (VLC, FFmpeg) pins the old context.

## How plugins are discovered

| | Extensions | Filters |
|---|------------|---------|
| Marker | `[ExtensionInfo(...)]` on the class | `ICanvasFilter` (+ `[FilterInfo]` for the UI) |
| Constructor | `(ICanvas canvas)` — may be `internal` | parameterless |
| UI parameters | `[ExtensionParameter]` on properties | `[FilterParameter]` on properties |
| Icon | 48×48 SVG, `EmbeddedResource`, `IconResourceName = "foo.svg"` | same |
| When it runs | `Start()` / `Stop()` on a canvas | `Apply(bitmap)` every composed frame |

Details, templates and a checklist: [docs/EXTENSIONS.md](docs/EXTENSIONS.md) and [docs/FILTERS.md](docs/FILTERS.md).

## Stock extensions (overview)

Clocks: Analog, Digital, Binary, Flip, Word, Tetris Clock.  
Content: Weather, News Ticker, Now Playing, Home Assistant (sensor, grid, graph, energy, weather, now-playing, climate, waste, **departures**), Advertising, Slideshow, Scroll text, GIF, YouTube, VLC, Audio, Network Stream (TPM2.NET), LAV1.  
Visuals: Starfield, Aquarium, Lava Lamp, Falling Sand, Game of Life, Arena screensaver.  
Games (original SkiaSharp drawing, fan-style): Pac-Man, Snake, Pong, Space Invaders, Dino Runner, Flappy Bird.

## Stock filters (overview)

Image: Blur, Pixelate, Grain, Vignette, Scanline, Oil painting, Ink sketch, Cel shading, Comic.  
Overlays: Matrix rain / overlay / transform, Neo Code Vision, Weather overlay.  
Seasonal: Christmas, Halloween, Valentine, New Year.

## Fonts

`Fonts/` ships X11 misc-fixed (public domain), TeX Gyre Adventor (GUST), and the Adobe/DEC `helvR12` BDF (redistributable with the notice in the file). See [Fonts/NOTICE.md](Fonts/NOTICE.md).

Private / non-redistributable files (Quake III logo & font, Webcomic Whore, Disney-named script) are **gitignored** and must not be added.

## License

MIT for the C# framework. See [LICENSE](LICENSE).

- LibVLCSharp (VLC player) is LGPL; VLC native libs are a **runtime** dependency, not in this repo.
- verpixeld as a combined application is **GPL-3.0-or-later** because of rpi-rgb-led-matrix and YouTubeMusicAPI.

Fan-style extensions are not affiliated with trademark owners. The Arena screensaver ships original placeholder art only.
