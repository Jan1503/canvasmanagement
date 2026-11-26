# Writing a CanvasManagement extension

An extension is a class library that **draws into one canvas**. The host (verpixeld) creates the canvas, instantiates your type with that canvas, and calls `Start()` / `Stop()`. The web UI reads `[ExtensionInfo]` and `[ExtensionParameter]` to build the settings form.

Minimal working example: `Extensions/CanvasManagement.Extension.Starfield`.

## 1. Project

Create `Extensions/CanvasManagement.Extension.MyThing/` and add it to `CanvasManagement.sln` (under the Extensions folder).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
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

SkiaSharp version comes from the solution’s `Directory.Packages.props` (**3.116.1**). Do not add your own version. Language version, nullable and implicit usings come from `Directory.Build.props`; Release builds treat warnings as errors.

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

Derive from `FrameExtension`. It implements `ICanvasExtension` and `IDisposable`, runs the frame loop, owns the back buffer (recreated when the canvas is resized), measures frame times, and stops cleanly: after `Stop()` returns no frame is drawn or submitted any more.

```csharp
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.MyThing;

[ExtensionInfo("My Thing",
    "Short description shown in the extension picker",
    "Visual Effects",                          // category in the UI
    IconResourceName = "my-thing.svg")]        // filename only
public sealed class MyThingExtension : FrameExtension
{
    private readonly SKPaint _paint = new() { IsAntialias = false };
    private float _x;

    internal MyThingExtension(ICanvas canvas) : base(canvas) { }

    public override string Name => "My Thing";

    [ExtensionParameter("Speed", "Pixels per second", DefaultValue = 40, MinValue = 1, MaxValue = 200)]
    public int Speed { get; set; } = 40;

    // 30 fps; read before every frame, so it may come from a parameter.
    protected override double FrameIntervalMs => 1000.0 / 30;

    protected override void OnStart() => _x = 0;            // reset state for a new run

    protected override void OnResize(int width, int height) { /* lay out for the new size */ }

    protected override void Render(SKCanvas canvas, int width, int height, float dt)
    {
        canvas.Clear(SKColors.Black);                         // the buffer keeps the last frame otherwise
        _x = (_x + Speed * dt) % width;
        _paint.Color = SKColors.Orange;
        canvas.DrawRect(_x, height / 2f, 4, 4, _paint);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _paint.Dispose();
    }
}
```

- `Render`, `OnStart`, `OnStop` and `OnResize` all run under the `Sync` lock. Methods called from outside (key input, `[ExtensionMethod]`s) take `lock (Sync)` before they change state.
- An exception in `Render` skips that frame and is logged as `[Name] Render error: …` (at most every 10 s); the loop keeps going.
- `BackBuffer` gives direct pixel access during `Render` (e.g. a cellular automaton writing a pixel span).
- `Time` is the seconds since `Start()`. `ClearOnStop` (default true) clears the canvas to black on stop; override it to clear to your own background.
- Do not draw pixel by pixel with `DrawRect`: use `PixelArt.Blit` for character sprites, `PixelArt.Disc/Ring`, `GameFx.GlowDisc` — they draw cached images. Keep `SKPaint`s as fields instead of creating them every frame.

Only an extension that cannot draw on a schedule (a video decoder, a network stream) implements `ICanvasExtension` itself and submits frames from its own pipeline with `SubmitCompletedFrame`.

### Double-buffer

The compositor may read the canvas bitmap while you draw. **Never** draw onto the `ICanvas` from a background thread pixel by pixel. `FrameExtension` draws into its own buffer and hands each finished frame over with `SubmitCompletedFrame`, which copies it under the canvas lock.

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

`KeyboardShortcut` is a hint the host can bind. verpixeld Studio only maps keys for methods whose **`Category` is `"Controls"`** (so audio volume shortcuts never steal arrow keys from a game). Specs are `|`-separated aliases; append `:up` for key-up:

```csharp
[ExtensionMethod("Go Left", "Hold left", Category = "Controls", KeyboardShortcut = "Left|A")]
public void GoLeft() { /* … */ }

[ExtensionMethod("Stop Left", "Release left", Category = "Controls", KeyboardShortcut = "Left:up|A:up")]
public void StopLeft() { /* … */ }
```

Games should AutoPilot until the first human input, then skip AI until `Start()` again. The wall has no keyboard — play happens in the Studio browser.

Key names: single letters, `Space`, `Up`/`Down`/`Left`/`Right`, `Enter`, `Escape`, `Backspace`. Gamepads (browser Gamepad API, works with the Docker/NAS host too — the pad is plugged into the PC/phone running Studio) report `PadA`, `PadB`, `PadX`, `PadY`, `PadLB`, `PadRB`, `PadLT`, `PadRT`, `PadBack`, `PadStart`, `PadUp`/`PadDown`/`PadLeft`/`PadRight` (D-pad and left stick). A pad key the game does not bind falls back to the keyboard: D-pad/stick → arrows, A/X → `Space`, B/Back → `Escape`, Start → `Enter`, so existing games are playable with a controller unchanged.

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
| `GetBestBdfFontForHeight` | largest registered BDF that still fits |
| `DrawRect` / `DrawFilledCircle` / `DrawLine` / `DrawPolygon` | shapes |
| `DrawBitmap` / `DrawBitmapWithAlpha` / `DrawBitmapRegion` | images / sprites |
| `FillGradient` | backgrounds |
| `ClearRect` | punch a transparent hole |
| `Width` / `Height` | always read live (layer editor resizes) |

BDF font names are the files in `Fonts/` (e.g. `"7x13"`, `"5x7"`). Pass `null` for the host default.

For LED sprites use **`PixelArt`** (`Scale`, `Blit`, `Disc`, `Ring`, `Hsv`): author in characters, blit 1:1 (scale 1 on a 128-tall wall). Do not anti-alias or use fractional `_px`. For HUD labels use **`CanvasText.Draw`** so BDF / Skia share the same baseline and “Use BDF Font” toggle.

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

`tools/ExtensionPreview` runs any built extension headless, presses its Controls on a schedule, saves a contact sheet of frames and records its sound:

```powershell
dotnet build Extensions\CanvasManagement.Extension.PacMan -c Release
dotnet run -c Release --project tools\ExtensionPreview -- PacMan --seconds 30 --every 2000 --wav --script "5:GoLeft,8:Pause"
```

Output goes to `%TEMP%\ExtensionPreview\<Name>` (`sheet.png`, `shot_*.png`, `sound.wav`).

```powershell
dotnet run --project CanvasManagement.WinForms.Demo -c Debug
```

Or run verpixeld with `"OutputMode": "simulation"` and the live preview.

## 12. Optional factory extension method

Some plugins also expose `canvas.GetStarfield()` in a static `Extension` class. That is only for in-process demos. The host **never** calls it; it always uses discovery + the `ICanvas` constructor.

## 13. Optional sound

The constructor stays `(ICanvas canvas)`. Sound is optional and lives on the canvas:

- `canvas.Audio` is null when the host has no board. Use `canvas.Audio?.Tone(...)`.
- `IExtensionAudio` is the shared voice pool: `Tone`, `Noise`, `Loop`, `StopLoop`, plus `Play` and `LoopSample` for a clip the extension decoded itself. The host mixes every extension into one stream. A pitched loop is how an engine follows speed; the samples stay in the plugin.
- `SoundSynth` (in `CanvasManagement.Interfaces`) builds clips in code: notes and sweeps (`Tone`, `Add`), tunes (`Melody`, MIDI note numbers), and seamless loops (`Loop`). Render them once at start and pass the arrays to `Play` / `LoopSample` with `SoundSynth.Rate`. Pac-Man's sounds are all made this way.
- `GameSound` (also in Interfaces) is the shortcut every bundled game uses: `Play(GameSfx.Coin)` for stock effects (jump, coin, explosion, line clear, game over …, throttled so a burst does not stack), and a looping background song from `ChipMusic` (eight original tunes composed in code) on slot 7. Games expose it as the parameters Sound, Music, Volume and Music Volume; call `Update()` once per frame and `Stop()` in `Stop()`.
- `GameFx` (Interfaces) is the shared juice kit: glowing particles, bursts, sprays, confetti, screen shake, flash, rising score pop-ups, soft glows, gradients and a parallax starfield, all 1:1 pixel friendly.
- Eight loop slots (0–7). Gain and pitch glide on the host, loops fade in and out, and switching the clip on a slot crossfades, so it is fine to call `LoopSample` every frame with new values.
- `ICanvasAudioSource` is for an extension that fills its own block. `Mix` runs on the audio thread, never inside the compositor lock and never inside a filter.
- A movie that is already streaming owns the board. Extension sound plays when nothing else is streaming.
- Stop every loop in `Stop()`.
- The audio panel shows the names of the running extensions (and no cover) while no film, YouTube video or radio station owns it. An extension that plays a medium implements `ICanvasNowPlaying` (`NowPlayingTitle`, `NowPlayingCover`: an image URL, data URI or a media file to grab a frame from); the VLC player does.

