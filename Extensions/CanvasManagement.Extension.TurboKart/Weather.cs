namespace CanvasManagement.Extension.TurboKart;

public enum TurboKartWeather { Auto, Clear, Rain, Snow }

/// <summary>Screen-space precipitation; shifts with camera yaw so it feels anchored to the world.</summary>
internal sealed class Weather
{
    private readonly int _w, _h, _s;
    private readonly float[] _x, _y, _z;
    private readonly Random _rnd = new(3);

    public Weather(int w, int h, int scale)
    {
        _w = w;
        _h = h;
        _s = scale;
        var n = Math.Max(60, w * h / 300);
        _x = new float[n];
        _y = new float[n];
        _z = new float[n];
        for (var i = 0; i < n; i++)
        {
            _x[i] = (float)_rnd.NextDouble() * w;
            _y[i] = (float)_rnd.NextDouble() * h;
            _z[i] = 0.3f + (float)_rnd.NextDouble() * 0.7f;
        }
    }

    public void Draw(int[] px, float[] depth, WeatherKind kind, float dt, float yawShift, int hzRow, float clock)
    {
        if (kind == WeatherKind.Clear) return;
        var count = kind == WeatherKind.Embers ? _x.Length / 3 : kind == WeatherKind.Snow ? _x.Length * 2 / 3 : _x.Length;
        for (var i = 0; i < count; i++)
        {
            var z = _z[i];
            _x[i] -= yawShift * z;
            switch (kind)
            {
                case WeatherKind.Rain:
                    _y[i] += 300f * z * _s * dt;
                    _x[i] -= 60f * z * _s * dt;
                    break;
                case WeatherKind.Snow:
                    _y[i] += 22f * z * _s * dt;
                    _x[i] += MathF.Sin(clock * 1.5f + i) * 10f * _s * dt;
                    break;
                default:
                    _y[i] -= 18f * z * _s * dt;
                    _x[i] += MathF.Sin(clock * 2f + i) * 8f * _s * dt;
                    break;
            }

            if (_y[i] > _h) { _y[i] -= _h; _x[i] = (float)_rnd.NextDouble() * _w; }
            if (_y[i] < 0) { _y[i] += _h; _x[i] = (float)_rnd.NextDouble() * _w; }
            _x[i] = ((_x[i] % _w) + _w) % _w;

            var x = (int)_x[i];
            var y = (int)_y[i];
            switch (kind)
            {
                case WeatherKind.Rain:
                {
                    var len = (int)(3 + 5 * z) * _s;
                    for (var k = 0; k < len; k++)
                        Blend(px, x + k / 4, y - k, Gfx.Rgb(180, 196, 230), 0.35f * z);
                    if (y > hzRow && _rnd.NextDouble() < 0.04)
                    {
                        Blend(px, x - 1, y, Gfx.Rgb(230, 240, 255), 0.6f);
                        Blend(px, x + 1, y, Gfx.Rgb(230, 240, 255), 0.6f);
                    }

                    break;
                }
                case WeatherKind.Snow:
                {
                    var size = z > 0.75f ? 2 * _s : _s;
                    for (var sy = 0; sy < size; sy++)
                    for (var sx = 0; sx < size; sx++)
                        Blend(px, x + sx, y + sy, Gfx.Rgb(255, 255, 255), 0.55f + 0.45f * z);
                    break;
                }
                default:
                {
                    var flick = 0.5f + 0.5f * MathF.Sin(clock * 9 + i);
                    var c = flick > 0.5f ? Gfx.Rgb(255, 200, 80) : Gfx.Rgb(255, 110, 30);
                    var o = y * _w + x;
                    if ((uint)x < (uint)_w && (uint)y < (uint)_h) px[o] = Gfx.AddBlend(px[o], c, 0.8f * z);
                    break;
                }
            }
        }
    }

    private void Blend(int[] px, int x, int y, int c, float a)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        var o = y * _w + x;
        px[o] = Gfx.Lerp(px[o], c, a);
    }
}
