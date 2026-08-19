<#
.SYNOPSIS
    Builds and gathers verpixeld + all extensions + filters + fonts into a single deploy/ folder
    ready to copy to the Raspberry Pi. Avoids the hand-copy mistakes (reference assemblies instead
    of real DLLs, corrupted/missing fonts, stale copies).

.DESCRIPTION
    1. Publishes the verpixeld app for the target RID (framework-dependent). This brings the core
       libraries, the rpi-rgb-led-matrix binding, SkiaSharp's Linux native assets, the Speech SDK
       native libs, wwwroot and appsettings.json.
    2. Builds every extension and copies its MANAGED dlls into deploy/Extensions/<name>/ (only the
       dlls that are not already shipped with the app, so each folder just carries the extension and
       its unique managed dependencies - SkiaSharp/native are resolved from the app at runtime).
    3. Builds the filters and copies them into deploy/Filters/.
    4. Copies BDF fonts into deploy/Fonts/ (from -FontsSource, ./Fonts, or the WinForms demo set).

.NOTES
    Native runtime dependencies that must exist ON THE PI (not produced by this script):
      - librgbmatrix.so.1   (build the rpi-rgb-led-matrix native lib on the Pi)
      - VLC / libVLC        (apt install vlc)            - only for the VLC player extension
      - BASS native libs    (libbass.so)                 - only for the Audio player extension
      - ffmpeg / yt-dlp     (system packages)            - for media playback

.EXAMPLE
    ./deploy.ps1
    ./deploy.ps1 -Configuration Release -Rid linux-arm64 -FontsSource C:\clean-fonts
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Rid = "linux-arm64",
    [string]$Output = "",
    [string]$VerpixeldProject = "",
    [string]$FontsSource = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root "deploy" }
if ([string]::IsNullOrWhiteSpace($VerpixeldProject)) {
    $VerpixeldProject = Join-Path $root "..\verpixeld\verpixeld\verpixeld.csproj"
}

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

if (-not (Test-Path $VerpixeldProject)) { throw "verpixeld project not found: $VerpixeldProject" }

# ---------------------------------------------------------------------------
# 1. Clean output
# ---------------------------------------------------------------------------
Write-Step "Preparing $Output"
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Path $Output | Out-Null

# ---------------------------------------------------------------------------
# 2. Publish the verpixeld app (app + core + native assets)
# ---------------------------------------------------------------------------
Write-Step "Publishing verpixeld ($Configuration, $Rid)"
dotnet publish $VerpixeldProject -c $Configuration -r $Rid --self-contained false -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw "verpixeld publish failed" }

# DLLs already shipped with the app (so we don't duplicate them under Extensions/Filters)
$appDlls = New-Object System.Collections.Generic.HashSet[string]
Get-ChildItem $Output -Filter *.dll | ForEach-Object { [void]$appDlls.Add($_.Name) }

# Helper: PUBLISH a plugin project (so NuGet dependencies are gathered - a plain 'build' of a
# class library does NOT copy NuGet deps like LibVLCSharp/ManagedBass/Svg.Skia to its output) and
# copy its unique managed dlls into a destination folder. SkiaSharp and shared assemblies that the
# app already ships are skipped; they (and native assets) are resolved from the app at runtime.
function Publish-Plugin($projPath, $destRoot) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($projPath)
    Write-Host "  publishing $name"

    $tmp = Join-Path $destRoot ("_tmp_" + $name)
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }

    dotnet publish $projPath -c $Configuration -o $tmp --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "  publish failed: $name (skipping)"
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
        return
    }

    $dest = Join-Path $destRoot $name
    New-Item -ItemType Directory -Path $dest -Force | Out-Null

    $copied = 0
    Get-ChildItem $tmp -Filter *.dll | Where-Object { -not $appDlls.Contains($_.Name) } | ForEach-Object {
        Copy-Item $_.FullName -Destination $dest -Force
        $copied++
    }

    Remove-Item $tmp -Recurse -Force

    if ($copied -eq 0) {
        # Nothing unique to copy (e.g. an empty/no-op extension) - drop the empty folder
        Remove-Item $dest -Recurse -Force
        Write-Host "    (no plugin output - skipped)"
    }
    else {
        Write-Host "    copied $copied dll(s)"
    }
}

# ---------------------------------------------------------------------------
# 3. Extensions
# ---------------------------------------------------------------------------
Write-Step "Collecting extensions"
$extOut = Join-Path $Output "Extensions"
New-Item -ItemType Directory -Path $extOut -Force | Out-Null
Get-ChildItem (Join-Path $root "Extensions") -Recurse -Filter *.csproj | ForEach-Object {
    Publish-Plugin $_.FullName $extOut
}

# ---------------------------------------------------------------------------
# 4. Filters
# ---------------------------------------------------------------------------
Write-Step "Collecting filters"
$filterOut = Join-Path $Output "Filters"
New-Item -ItemType Directory -Path $filterOut -Force | Out-Null
Get-ChildItem (Join-Path $root "Filters") -Recurse -Filter *.csproj | ForEach-Object {
    Publish-Plugin $_.FullName $filterOut
}

# ---------------------------------------------------------------------------
# 5. Fonts
# ---------------------------------------------------------------------------
Write-Step "Collecting fonts"
if ([string]::IsNullOrWhiteSpace($FontsSource)) {
    $candidates = @(
        (Join-Path $root "Fonts"),
        (Join-Path $root "CanvasManagement.WinForms.Demo\bin\Debug\net10.0-windows\Fonts"),
        (Join-Path $root "CanvasManagement.WinForms.Demo\bin\Debug\net8.0-windows\Fonts")
    )
    $FontsSource = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ($FontsSource -and (Test-Path $FontsSource)) {
    $fontOut = Join-Path $Output "Fonts"
    New-Item -ItemType Directory -Path $fontOut -Force | Out-Null
    $fonts = Get-ChildItem $FontsSource -Filter *.bdf
    foreach ($f in $fonts) { Copy-Item $f.FullName -Destination $fontOut -Force }
    Write-Host "  copied $($fonts.Count) font(s) from $FontsSource"

    # Optional Material Design Icons assets for the Home Assistant extension (drop them into the
    # fonts source folder to enable real MDI glyphs): the webfont .ttf + the meta.json name->codepoint map.
    foreach ($mdi in @("materialdesignicons-webfont.ttf", "MaterialDesignIcons.ttf", "mdi.ttf",
                       "mdi-meta.json", "meta.json", "materialdesignicons.json")) {
        $p = Join-Path $FontsSource $mdi
        if (Test-Path $p) { Copy-Item $p -Destination $fontOut -Force; Write-Host "  copied MDI asset: $mdi" }
    }

    # Integrity sanity check: a clean BDF has matching STARTCHAR/ENDCHAR counts
    foreach ($f in (Get-ChildItem $fontOut -Filter *.bdf)) {
        $text = Get-Content $f.FullName -Raw
        $starts = ([regex]::Matches($text, "(?m)^STARTCHAR")).Count
        $ends = ([regex]::Matches($text, "(?m)^ENDCHAR")).Count
        if ($starts -ne $ends) {
            Write-Warning "  $($f.Name): STARTCHAR=$starts but ENDCHAR=$ends - font may be CORRUPT"
        }
    }
}
else {
    Write-Warning "  No fonts copied. Pass -FontsSource <folder-with-clean-.bdf-files>."
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Step "Done"
Write-Host "Deploy folder: $Output"
Write-Host "Copy it to the Pi, e.g.:"
Write-Host "  rsync -av --delete `"$Output/`" pi@raspberrypi:/home/pi/verpixeld/"
Write-Host ""
Write-Host "Reminder - ensure these native deps exist on the Pi:"
Write-Host "  librgbmatrix.so.1 (built on the Pi), and (if used) VLC, BASS, ffmpeg, yt-dlp."
