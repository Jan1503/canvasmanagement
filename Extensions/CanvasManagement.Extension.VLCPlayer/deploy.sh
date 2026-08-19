#!/bin/bash
# Deploy VLC Player Extension to Raspberry Pi
# Usage: ./deploy.sh [raspberry-pi-ip]

PI_IP="${1:-192.168.10.38}"
PI_USER="pi"
DEPLOY_DIR="/home/pi/CanvasManagement"

echo "🚀 Deploying VLC Player Extension to $PI_IP"
echo ""

# Check if build succeeded
if [ ! -f "./bin/Release/net8.0/CanvasManagement.Canvas.Extension.VLCPlayer.dll" ]; then
    echo "❌ Build files not found. Building project..."
    dotnet build -c Release
    
    if [ $? -ne 0 ]; then
        echo "❌ Build failed!"
        exit 1
    fi
fi

echo "✅ Build files found"
echo ""

# Install VLC on Raspberry Pi
echo "📦 Installing VLC on Raspberry Pi..."
ssh $PI_USER@$PI_IP << 'EOF'
if ! command -v vlc &> /dev/null; then
    echo "Installing VLC..."
    sudo apt-get update
    sudo apt-get install -y vlc libvlc-dev
    echo "✅ VLC installed"
else
    echo "✅ VLC already installed"
fi
EOF

echo ""

# Create deployment directory
echo "📁 Creating deployment directory..."
ssh $PI_USER@$PI_IP "mkdir -p $DEPLOY_DIR/extensions"

# Copy extension files
echo "📤 Copying extension files..."
scp -r ./bin/Release/net8.0/* $PI_USER@$PI_IP:$DEPLOY_DIR/extensions/

# Copy web interface
echo "📤 Copying web interface..."
scp ./vlc-remote.html $PI_USER@$PI_IP:$DEPLOY_DIR/wwwroot/

echo ""
echo "✅ Deployment complete!"
echo ""
echo "📱 Access remote control at: http://$PI_IP:5000/vlc-remote.html"
echo "🎬 Extension will appear in Canvas Management UI"
echo ""
echo "Next steps:"
echo "1. Restart your Canvas Management application"
echo "2. Open the web UI"
echo "3. Assign 'VLC Media Player' to a canvas"
echo "4. Open the remote control page on your phone"
echo "5. Enter a media URL and click Play!"
echo ""
echo "Enjoy! 🎉"
