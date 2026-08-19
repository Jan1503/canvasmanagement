# Updating the big display (Raspberry Pi, 384x192)

Targets .NET 10. Run all Pi commands over SSH. Replace `<pi>` with the host/IP.

## 0. Back up config (rsync --delete will overwrite it)
```bash
cp ~/verpixeld/appsettings.json ~/appsettings.backup.json
```

## 1. System upgrade (Pi)
```bash
sudo apt update && sudo apt full-upgrade -y
sudo apt install -y ffmpeg vlc          # ffmpeg = media; vlc/libVLC = VLC player ext (optional)
sudo reboot                              # if kernel/firmware updated
```

## 2. .NET 10 ASP.NET Core runtime (Pi, arm64)
App is framework-dependent, so it needs the runtime (not the SDK).
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
# Installs both Microsoft.AspNetCore.App 10.0 and Microsoft.NETCore.App 10.0 (arm64),
# side-by-side with your existing 8.0.22
sudo ./dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /opt/dotnet
/opt/dotnet/dotnet --list-runtimes                   # expect Microsoft.AspNetCore.App 10.x
```

## 3. yt-dlp (static binary; apt version is too old)
```bash
sudo apt remove -y yt-dlp
sudo wget https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64 -O /usr/local/bin/yt-dlp
sudo chmod a+rx /usr/local/bin/yt-dlp
hash -r
yt-dlp --version                         # expect 2026.x
```

## 4. Build & deploy (Windows dev machine)
```powershell
cd D:\Development\RGB-Display\CanvasManagement
./deploy.ps1 -Configuration Release -Rid linux-arm64 -FontsSource <folder-with-clean-.bdf-files>
```
Stop the service, then copy (excludes config + saved data so they aren't wiped):
```powershell
ssh pi@<pi> "sudo systemctl stop rgbdisplay"
rsync -av --delete `
  --exclude appsettings.json --exclude Layouts/ --exclude Schedules/ --exclude Favorites/ --exclude server.pfx `
  deploy/ pi@<pi>:/home/pi/verpixeld/
```
`deploy/` already contains the app (`verpixeld.dll`), all Extensions/, Filters/, and Fonts/ (BDF).

## 5. Fonts (Pi)
`deploy/Fonts/*.bdf` are copied by step 4. Verify:
```bash
ls ~/verpixeld/Fonts/*.bdf
```
deploy.ps1 warns if any BDF has mismatched STARTCHAR/ENDCHAR (corrupt).

## 6. Config (Pi) — keep your appsettings.json
Ensure `~/verpixeld/appsettings.json` has (edit the backup if step 4 replaced it):
- `Matrix`: Rows/Cols/ChainLength/Parallel for the 384x192 panel.
- `HomeAssistant`: `Enabled: true`, `BaseUrl`, `Token` (long-lived token).
- `WebServer`: ports / cert as before.

## 7. Native deps (only if missing)
- `librgbmatrix.so.1` — built from rpi-rgb-led-matrix (required).
- `libbass.so` — only for the Audio Player extension.

## 7a. Material Design Icons (optional — real HA icons)
The Home Assistant extensions render proper MDI glyphs if the webfont + its name→codepoint map are present
in `Fonts/`; otherwise they fall back to the built-in drawn icons. Use the **same MDI version** for both files.
```bash
cd /home/pi/verpixeld/Fonts          # (or your -FontsSource folder before deploy)
VER=7.4.47
wget -O materialdesignicons-webfont.ttf "https://cdn.jsdelivr.net/npm/@mdi/font@${VER}/fonts/materialdesignicons-webfont.ttf"
wget -O meta.json                        "https://cdn.jsdelivr.net/npm/@mdi/svg@${VER}/meta.json"
sudo systemctl restart rgbdisplay
```
- Files recognised: `materialdesignicons-webfont.ttf` + `meta.json` (or `mdi-meta.json`).
- `deploy.ps1` auto-copies these from `-FontsSource` into `deploy/Fonts/` if present.
- On startup the log shows `[MDI] Loaded N icon names …` (N ≈ 7000) when enabled.

## 8. Restart & verify
```bash
sudo systemctl restart rgbdisplay
sudo systemctl status rgbdisplay
sudo journalctl -u rgbdisplay -f        # watch logs
```
Checks:
- Web UI loads; default layout auto-loads on startup.
- `curl http://<pi>:5000/api/homeassistant/status` → `connected: true`.
- `curl "http://<pi>:5000/api/homeassistant/entities?q=temp"` lists entities.
