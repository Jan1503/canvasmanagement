namespace CanvasManagement.Extension.TurboKart;

/// <summary>Theme textures for vertical quads and the 360° panoramas.</summary>
internal static partial class Art
{
    public const int FlowMarker = 0xF9;
    public const int NeonMarker = 0xF8;

    // ------------------------------------------------------------------ ramps

    public static Tex RampSideTexture()
    {
        var t = new Tex(24, 8);
        for (var x = 0; x < t.W; x++)
        {
            var top = (int)MathF.Round(t.H * (1 - (x + 1) / (float)t.W));
            for (var y = top; y < t.H; y++)
            {
                var stripe = ((x + y) / 3 & 1) == 0;
                var c = stripe ? Gfx.Rgb(250, 196, 30) : Gfx.Rgb(36, 36, 46);
                if (y == top) c = Gfx.Rgb(255, 255, 255);
                t.Set(x, y, c);
            }
        }

        return t;
    }

    public static Tex RampFaceTexture()
    {
        var t = new Tex(68, 8);
        for (var x = 0; x < t.W; x++)
        for (var y = 0; y < t.H; y++)
        {
            var c = ((x + y) / 4 & 1) == 0 ? Gfx.Rgb(250, 196, 30) : Gfx.Rgb(36, 36, 46);
            if (y == 0) c = Gfx.Rgb(255, 255, 255);
            if (y == t.H - 1) c = Gfx.Rgb(20, 20, 26);
            t.Set(x, y, c);
        }

        return t;
    }

    // ------------------------------------------------------------------ walls

    public static Tex WallTexture(ThemeKind kind)
    {
        switch (kind)
        {
            case ThemeKind.Beach:
            {
                var t = new Tex(32, 7);
                for (var x = 0; x < t.W; x++)
                {
                    t.Set(x, 0, Gfx.Rgb(255, 255, 255));
                    for (var y = 1; y < 6; y++)
                        t.Set(x, y, ((x + y) / 4 & 1) == 0 ? Gfx.Rgb(30, 120, 230) : Gfx.Rgb(250, 250, 250));
                    t.Set(x, 6, Gfx.Rgb(20, 40, 80));
                }

                return t;
            }
            case ThemeKind.Canyon:
            {
                var t = new Tex(32, 8);
                for (var x = 0; x < t.W; x++)
                for (var y = 0; y < 8; y++)
                {
                    var tx = x % 8;
                    var row = y / 4;
                    var ly = y % 4;
                    var painted = (x / 8 + row) % 3 == 0;
                    var rim = ly == 0 || ly == 3 || tx == 0;
                    var c = painted ? (x / 8 % 2 == 0 ? Gfx.Rgb(230, 40, 40) : Gfx.Rgb(245, 245, 245)) : Gfx.Rgb(46, 46, 52);
                    if (rim) c = Gfx.Rgb(20, 20, 24);
                    else if (tx is 3 or 4 && ly is 1 or 2) c = Gfx.Rgb(14, 14, 16);
                    t.Set(x, y, c);
                }

                return t;
            }
            case ThemeKind.Autumn:
            {
                var t = new Tex(32, 10);
                for (var x = 0; x < t.W; x++)
                for (var y = 0; y < 10; y++)
                {
                    var post = x % 16 < 2;
                    var plank = y is >= 1 and < 4 || y is >= 6 and < 9;
                    if (post) t.Set(x, y, x % 16 == 0 ? Gfx.Rgb(120, 76, 40) : Gfx.Rgb(84, 50, 26));
                    else if (plank) t.Set(x, y, y is 1 or 6 ? Gfx.Rgb(200, 150, 96) : Gfx.Rgb(166, 116, 66));
                }

                return t;
            }
            case ThemeKind.Snow:
            {
                var t = new Tex(64, 10);
                for (var x = 0; x < t.W; x++)
                {
                    var top = (int)(2 + 2 * MathF.Sin(x * 0.3f) + 1.2f * MathF.Sin(x * 0.77f));
                    for (var y = Math.Max(0, top); y < 10; y++)
                    {
                        var c = Gfx.Lerp(Gfx.Rgb(252, 254, 255), Gfx.Rgb(170, 192, 232), (y - top) / 8f);
                        if (y == top) c = Gfx.Rgb(255, 255, 255);
                        t.Set(x, y, c);
                    }
                }

                return t;
            }
            case ThemeKind.City:
            {
                var t = new Tex(32, 10);
                for (var x = 0; x < t.W; x++)
                for (var y = 0; y < 10; y++)
                {
                    var c = Gfx.Lerp(Gfx.Rgb(170, 170, 180), Gfx.Rgb(100, 100, 114), y / 9f);
                    if (y is 3 or 4) c = x % 16 < 12 ? Gfx.Rgb(40, 250, 255) : Gfx.Rgb(10, 90, 110);
                    if (x % 16 == 0) c = Gfx.Shade(c, 0.6f);
                    if (y == 9) c = Gfx.Rgb(30, 30, 40);
                    t.Set(x, y, c);
                }

                return t;
            }
            default:
            {
                var t = new Tex(64, 10);
                var purple = Gfx.Rgb(124, 58, 214);
                var blue = Gfx.Rgb(36, 108, 232);
                for (var x = 0; x < t.W; x++)
                {
                    t.Set(x, 0, Gfx.Rgb(255, 255, 255));
                    t.Set(x, 1, Gfx.Rgb(214, 214, 226));
                    var panel = x < 32 ? purple : blue;
                    var lx = x % 32;
                    for (var y = 2; y < 8; y++)
                    {
                        var c = Gfx.Shade(panel, 1.12f - (y - 2) * 0.06f);
                        if (lx == 0) c = Gfx.Shade(panel, 0.55f);
                        if (y is 4 or 5 && lx is >= 8 and < 24 && (lx & 3) != 3) c = Gfx.Rgb(238, 238, 255);
                        t.Set(x, y, c);
                    }

                    t.Set(x, 8, Gfx.Rgb(40, 34, 70));
                    t.Set(x, 9, Gfx.Rgb(24, 20, 40));
                }

                return t;
            }
        }
    }

    // ------------------------------------------------------------------ facades

    public static Tex FacadeTexture(ThemeKind kind) => kind switch
    {
        ThemeKind.City => CityFacade(),
        ThemeKind.Lava => CastleFacade(),
        _ => CliffFacade()
    };

    private static Tex CityFacade()
    {
        var t = new Tex(512, 220);
        var rnd = new Random(5);
        var signs = new[] { "HOTEL", "KART", "24H", "NEON", "BAR", "ARCADE" };
        var x0 = 0;
        var b = 0;
        while (x0 < t.W)
        {
            var w = Math.Min(t.W - x0, 46 + rnd.Next(50));
            var h = 110 + rnd.Next(110);
            var top = t.H - h;
            var body = Gfx.Rgb(26 + rnd.Next(20), 26 + rnd.Next(16), 46 + rnd.Next(30));
            var lit = new[] { Gfx.Rgb(255, 220, 120), Gfx.Rgb(140, 220, 255), Gfx.Rgb(255, 150, 220) }[rnd.Next(3)];
            for (var y = top; y < t.H; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                var lx = x - x0;
                var ly = y - top;
                var c = body;
                if (ly < 2) c = Gfx.Shade(body, 1.6f);
                else if (lx == 0 || lx == w - 1) c = Gfx.Shade(body, 0.6f);
                else if (y >= t.H - 14)
                    c = y < t.H - 12 ? Gfx.Rgb(255, 60, 180) : lx % 12 < 9 ? Gfx.Rgb(250, 230, 170) : Gfx.Rgb(40, 30, 40);
                else if (lx % 6 is >= 2 and < 5 && ly % 8 is >= 3 and < 7)
                    c = Gfx.Hash(x / 6, y / 8, b) < 0.45f ? lit : Gfx.Rgb(16, 18, 30);
                t.Set(x, y, c);
            }

            if (rnd.NextDouble() < 0.6 && w > 40)
            {
                var word = signs[rnd.Next(signs.Length)];
                var sw = TinyFont.Measure(word, 1);
                var sx = x0 + (w - sw) / 2;
                var sy = top + 8 + rnd.Next(30);
                var neon = new[] { Gfx.Rgb(255, 60, 200), Gfx.Rgb(40, 250, 255), Gfx.Rgb(255, 220, 40) }[rnd.Next(3)];
                t.Fill(sx - 3, sy - 3, sw + 6, 13, Gfx.Rgb(14, 10, 24));
                TinyFont.Paint(t.Px, t.W, t.H, word, sx, sy, 1, (neon & 0xFFFFFF) | (NeonMarker << 24));
            }

            if (rnd.NextDouble() < 0.4) t.Set(x0 + w / 2, top - 1, Gfx.Rgb(255, 40, 40));
            x0 += w;
            b++;
        }

        return t;
    }

    private static Tex CliffFacade()
    {
        var t = new Tex(256, 140);
        var strata = new[]
        {
            Gfx.Rgb(196, 98, 56), Gfx.Rgb(214, 124, 70), Gfx.Rgb(178, 82, 48), Gfx.Rgb(226, 146, 88),
            Gfx.Rgb(160, 70, 42)
        };
        for (var x = 0; x < t.W; x++)
        {
            var th = x / (float)t.W * MathF.Tau;
            var top = (int)(14 + 10 * MathF.Sin(th * 2 + 1) + 6 * MathF.Sin(th * 5) + 3 * MathF.Sin(th * 13));
            for (var y = Math.Max(0, top); y < t.H; y++)
            {
                var band = (int)((y + 6 * MathF.Sin(th * 3)) / 9) % strata.Length;
                var c = strata[band];
                var crack = MathF.Abs(MathF.Sin(x * 0.21f + MathF.Sin(y * 0.05f) * 2)) < 0.05f;
                if (crack) c = Gfx.Shade(c, 0.6f);
                c = Gfx.Add(c, (int)(Gfx.Hash(x / 2, y / 2, 4) * 16 - 8));
                if (y - top < 2) c = Gfx.Rgb(238, 170, 110);
                var fall = MathF.Abs(x - 70) < 4 + y * 0.03f || MathF.Abs(x - 196) < 3 + y * 0.03f;
                if (fall && y > top + 3) c = Gfx.Argb(FlowMarker, Gfx.R(c), Gfx.G(c), Gfx.B(c));
                if (fall && y > t.H - 8) c = Gfx.Rgb(236, 246, 255);
                t.Set(x, y, c);
            }
        }

        return t;
    }

    private static Tex CastleFacade()
    {
        var t = new Tex(128, 84);
        for (var x = 0; x < t.W; x++)
        for (var y = 0; y < t.H; y++)
        {
            if (y < 8 && x % 16 >= 9) continue;
            var row = y / 6;
            var bx = (x + (row % 2) * 6) % 12;
            var mortar = y % 6 == 0 || bx == 0;
            var c = mortar ? Gfx.Rgb(34, 30, 34) : Gfx.Add(Gfx.Rgb(78, 72, 80), (int)(Gfx.Hash(x / 12, row, 2) * 26 - 13));
            if (!mortar && y % 6 == 1) c = Gfx.Add(c, 14);
            t.Set(x, y, c);
        }

        foreach (var tx in new[] { 30, 94 })
        {
            t.Fill(tx - 1, 34, 3, 10, Gfx.Rgb(60, 40, 30));
            t.Fill(tx - 2, 28, 5, 6, Gfx.Rgb(255, 140, 30));
            t.Fill(tx - 1, 26, 3, 4, Gfx.Rgb(255, 230, 120));
        }

        t.Fill(60, 24, 6, 18, Gfx.Rgb(20, 8, 8));
        t.Fill(61, 26, 4, 14, Gfx.Rgb(200, 60, 20));
        return t;
    }

    // ------------------------------------------------------------------ start arch

    private readonly record struct ArchColors(int A, int B, int Edge, int Text, int Outline);

    private static ArchColors Arch(ThemeKind kind) => kind switch
    {
        ThemeKind.Beach => new(Gfx.Rgb(80, 220, 240), Gfx.Rgb(30, 130, 230), Gfx.Rgb(250, 250, 250), Gfx.Rgb(255, 255, 255), Gfx.Rgb(20, 60, 140)),
        ThemeKind.Canyon => new(Gfx.Rgb(250, 180, 90), Gfx.Rgb(200, 100, 50), Gfx.Rgb(90, 44, 24), Gfx.Rgb(255, 244, 220), Gfx.Rgb(90, 30, 10)),
        ThemeKind.Autumn => new(Gfx.Rgb(220, 60, 40), Gfx.Rgb(150, 30, 24), Gfx.Rgb(250, 200, 50), Gfx.Rgb(255, 220, 60), Gfx.Rgb(80, 10, 10)),
        ThemeKind.Snow => new(Gfx.Rgb(250, 252, 255), Gfx.Rgb(180, 210, 250), Gfx.Rgb(40, 110, 230), Gfx.Rgb(30, 90, 210), Gfx.Rgb(255, 255, 255)),
        ThemeKind.City => new(Gfx.Rgb(60, 20, 90), Gfx.Rgb(24, 10, 40), Gfx.Rgb(255, 50, 190), Gfx.Rgb(60, 250, 255), Gfx.Rgb(10, 10, 30)),
        ThemeKind.Lava => new(Gfx.Rgb(70, 62, 66), Gfx.Rgb(40, 34, 38), Gfx.Rgb(255, 120, 30), Gfx.Rgb(255, 150, 40), Gfx.Rgb(30, 10, 6)),
        ThemeKind.Space => new(Gfx.Rgb(30, 20, 80), Gfx.Rgb(10, 6, 30), Gfx.Rgb(60, 240, 255), Gfx.Rgb(255, 90, 230), Gfx.Rgb(10, 0, 20)),
        _ => new(Gfx.Rgb(255, 214, 50), Gfx.Rgb(255, 150, 26), Gfx.Rgb(214, 34, 40), Gfx.Rgb(214, 30, 40), Gfx.Rgb(255, 255, 255))
    };

    public static Tex PillarTexture(ThemeKind kind)
    {
        var col = Arch(kind);
        var t = new Tex(16, 68);
        for (var y = 0; y < t.H; y++)
        for (var x = 0; x < t.W; x++)
        {
            var g = Gfx.Lerp(col.A, col.B, y / (float)t.H);
            if (x < 2 || x > 13 || y < 4) g = col.Edge;
            if (x == 0 || x == 15) g = Gfx.Shade(col.Edge, 0.6f);
            t.Set(x, y, g);
        }

        var words = new[] { "GP", "TK", "GO" };
        for (var i = 0; i < 3; i++)
        {
            var y0 = 10 + i * 18;
            t.Fill(2, y0, 12, 11, col.Outline);
            t.Fill(2, y0, 12, 1, col.Edge);
            t.Fill(2, y0 + 10, 12, 1, col.Edge);
            TinyFont.Paint(t.Px, t.W, t.H, words[i], 3, y0 + 2, 1, col.Text);
        }

        return t;
    }

    public static Tex BannerTexture(ThemeKind kind, string text)
    {
        var col = Arch(kind);
        var t = new Tex(240, 40);
        for (var y = 0; y < t.H; y++)
        for (var x = 0; x < t.W; x++)
        {
            int c;
            if (y < 6 || y >= t.H - 6)
            {
                var row = y < 6 ? y / 3 : (y - (t.H - 6)) / 3;
                c = ((x / 3 + row) & 1) == 0 ? Gfx.Rgb(250, 250, 250) : Gfx.Rgb(20, 20, 26);
            }
            else if (y is 6 or 33)
            {
                c = col.Edge;
            }
            else
            {
                c = Gfx.Lerp(col.A, col.B, (y - 7) / 26f);
                if (x < 2 || x >= t.W - 2) c = col.Edge;
            }

            t.Set(x, y, c);
        }

        var scale = TinyFont.Measure(text, 2) <= t.W - 8 ? 2 : 1;
        var w = TinyFont.Measure(text, scale);
        TinyFont.Paint(t.Px, t.W, t.H, text, (t.W - w) / 2, 20 - TinyFont.GlyphH * scale / 2, scale, col.Text,
            col.Outline);
        return t;
    }

    public static Tex StandTexture()
    {
        var t = new Tex(256, 104);
        var rnd = new Random(77);
        var shirts = new[]
        {
            Gfx.Rgb(230, 50, 50), Gfx.Rgb(250, 210, 40), Gfx.Rgb(50, 120, 240), Gfx.Rgb(60, 200, 90),
            Gfx.Rgb(250, 250, 250), Gfx.Rgb(170, 80, 230), Gfx.Rgb(255, 140, 40), Gfx.Rgb(255, 120, 190)
        };
        var skins = new[] { Gfx.Rgb(255, 214, 170), Gfx.Rgb(222, 170, 120), Gfx.Rgb(150, 100, 64) };

        for (var y = 0; y < 10; y++)
        for (var x = 0; x < t.W; x++)
        {
            var c = Gfx.Lerp(Gfx.Rgb(70, 76, 96), Gfx.Rgb(40, 44, 58), y / 9f);
            if (y == 9) c = Gfx.Rgb(240, 240, 248);
            if (x % 128 is >= 18 and < 34 && y is >= 2 and < 6)
                c = (x & 1) == 0 ? Gfx.Rgb(255, 255, 224) : Gfx.Rgb(255, 240, 170);
            t.Set(x, y, c);
        }

        for (var y = 10; y < 90; y++)
        {
            var tierRow = (y - 10) % 5;
            for (var x = 0; x < t.W; x++)
            {
                if (x % 64 is >= 30 and < 34)
                    t.Set(x, y, tierRow == 0 ? Gfx.Rgb(150, 150, 166) : Gfx.Rgb(120, 120, 136));
                else
                    t.Set(x, y, tierRow == 0 ? Gfx.Rgb(104, 104, 124) : Gfx.Rgb(60, 56, 120));
            }
        }

        for (var tier = 0; tier < 16; tier++)
        {
            var y0 = 10 + tier * 5 + 1;
            for (var x = 0; x < t.W; x += 2)
            {
                if (x % 64 is >= 28 and < 34 || rnd.NextDouble() < 0.12) continue;
                var shirt = shirts[rnd.Next(shirts.Length)];
                var skin = skins[rnd.Next(skins.Length)];
                var hair = rnd.NextDouble() < 0.5 ? Gfx.Rgb(60, 40, 24) : Gfx.Rgb(30, 26, 22);
                t.Set(x, y0, hair);
                t.Set(x + 1, y0, hair);
                t.Set(x, y0 + 1, skin);
                t.Set(x + 1, y0 + 1, skin);
                for (var yy = y0 + 2; yy < y0 + 4; yy++)
                {
                    t.Set(x, yy, (shirt & 0xFFFFFF) | (CrowdMarker << 24));
                    t.Set(x + 1, yy, (Gfx.Shade(shirt, 0.8f) & 0xFFFFFF) | (CrowdMarker << 24));
                }
            }
        }

        var boards = new[]
        {
            ("TURBO", Gfx.Rgb(30, 80, 210), Gfx.Rgb(255, 255, 255)),
            ("NOVA OIL", Gfx.Rgb(255, 208, 30), Gfx.Rgb(200, 30, 40)),
            ("KART TV", Gfx.Rgb(245, 245, 250), Gfx.Rgb(30, 80, 210)),
            ("VOLT", Gfx.Rgb(210, 30, 40), Gfx.Rgb(255, 255, 255))
        };
        for (var i = 0; i < 4; i++)
        {
            var (word, bg, fg) = boards[i];
            var x0 = i * 64;
            t.Fill(x0, 90, 64, 13, bg);
            t.Fill(x0, 90, 64, 1, Gfx.Rgb(255, 255, 255));
            t.Fill(x0, 90, 1, 13, Gfx.Rgb(20, 20, 30));
            TinyFont.Paint(t.Px, t.W, t.H, word, x0 + (64 - TinyFont.Measure(word, 1)) / 2, 93, 1, fg);
        }

        t.Fill(0, 103, t.W, 1, Gfx.Rgb(20, 20, 30));
        return t;
    }

    // ------------------------------------------------------------------ panorama

    private static float PNoise(float x, float y, int period, int seed)
    {
        var ix = (int)MathF.Floor(x);
        var iy = (int)MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int W(int v) => ((v % period) + period) % period;
        var a = Gfx.Hash(W(ix), iy, seed);
        var b = Gfx.Hash(W(ix + 1), iy, seed);
        var c = Gfx.Hash(W(ix), iy + 1, seed);
        var d = Gfx.Hash(W(ix + 1), iy + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    private static float PFbm(float x, float y, int period, int seed, int octaves = 4)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (var i = 0; i < octaves; i++)
        {
            sum += PNoise(x, y, period, seed + i * 31) * amp;
            norm += amp;
            amp *= 0.5f;
            x *= 2;
            y *= 2;
            period *= 2;
        }

        return sum / norm;
    }

    private static float Ridge(float th, float baseH, int seed, float amp)
    {
        var p = seed * 1.7f;
        return baseH + amp * (0.5f * MathF.Sin(3 * th + p) + 0.3f * MathF.Sin(7 * th + p * 2.1f) +
                              0.15f * MathF.Sin(13 * th + p * 0.7f) + 0.07f * MathF.Sin(29 * th + p * 3.3f));
    }

    private sealed class PanoCtx
    {
        public int Pw, Hz, Fog;
        public bool Night;
        public float Th(int x) => x / (float)Pw * MathF.Tau;
    }

    [ThreadStatic] private static bool _skyPixel;

    private static int SkyGradient(int top, int bottom, int y, int hz)
    {
        _skyPixel = true;
        if (_nightPano)
        {
            var c = Gfx.Lerp(Gfx.Rgb(6, 8, 28), Gfx.Rgb(46, 34, 86), MathF.Pow(y / (float)hz, 1.4f));
            var h = Gfx.Hash(_pixelX, y, 99);
            if (h < 0.018f && y < hz * 0.8f) c = Gfx.Lerp(c, Gfx.Rgb(255, 255, 255), 0.4f + h / 0.018f * 0.6f);
            return c;
        }

        return Gfx.Lerp(top, bottom, MathF.Pow(y / (float)hz, 1.3f));
    }

    [ThreadStatic] private static bool _nightPano;
    [ThreadStatic] private static bool _emissive;
    [ThreadStatic] private static int _pixelX;

    private static int Sun(PanoCtx c, int col, float th, int y, float sunTh, float sunY, float r, int color)
    {
        if (c.Night)
        {
            sunTh += 2.4f;
            r *= 0.8f;
            color = Gfx.Rgb(232, 236, 252);
        }

        var dx = Gfx.WrapAngle(th - sunTh) * c.Pw / MathF.Tau;
        var dy = y - c.Hz * sunY;
        var sd = MathF.Sqrt(dx * dx + dy * dy);
        if (sd < r) return color;
        var halo = c.Night ? 2.2f : 3.4f;
        if (sd < r * halo) return Gfx.Lerp(col, color, (r * halo - sd) / (r * halo) * (c.Night ? 0.3f : 0.55f));
        return col;
    }

    private static int Clouds(int col, float th, int y, int hz, int seed, int shadow, int lit, float amount = 0.5f)
    {
        var dens = PFbm(th / MathF.Tau * 20f, y * 0.16f, 20, seed) - amount - MathF.Max(0, y / (float)hz - 0.6f) * 0.9f;
        if (dens <= 0) return col;
        if (_nightPano)
        {
            shadow = Gfx.Rgb(30, 30, 56);
            lit = Gfx.Rgb(78, 78, 110);
        }

        var cloud = Gfx.Lerp(shadow, lit, Math.Clamp(dens * 7, 0, 1));
        return Gfx.Lerp(col, cloud, Math.Clamp(dens * 9, 0, 1));
    }

    private static int Stars(int col, int x, int y, int seed, float density)
    {
        var h = Gfx.Hash(x, y, seed);
        if (h > density) return col;
        var b = h / density;
        return Gfx.Lerp(col, b < 0.3f ? Gfx.Rgb(180, 200, 255) : Gfx.Rgb(255, 255, 255), 0.5f + b * 0.5f);
    }

    /// <summary>360° background, <paramref name="pw"/> columns per full turn, <paramref name="hz"/> rows to the horizon.</summary>
    public static int[] Panorama(int pw, int hz, Theme theme, bool night = false)
    {
        var px = new int[pw * hz];
        var ctx = new PanoCtx { Pw = pw, Hz = hz, Fog = night ? Gfx.Rgb(40, 40, 70) : theme.Fog, Night = night };
        Func<PanoCtx, int, int, int> pixel = theme.Kind switch
        {
            ThemeKind.Beach => BeachPixel,
            ThemeKind.Canyon => CanyonPixel,
            ThemeKind.Autumn => AutumnPixel,
            ThemeKind.Snow => SnowPixel,
            ThemeKind.City => CityPixel,
            ThemeKind.Lava => LavaPixel,
            ThemeKind.Space => SpacePixel,
            _ => StadiumPixel
        };
        Parallel.For(0, pw, x =>
        {
            _nightPano = night;
            _pixelX = x;
            for (var y = 0; y < hz; y++)
            {
                _skyPixel = false;
                _emissive = false;
                var col = pixel(ctx, x, y);
                if (night && !_skyPixel && !_emissive)
                    col = Gfx.Lerp(Gfx.Shade(col, 0.38f), Gfx.Rgb(20, 22, 50), 0.25f);
                var b = hz - 1 - y + 0.5f;
                if (b < 2.5f && ((col >> 24) & 255) == 255) col = Gfx.Lerp(col, ctx.Fog, 0.5f);
                px[y * pw + x] = col;
            }
        });
        return px;
    }

    private static readonly float[] StadiumCenters = { 0.6f, 2.75f, 4.85f };

    private static readonly int[] CrowdColors =
    {
        Gfx.Rgb(230, 60, 60), Gfx.Rgb(250, 220, 60), Gfx.Rgb(70, 130, 240), Gfx.Rgb(250, 250, 250),
        Gfx.Rgb(80, 200, 100), Gfx.Rgb(200, 100, 230)
    };

    private static int StadiumPixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var b = hz - 1 - y + 0.5f;
        const float standHalf = 0.42f;
        var standH = 0f;
        var sectorEdge = false;
        var sectorX = 0f;
        foreach (var center in StadiumCenters)
        {
            var d = MathF.Abs(Gfx.WrapAngle(th - center));
            if (d > standHalf) continue;
            var taper = Math.Clamp((standHalf - d) / 0.07f, 0, 1);
            standH = hz * (0.38f + 0.24f * taper);
            sectorX = Gfx.WrapAngle(th - center) * c.Pw / MathF.Tau;
            sectorEdge = MathF.Abs(d - (standHalf - 0.05f)) < 0.006f;
        }

        if (standH > 0 && b < standH)
        {
            var fromTop = standH - b;
            if (fromTop < 3) return fromTop < 1 ? Gfx.Rgb(236, 236, 244) : Gfx.Rgb(60, 66, 84);
            if (b < 3)
                return ((int)(sectorX / 9) & 3) switch
                {
                    0 => Gfx.Rgb(40, 100, 230), 1 => Gfx.Rgb(250, 250, 250), 2 => Gfx.Rgb(250, 200, 40),
                    _ => Gfx.Rgb(220, 40, 50)
                };
            if (((int)fromTop & 3) == 0) return Gfx.Rgb(96, 96, 118);
            var h = Gfx.Hash(x, y, 4);
            var col = h < 0.18f ? Gfx.Rgb(56, 50, 110) : CrowdColors[(int)(h * 997) % CrowdColors.Length];
            return (col & 0xFFFFFF) | (CrowdMarker << 24);
        }

        if (sectorEdge && b < standH + hz * 0.2f)
        {
            var lamp = b > standH + hz * 0.2f - 3;
            _emissive = lamp;
            return lamp ? Gfx.Rgb(255, 255, 230) : Gfx.Rgb(150, 150, 170);
        }

        var hh = hz * (0.2f + 0.05f * MathF.Sin(5 * th + 3) + 0.035f * MathF.Sin(11 * th + 1) +
                       0.015f * MathF.Sin(37 * th + 2)) + 1.4f * MathF.Abs(MathF.Sin(95 * th));
        if (b < hh)
        {
            var col = Gfx.Lerp(Gfx.Rgb(40, 116, 58), Gfx.Rgb(86, 170, 82), b / hh);
            if (b > hh - 1.5f) col = Gfx.Rgb(110, 190, 96);
            return Gfx.Lerp(col, c.Fog, 0.18f);
        }

        var hm = Ridge(th, hz * 0.44f, 1, hz * 0.24f);
        if (b < hm)
        {
            var slope = MathF.Cos(7 * th + 2.3f) * 0.5f + MathF.Cos(13 * th + 0.7f) * 0.5f;
            var col = slope > 0 ? Gfx.Rgb(118, 136, 206) : Gfx.Rgb(92, 108, 180);
            if (b > hm - 2.6f && hm > hz * 0.5f) col = Gfx.Rgb(244, 246, 255);
            return Gfx.Lerp(col, c.Fog, 0.35f);
        }

        var sky = SkyGradient(Gfx.Rgb(34, 88, 208), Gfx.Rgb(150, 206, 250), y, hz);
        sky = Sun(c, sky, th, y, 3.7f, 0.22f, 3.5f, Gfx.Rgb(255, 252, 220));
        return Clouds(sky, th, y, hz, 9, Gfx.Rgb(198, 214, 242), Gfx.Rgb(255, 255, 255));
    }

    private static int BeachPixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var b = hz - 1 - y + 0.5f;
        var island = Ridge(th, -hz * 0.05f, 4, hz * 0.5f);
        if (b < island)
        {
            var col = b > island - 2 ? Gfx.Rgb(90, 170, 90) : Gfx.Rgb(60, 130, 80);
            if (b < 1.5f) col = Gfx.Rgb(236, 220, 170);
            return Gfx.Lerp(col, c.Fog, 0.45f);
        }

        if (b < 1.5f) return Gfx.Lerp(Gfx.Rgb(40, 130, 210), c.Fog, 0.3f);
        var sky = SkyGradient(Gfx.Rgb(30, 120, 226), Gfx.Rgb(176, 230, 252), y, hz);
        sky = Sun(c, sky, th, y, 1.2f, 0.18f, 4f, Gfx.Rgb(255, 255, 230));
        return Clouds(sky, th, y, hz, 12, Gfx.Rgb(206, 226, 246), Gfx.Rgb(255, 255, 255), 0.55f);
    }

    private static int CanyonPixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var b = hz - 1 - y + 0.5f;
        var near = Ridge(th, hz * 0.16f, 6, hz * 0.12f);
        if (b < near)
            return Gfx.Lerp(((int)(b / 3) & 1) == 0 ? Gfx.Rgb(150, 66, 38) : Gfx.Rgb(130, 56, 32), c.Fog, 0.15f);

        var m = PFbm(th / MathF.Tau * 9f, 0.5f, 9, 21, 2);
        var mesa = m > 0.56f ? hz * 0.62f : m > 0.5f ? hz * (0.2f + (m - 0.5f) / 0.06f * 0.42f) : hz * 0.2f;
        if (b < mesa)
        {
            var band = ((int)((b + MathF.Sin(th * 40) * 0.6f) / 3) % 3) switch
            {
                0 => Gfx.Rgb(214, 116, 66), 1 => Gfx.Rgb(196, 98, 56), _ => Gfx.Rgb(232, 146, 90)
            };
            if (b > mesa - 1.5f && mesa > hz * 0.3f) band = Gfx.Rgb(244, 176, 120);
            var shadow = PFbm(th / MathF.Tau * 9f + 0.06f, 0.5f, 9, 21, 2) < m - 0.01f;
            if (shadow) band = Gfx.Shade(band, 0.8f);
            return Gfx.Lerp(band, c.Fog, 0.35f);
        }

        var sky = SkyGradient(Gfx.Rgb(56, 118, 210), Gfx.Rgb(252, 220, 168), y, hz);
        sky = Sun(c, sky, th, y, 4.6f, 0.55f, 4.5f, Gfx.Rgb(255, 240, 190));
        return Clouds(sky, th, y, hz, 14, Gfx.Rgb(240, 200, 170), Gfx.Rgb(255, 246, 230), 0.62f);
    }

    private static readonly int[] AutumnCanopy =
    {
        Gfx.Rgb(230, 110, 30), Gfx.Rgb(196, 50, 30), Gfx.Rgb(240, 196, 50), Gfx.Rgb(60, 110, 50),
        Gfx.Rgb(210, 140, 40)
    };

    private static int AutumnPixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var b = hz - 1 - y + 0.5f;
        var hh = hz * 0.22f + 2.2f * MathF.Abs(MathF.Sin(70 * th)) + 1.2f * MathF.Sin(9 * th);
        if (b < hh)
        {
            var col = AutumnCanopy[(int)(Gfx.Hash(x / 5, 0, 7) * AutumnCanopy.Length) % AutumnCanopy.Length];
            col = Gfx.Shade(col, 0.7f + 0.3f * b / hh);
            return Gfx.Lerp(col, c.Fog, 0.2f);
        }

        var hm = Ridge(th, hz * 0.4f, 8, hz * 0.22f);
        if (b < hm)
        {
            var slope = MathF.Cos(7 * th + 8 * 1.7f * 2.1f) > 0;
            return Gfx.Lerp(slope ? Gfx.Rgb(130, 120, 170) : Gfx.Rgb(104, 96, 150), c.Fog, 0.4f);
        }

        var sky = SkyGradient(Gfx.Rgb(70, 130, 214), Gfx.Rgb(230, 214, 220), y, hz);
        sky = Sun(c, sky, th, y, 2.2f, 0.35f, 3.5f, Gfx.Rgb(255, 236, 190));
        return Clouds(sky, th, y, hz, 15, Gfx.Rgb(220, 200, 214), Gfx.Rgb(255, 250, 246));
    }

    private static int SnowPixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var b = hz - 1 - y + 0.5f;
        var trees = hz * 0.14f + 2.5f * MathF.Abs(MathF.Sin(110 * th));
        if (b < trees)
        {
            var col = Gfx.Hash(x, y, 3) < 0.2f ? Gfx.Rgb(230, 240, 255) : Gfx.Rgb(28, 76, 64);
            return Gfx.Lerp(col, c.Fog, 0.3f);
        }

        var peak = Ridge(th, hz * 0.55f, 11, hz * 0.45f);
        if (b < peak)
        {
            var slope = MathF.Cos(3 * th + 11 * 1.7f) + MathF.Cos(7 * th + 11 * 1.7f * 2.1f) * 0.6f > 0;
            var snowLine = peak * 0.55f + MathF.Sin(th * 60) * 2;
            int col;
            if (b > snowLine) col = slope ? Gfx.Rgb(252, 254, 255) : Gfx.Rgb(190, 206, 238);
            else col = slope ? Gfx.Rgb(120, 130, 160) : Gfx.Rgb(90, 98, 130);
            return Gfx.Lerp(col, c.Fog, 0.25f);
        }

        var sky = SkyGradient(Gfx.Rgb(70, 136, 226), Gfx.Rgb(222, 236, 252), y, hz);
        sky = Sun(c, sky, th, y, 0.8f, 0.2f, 3.5f, Gfx.Rgb(255, 255, 240));
        return Clouds(sky, th, y, hz, 16, Gfx.Rgb(214, 226, 246), Gfx.Rgb(255, 255, 255), 0.6f);
    }

    private static int CityPixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var b = hz - 1 - y + 0.5f;
        var building = (int)(x / (3f + Gfx.Hash(x / 6, 0, 2) * 3)) ;
        var bh = hz * (0.15f + Gfx.Hash(building, 1, 5) * 0.55f);
        if (Gfx.Hash(building, 2, 6) < 0.15f) bh *= 1.3f;
        if (b < bh)
        {
            var win = (x & 1) == 0 && ((int)b & 1) == 0 && Gfx.Hash(x, y, 8) < 0.35f;
            var col = win ? (Gfx.Hash(x, y, 9) < 0.7f ? Gfx.Rgb(255, 220, 130) : Gfx.Rgb(140, 220, 255))
                : Gfx.Rgb(18, 16, 36);
            if (b < 3) col = Gfx.Lerp(col, Gfx.Rgb(255, 60, 200), 0.35f);
            return col;
        }

        if (b < bh + 1 && Gfx.Hash(building, 3, 7) < 0.3f && x % 4 == 1) return Gfx.Rgb(255, 40, 40);
        var sky = SkyGradient(Gfx.Rgb(6, 6, 26), Gfx.Rgb(80, 30, 100), y, hz);
        sky = Stars(sky, x, y, 31, y < hz * 0.6f ? 0.02f : 0.004f);
        return Sun(c, sky, th, y, 2.0f, 0.22f, 4f, Gfx.Rgb(244, 240, 220));
    }

    private static int LavaPixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var b = hz - 1 - y + 0.5f;
        const float volcano = 3.1f;
        var dv = MathF.Abs(Gfx.WrapAngle(th - volcano));
        var cone = dv < 0.6f ? hz * 0.85f * (1 - dv / 0.6f) : 0;
        var crater = cone > hz * 0.72f;
        if (crater) cone = hz * 0.72f;
        var ridge = Ridge(th, hz * 0.22f, 13, hz * 0.2f) + 1.5f * MathF.Abs(MathF.Sin(50 * th));
        if (b < cone)
        {
            var streak = MathF.Abs(MathF.Sin(th * 70 + b * 0.15f)) < 0.08f && b > cone * 0.3f;
            var col = streak ? Gfx.Rgb(255, 120, 30) : Gfx.Rgb(46, 30, 32);
            if (b > cone - 2) col = Gfx.Rgb(255, 170, 60);
            return col;
        }

        if (b < ridge) return b > ridge - 1.2f ? Gfx.Rgb(140, 40, 24) : Gfx.Rgb(34, 22, 24);

        var sky = SkyGradient(Gfx.Rgb(16, 4, 8), Gfx.Rgb(160, 46, 22), y, hz);
        if (dv < 0.25f && b > hz * 0.7f)
        {
            var smoke = PFbm(th / MathF.Tau * 40f, y * 0.25f, 40, 5);
            if (smoke > 0.45f) sky = Gfx.Lerp(sky, Gfx.Rgb(50, 40, 40), Math.Clamp((smoke - 0.45f) * 4, 0, 0.9f));
        }

        if (dv < 0.3f) sky = Gfx.Lerp(sky, Gfx.Rgb(255, 120, 40), MathF.Max(0, 0.4f - dv) * MathF.Max(0, 1 - MathF.Abs(b - hz * 0.75f) / (hz * 0.3f)));
        return Stars(sky, x, y, 41, 0.006f);
    }

    private static int SpacePixel(PanoCtx c, int x, int y)
    {
        var hz = c.Hz;
        var th = c.Th(x);
        var sky = SkyGradient(Gfx.Rgb(2, 2, 12), Gfx.Rgb(22, 12, 46), y, hz);
        var neb = PFbm(th / MathF.Tau * 12f, y * 0.08f, 12, 51);
        if (neb > 0.52f)
        {
            var hueMix = PFbm(th / MathF.Tau * 6f, y * 0.05f, 6, 52);
            var nc = Gfx.Lerp(Gfx.Rgb(200, 40, 180), Gfx.Rgb(40, 160, 240), hueMix);
            sky = Gfx.Lerp(sky, nc, Math.Clamp((neb - 0.52f) * 3.2f, 0, 0.75f));
        }

        sky = Stars(sky, x, y, 61, 0.03f);

        var dx = Gfx.WrapAngle(th - 1.5f) * c.Pw / MathF.Tau;
        var dy = y - hz * 0.35f;
        var r = hz * 0.22f;
        var ringV = dy * 3.2f + dx * 0.35f;
        var ringD = MathF.Sqrt(dx * dx + ringV * ringV);
        var inPlanet = dx * dx + dy * dy < r * r;
        if (ringD > r * 1.4f && ringD < r * 1.9f && (!inPlanet || ringV > 0))
            return Gfx.Lerp(sky, Gfx.Rgb(230, 200, 160), 0.7f);
        if (inPlanet)
        {
            var band = MathF.Sin((dy / r) * 9 + MathF.Sin(dx * 0.4f));
            var col = band > 0 ? Gfx.Rgb(232, 156, 90) : Gfx.Rgb(180, 100, 60);
            var shade = 0.45f + 0.55f * Math.Clamp(1 - (dx + r * 0.4f) / (r * 1.6f), 0, 1);
            return Gfx.Shade(col, shade);
        }

        var mdx = Gfx.WrapAngle(th - 4.4f) * c.Pw / MathF.Tau;
        var mdy = y - hz * 0.2f;
        if (mdx * mdx + mdy * mdy < 9) return mdx < 0 ? Gfx.Rgb(220, 220, 236) : Gfx.Rgb(130, 130, 150);
        return sky;
    }
}
