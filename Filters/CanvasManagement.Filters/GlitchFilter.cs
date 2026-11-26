using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Digital glitches in short bursts: bands of rows jump sideways and red/blue split apart, then the
///     picture settles again. Quiet most of the time, so it surprises instead of tiring.
/// </summary>
[FilterInfo("Glitch",
    "Short bursts of digital glitches: bands jump sideways and colours split",
    "Artistic",
    IconResourceName = "glitch.svg")]
public sealed class GlitchFilter : ICanvasFilter
{
    [FilterParameter("Frequency", "How often a burst starts (0 = rarely, 1 = constantly)", MinValue = 0.0,
        MaxValue = 1.0, DefaultValue = 0.35)]
    public float Frequency { get; set; } = 0.35f;

    [FilterParameter("Shift", "How far bands jump, in percent of the width", MinValue = 1, MaxValue = 40,
        DefaultValue = 12)]
    public int Shift { get; set; } = 12;

    [FilterParameter("Speed", "Speed of the bursts", MinValue = 0.2, MaxValue = 4.0, DefaultValue = 1.0)]
    public float PlaybackSpeed { get; set; } = 1f;

    public string Name => "Glitch";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    private readonly Random _random = new();
    private FrameClock _clock;
    private float _burstLeft;       // seconds of the current burst
    private float _nextChange;      // seconds until the bands are rolled again
    private Band[] _bands = [];
    private byte[] _row = [];

    private readonly record struct Band(int Y, int Height, int Offset, int Split);

    public unsafe SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0 || !ChannelLayout.Supported(source)) return source;
        var bitmap = inPlace ? source : source.Copy();
        var w = bitmap.Width;
        var h = bitmap.Height;
        var intensity = Math.Clamp(Intensity, 0f, 1f);
        var dt = _clock.Tick() * Math.Clamp(PlaybackSpeed, 0.2f, 4f);

        if (_burstLeft <= 0)
        {
            // chance per second to start a burst: 0.2/s .. 6/s
            var rate = 0.2f + 5.8f * Math.Clamp(Frequency, 0f, 1f) * Math.Clamp(Frequency, 0f, 1f);
            if (_random.NextDouble() < rate * dt)
            {
                _burstLeft = 0.08f + (float)_random.NextDouble() * 0.35f;
                _nextChange = 0;
            }
            else return bitmap;
        }

        _burstLeft -= dt;
        _nextChange -= dt;
        if (_nextChange <= 0)
        {
            Roll(w, h, intensity);
            _nextChange = 0.03f + (float)_random.NextDouble() * 0.06f;
        }

        if (_row.Length < w * 4) _row = new byte[w * 4];
        var layout = ChannelLayout.Of(bitmap);
        var stride = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();
        fixed (byte* copy = _row)
        {
            foreach (var band in _bands)
                for (var y = band.Y; y < Math.Min(h, band.Y + band.Height); y++)
                {
                    var line = pixels + y * stride;
                    Buffer.MemoryCopy(line, copy, w * 4, w * 4);
                    for (var x = 0; x < w; x++)
                    {
                        var sx = Wrap(x - band.Offset, w);
                        var rx = Wrap(sx - band.Split, w);
                        var bx = Wrap(sx + band.Split, w);
                        var p = line + x * 4;
                        p[layout.R] = copy[rx * 4 + layout.R];
                        p[layout.G] = copy[sx * 4 + layout.G];
                        p[layout.B] = copy[bx * 4 + layout.B];
                        p[layout.A] = Math.Max(Math.Max(copy[rx * 4 + layout.A], copy[sx * 4 + layout.A]), copy[bx * 4 + layout.A]);
                    }
                }
        }
        return bitmap;
    }

    private void Roll(int w, int h, float intensity)
    {
        var count = 1 + _random.Next(Math.Max(1, (int)(5 * intensity)));
        var maxShift = Math.Max(1, (int)(w * Math.Clamp(Shift, 1, 40) / 100f * intensity));
        var bands = new Band[count];
        for (var i = 0; i < count; i++)
        {
            var height = 1 + _random.Next(Math.Max(1, h / 10));
            bands[i] = new Band(
                _random.Next(h),
                height,
                _random.Next(-maxShift, maxShift + 1),
                _random.Next(0, Math.Max(1, (int)(3 * intensity)) + 1));
        }
        _bands = bands;
    }

    private static int Wrap(int x, int w) => ((x % w) + w) % w;
}
