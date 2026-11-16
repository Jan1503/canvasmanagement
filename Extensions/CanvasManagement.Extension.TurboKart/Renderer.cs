using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

/// <summary>Software renderer: Mode-7 floor, panorama, depth-tested quads/billboards, HUD pixels.</summary>
internal sealed class Renderer
{
    public readonly int W, H;
    public readonly int[] Px;
    public readonly int Scale;
    private readonly float[] _depth;
    private readonly float _focal, _baseHz;
    private float _hz;
    private int _hzRow;
    private readonly int _panW, _panH;
    private readonly int[] _pano;
    private readonly int[]? _panoNight;
    private readonly Weather _weather;
    private float _lastClock, _lastYaw;
    private readonly float[] _colAngle;
    private readonly Sprite[][] _karts;
    private readonly Art.SceneSprites _scene;
    private readonly Track _t;
    private readonly Theme _theme;
    private readonly Vector2[] _mini;
    private readonly int _miniX, _miniY, _miniSize;
    private float _cos, _sin, _camX, _camY, _camH, _clock;

    public Renderer(int w, int h, Track track)
    {
        W = w;
        H = h;
        _t = track;
        _theme = track.Theme;
        Px = new int[w * h];
        _depth = new float[w * h];
        Scale = Math.Max(1, (int)MathF.Round(h / 128f));
        _focal = h * 1.5f;
        _baseHz = MathF.Round(h * 0.36f);
        _hz = _baseHz;
        _panH = _hzRow = (int)_baseHz;
        _panW = (int)MathF.Round(MathF.Tau * _focal);
        _pano = Art.Panorama(_panW, _panH, _theme);
        if (_theme.Kind is ThemeKind.Stadium or ThemeKind.Beach or ThemeKind.Canyon or ThemeKind.Autumn or ThemeKind.Snow)
            _panoNight = Art.Panorama(_panW, _panH, _theme, true);
        _weather = new Weather(w, h, Scale);
        _colAngle = new float[w];
        for (var x = 0; x < w; x++) _colAngle[x] = MathF.Atan((x + 0.5f - w / 2f) / _focal);

        var ppv = Art.KartWorldWidth / 18f * _focal / Race.CamDist;
        _karts = Art.KartSprites(ppv);
        _scene = Art.Scene(_focal / Race.CamDist * 0.5f);

        _miniSize = (int)(h * 0.4f);
        _miniX = w - _miniSize - 3 * Scale;
        _miniY = (int)(h * 0.2f);
        var minX = track.P.Min(p => p.X);
        var maxX = track.P.Max(p => p.X);
        var minY = track.P.Min(p => p.Y);
        var maxY = track.P.Max(p => p.Y);
        var s = (_miniSize - 4) / MathF.Max(maxX - minX, maxY - minY);
        var ox = _miniX + (_miniSize - (maxX - minX) * s) / 2;
        var oy = _miniY + (_miniSize - (maxY - minY) * s) / 2;
        _mini = track.MiniMap.Select(p => new Vector2(ox + (p.X - minX) * s, oy + (p.Y - minY) * s)).ToArray();
        _miniMap = (p => new Vector2(ox + (p.X - minX) * s, oy + (p.Y - minY) * s));
    }

    private readonly Func<Vector2, Vector2> _miniMap;

    public Track Track => _t;

    public Sprite KartSprite(int look, int angle) => _karts[look][((angle % Art.KartAngles) + Art.KartAngles) % Art.KartAngles];

    public void Render(Race race)
    {
        var dt = Math.Clamp(race.Clock - _lastClock, 0f, 0.1f);
        _lastClock = race.Clock;
        _clock = race.Clock;
        var yaw = race.CamYaw + race.ShakeX * 0.004f;
        _cos = MathF.Cos(yaw);
        _sin = MathF.Sin(yaw);
        _camX = race.CamPos.X;
        _camY = race.CamPos.Y;
        _camH = race.CamH;
        _hz = Math.Clamp(_baseHz + (race.CamPitch + race.ShakeY) * Scale, 4, H - 8);
        _hzRow = (int)MathF.Ceiling(_hz - 0.5f);
        var dYaw = Gfx.WrapAngle(yaw - _lastYaw);
        _lastYaw = yaw;
        Array.Fill(_depth, float.MaxValue);
        var rain = race.Weather == WeatherKind.Rain;

        DrawSky(yaw, race);
        DrawFloor(rain);
        DrawSkids(race);

        if (_theme.Lamps) DrawLampGlow(true);
        foreach (var o in race.Oils) DrawOil(o);
        foreach (var k in race.Karts) DrawShadow(k.Pos, k.Z);

        foreach (var q in _t.Quads) DrawQuad(q);
        DrawScenery();
        DrawPickups(race);
        foreach (var k in race.Karts) DrawKart(race, k, rain);
        DrawParticles(race, false);
        DrawSmoke(race);

        Grade(race);

        var night = race.NightLight;
        if (_theme.Lamps) DrawLampGlow(false);
        if (night > 0.3f)
            foreach (var k in race.Karts) DrawHeadlights(k, night);
        DrawParticles(race, true);
        foreach (var o in race.Orbs) DrawOrb(o);
        foreach (var k in race.Karts)
            if (k.Shield > 0) DrawShield(k);

        _weather.Draw(Px, _depth, race.Weather, dt, dYaw * _focal, _hzRow, race.Clock);
        if (race.Lightning > 0) Flash(race.Lightning);
        if (race.Player.Boost > 0 && race.Phase == RacePhase.Racing && !race.Replaying) DrawSpeedLines();
    }

    // ------------------------------------------------------------------ grading + lights

    private static (float r, float g, float b) DayTint(float d)
    {
        if (d <= 0.5f)
        {
            var t = d * 2;
            return (1f, 1f - 0.2f * t, 1f - 0.36f * t);
        }

        var u = (d - 0.5f) * 2;
        return (1f - 0.66f * u, 0.8f - 0.44f * u, 0.64f - 0.1f * u);
    }

    /// <summary>Darkens world pixels for dusk/night and rain; the panorama is blended separately.</summary>
    private void Grade(Race race)
    {
        var (r, g, b) = DayTint(race.Darkness);
        if (race.Weather == WeatherKind.Rain) { r *= 0.8f; g *= 0.82f; b *= 0.88f; }
        if (r > 0.99f && g > 0.99f && b > 0.99f) return;
        var ir = (int)(r * 256);
        var ig = (int)(g * 256);
        var ib = (int)(b * 256);
        Parallel.For(0, H, y =>
        {
            var row = y * W;
            var floor = y >= _hzRow;
            for (var x = 0; x < W; x++)
            {
                var i = row + x;
                if (!floor && _depth[i] == float.MaxValue) continue;
                var c = Px[i];
                Px[i] = unchecked((int)0xFF000000) | ((((c >> 16) & 255) * ir >> 8) << 16) |
                        ((((c >> 8) & 255) * ig >> 8) << 8) | ((c & 255) * ib >> 8);
            }
        });
    }

    private void DrawHeadlights(Kart k, float night)
    {
        if (k.Fall > 0 || !InView(k.Pos, 700, 30)) return;
        var strength = Math.Clamp((night - 0.3f) * 1.6f, 0f, 1f);
        var dir = k.Dir;
        if (k.Z < 1f)
            for (var i = 0; i < 5; i++)
            {
                var p = k.Pos + dir * (6f + i * 6.5f);
                var r = 2.6f + i * 1.7f;
                var a = strength * 0.38f * (1 - i / 6f);
                GroundEllipse(p, r, (c, d) => Gfx.AddBlend(c, Gfx.Rgb(255, 236, 190), (1 - d) * a), true);
            }

        var right = k.Right;
        foreach (var s in new[] { -1f, 1f })
        {
            var front = k.Pos + dir * 4.2f + right * s * 1.8f;
            if (Project(front.X, front.Y, k.Z + 2.2f, out var fx, out var fy, out var ff))
                Glow(fx, fy, MathF.Max(1.2f, 1.3f * _focal / ff), Gfx.Rgb(255, 250, 220), strength * 1.4f, ff - 2);
            var back = k.Pos - dir * 4.4f + right * s * 2.2f;
            if (Project(back.X, back.Y, k.Z + 2.6f, out var bx, out var by, out var bf))
                Glow(bx, by, MathF.Max(1f, (k.Brake ? 1.4f : 0.9f) * _focal / bf), Gfx.Rgb(255, 40, 30),
                    strength * (k.Brake ? 1.6f : 0.9f), bf - 2);
        }
    }

    private void Flash(float amount)
    {
        var a = amount * amount * 0.55f;
        for (var i = 0; i < Px.Length; i++) Px[i] = Gfx.AddBlend(Px[i], Gfx.Rgb(220, 226, 255), a);
    }

    private void DrawSkids(Race race)
    {
        foreach (var s in race.Skids)
        {
            if (s.Size <= 0 || s.Age > Race.SkidLife) continue;
            if (!Project(s.Pos.X, s.Pos.Y, 0, out var sx, out var sy, out var f) || f > 420) continue;
            if (sx < -4 || sx > W + 4 || sy < _hzRow || sy > H + 2) continue;
            var a = (1 - s.Age / Race.SkidLife) * 0.55f;
            var rw = MathF.Max(0.5f, s.Size * _focal / f * 0.5f);
            var rh = MathF.Max(0.5f, rw * _camH / f * 1.6f);
            var x0 = Math.Max(0, (int)(sx - rw));
            var x1 = Math.Min(W - 1, (int)(sx + rw));
            var y0 = Math.Max(_hzRow, (int)(sy - rh));
            var y1 = Math.Min(H - 1, (int)(sy + rh));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var i = y * W + x;
                Px[i] = Gfx.Lerp(Px[i], s.Color, a);
            }
        }
    }

    private void DrawSmoke(Race race)
    {
        foreach (var p in race.Particles)
        {
            if (!p.Smoke) continue;
            if (!Project(p.Pos.X, p.Pos.Y, p.Pos.Z, out var sx, out var sy, out var f) || f > 500) continue;
            var t = 1 - p.Life / p.MaxLife;
            var r = MathF.Max(1f, p.Size * (0.6f + t * 1.4f) * _focal / f * 0.5f);
            var a = (1 - t) * 0.45f;
            var col = Gfx.Lerp(p.Color, p.Color2, t);
            var x0 = Math.Max(0, (int)(sx - r));
            var x1 = Math.Min(W - 1, (int)(sx + r));
            var y0 = Math.Max(0, (int)(sy - r));
            var y1 = Math.Min(H - 1, (int)(sy + r));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var i = y * W + x;
                if (f >= _depth[i]) continue;
                var d = ((x + 0.5f - sx) * (x + 0.5f - sx) + (y + 0.5f - sy) * (y + 0.5f - sy)) / (r * r);
                if (d > 1) continue;
                Px[i] = Gfx.Lerp(Px[i], col, a * (1 - d));
            }
        }
    }

    // ------------------------------------------------------------------ projection

    private bool Project(float wx, float wy, float wz, out float sx, out float sy, out float f)
    {
        var dx = wx - _camX;
        var dy = wy - _camY;
        f = dx * _cos + dy * _sin;
        if (f < 1.5f) { sx = sy = 0; return false; }
        var r = -dx * _sin + dy * _cos;
        sx = W / 2f + r / f * _focal;
        sy = _hz + (_camH - wz) / f * _focal;
        return true;
    }

    private float FogAmount(float z) =>
        z < _theme.FogStart ? 0 : MathF.Min(_theme.FogMax, MathF.Pow((z - _theme.FogStart) / _theme.FogRange, 1.1f));

    private int Fogged(int c, float fog) => fog > 0 ? Gfx.Lerp(c, _theme.Fog, fog) : c;

    private int Lit(int c) => _theme.Ambient >= 1f ? c : Gfx.Shade(c, _theme.Ambient);

    private int OutsideColor(float wx, float wy)
    {
        var stripe = ((((int)wx + (int)wy) >> 5) & 1) == 0;
        return _theme.Kind switch
        {
            ThemeKind.Beach => Track.WaterDeep,
            ThemeKind.Canyon => stripe ? Gfx.Rgb(190, 104, 60) : Gfx.Rgb(180, 96, 56),
            ThemeKind.Autumn => stripe ? Gfx.Rgb(134, 168, 66) : Gfx.Rgb(118, 150, 58),
            ThemeKind.Snow => stripe ? Gfx.Rgb(236, 242, 252) : Gfx.Rgb(220, 230, 248),
            ThemeKind.City => Gfx.Rgb(50, 52, 66),
            ThemeKind.Lava => Gfx.Rgb(230, 86, 20),
            ThemeKind.Space => Gfx.Rgb(6, 4, 16),
            _ => stripe ? Gfx.Rgb(66, 160, 58) : Gfx.Rgb(58, 142, 52)
        };
    }

    // ------------------------------------------------------------------ background + floor

    private void DrawSky(float yaw, Race race)
    {
        var tick = (int)(_clock * 6);
        var baseCol = yaw / MathF.Tau * _panW;
        var night = _panoNight != null ? race.Darkness : 0f;
        var dusk = night > 0 ? (1 - MathF.Abs(night * 2 - 1)) * 0.3f : 0f;
        var rain = race.Weather == WeatherKind.Rain;
        var shift = _hzRow - _panH;
        for (var x = 0; x < W; x++)
        {
            var col = (int)(baseCol + _colAngle[x] / MathF.Tau * _panW);
            col = ((col % _panW) + _panW) % _panW;
            for (var y = 0; y < Math.Min(_hzRow, H); y++)
            {
                var o = Math.Clamp(y - shift, 0, _panH - 1) * _panW + col;
                var c = _pano[o];
                if (((c >> 24) & 255) == Art.CrowdMarker)
                    c = Gfx.Hash(col, y, tick) < 0.14f ? Gfx.Add(c, 70) : c | unchecked((int)0xFF000000);
                if (night > 0.01f) c = Gfx.Lerp(c, _panoNight![o] | unchecked((int)0xFF000000), night);
                if (dusk > 0.01f) c = Gfx.Lerp(c, Gfx.Rgb(255, 130, 70), dusk);
                if (rain) c = Gfx.Lerp(c, Gfx.Rgb(90, 96, 112), 0.45f);
                Px[y * W + x] = c;
            }
        }
    }

    private void DrawFloor(bool rain)
    {
        var mips = _t.Mip;
        var pulse = _clock * 2.4f;
        Parallel.For(_hzRow, H, y =>
        {
            var dy = y + 0.5f - _hz;
            if (dy <= 0.01f) return;
            var z = _camH * _focal / dy;
            var stepX = -_sin * z / _focal;
            var stepY = _cos * z / _focal;
            var wx = _camX + _cos * z + (-W / 2f + 0.5f) * stepX;
            var wy = _camY + _sin * z + (-W / 2f + 0.5f) * stepY;
            var tpp = z / _focal;
            var level = tpp <= 1.3f ? 0 : Math.Min(Track.MipLevels - 1, (int)MathF.Log2(tpp / 1.3f) + 1);
            var tex = mips[level];
            var size = Track.MapSize >> level;
            var fog = FogAmount(z);
            var row = y * W;
            for (var x = 0; x < W; x++, wx += stepX, wy += stepY)
            {
                int c;
                if (wx >= 0 && wy >= 0 && wx < Track.MapSize && wy < Track.MapSize)
                {
                    c = tex[((int)wy >> level) * size + ((int)wx >> level)];
                    var marker = (c >> 24) & 255;
                    if (marker != 255) c = Animate(c, marker, pulse);
                }
                else
                {
                    c = OutsideColor(wx, wy);
                }

                if (rain) c = Gfx.Lerp(c, Gfx.Rgb(70, 80, 100), 0.18f);
                Px[row + x] = Fogged(c, fog);
            }
        });
    }

    private int Animate(int c, int marker, float pulse)
    {
        var r = ((c >> 16) & 255) / 255f;
        var g = ((c >> 8) & 255) / 255f;
        switch (marker)
        {
            case Track.BoostMarker:
            {
                var ph = r - pulse;
                ph -= MathF.Floor(ph);
                return ph < 0.5f ? Gfx.Lerp(Gfx.Rgb(255, 246, 120), Gfx.Rgb(255, 130, 20), ph * 2) : Gfx.Rgb(170, 60, 12);
            }
            case Track.WaterMarker:
            {
                var ph = r - _clock * 0.25f;
                ph -= MathF.Floor(ph);
                var col = Gfx.Lerp(Track.WaterShallow, Track.WaterDeep, g);
                if (g < 0.04f && ph < 0.3f) return Gfx.Rgb(240, 250, 255);
                return ph < 0.07f ? Gfx.Add(col, 60) : col;
            }
            case Track.LavaMarker:
            {
                var ph = r * 3 + _clock * 0.35f;
                var wave = 0.5f + 0.5f * MathF.Sin(ph * MathF.Tau);
                var hot = Math.Clamp(g * 0.6f + wave * 0.5f, 0, 1);
                return hot > 0.75f
                    ? Gfx.Lerp(Gfx.Rgb(255, 170, 40), Gfx.Rgb(255, 240, 150), (hot - 0.75f) * 4)
                    : Gfx.Lerp(Gfx.Rgb(170, 30, 10), Gfx.Rgb(255, 120, 20), hot / 0.75f);
            }
            case Track.StarMarker:
            {
                var tw = 0.5f + 0.5f * MathF.Sin(_clock * 3 + r * 40);
                return Gfx.Lerp(Gfx.Rgb(40, 40, 80), Gfx.Rgb(255, 255, 255), tw);
            }
            default:
                return c | unchecked((int)0xFF000000);
        }
    }

    private void GroundEllipse(Vector2 p, float radius, Func<int, float, int> shader, bool floorOnly = false)
    {
        if (!Project(p.X, p.Y, 0, out var sx, out var sy, out var f)) return;
        if (f > 900) return;
        var rx = radius * _focal / f;
        var ry = MathF.Max(0.8f, _camH * _focal * radius * 0.55f / (f * f));
        var x0 = Math.Max(0, (int)(sx - rx));
        var x1 = Math.Min(W - 1, (int)(sx + rx));
        var y0 = Math.Max(_hzRow, (int)(sy - ry));
        var y1 = Math.Min(H - 1, (int)(sy + ry));
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var u = (x + 0.5f - sx) / rx;
            var v = (y + 0.5f - sy) / ry;
            var d = u * u + v * v;
            if (d > 1) continue;
            var i = y * W + x;
            if (floorOnly && _depth[i] != float.MaxValue) continue;
            Px[i] = shader(Px[i], d);
        }
    }

    private void DrawShadow(Vector2 p, float z)
    {
        var r = 4.6f - MathF.Min(2f, z * 0.15f);
        GroundEllipse(p, r, (c, d) => Gfx.Shade(c, 0.5f + 0.25f * d));
    }

    private void DrawOil(OilSlick o)
    {
        var fade = Math.Clamp((o.Life - o.Age) / 2f, 0, 1);
        var hue = _clock * 60;
        GroundEllipse(o.Pos, 6.5f * (0.4f + 0.6f * Math.Clamp(o.Age * 4, 0, 1)), (c, d) =>
        {
            var oil = d > 0.75f ? Gfx.Rgb(60, 30, 70) : d < 0.18f ? Gfx.Hsv(hue + d * 300, 0.5f, 0.8f) : Gfx.Rgb(24, 14, 34);
            return Gfx.Lerp(c, oil, fade);
        });
    }

    // ------------------------------------------------------------------ quads

    private void DrawQuad(Quad q)
    {
        var ax = q.A.X - _camX;
        var ay = q.A.Y - _camY;
        var bx = q.B.X - _camX;
        var by = q.B.Y - _camY;
        var fa = ax * _cos + ay * _sin;
        var fb = bx * _cos + by * _sin;
        const float near = 2f;
        if (fa < near && fb < near) return;
        if (fa > 1700 && fb > 1700) return;
        var ra = -ax * _sin + ay * _cos;
        var rb = -bx * _sin + by * _cos;
        float ua = q.U0, ub = q.U1;
        if (fa < near)
        {
            var t = (near - fa) / (fb - fa);
            ra += (rb - ra) * t;
            ua += (ub - ua) * t;
            fa = near;
        }
        else if (fb < near)
        {
            var t = (near - fb) / (fa - fb);
            rb += (ra - rb) * t;
            ub += (ua - ub) * t;
            fb = near;
        }

        var xa = W / 2f + ra / fa * _focal;
        var xb = W / 2f + rb / fb * _focal;
        if (xa > xb)
        {
            (xa, xb) = (xb, xa);
            (fa, fb) = (fb, fa);
            (ua, ub) = (ub, ua);
        }

        if (xb < 0 || xa >= W || xb - xa < 0.01f) return;
        var ia = 1f / fa;
        var ib = 1f / fb;
        var uaz = ua * ia;
        var ubz = ub * ib;
        var tex = q.Tex;
        var x0 = Math.Max(0, (int)MathF.Ceiling(xa - 0.5f));
        var x1 = Math.Min(W - 1, (int)MathF.Floor(xb - 0.5f));
        var tick = (int)(_clock * 6);
        for (var x = x0; x <= x1; x++)
        {
            var t = (x + 0.5f - xa) / (xb - xa);
            var iz = ia + (ib - ia) * t;
            var z = 1f / iz;
            var u = (uaz + (ubz - uaz) * t) * z;
            var top = _hz + (_camH - q.Z1) * _focal * iz;
            var bot = _hz + (_camH - q.Z0) * _focal * iz;
            var y0 = Math.Max(0, (int)MathF.Ceiling(top - 0.5f));
            var y1 = Math.Min(H - 1, (int)MathF.Ceiling(bot - 0.5f) - 1);
            if (y1 < y0) continue;
            var tu = (int)MathF.Floor(u) % tex.W;
            if (tu < 0) tu += tex.W;
            var fog = FogAmount(z);
            var span = bot - top;
            for (var y = y0; y <= y1; y++)
            {
                var i = y * W + x;
                if (z >= _depth[i]) continue;
                var tv = Math.Clamp((int)((y + 0.5f - top) / span * tex.H), 0, tex.H - 1);
                var c = tex.Px[tv * tex.W + tu];
                if (c == 0) continue;
                if (((c >> 24) & 255) == Art.CrowdMarker)
                    c = Gfx.Hash(tu, tv, tick) < 0.12f ? Gfx.Add(c, 60) : c | unchecked((int)0xFF000000);
                _depth[i] = z;
                Px[i] = Fogged(c, fog);
            }
        }
    }

    // ------------------------------------------------------------------ billboards

    private void DrawSprite(Sprite s, float wx, float wy, float wz, float extra = 1f, bool flash = false,
        bool lit = false, bool clipGround = false)
    {
        if (!Project(wx, wy, wz, out var sx, out var sy, out var f)) return;
        var groundY = clipGround ? _hz + _camH / f * _focal : float.MaxValue;
        var scale = _focal / f * s.WorldPerPixel * extra;
        var dw = s.W * scale;
        var dh = s.H * scale;
        if (dw < 0.6f || dh < 0.6f) return;
        var left = sx - s.AnchorX * scale;
        var top = sy - s.AnchorY * scale;
        if (left > W || left + dw < 0 || top > H || top + dh < 0) return;
        var x0 = Math.Max(0, (int)MathF.Ceiling(left - 0.5f));
        var x1 = Math.Min(W - 1, (int)MathF.Ceiling(left + dw - 0.5f) - 1);
        var y0 = Math.Max(0, (int)MathF.Ceiling(top - 0.5f));
        var y1 = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Min(top + dh, groundY) - 0.5f) - 1);
        var inv = 1f / scale;
        var fog = FogAmount(f);
        var dim = lit && _theme.Ambient < 1f;
        for (var y = y0; y <= y1; y++)
        {
            var sy2 = Math.Min(s.H - 1, (int)((y + 0.5f - top) * inv));
            var row = sy2 * s.W;
            for (var x = x0; x <= x1; x++)
            {
                var i = y * W + x;
                if (f >= _depth[i]) continue;
                var sx2 = Math.Min(s.W - 1, (int)((x + 0.5f - left) * inv));
                var c = s.Px[row + sx2];
                if (c == 0) continue;
                if (dim) c = Lit(c);
                if (flash) c = Gfx.Add(c, 90);
                _depth[i] = f;
                Px[i] = Fogged(c, fog);
            }
        }
    }

    private bool InView(Vector2 p, float maxDist, float margin)
    {
        var dx = p.X - _camX;
        var dy = p.Y - _camY;
        var f = dx * _cos + dy * _sin;
        if (f < 2 || f > maxDist) return false;
        var r = -dx * _sin + dy * _cos;
        return MathF.Abs(r) < f * (W / 2f) / _focal + margin;
    }

    private void DrawScenery()
    {
        foreach (var s in _t.Scenery)
        {
            if (!InView(s.Pos, 1700, 40)) continue;
            var set = _scene.Kinds[s.Kind];
            var z = s.Z > 0 ? s.Z + MathF.Sin(_clock * 0.6f + s.Pos.X * 0.01f) * 2f : 0f;
            DrawSprite(set[s.Variant % set.Length], s.Pos.X, s.Pos.Y, z, lit: true);
        }
    }

    private void DrawLampGlow(bool ground)
    {
        var right = new Vector2(-_sin, _cos);
        foreach (var s in _t.Scenery)
        {
            if (s.Kind != SceneryKind.Lamp || !InView(s.Pos, 900, 20)) continue;
            foreach (var side in new[] { -1f, 1f })
            {
                var p = s.Pos + right * side * Art.LampHeadX;
                if (ground)
                {
                    GroundEllipse(p, 9f, (c, d) => Gfx.AddBlend(c, Gfx.Rgb(255, 200, 120), (1 - d) * 0.22f));
                    continue;
                }

                if (!Project(p.X, p.Y, Art.LampHeadZ, out var sx, out var sy, out var f)) continue;
                var r = MathF.Max(1.5f, 5f * _focal / f);
                Glow(sx, sy, r, Gfx.Rgb(255, 210, 140), 0.55f, f - 1);
            }
        }
    }

    private void DrawPickups(Race race)
    {
        for (var i = 0; i < race.Boxes.Count; i++)
        {
            var b = race.Boxes[i];
            if (b.Respawn > 0.35f || !InView(b.Pos, 1200, 10)) continue;
            var grow = b.Respawn > 0 ? 1 - b.Respawn / 0.35f : 1f;
            var frame = (int)(_clock * 9 + i * 3) & 15;
            DrawSprite(_scene.Crystal[frame], b.Pos.X, b.Pos.Y, 2.4f + MathF.Sin(_clock * 3 + i) * 0.7f, grow);
        }

        for (var i = 0; i < race.GemPickups.Count; i++)
        {
            var g = race.GemPickups[i];
            if (g.Respawn > 0 || !InView(g.Pos, 900, 6)) continue;
            var frame = (int)(_clock * 10 + i) & 7;
            DrawSprite(_scene.Gem[frame], g.Pos.X, g.Pos.Y, 1.2f + MathF.Sin(_clock * 4 + i) * 0.4f);
        }
    }

    private void DrawKart(Race race, Kart k, bool rain)
    {
        if (!InView(k.Pos, 1400, 20)) return;
        if (k.Invuln > 0 && k.Spin <= 0 && ((int)(_clock * 20) & 1) == 0) return;
        var view = MathF.Atan2(k.Pos.Y - _camY, k.Pos.X - _camX);
        var rho = k.Heading + k.SpinAngle - view;
        if (k.IsPlayer && k.Spin <= 0) rho += k.Steer * 0.16f;
        var idx = (int)MathF.Round(rho / (MathF.Tau / Art.KartAngles));
        idx = ((idx % Art.KartAngles) + Art.KartAngles) % Art.KartAngles;
        var bob = k.Offroad && k.Speed > 30 ? MathF.Abs(MathF.Sin(_clock * 38 + k.Id)) * 0.5f : 0f;
        DrawSprite(_karts[k.Look][idx], k.Pos.X, k.Pos.Y, k.Z + bob, 1f, k.Spin > 0 && ((int)(_clock * 16) & 1) == 0,
            lit: true, clipGround: k.Fall > 0);
    }

    private void DrawParticles(Race race, bool glow)
    {
        foreach (var p in race.Particles)
        {
            if (p.Glow != glow) continue;
            if (!Project(p.Pos.X, p.Pos.Y, p.Pos.Z, out var sx, out var sy, out var f)) continue;
            if (f > 900) continue;
            var t = 1 - p.Life / p.MaxLife;
            var c = Gfx.Lerp(p.Color, p.Color2, t);
            if (glow)
            {
                var radius = MathF.Max(1f, p.Size * 0.5f * _focal / f * (1f - t * 0.5f));
                Glow(sx, sy, radius + 0.5f, c, 1.3f * (1 - t * 0.6f), f);
                continue;
            }

            var size = Math.Clamp(p.Size * 0.6f * _focal / f, 1f, 2f * Scale);
            var half = size / 2f;
            var x0 = Math.Max(0, (int)(sx - half));
            var x1 = Math.Min(W - 1, (int)(sx + half));
            var y0 = Math.Max(0, (int)(sy - half));
            var y1 = Math.Min(H - 1, (int)(sy + half));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var i = y * W + x;
                if (f >= _depth[i]) continue;
                Px[i] = c;
                _depth[i] = f;
            }
        }
    }

    private void Glow(float sx, float sy, float radius, int color, float strength, float depth)
    {
        var x0 = Math.Max(0, (int)(sx - radius));
        var x1 = Math.Min(W - 1, (int)(sx + radius));
        var y0 = Math.Max(0, (int)(sy - radius));
        var y1 = Math.Min(H - 1, (int)(sy + radius));
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var i = y * W + x;
            if (depth >= _depth[i]) continue;
            var d = MathF.Sqrt((x + 0.5f - sx) * (x + 0.5f - sx) + (y + 0.5f - sy) * (y + 0.5f - sy)) / radius;
            if (d > 1) continue;
            Px[i] = Gfx.AddBlend(Px[i], color, (1 - d) * (1 - d) * strength);
        }
    }

    private void DrawOrb(Orb o)
    {
        if (!Project(o.Pos.X, o.Pos.Y, 2.6f, out var sx, out var sy, out var f)) return;
        var r = MathF.Max(1.5f, 2.6f * _focal / f);
        Glow(sx, sy, r * 2.2f, Gfx.Rgb(40, 120, 255), 0.9f, f);
        Glow(sx, sy, r, Gfx.Rgb(140, 240, 255), 1.4f, f);
        Glow(sx, sy, r * 0.45f, Gfx.Rgb(255, 255, 255), 2f, f);
    }

    private void DrawShield(Kart k)
    {
        if (!Project(k.Pos.X, k.Pos.Y, k.Z + 3.2f, out var sx, out var sy, out var f)) return;
        var r = 5.8f * _focal / f;
        var fade = k.Shield < 1.5f && ((int)(_clock * 12) & 1) == 0 ? 0.3f : 1f;
        var x0 = Math.Max(0, (int)(sx - r - 1));
        var x1 = Math.Min(W - 1, (int)(sx + r + 1));
        var y0 = Math.Max(0, (int)(sy - r - 1));
        var y1 = Math.Min(H - 1, (int)(sy + r + 1));
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var dx = x + 0.5f - sx;
            var dy = y + 0.5f - sy;
            var d = MathF.Sqrt(dx * dx + dy * dy) / r;
            if (d > 1) continue;
            var i = y * W + x;
            var hue = 180 + MathF.Atan2(dy, dx) * 40 + _clock * 120;
            var col = Gfx.Hsv(hue, 0.55f, 1f);
            var k2 = d > 0.82f ? 0.75f : 0.1f + d * d * 0.2f;
            if (dx < -r * 0.3f && dy < -r * 0.3f && d > 0.5f && d < 0.7f) k2 = 0.9f;
            Px[i] = Gfx.AddBlend(Px[i], col, k2 * fade);
        }
    }

    private void DrawSpeedLines()
    {
        var frame = (int)(_clock * 30);
        for (var n = 0; n < 7; n++)
        {
            var h = Gfx.Hash(n, frame, 3);
            var left = (n & 1) == 0;
            var y = (int)(_hz + 4 + h * (H - _hz - 8));
            var len = (int)(6 + Gfx.Hash(n, frame, 8) * 14) * Scale;
            var xs = left ? (int)(Gfx.Hash(n, frame, 5) * W * 0.18f) : W - 1 - (int)(Gfx.Hash(n, frame, 5) * W * 0.18f);
            for (var i = 0; i < len; i++)
            {
                var x = left ? xs + i : xs - i;
                if ((uint)x >= (uint)W || (uint)y >= (uint)H) continue;
                Px[y * W + x] = Gfx.AddBlend(Px[y * W + x], Gfx.Rgb(255, 255, 255), 0.5f * (1 - i / (float)len));
            }
        }
    }

    // ------------------------------------------------------------------ HUD pixels

    private static readonly string[] IconNitro =
    {
        "....kkkk....",
        "...kwwwwk...",
        "..kkkkkkkk..",
        "..krrwrrrk..",
        "..krwwrrrk..",
        "..krwrrrrk..",
        "..kyyyyyyk..",
        "..krrrrrrk..",
        "..krrrrrrk..",
        "..kkkkkkkk..",
        "...koyyok...",
        "....kook...."
    };

    private static readonly string[] IconOil =
    {
        ".....kk.....",
        "....kddk....",
        "....kddk....",
        "...kddddk...",
        "...kdwddk...",
        "..kddwdddk..",
        "..kdwddddk..",
        "..kddddddk..",
        "..kddddddk..",
        "...kddddk...",
        "....kkkk....",
        "............"
    };

    private static readonly string[] IconOrb =
    {
        "....kkkk....",
        "..kkbbbbkk..",
        ".kbbccccbbk.",
        ".kbcwwccbbk.",
        "kbccwcccccbk",
        "kbcccccccbbk",
        "kbcccccccbbk",
        "kbbcccccbbbk",
        ".kbbcccbbbk.",
        ".kbbbbbbbbk.",
        "..kkbbbbkk..",
        "....kkkk...."
    };

    private static readonly string[] IconShield =
    {
        "....kkkk....",
        "..kkaaaakk..",
        ".kaaffffaak.",
        ".kafwwfffak.",
        "kafwffffffak",
        "kaffffffffak",
        "kaffffffffak",
        "kaffffffffak",
        ".kaffffffak.",
        ".kaaffffaak.",
        "..kkaaaakk..",
        "....kkkk...."
    };

    private static readonly string[] IconGem =
    {
        "..kkk..",
        ".kwcck.",
        "kwccbck",
        "kccbbck",
        ".kcbck.",
        "..kck..",
        "...k..."
    };

    private static readonly string[] IconFlag =
    {
        "gwkwkwk",
        "gkwkwkw",
        "gwkwkwk",
        "gkwkwkw",
        "g......",
        "g......"
    };

    private static int IconColor(char ch) => ch switch
    {
        'k' => Gfx.Rgb(16, 16, 24),
        'w' => Gfx.Rgb(255, 255, 255),
        'r' => Gfx.Rgb(232, 44, 40),
        'y' => Gfx.Rgb(255, 214, 40),
        'o' => Gfx.Rgb(255, 130, 20),
        'd' => Gfx.Rgb(60, 30, 90),
        'b' => Gfx.Rgb(30, 90, 220),
        'c' => Gfx.Rgb(110, 230, 255),
        'a' => Gfx.Rgb(120, 220, 255),
        'f' => Gfx.Argb(110, 70, 150, 230),
        'g' => Gfx.Rgb(200, 200, 210),
        _ => 0
    };

    private void Blit(string[] rows, int x0, int y0, int px, Func<char, int>? palette = null)
    {
        palette ??= IconColor;
        for (var y = 0; y < rows.Length; y++)
        for (var x = 0; x < rows[y].Length; x++)
        {
            var c = palette(rows[y][x]);
            if (c == 0) continue;
            var a = (c >> 24) & 255;
            for (var sy = 0; sy < px; sy++)
            for (var sx = 0; sx < px; sx++)
            {
                var tx = x0 + x * px + sx;
                var ty = y0 + y * px + sy;
                if ((uint)tx >= (uint)W || (uint)ty >= (uint)H) continue;
                var i = ty * W + tx;
                Px[i] = a == 255 ? c : Gfx.Lerp(Px[i], c, a / 255f);
            }
        }
    }

    private void FillRect(int x0, int y0, int w, int h, int c, float alpha = 1f)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(H, y0 + h); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(W, x0 + w); x++)
        {
            var i = y * W + x;
            Px[i] = alpha >= 1 ? c : Gfx.Lerp(Px[i], c, alpha);
        }
    }

    private void Frame(int x0, int y0, int w, int h, int c)
    {
        FillRect(x0, y0, w, 1, c);
        FillRect(x0, y0 + h - 1, w, 1, c);
        FillRect(x0, y0, 1, h, c);
        FillRect(x0 + w - 1, y0, 1, h, c);
    }

    public static string[] IconFor(ItemKind k) => k switch
    {
        ItemKind.Nitro => IconNitro,
        ItemKind.Oil => IconOil,
        ItemKind.Orb => IconOrb,
        ItemKind.Shield => IconShield,
        _ => Array.Empty<string>()
    };

    /// <summary>Pixel HUD; text is drawn afterwards with BDF by the extension.</summary>
    public void DrawHud(Race race, HudLayout l)
    {
        if (race.Replaying)
        {
            var bar = H / 9;
            FillRect(0, 0, W, bar, Gfx.Rgb(0, 0, 0));
            FillRect(0, H - bar, W, bar, Gfx.Rgb(0, 0, 0));
            return;
        }

        var p = race.Player;
        var s = Scale;

        // Item window
        var box = 20 * s;
        FillRect(l.ItemX, l.ItemY, box, box, Gfx.Rgb(10, 14, 40), 0.6f);
        Frame(l.ItemX, l.ItemY, box, box, Gfx.Rgb(255, 255, 255));
        Frame(l.ItemX + 1, l.ItemY + 1, box - 2, box - 2, Gfx.Rgb(90, 170, 255));
        var icon = p.RollTime > 0
            ? IconFor((ItemKind)((int)(race.Clock * 14) % 4 + 1))
            : IconFor(p.Item);
        if (icon.Length > 0) Blit(icon, l.ItemX + 4 * s, l.ItemY + 4 * s, s);

        // Gem + lap icons
        Blit(IconGem, l.GemX, l.GemY, s);
        Blit(IconFlag, l.LapX, l.LapY, s);

        DrawMiniMap(race);

        if (race.Phase == RacePhase.Countdown || race.Phase == RacePhase.Racing && race.PhaseTime < 1f)
            DrawLights(race);

        if (race.Phase == RacePhase.Results) FillRect(0, 0, W, H, Gfx.Rgb(6, 8, 30), 0.62f);
    }

    private void DrawMiniMap(Race race)
    {
        var s = Scale;
        for (var pass = 0; pass < 2; pass++)
        {
            var r = pass == 0 ? 1 + s : s - 1 + (s == 1 ? 0 : 0);
            var c = pass == 0 ? Gfx.Rgb(16, 24, 70) : Gfx.Rgb(150, 214, 255);
            for (var i = 0; i < _mini.Length; i++)
            {
                var a = _mini[i];
                var b = _mini[(i + 1) % _mini.Length];
                var steps = (int)MathF.Ceiling(Vector2.Distance(a, b)) + 1;
                for (var k = 0; k <= steps; k++)
                {
                    var q = Vector2.Lerp(a, b, k / (float)steps);
                    FillRect((int)q.X - r, (int)q.Y - r, r * 2 + 1, r * 2 + 1, c);
                }
            }
        }

        var start = _miniMap(_t.P[0]);
        FillRect((int)start.X - s, (int)start.Y - s, 2 * s + 1, 2 * s + 1, Gfx.Rgb(255, 255, 255));

        foreach (var k in race.Karts.OrderBy(k => k.IsPlayer))
        {
            var m = _miniMap(k.Pos);
            var col = Art.Racers[k.Look].Body;
            if (k.IsPlayer)
            {
                var blink = ((int)(race.Clock * 4) & 1) == 0;
                FillRect((int)m.X - 2 * s, (int)m.Y - 2 * s, 4 * s + 1, 4 * s + 1, blink ? Gfx.Rgb(255, 255, 255) : Gfx.Rgb(20, 20, 30));
                FillRect((int)m.X - s, (int)m.Y - s, 2 * s + 1, 2 * s + 1, col);
            }
            else
            {
                FillRect((int)m.X - s, (int)m.Y - s, 2 * s + 1, 2 * s + 1, Gfx.Rgb(16, 16, 24));
                FillRect((int)m.X - s + 1, (int)m.Y - s + 1, Math.Max(1, 2 * s - 1), Math.Max(1, 2 * s - 1), col);
            }
        }
    }

    private void DrawLights(Race race)
    {
        var s = Scale;
        var r = 4 * s;
        var gap = 3 * s;
        var total = 3 * (2 * r + 1) + 2 * gap + 6 * s;
        var x0 = (W - total) / 2;
        var y0 = 6 * s;
        FillRect(x0, y0, total, 2 * r + 1 + 6 * s, Gfx.Rgb(20, 20, 28));
        Frame(x0, y0, total, 2 * r + 1 + 6 * s, Gfx.Rgb(120, 120, 140));
        var go = race.Phase == RacePhase.Racing;
        var lit = go ? 3 : Math.Min(3, (int)race.PhaseTime + 1);
        for (var i = 0; i < 3; i++)
        {
            var cx = x0 + 3 * s + r + i * (2 * r + 1 + gap);
            var cy = y0 + 3 * s + r;
            var on = i < lit;
            var col = go ? Gfx.Rgb(60, 255, 90) : on ? Gfx.Rgb(255, 40, 40) : Gfx.Rgb(70, 20, 20);
            for (var y = -r; y <= r; y++)
            for (var x = -r; x <= r; x++)
            {
                if (x * x + y * y > r * r) continue;
                var c = col;
                if (on && x < 0 && y < 0 && x * x + y * y < r * r / 4) c = Gfx.Add(col, 80);
                FillRect(cx + x, cy + y, 1, 1, c);
            }

            if (on) Glow(cx + 0.5f, cy + 0.5f, r * 2.2f, col, 0.5f, -1);
        }
    }
}

internal readonly record struct HudLayout(int ItemX, int ItemY, int GemX, int GemY, int LapX, int LapY);
