# CanvasManagement

.NET 10 canvas engine for [verpixeld](https://github.com/Jan1503/verpixeld): multi-layer composition, BDF fonts, visual filters, and content extensions.

This repository is the **shared framework**. The host process, web UI, media player and hardware outputs live in verpixeld. Build/deploy with `deploy.ps1` from this folder.

## Layout

```
CanvasManagement/            core compositor
CanvasManagement.Interfaces/ plugin contracts
CanvasManagement.BdfFontManager/
Extensions/                  clocks, weather, games, stream players, …
Filters/
Fonts/                       redistributable BDF set (see Fonts/NOTICE.md)
deploy.ps1                   publish verpixeld + plugins + fonts
```

Sibling checkouts expected next to this repo:

- `../verpixeld`
- `../pixplane`

## Build

```powershell
dotnet build CanvasManagement.sln -c Release
./deploy.ps1 -Configuration Release -Rid linux-arm64
```

Copy `appsettings.example.json` from verpixeld to `appsettings.json` on the device. Runtime config, certificates and media are not part of this tree.

## License

MIT. See `LICENSE`. Fonts have their own terms in `Fonts/NOTICE.md`. LibVLCSharp (VLC player extension) is LGPL; VLC native libraries are a runtime dependency, not bundled.

Some extensions are **fan-style homages** (original SkiaSharp drawing, no ripped sprite sheets). They are not affiliated with the trademark owners.

The Arena screensaver ships original placeholder artwork. Official Quake III Arena logo/font files are gitignored and must not be added to this repository.
