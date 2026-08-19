#!/bin/bash

# Audio Player Extension Deployment Script for Raspberry Pi

PI_HOST="pi@raspberrypi"
DEPLOY_PATH="/opt/canvas-management/extensions/AudioPlayer"

echo "Building Audio Player Extension..."
dotnet build -c Release

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "Creating deployment directory on Raspberry Pi..."
ssh $PI_HOST "mkdir -p $DEPLOY_PATH"

echo "Deploying extension files..."
scp -r bin/Release/net8.0/* $PI_HOST:$DEPLOY_PATH/

echo "Deploying HTML remote control..."
scp audio-remote.html $PI_HOST:$DEPLOY_PATH/

echo "Setting permissions..."
ssh $PI_HOST "chmod -R 755 $DEPLOY_PATH"

echo ""
echo "✅ Deployment complete!"
echo ""
echo "Next steps:"
echo "1. Restart Canvas Management service on Raspberry Pi"
echo "2. Open web interface and assign 'Audio Player with VU Meters' extension"
echo "3. Or access remote control at: http://raspberrypi:5000/extensions/AudioPlayer/audio-remote.html"
echo ""
echo "Quick test:"
echo "  ssh $PI_HOST 'systemctl restart canvas-management'"
