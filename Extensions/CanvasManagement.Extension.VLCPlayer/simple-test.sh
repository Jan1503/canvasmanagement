#!/bin/bash
# Simple VLC Hardware Decode Test
# If you see CRLF errors, run: sed -i 's/\r$//' simple-test.sh

echo "Testing VLC Hardware Decoder..."
echo ""

# Quick test
echo "Running 5-second test video with V4L2 M2M..."
vlc --codec=h264_v4l2m2m \
    --v4l2-chroma=RV32 \
    --no-audio \
    --vout=dummy \
    --run-time=5 \
    http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4 \
    vlc://quit \
    2>&1 | grep -i "v4l2\|error\|failed"

echo ""
echo "If you see 'v4l2' messages above without errors, hardware decode should work!"
echo "If you see errors, the VLC extension will use software decode (slower)."
