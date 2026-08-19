# Writing a CanvasManagement filter

A filter is a class that **post-processes the composited wall**, after every canvas has been drawn. It does not own a canvas. Blur, CRT scanlines, seasonal overlays and “matrix rain” are filters. Clocks and games are [extensions](EXTENSIONS.md).

Stock filters live in one assembly: `Filters/CanvasManagement.Filters/`. You can add a class there **or** ship a separate `Filters/*.dll`.

## 1. When your code runs

Each display frame, `CanvasManager`:

1. Clears the main bitmap.
2. Draws visible canvases in z-order (opacity / transparent holes).
3. Calls `filter.Apply(_mainCanvasBitmap)` for every **enabled** filter, in list order.
4. Applies global brightness.

So:

- You see **all** layers already composited (clock + video + overlay).
- You run **every frame** at wall resolution (e.g. 256×128 or 384×192). Keep `Apply` cheap.
- Order matters: scanline after blur looks different from blur after scanline. The UI list order is the apply order.

Image correction (gamma / white-balance) in verpixeld runs **after** this, in the host render loop — not inside CanvasManagement.

## 2. Contract

```csharp
public interface ICanvasFilter
{
    string Name { get; }
    float Intensity { get; set; }   // 0 = off, 1 = full
    bool Enabled { get; set; }
    SKBitmap Apply(SKBitmap source, bool inPlace = true);
}
```

Discovery (`FilterDiscoveryService`):

- Loads `Filters/**/*.dll` next to the host.
- Takes every non-abstract class that **implements `ICanvasFilter`**.
- Instantiates with **`Activator.CreateInstance(type)`** — you need a **public parameterless constructor** (the implicit one is enough).

`[FilterInfo]` is not required for loading, but **without it the UI has no nice name, category or icon**. Always add it.

## 3. Project

Adding to the stock assembly (simplest):

1. New `MyLookFilter.cs` in `Filters/CanvasManagement.Filters/`.
2. New `Icons/my-look.svg` (48×48). The csproj already embeds `Icons\*.svg`.
3. Rebuild / `deploy.ps1`.

Separate plugin assembly:

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
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Icons\*.svg" />
  </ItemGroup>
</Project>
```

Put the csproj under `Filters/` so `deploy.ps1` publishes it into `deploy/Filters/`. Pin SkiaSharp via `Directory.Packages.props` (3.116.1).

## 4. Class skeleton

```csharp
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

[FilterInfo("Scanline",
    "Horizontal CRT-style lines",
    "Image Enhancement",
    IconResourceName = "scanline.svg")]
public sealed class ScanlineFilter : ICanvasFilter
{
    public string Name => "Scanline";
    public float Intensity { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;

    [FilterParameter("Line opacity", "How dark the lines are",
        MinValue = 0.0, MaxValue = 1.0, DefaultValue = 0.35)]
    public float LineOpacity { get; set; } = 0.35f;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();
        // mutate bitmap …
        return bitmap;
    }
}
```

### `Apply` rules

- **Default path is in-place.** The compositor passes the main bitmap with `inPlace: true` (the default). Mutate `source` and return it. Do not leak a second bitmap every frame.
- If `inPlace` is false, `source.Copy()` first and return the copy.
- If disabled or `Intensity <= 0`, return `source` unchanged (no work).
- `Intensity` is the master mix (0…1). Scale your effect with it so the UI slider does something obvious.
- Do not `Dispose()` the source bitmap.
- Pixel format is **BGRA**, premultiplied, wall-sized. Do not assume 1920×1080.

### Performance

This runs at 30–60 Hz on a Raspberry Pi or on the panel host. Prefer:

- integer / byte loops over the pixmap (`source.GetPixels()`) for simple effects;
- one `SKSurface` + `SKImageFilter` for blurs (see `BlurFilter`);
- **no** LINQ, **no** per-pixel `new SKPaint()` in the inner loop.

Allocate paints / buffers in fields and reuse them. Recreate only if `source.Width` / `Height` change.

## 5. Parameters

`[FilterParameter]` on public properties shows up in the filter settings UI (same idea as extensions, flatter: no nested objects).

Supported well: `float`, `int`, `bool`, `string`, enums. Use `MinValue` / `MaxValue` / `DefaultValue`.

`Name`, `Intensity` and `Enabled` are the interface members — the host already exposes intensity/enabled. Extra `[FilterParameter]` fields are for filter-specific knobs (density, colour, threshold, …).

## 6. Icon

Same as extensions: 48×48 SVG, embedded, `IconResourceName = "scanline.svg"` (filename only). Original artwork only.

## 7. Checklist

- [ ] Implements `ICanvasFilter` with a public parameterless constructor.
- [ ] `[FilterInfo]` + unique `Name` (layouts store the filter by name).
- [ ] `Apply` is in-place, returns `source` when off, does not dispose the bitmap.
- [ ] Scales with `Intensity`.
- [ ] Cheap enough for every frame at wall resolution.
- [ ] SVG icon embedded.
- [ ] Either in `CanvasManagement.Filters` or a csproj under `Filters/` so `deploy.ps1` copies it.
- [ ] Restart verpixeld after copying DLLs.

## 8. Debug

`CanvasManagement.WinForms.Demo` composites and can apply filters. Or run verpixeld in `simulation` mode and toggle the filter in the Effects tab.

Compare with `BlurFilter.cs` (Skia image filter) and `ScanlineFilter.cs` (per-row darkening) for the two usual styles.
