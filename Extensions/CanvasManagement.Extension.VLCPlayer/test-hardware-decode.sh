#!/bin/bash
# Quick Hardware Decode Test for Raspberry Pi 4
# Run this BEFORE deploying the VLC extension to verify hardware decode works

echo "==================================================================="
echo "  Raspberry Pi 4 Hardware Video Decode Test"
echo "==================================================================="
echo ""

# Check for V4L2 devices
echo "Step 1: Checking for V4L2 video devices..."
if ls /dev/video* 1> /dev/null 2>&1; then
    echo "✓ Found V4L2 devices:"
    ls -la /dev/video*
else
    echo "✗ No V4L2 devices found!"
    echo "  This might be normal on older Pi OS versions"
fi
echo ""

# Check user permissions
echo "Step 2: Checking video group membership..."
if groups | grep -q video; then
    echo "✓ User is in video group"
else
    echo "✗ User NOT in video group!"
    echo "  Fix: sudo usermod -a -G video $USER"
    echo "  Then: logout and login again"
fi
echo ""

# Check VLC installation
echo "Step 3: Checking VLC installation..."
if command -v vlc &> /dev/null; then
    echo "✓ VLC is installed"
    vlc --version | head -1
else
    echo "✗ VLC not found!"
    echo "  Fix: sudo apt-get install vlc libvlc-dev"
    exit 1
fi
echo ""

# Test URL
TEST_URL="http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"

# Test 1: V4L2 M2M
echo "==================================================================="
echo "Test 1: V4L2 M2M Hardware Decoder"
echo "==================================================================="
timeout 10 vlc \
    --codec=h264_v4l2m2m \
    --v4l2-chroma=RV32 \
    --no-audio \
    --vout=dummy \
    --run-time=5 \
    "$TEST_URL" \
    vlc://quit \
    2>&1 | grep -E "(v4l2|h264|using|error|failed)" | head -20

if [ $? -eq 0 ] || [ $? -eq 124 ]; then
    echo ""
    echo "✓ V4L2 M2M test completed (check above for errors)"
else
    echo ""
    echo "✗ V4L2 M2M test failed"
fi
echo ""

# Test 2: AVCODEC Hardware
echo "==================================================================="
echo "Test 2: AVCODEC Hardware Auto-Detection"
echo "==================================================================="
timeout 10 vlc \
    --avcodec-hw=any \
    --avcodec-dr \
    --no-audio \
    --vout=dummy \
    --run-time=5 \
    "$TEST_URL" \
    vlc://quit \
    2>&1 | grep -E "(avcodec|hardware|decoder|error|failed)" | head -20

if [ $? -eq 0 ] || [ $? -eq 124 ]; then
    echo ""
    echo "✓ AVCODEC hardware test completed (check above for errors)"
else
    echo "✗ AVCODEC hardware test failed"
fi
echo ""

# CPU usage test
echo "==================================================================="
echo "Test 3: CPU Usage Comparison (Hardware vs Software)"
echo "==================================================================="
echo "Testing SOFTWARE decode (should use ~60-90% CPU)..."
vlc \
    --codec=avcodec \
    --no-audio \
    --vout=dummy \
    --run-time=10 \
    "$TEST_URL" \
    vlc://quit \
    2>&1 > /dev/null &
VLC_PID=$!
sleep 3
CPU_SW=$(top -bn1 -p $VLC_PID | tail -1 | awk '{print $9}')
killall vlc 2>/dev/null
wait $VLC_PID 2>/dev/null
echo "Software decode CPU: ${CPU_SW}%"
sleep 2

echo ""
echo "Testing HARDWARE decode (should use ~15-25% CPU)..."
vlc \
    --codec=h264_v4l2m2m \
    --v4l2-chroma=RV32 \
    --no-audio \
    --vout=dummy \
    --run-time=10 \
    "$TEST_URL" \
    vlc://quit \
    2>&1 > /dev/null &
VLC_PID=$!
sleep 3
CPU_HW=$(top -bn1 -p $VLC_PID | tail -1 | awk '{print $9}')
killall vlc 2>/dev/null
wait $VLC_PID 2>/dev/null
echo "Hardware decode CPU: ${CPU_HW}%"
echo ""

# Recommendations
echo "==================================================================="
echo "  Results & Recommendations"
echo "==================================================================="
echo ""
if [ ! -z "$CPU_HW" ] && [ ! -z "$CPU_SW" ]; then
    echo "CPU Usage Comparison:"
    echo "  Software: ${CPU_SW}%"
    echo "  Hardware: ${CPU_HW}%"
    echo ""
    
    # Try to compare (bash doesn't do floating point, so this is approximate)
    if (( $(echo "$CPU_HW < 30" | bc -l) )); then
        echo "✓ Hardware decoding appears to be working!"
        echo "  The VLC extension should use the V4L2 M2M decoder automatically."
    else
        echo "⚠  Hardware decoding may not be working properly."
        echo "  The extension will fall back to software decode (slower)."
    fi
else
    echo "⚠  Could not measure CPU usage accurately."
    echo "  Check the test output above for errors."
fi

echo ""
echo "To deploy the VLC extension:"
echo "  1. Build: dotnet build -c Release"
echo "  2. Deploy to Pi"
echo "  3. Check logs for: '[VLC] ✓ Decoder initialized: --codec=h264_v4l2m2m'"
echo ""
echo "==================================================================="
