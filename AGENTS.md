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
- Implement `ICanvasExtension` (`Name`, `IsRunning`, `Start`, `Stop`) and `IDisposable`.
- Draw with SkiaSharp. Pixel text is BDF (`DrawBdfText`). `IsAntialias = false` on a 1:1 LED grid.
- Read `canvas.Width` / `Height` every frame. Do not cache 256×128.
- Prefer `ExtensionLoop` for animation (token, target fps, Stop/Dispose). Do not spin a raw `Task.Run` without cancellation.
- Filters run **inside** the compositor lock — a slow filter drops the whole wall.

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
```

## Do not

- Pin a private SkiaSharp version (use `Directory.Packages.props`).
- Add a project reference from verpixeld to a new extension — `deploy.ps1` / the Docker seed copy DLLs.
- Use Skia typefaces for HUD text on the wall (blurry). Use BDF.
