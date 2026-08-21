# Writing a CanvasManagement extension

An extension is a class library that **draws into one canvas**. The host (verpixeld) creates the canvas, instantiates your type with that canvas, and calls `Start()` / `Stop()`. The web UI reads `[ExtensionInfo]` and `[ExtensionParameter]` to build the settings form.

Minimal working example: `Extensions/CanvasManagement.Extension.Starfield`.

## 1. Project

Create `Extensions/CanvasManagement.Extension.MyThing/` and add it to `CanvasManagement.sln` (under the Extensions folder).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SkiaSharp" />
    <ProjectReference Include="..\..\CanvasManagement.Interfaces\CanvasManagement.Interfaces.csproj" />
    <!-- only if you draw BDF text -->
    <ProjectReference Include="..\..\CanvasManagement.BdfFontManager\CanvasManagement.BdfFontManager.csproj" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Icons\*.svg" />
  </ItemGroup>
</Project>
```

SkiaSharp version comes from the solution’s `Directory.Packages.props` (**3.116.1**). Do not add your own version.

`deploy.ps1` publishes every `Extensions/**/*.csproj` automatically. You do **not** need a project reference from verpixeld.

## 2. Discovery (what the host actually looks for)

`ExtensionDiscoveryService` loads `Extensions/**/*.dll` next to the host, then scans **all loaded assemblies** for:

```text
class, not abstract, has [ExtensionInfo]
```

`ICanvasExtension` is the intended contract (`Name`, `IsRunning`, `Start`, `Stop`) but **discovery is attribute-based**. Implement the interface anyway so the host and layouts can treat you uniformly.

Instantiation:

```csharp
Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
    null, new object[] { canvas }, null);
```

So the constructor **must** be exactly `(ICanvas canvas)`. `internal` is fine (and preferred). A public parameterless constructor will **not** be used.

## 3. Class skeleton

```csharp
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.MyThing;

[ExtensionInfo("My Thing",
    "Short description shown in the extension picker",
    "Visual Effects",                          // category in the UI
    IconResourceName = "my-thing.svg")]        // filename only
public sealed class MyThingExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private SKBitmap? _backBuffer;

    internal MyThingExtension(ICanvas canvas) => _canvas = canvas;

    public string Name => "My Thing";
    public bool IsRunning { get; private set; }

    [ExtensionParameter("Speed", "Animation speed", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0)]
    public double Speed { get; set; } = 1.0;

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _backBuffer = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        _backBuffer?.Dispose();
        _backBuffer = null;
        _canvas.Clear();
        IsRunning = false;
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using (var sk = new SKCanvas(_backBuffer))
                {
                    sk.Clear(SKColors.Black);
                    // draw into _backBuffer …
                }
                _canvas.SubmitCompletedFrame(_backBuffer);
                await Task.Delay(16, ct); // ~60 fps; host also caps the wall
            }
        }
        catch (OperationCanceledException) { }
        finally { IsRunning = false; }
    }
}
```

### Double-buffer (required for animation)

The compositor may read the canvas bitmap while you draw. **Never** draw onto `_canvas` from a background thread pixel-by-pixel.

1. Allocate an `SKBitmap` the size of `_canvas` (`Bgra8888` + `Premul`).
2. Draw into that bitmap.
3. Call `_canvas.SubmitCompletedFrame(bitmap)` — that copies the finished frame under the canvas lock.

If the user resizes the canvas in the layer editor, `_canvas.Width` / `Height` change. Recreate the back buffer when they differ.

### Transparent overlay canvases

If the canvas is in **transparent background** mode, `Clear(SKColors.Black)` fills opaque black and covers layers below. Use `sk.Clear(SKColors.Transparent)` (or `_canvas.MakeTransparent()` / `ClearRect`) so holes stay holes.

## 4. Parameters (settings UI)

Put `[ExtensionParameter]` on **public instance properties**. The web UI binds them live; layouts persist the values as JSON.

| CLR type | UI widget |
|----------|-----------|
| `int`, `float`, `double` | slider / number (`MinValue` / `MaxValue`) |
| `bool` | checkbox |
| `string` | text field |
| `enum` | dropdown (names) |
| `SKColor` | colour picker (`DefaultValue = "#RRGGBB"`) |
| nested class/struct with its own `[ExtensionParameter]` props | grouped fields |
| `List<T>` of such a nested type | repeatable list (e.g. HA grid rows) |

```csharp
[ExtensionParameter("Glow", "Halo around the digits", DefaultValue = true)]
public bool Glow { get; set; } = true;

[ExtensionParameter("Align", "Horizontal alignment", DefaultValue = ClockHAlign.Center)]
public ClockHAlign Align { get; set; } = ClockHAlign.Center;
```

Rules:

- The host **reads and writes the property** while `Start()` is running. Keep getters/setters cheap and thread-safe (or accept a one-frame glitch).
- `ReadOnly = true` shows the value but does not edit it.
- `Order` controls field order inside a group.
- Depth is capped at 4 nested objects.

## 5. Actions (`[ExtensionMethod]`)

Optional. Public methods tagged with `[ExtensionMethod]` show up as buttons / API actions (play, pause, reset, …).

```csharp
[ExtensionMethod("Reset", "Restart the animation", Category = "Playback", Order = 1)]
public void Reset() { /* … */ }
```

## 6. Icon

- 48×48 SVG in `Icons/my-thing.svg`.
- `EmbeddedResource Include="Icons\*.svg"`.
- `[ExtensionInfo(..., IconResourceName = "my-thing.svg")]` — **filename only**, not a path.
- Keep it original (no third-party logos).

## 7. Drawing helpers on `ICanvas`

`ICanvas.PanelColorBits` (8 or 14) is the preferred network-panel depth while this canvas is visible. The host uses the **max of visible canvases**; HDMI / GPIO / SPI ignore it. Leave the default (14) unless you are building a layout that should run the wall in 8-bit. Do not toggle it every frame.

You can draw through `ICanvas` from the **host thread** (or after `SubmitCompletedFrame`). Useful bits for LED walls:

| Method | Use |
|--------|-----|
| `DrawBdfText` / `MeasureBdfText` / `RenderBdfTextToBitmap` | pixel-perfect labels |
| `DrawRect` / `DrawFilledCircle` / `DrawLine` / `DrawPolygon` | shapes |
| `DrawBitmap` / `DrawBitmapWithAlpha` / `DrawBitmapRegion` | images / sprites |
| `FillGradient` | backgrounds |
| `ClearRect` | punch a transparent hole |
| `Width` / `Height` | always read live (layer editor resizes) |

BDF font names are the files in `Fonts/` (e.g. `"7x13"`, `"5x7"`). Pass `null` for the host default.

## 8. Home Assistant

Do **not** put a long-lived token on an extension parameter. The host fills `HomeAssistantBridge`; extensions only read:

```csharp
if (HomeAssistantBridge.TryGet("sensor.office_temp", out var s))
    _canvas.DrawBdfText($"{s.State} {s.Unit}", 2, 2, SKColors.White);
```

Graphs: `RequestHistory(entityId)` then `GetHistory(entityId)`. See the Home Assistant extension.

## 9. Lifecycle and threading

| Call | Thread | What you should do |
|------|--------|--------------------|
| constructor | host | store `ICanvas`, do not start tasks |
| `Start()` | host | allocate buffer, spawn loop |
| property setters | host / API | update fields; the loop reads them next frame |
| `Stop()` | host | cancel, wait briefly, dispose bitmaps, `Clear()` |
| `Dispose()` | host | same as `Stop()` |

If `Start()` is called twice, no-op. If `Stop()` is called when idle, no-op.

## 10. Checklist before it “just works” in verpixeld

- [ ] `[ExtensionInfo]` with a **unique display name** (layouts store that name).
- [ ] Constructor `(ICanvas canvas)` (internal is OK).
- [ ] `Start` / `Stop` / `Name` / `IsRunning`.
- [ ] Animation uses `SubmitCompletedFrame`, not unsynchronized `SetPixel`.
- [ ] SVG icon embedded; `IconResourceName` matches the file name.
- [ ] Project is under `Extensions/` so `deploy.ps1` publishes it.
- [ ] Unique managed DLLs only — SkiaSharp is already in the host; `deploy.ps1` skips duplicates.
- [ ] Restart verpixeld after copying DLLs (no hot-reload).
- [ ] No secrets, no third-party logos/fonts in the plugin.

## 11. Debug without a panel

```powershell
dotnet run --project CanvasManagement.WinForms.Demo -c Debug
```

Or run verpixeld with `"OutputMode": "simulation"` and the live preview.

## 12. Optional factory extension method

Some plugins also expose `canvas.GetStarfield()` in a static `Extension` class. That is only for in-process demos. The host **never** calls it; it always uses discovery + the `ICanvas` constructor.
