using System;
using BenchmarkDotNet.Attributes;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class Lav1StreamPlayerBenchmark
{
    private byte[] _rgbFrame;
    private byte[] _bgraFrame;
    private byte[] _destinationBuffer;
    private const int Width = 640;
    private const int Height = 480;
    private const int RgbSize = Width * Height * 3;
    private const int BgraSize = Width * Height * 4;
    [GlobalSetup]
    public void Setup()
    {
        _rgbFrame = new byte[RgbSize];
        _bgraFrame = new byte[BgraSize];
        _destinationBuffer = new byte[BgraSize];
        // Fill with sample data
        Random rand = new Random(42);
        rand.NextBytes(_rgbFrame);
        rand.NextBytes(_bgraFrame);
    }

    [Benchmark]
    public void RgbToBgraConversion_Current()
    {
        unsafe
        {
            fixed (byte* dst = _destinationBuffer)
            fixed (byte* src = _rgbFrame)
            {
                byte* dstPtr = dst;
                int si = 0;
                for (var i = 0; i < Width * Height; i++)
                {
                    dstPtr[2] = src[si++];
                    dstPtr[1] = src[si++];
                    dstPtr[0] = src[si++];
                    dstPtr[3] = 255;
                    dstPtr += 4;
                }
            }
        }
    }

    [Benchmark]
    public void BgraDirectCopy_Current()
    {
        unsafe
        {
            fixed (byte* dst = _destinationBuffer)
            fixed (byte* src = _bgraFrame)
            {
                var srcSpan = new Span<byte>(src, BgraSize);
                var dstSpan = new Span<byte>(dst, BgraSize);
                srcSpan.CopyTo(dstSpan);
            }
        }
    }

    [Benchmark]
    public void TimestampConversion_Current()
    {
        long basePts = 1000000;
        long baseUtc = DateTime.UtcNow.Ticks;
        long pts = 1500000;
        int holdMs = 200;
        var deltaUs = pts - basePts;
        var result = baseUtc + TimeSpan.FromMilliseconds(deltaUs / 1000.0 + holdMs).Ticks;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    // Cleanup if needed
    }
}