# Deploy VLC Player Extension to Raspberry Pi
# Usage: .\deploy.ps1 [-PiIP "192.168.10.38"]

param(
    [string]$PiIP = "192.168.10.38",
    [string]$PiUser = "pi",
    [string]$DeployDir = "/home/pi/CanvasManagement"
)

Write-Host "🚀 Deploying VLC Player Extension to $PiIP" -ForegroundColor Cyan
Write-Host ""

# Check if build succeeded
if (-not (Test-Path "./bin/Release/net8.0/CanvasManagement.Canvas.Extension.VLCPlayer.dll")) {
    Write-Host "❌ Build files not found. Building project..." -ForegroundColor Yellow
    dotnet build -c Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Build failed!" -ForegroundColor Red
        exit 1
    }
}

Write-Host "✅ Build files found" -ForegroundColor Green
Write-Host ""

# Check if plink/pscp are available (PuTTY)
$hasPuTTY = (Get-Command plink -ErrorAction SilentlyContinue) -and (Get-Command pscp -ErrorAction SilentlyContinue)

if (-not $hasPuTTY) {
    Write-Host "⚠️  PuTTY tools (plink/pscp) not found in PATH" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Manual deployment steps:" -ForegroundColor Cyan
    Write-Host "1. Install VLC on Raspberry Pi:"
    Write-Host "   ssh $PiUser@$PiIP"
    Write-Host "   sudo apt-get update && sudo apt-get install -y vlc libvlc-dev"
    Write-Host ""
    Write-Host "2. Copy extension files:"
    Write-Host "   scp -r ./bin/Release/net8.0/* $PiUser@$PiIP`:$DeployDir/extensions/"
    Write-Host ""
    Write-Host "3. Copy web interface:"
    Write-Host "   scp ./vlc-remote.html $PiUser@$PiIP`:$DeployDir/wwwroot/"
    Write-Host ""
    Write-Host "Or install PuTTY from: https://www.putty.org/" -ForegroundColor Yellow
    exit 0
}

# Install VLC on Raspberry Pi
Write-Host "📦 Installing VLC on Raspberry Pi..." -ForegroundColor Cyan
$installCmd = @"
if ! command -v vlc &> /dev/null; then
    echo 'Installing VLC...'
    sudo apt-get update
    sudo apt-get install -y vlc libvlc-dev
    echo 'VLC installed'
else
    echo 'VLC already installed'
fi
"@

plink -batch -pw raspberry $PiUser@$PiIP $installCmd

Write-Host ""

# Create deployment directory
Write-Host "📁 Creating deployment directory..." -ForegroundColor Cyan
plink -batch -pw raspberry $PiUser@$PiIP "mkdir -p $DeployDir/extensions $DeployDir/wwwroot"

# Copy extension files
Write-Host "📤 Copying extension files..." -ForegroundColor Cyan
pscp -batch -pw raspberry -r "./bin/Release/net8.0/*" "$PiUser@$PiIP`:$DeployDir/extensions/"

# Copy web interface
Write-Host "📤 Copying web interface..." -ForegroundColor Cyan
pscp -batch -pw raspberry "./vlc-remote.html" "$PiUser@$PiIP`:$DeployDir/wwwroot/"

Write-Host ""
Write-Host "✅ Deployment complete!" -ForegroundColor Green
Write-Host ""
Write-Host "📱 Access remote control at: http://$PiIP`:5000/vlc-remote.html" -ForegroundColor Cyan
Write-Host "🎬 Extension will appear in Canvas Management UI" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Restart your Canvas Management application"
Write-Host "2. Open the web UI"
Write-Host "3. Assign 'VLC Media Player' to a canvas"
Write-Host "4. Open the remote control page on your phone"
Write-Host "5. Enter a media URL and click Play!"
Write-Host ""
Write-Host "Enjoy! 🎉" -ForegroundColor Green
