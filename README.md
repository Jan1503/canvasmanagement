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

- **Canvases** are independent bitmaps with position, size, z-order, opacity and optional per-pixel alpha.
- **Extensions** are plugins that draw into a canvas (animation loop, clocks, stream players).
- **Filters** run on the *composited* frame, not on a single canvas.
- **BDF fonts** give pixel-perfect text on a 256×128 or 384×192 wall.

If you want to add a new clock, game or visual effect: start with [docs/EXTENSIONS.md](docs/EXTENSIONS.md).  
If you want a look (CRT scanlines, blur, seasonal overlay): start with [docs/FILTERS.md](docs/FILTERS.md).

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

A new plugin is picked up after **process restart**. There is no assembly hot-reload.

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
Content: Weather, News Ticker, Now Playing, Home Assistant, Advertising, Slideshow, Scroll text, GIF, YouTube, VLC, Audio, Network Stream (TPM2.NET), LAV1.  
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
