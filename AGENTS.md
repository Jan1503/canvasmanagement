# CanvasManagement — agent notes

.NET 10 canvas engine for LED walls. The host is **[verpixeld](https://github.com/Jan1503/verpixeld)** (sibling folder). This repo is the compositor, plugins, BDF fonts, and `deploy.ps1`.

```
CanvasManagement/     engine (Canvas, CanvasManager)
CanvasManagement.Interfaces/   ICanvas, ICanvasExtension, ICanvasFilter, PixelArt
CanvasManagement.BdfFontManager/
Extensions/           one csproj per plugin; deploy.ps1 publishes all
Filters/
```

## Contract

- Discovery is `[ExtensionInfo]` on a non-abstract class. Constructor **must** be `(ICanvas canvas)`.
- Derive from `FrameExtension` (implements `ICanvasExtension` and `IDisposable`): override `Name`, `Render(canvas, w, h, dt)` and as needed `FrameIntervalMs`, `OnStart`, `OnStop`, `OnResize`. It owns the loop, the back buffer (recreated on resize) and a clean Stop. Hooks run under `Sync`; take it in input methods. No `System.Timers.Timer` or raw `Task.Run` render loops.
- Only an extension that cannot draw frames on a schedule (a video decoder, a stream) implements `ICanvasExtension` itself, with `ExtensionLoop` or its own pipeline.
- Draw with SkiaSharp. Pixel text is BDF (`CanvasText`). `IsAntialias = false` on a 1:1 LED grid.
- Read the size every frame (`Render` gets it). Do not cache 256×128.
- Sprites, discs and glows: `PixelArt.Blit/Disc/Ring`, `GameFx.GlowDisc` (cached images). Never one `DrawRect` per pixel in a frame; no `new SKPaint` per frame in hot paths.
- Filters run **inside** the compositor lock — a slow filter drops the whole wall. They also run per canvas (`Canvas.SetFilters`, “Effects” in the Studio), so they get bitmaps of different sizes and BGRA or RGBA (`ChannelLayout`).
- A parameter whose setter sets other parameters (a preset or theme) is marked `AppliesFirst = true` and only acts when its value changes: hosts re-apply whole saved configurations.

Full recipe: [docs/EXTENSIONS.md](docs/EXTENSIONS.md), [docs/FILTERS.md](docs/FILTERS.md).

## Display size

`CanvasManager.Resize` swaps the composite bitmap while the render loop runs and raises `DisplayResized`. Existing canvases keep geometry; pixels outside them stay black. The host follows overlays; Studio canvases are stretched by the user.

## Hosts

Plugins ship in both the Pi deploy (`deploy.ps1`) and the NAS image. After changing an extension/filter that the NAS image bakes in:

```powershell
D:\Development\RGB-Display\verpixeld\docker\build.ps1 -Tar
```

Do not flash firmware. Do not commit the tar.

## Tests

```powershell
dotnet test D:\Development\RGB-Display\CanvasManagement\CanvasManagement.Tests\CanvasManagement.Tests.csproj
dotnet test D:\Development\RGB-Display\CanvasManagement\CanvasManagement.Extensions.Tests -c Release
```

The second is the smoke test of every extension (two sizes, ~6 min). `tools/ExtensionPreview` renders an extension to PNG sheets and prints fps, render time, CPU and allocations.

## Do not

- Pin a private SkiaSharp version (use `Directory.Packages.props`).
- Add a project reference from verpixeld to a new extension — `deploy.ps1` / the Docker seed copy DLLs.
- Use Skia typefaces for HUD text on the wall (blurry). Use BDF.
