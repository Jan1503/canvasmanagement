using System.Collections.Concurrent;
using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

internal readonly record struct RacerLook(string Name, int Body, int Helmet, int Suit);

/// <summary>Procedural art: voxel karts pre-rendered from 32 angles, scenery billboards, panorama, quad textures.</summary>
internal static partial class Art
{
    public const int KartAngles = 32;
    public const float KartWorldWidth = 7f;
    public const float WallDensity = 2f;
    public const float StandDensity = 2f;
    public const float FacadeDensity = 2f;
    public const float SpritePitch = 0.38f;
    public const int CrowdMarker = 0xFD;

    public static readonly RacerLook[] Racers =
    {
        new("BLAZE", Gfx.Rgb(222, 34, 40), Gfx.Rgb(246, 246, 250), Gfx.Rgb(40, 64, 200)),
        new("VOLT", Gfx.Rgb(252, 204, 30), Gfx.Rgb(44, 44, 54), Gfx.Rgb(240, 130, 20)),
        new("AQUA", Gfx.Rgb(30, 150, 245), Gfx.Rgb(255, 255, 255), Gfx.Rgb(20, 70, 160)),
        new("FERN", Gfx.Rgb(60, 196, 70), Gfx.Rgb(252, 232, 80), Gfx.Rgb(26, 104, 40)),
        new("NOVA", Gfx.Rgb(150, 70, 226), Gfx.Rgb(255, 130, 210), Gfx.Rgb(86, 36, 150)),
        new("RUBY", Gfx.Rgb(244, 96, 176), Gfx.Rgb(255, 244, 250), Gfx.Rgb(176, 36, 110)),
        new("ONYX", Gfx.Rgb(56, 56, 68), Gfx.Rgb(255, 136, 24), Gfx.Rgb(28, 28, 34)),
        new("FROST", Gfx.Rgb(232, 242, 252), Gfx.Rgb(60, 204, 244), Gfx.Rgb(110, 166, 222))
    };

    // ------------------------------------------------------------------ voxel kart

    private const int VX = 18, VY = 26, VZ = 18;
    private static readonly Lazy<byte[]> Voxels = new(BuildVoxels);
    private static readonly ConcurrentDictionary<int, Sprite[][]> KartCache = new();

    private static bool In(float v, float a, float b) => v >= a && v < b;

    private static byte[] BuildVoxels()
    {
        var g = new byte[VX * VY * VZ];
        for (var z = 0; z < VZ; z++)
        for (var y = 0; y < VY; y++)
        for (var x = 0; x < VX; x++)
            g[(z * VY + y) * VX + x] = VoxelMat(x + 0.5f, y + 0.5f, z + 0.5f);
        return g;
    }

    // 1 tread, 2 tyre wall, 3 rim, 4 body, 5 body dark, 6 white, 7 metal, 8 dark metal, 9 suit,
    // 10 helmet, 11 visor, 12 glove, 13 seat, 14 helmet stripe, 15 pipe bore
    private static byte VoxelMat(float x, float y, float z)
    {
        byte m = 0;
        if (In(x, 3, 15) && In(y, 1, 25) && In(z, 1.5f, 3)) m = 5;
        if (In(x, 2.5f, 15.5f) && In(y, 9, 18) && In(z, 2.5f, 6))
            m = (x < 3.5f || x > 14.5f) && In(z, 4, 5) ? (byte)6 : (byte)4;
        if (In(x, 5, 13) && In(y, 17, 25) && In(z, 2.5f, 6.2f - (y - 17) * 0.32f)) m = 4;
        if (In(x, 7.5f, 10.5f) && In(y, 18, 23) && In(z, 4.4f, 6.2f - (y - 17) * 0.32f + 0.6f)) m = 6;
        if (In(x, 4, 14) && In(y, 24, 26) && In(z, 1.5f, 3.6f)) m = 6;
        if (In(x, 5, 13) && In(y, 1, 6) && In(z, 3, 7)) m = 8;
        if (In(x, 5.5f, 12.5f) && In(y, 1.5f, 5.5f) && In(z, 6, 7.4f)) m = 7;
        foreach (var ex in new[] { 6.2f, 11.8f })
        {
            var r = MathF.Sqrt((x - ex) * (x - ex) + (z - 7.8f) * (z - 7.8f));
            if (r < 1.3f && In(y, 0, 4.5f)) m = y < 1 && r < 0.7f ? (byte)15 : (byte)7;
        }

        if ((In(x, 4, 5) || In(x, 13, 14)) && In(y, 1, 2) && In(z, 6, 10.5f)) m = 8;
        if (In(x, 1, 17) && In(y, 0, 2.8f) && In(z, 10, 11.8f))
            m = x < 2.2f || x > 15.8f ? (byte)6 : (byte)4;
        if (In(x, 6, 12) && In(y, 6, 8.5f) && In(z, 3, 10)) m = 13;
        if (In(x, 6, 12) && In(y, 8.5f, 12.5f) && In(z, 5, 11)) m = 9;
        if ((In(x, 4.6f, 6.6f) || In(x, 11.4f, 13.4f)) && In(y, 10, 15.5f) && In(z, 7.8f, 9.8f)) m = 9;
        if ((In(x, 4.6f, 6.8f) || In(x, 11.2f, 13.4f)) && In(y, 15, 17) && In(z, 7.6f, 10)) m = 12;
        if (In(y, 16.6f, 17.6f))
        {
            var r = MathF.Sqrt((x - 9) * (x - 9) + (z - 9.4f) * (z - 9.4f));
            if (r > 1.5f && r < 2.6f) m = 8;
        }

        if (In(x, 8.5f, 9.5f) && In(y, 15, 17) && In(z, 5.5f, 8.5f)) m = 8;

        var hd = MathF.Sqrt((x - 9) * (x - 9) + (y - 10.6f) * (y - 10.6f) + (z - 13.3f) * (z - 13.3f));
        if (hd < 3.8f)
        {
            m = 10;
            if (y - 10.6f > 1.4f && In(z, 11.6f, 14.5f)) m = 11;
            if (MathF.Abs(x - 9) < 0.9f && z > 13.4f) m = 14;
        }

        Wheel(ref m, x, y, z, 5.5f, 4.1f, 4.1f, 0, 4.4f, true);
        Wheel(ref m, x, y, z, 5.5f, 4.1f, 4.1f, 13.6f, 18, false);
        Wheel(ref m, x, y, z, 21.5f, 3.3f, 3.3f, 0.6f, 4, true);
        Wheel(ref m, x, y, z, 21.5f, 3.3f, 3.3f, 14, 17.4f, false);
        return m;
    }

    private static void Wheel(ref byte m, float x, float y, float z, float cy, float cz, float r, float x0, float x1,
        bool left)
    {
        if (!In(x, x0, x1)) return;
        var rr = MathF.Sqrt((y - cy) * (y - cy) + (z - cz) * (z - cz));
        if (rr >= r) return;
        var outerFace = left ? x < x0 + 1 : x >= x1 - 1;
        if (rr > r - 1.1f) m = 1;
        else if (outerFace && rr < r * 0.5f) m = 3;
        else m = 2;
    }

    private static int[] KartPalette(RacerLook look) => new[]
    {
        0,
        Gfx.Rgb(30, 30, 36), Gfx.Rgb(56, 56, 64), Gfx.Rgb(232, 192, 70),
        look.Body, Gfx.Shade(look.Body, 0.62f), Gfx.Rgb(242, 242, 246),
        Gfx.Rgb(176, 178, 190), Gfx.Rgb(70, 72, 84), look.Suit,
        look.Helmet, Gfx.Rgb(26, 40, 78), Gfx.Rgb(252, 252, 252),
        Gfx.Rgb(40, 40, 48), look.Body, Gfx.Rgb(20, 20, 20)
    };

    /// <summary>Sprites per racer per view angle, rendered at <paramref name="ppv"/> screen pixels per voxel.</summary>
    public static Sprite[][] KartSprites(float ppv)
    {
        var key = (int)MathF.Round(ppv * 100);
        return KartCache.GetOrAdd(key, _ =>
        {
            var vox = Voxels.Value;
            var result = new Sprite[Racers.Length][];
            for (var r = 0; r < Racers.Length; r++) result[r] = new Sprite[KartAngles];
            Parallel.For(0, Racers.Length * KartAngles, job =>
            {
                var r = job / KartAngles;
                var a = job % KartAngles;
                result[r][a] = RenderKart(vox, KartPalette(Racers[r]), a * MathF.Tau / KartAngles, ppv);
            });
            return result;
        });
    }

    private static Sprite RenderKart(byte[] vox, int[] pal, float rho, float ppv)
    {
        var size = (int)MathF.Ceiling(2 * 20f * ppv) + 6;
        var px = new int[size * size];
        var cp = MathF.Cos(SpritePitch);
        var sp = MathF.Sin(SpritePitch);
        var f = new Vector3(-MathF.Sin(rho) * cp, MathF.Cos(rho) * cp, -sp);
        var r = new Vector3(MathF.Cos(rho), MathF.Sin(rho), 0);
        var u = Vector3.Cross(r, f);
        var center = new Vector3(VX / 2f, VY / 2f, 6f);
        var light = Vector3.Normalize(-f * 0.55f + u * 0.8f - r * 0.45f);
        var half = size / 2f;

        for (var py = 0; py < size; py++)
        for (var pxi = 0; pxi < size; pxi++)
        {
            var o = center + r * ((pxi + 0.5f - half) / ppv) + u * ((half - (py + 0.5f)) / ppv) - f * 60f;
            if (!Cast(vox, o, f, out var mat, out var n, out var hitZ)) continue;
            var c = pal[mat];
            var lam = MathF.Max(0, Vector3.Dot(n, light));
            var shade = 0.4f + 0.62f * lam;
            shade *= 0.84f + 0.16f * Math.Clamp(hitZ / 8f, 0, 1);
            var col = Gfx.Shade(c, shade);
            if (mat is 10 or 11 or 4 or 14)
            {
                var h = Vector3.Normalize(light - f);
                var spec = MathF.Pow(MathF.Max(0, Vector3.Dot(n, h)), 24) * (mat == 11 ? 1f : 0.55f);
                col = Gfx.Add(col, (int)(spec * 160));
            }

            px[py * size + pxi] = col;
        }

        Outline(px, size, size, Gfx.Rgb(14, 14, 22));
        var ground = new Vector3(VX / 2f, VY / 2f, 0) - center;
        var ax = half + Vector3.Dot(ground, r) * ppv;
        var ay = half - Vector3.Dot(ground, u) * ppv;
        return Crop(px, size, size, ax, ay, KartWorldWidth / VX / ppv);
    }

    private static bool Cast(byte[] vox, Vector3 o, Vector3 d, out byte mat, out Vector3 normal, out float hitZ)
    {
        mat = 0;
        normal = default;
        hitZ = 0;
        float tmin = -1e9f, tmax = 1e9f;
        var enter = -1;
        Span<float> os = stackalloc float[] { o.X, o.Y, o.Z };
        Span<float> ds = stackalloc float[] { d.X, d.Y, d.Z };
        Span<int> dims = stackalloc int[] { VX, VY, VZ };
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(ds[a]) < 1e-7f)
            {
                if (os[a] < 0 || os[a] > dims[a]) return false;
                continue;
            }

            var t1 = (0 - os[a]) / ds[a];
            var t2 = (dims[a] - os[a]) / ds[a];
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tmin) { tmin = t1; enter = a; }
            if (t2 < tmax) tmax = t2;
        }

        if (tmax < tmin || enter < 0) return false;
        var p = o + d * (tmin + 1e-4f);
        Span<int> cell = stackalloc int[]
        {
            Math.Clamp((int)p.X, 0, VX - 1), Math.Clamp((int)p.Y, 0, VY - 1), Math.Clamp((int)p.Z, 0, VZ - 1)
        };
        Span<float> ps = stackalloc float[] { p.X, p.Y, p.Z };
        Span<int> step = stackalloc int[3];
        Span<float> tMax = stackalloc float[3];
        Span<float> tDelta = stackalloc float[3];
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(ds[a]) < 1e-7f) { step[a] = 0; tMax[a] = 1e9f; tDelta[a] = 1e9f; continue; }
            step[a] = ds[a] > 0 ? 1 : -1;
            tDelta[a] = MathF.Abs(1f / ds[a]);
            var next = ds[a] > 0 ? cell[a] + 1 - ps[a] : ps[a] - cell[a];
            tMax[a] = next * tDelta[a];
        }

        var axis = enter;
        for (var guard = 0; guard < 128; guard++)
        {
            var m = vox[(cell[2] * VY + cell[1]) * VX + cell[0]];
            if (m != 0)
            {
                mat = m;
                var nv = new float[3];
                nv[axis] = -step[axis] != 0 ? -step[axis] : (ds[axis] > 0 ? -1 : 1);
                normal = new Vector3(nv[0], nv[1], nv[2]);
                hitZ = cell[2];
                return true;
            }

            axis = tMax[0] < tMax[1] ? (tMax[0] < tMax[2] ? 0 : 2) : (tMax[1] < tMax[2] ? 1 : 2);
            cell[axis] += step[axis];
            tMax[axis] += tDelta[axis];
            if ((uint)cell[axis] >= (uint)dims[axis]) return false;
        }

        return false;
    }

    private static void Outline(int[] px, int w, int h, int color)
    {
        var copy = (int[])px.Clone();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (copy[y * w + x] != 0) continue;
            var edge = (x > 0 && copy[y * w + x - 1] != 0) || (x < w - 1 && copy[y * w + x + 1] != 0) ||
                       (y > 0 && copy[(y - 1) * w + x] != 0) || (y < h - 1 && copy[(y + 1) * w + x] != 0);
            if (edge) px[y * w + x] = color;
        }
    }

    private static Sprite Crop(int[] px, int w, int h, float ax, float ay, float worldPerPixel)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (px[y * w + x] == 0) continue;
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }

        if (x1 < 0) return new Sprite { W = 1, H = 1, Px = new int[1], WorldPerPixel = worldPerPixel };
        var cw = x1 - x0 + 1;
        var ch = y1 - y0 + 1;
        var outPx = new int[cw * ch];
        for (var y = 0; y < ch; y++)
            Array.Copy(px, (y + y0) * w + x0, outPx, y * cw, cw);
        return new Sprite
        {
            W = cw, H = ch, Px = outPx, AnchorX = ax - x0, AnchorY = ay - y0, WorldPerPixel = worldPerPixel
        };
    }

    // ------------------------------------------------------------------ billboards

    public sealed class SceneSprites
    {
        public Sprite[] Crystal = null!, Gem = null!;
        public readonly Dictionary<SceneryKind, Sprite[]> Kinds = new();
    }

    private static readonly ConcurrentDictionary<int, SceneSprites> SceneCache = new();

    public static SceneSprites Scene(float ppu)
    {
        var key = (int)MathF.Round(ppu * 100);
        return SceneCache.GetOrAdd(key, _ =>
        {
            var s = new SceneSprites
            {
                Crystal = Enumerable.Range(0, 16).Select(k => Crystal(ppu * 2, k)).ToArray(),
                Gem = Enumerable.Range(0, 8).Select(k => GemSprite(ppu * 2, k)).ToArray()
            };
            Sprite[] Three(Func<int, Sprite> f) => Enumerable.Range(0, 3).Select(f).ToArray();
            s.Kinds[SceneryKind.RoundTree] = Three(v => Tree(ppu, v, TreeLeaves.Green));
            s.Kinds[SceneryKind.BlossomTree] = Three(v => Tree(ppu, v, TreeLeaves.Blossom));
            s.Kinds[SceneryKind.AutumnTree] = Three(v => Tree(ppu, v, (TreeLeaves)((int)TreeLeaves.Orange + v)));
            s.Kinds[SceneryKind.PineTree] = Three(v => Pine(ppu, v, false));
            s.Kinds[SceneryKind.SnowPine] = Three(v => Pine(ppu, v, true));
            s.Kinds[SceneryKind.Bush] = Three(v => Bush(ppu, v));
            s.Kinds[SceneryKind.FlagPole] = Enumerable.Range(0, 15).Select(i => FlagPole(ppu, i / 3, i % 3)).ToArray();
            s.Kinds[SceneryKind.Balloon] = Three(v => Balloon(ppu, v));
            s.Kinds[SceneryKind.Gull] = new[] { Gull(ppu * 2, 0), Gull(ppu * 2, 1) };
            s.Kinds[SceneryKind.Palm] = Three(v => Palm(ppu, v));
            s.Kinds[SceneryKind.Parasol] = Three(v => Parasol(ppu, v));
            s.Kinds[SceneryKind.Cactus] = Three(v => Cactus(ppu, v));
            s.Kinds[SceneryKind.DesertRock] = Three(v => Rock(ppu, v, RockStyle.Desert));
            s.Kinds[SceneryKind.SnowRock] = Three(v => Rock(ppu, v, RockStyle.Snow));
            s.Kinds[SceneryKind.Asteroid] = Three(v => Rock(ppu, v, RockStyle.Asteroid));
            s.Kinds[SceneryKind.LavaRock] = Three(v => LavaSpire(ppu, v));
            s.Kinds[SceneryKind.Lamp] = new[] { Lamp(ppu) };
            return s;
        });
    }

    private enum TreeLeaves { Green, Blossom, Orange, Red, Yellow }

    private enum RockStyle { Desert, Snow, Asteroid }

    private sealed class Canvas2D
    {
        public readonly int W, H;
        public readonly int[] Px;
        public readonly float Ppu;
        public Canvas2D(float wUnits, float hUnits, float ppu)
        {
            Ppu = ppu;
            W = Math.Max(3, (int)MathF.Ceiling(wUnits * ppu) + 2);
            H = Math.Max(3, (int)MathF.Ceiling(hUnits * ppu) + 2);
            Px = new int[W * H];
        }

        /// <summary>World-ish coordinates: x centred, y up from the ground.</summary>
        public void Each(Func<float, float, int> shader)
        {
            for (var py = 0; py < H; py++)
            for (var px = 0; px < W; px++)
            {
                var x = (px + 0.5f - W / 2f) / Ppu;
                var y = (H - 1 - py + 0.5f) / Ppu;
                var c = shader(x, y);
                if (c != 0) Px[py * W + px] = c;
            }
        }

        public Sprite ToSprite(int outline)
        {
            if (outline != 0) Outline(Px, W, H, outline);
            return Crop(Px, W, H, W / 2f, H - 1, 1f / Ppu);
        }
    }

    private static int Cel(float shade, int dark, int mid, int light, int hi) =>
        shade < 0.38f ? dark : shade < 0.62f ? mid : shade < 0.86f ? light : hi;

    private static Sprite Tree(float ppu, int variant, TreeLeaves leaves)
    {
        var blossom = leaves != TreeLeaves.Green;
        var scale = 1f + variant * 0.16f;
        var c = new Canvas2D(28 * scale, 38 * scale, ppu);
        var blobs = new (float x, float y, float r)[]
        {
            (0, 22, 9.5f), (-7, 17.5f, 7), (7, 17.5f, 7), (-3.5f, 27, 7), (4, 26, 6.5f), (0, 15, 6)
        };
        var light = Vector3.Normalize(new Vector3(-0.55f, 0.65f, 0.55f));
        var tint = variant * 8;
        var (dark, mid, lit, hi) = leaves switch
        {
            TreeLeaves.Blossom => (Gfx.Rgb(176, 60, 120), Gfx.Rgb(232, 110, 170), Gfx.Rgb(252, 160, 205),
                Gfx.Rgb(255, 214, 236)),
            TreeLeaves.Orange => (Gfx.Rgb(170, 70, 20), Gfx.Rgb(226, 118, 30), Gfx.Rgb(248, 166, 50),
                Gfx.Rgb(255, 214, 110)),
            TreeLeaves.Red => (Gfx.Rgb(130, 24, 24), Gfx.Rgb(192, 44, 32), Gfx.Rgb(232, 86, 46),
                Gfx.Rgb(250, 150, 90)),
            TreeLeaves.Yellow => (Gfx.Rgb(170, 130, 20), Gfx.Rgb(222, 184, 40), Gfx.Rgb(246, 220, 80),
                Gfx.Rgb(255, 246, 160)),
            _ => (Gfx.Rgb(24, 86 + tint, 42), Gfx.Rgb(40, 134 + tint, 52), Gfx.Rgb(82, 184 + tint, 64),
                Gfx.Rgb(156, 226, 98))
        };

        c.Each((x, y) =>
        {
            x /= scale;
            y /= scale;
            var best = -1f;
            var n = Vector3.Zero;
            foreach (var b in blobs)
            {
                var dx = (x - b.x) / b.r;
                var dy = (y - b.y) / b.r;
                var dd = dx * dx + dy * dy;
                if (dd >= 1) continue;
                var depth = (1 - dd) * b.r;
                if (depth <= best) continue;
                best = depth;
                n = new Vector3(dx, dy, MathF.Sqrt(1 - dd));
            }

            if (best > 0)
            {
                var leaf = Gfx.Hash((int)(x * 3), (int)(y * 3), 33) * 0.22f - 0.11f;
                var s = 0.25f + 0.75f * MathF.Max(0, Vector3.Dot(n, light)) + leaf;
                return Cel(s, dark, mid, lit, hi);
            }

            if (MathF.Abs(x) < 1.8f && y < 16)
                return x < -0.5f ? Gfx.Rgb(150, 98, 58) : x < 0.9f ? Gfx.Rgb(118, 74, 40) : Gfx.Rgb(84, 50, 28);
            return 0;
        });
        return c.ToSprite(blossom ? Gfx.Shade(dark, 0.5f) : Gfx.Rgb(14, 48, 24));
    }

    private static Sprite Pine(float ppu, int variant, bool snow)
    {
        var scale = 1f + variant * 0.18f;
        var c = new Canvas2D(20 * scale, 44 * scale, ppu);
        c.Each((x, y) =>
        {
            x /= scale;
            y /= scale;
            for (var layer = 0; layer < 4; layer++)
            {
                var baseY = 8 + layer * 8f;
                var topY = baseY + 14;
                var halfW = 9.5f - layer * 1.9f;
                if (y < baseY || y > topY) continue;
                var w = halfW * (1 - (y - baseY) / (topY - baseY));
                if (MathF.Abs(x) > w) continue;
                var side = x / MathF.Max(0.5f, w);
                var under = (y - baseY) < 1.6f;
                if (snow && (MathF.Abs(x) > w - 1.6f && !under || y > topY - 2.2f))
                    return side < 0.2f ? Gfx.Rgb(250, 252, 255) : Gfx.Rgb(196, 214, 244);
                if (snow && Gfx.Hash((int)(x * 3), (int)(y * 3), 9) < 0.08f) return Gfx.Rgb(240, 246, 255);
                if (under) return snow ? Gfx.Rgb(220, 230, 250) : Gfx.Rgb(16, 66, 46);
                return side < -0.35f ? Gfx.Rgb(72, 168, 96) : side < 0.3f ? Gfx.Rgb(34, 122, 70) : Gfx.Rgb(22, 88, 54);
            }

            if (MathF.Abs(x) < 1.4f && y < 10) return x < 0 ? Gfx.Rgb(130, 84, 48) : Gfx.Rgb(88, 54, 30);
            return 0;
        });
        return c.ToSprite(Gfx.Rgb(10, 40, 26));
    }

    private static Sprite Bush(float ppu, int variant)
    {
        var c = new Canvas2D(16, 9, ppu);
        var light = Vector3.Normalize(new Vector3(-0.5f, 0.7f, 0.5f));
        c.Each((x, y) =>
        {
            var best = -1f;
            var n = Vector3.Zero;
            foreach (var (bx, by, br) in new[] { (-4f, 3.4f, 4f), (3.6f, 3.2f, 4.2f), (0f, 5f, 4.6f) })
            {
                var dx = (x - bx) / br;
                var dy = (y - by) / br;
                var dd = dx * dx + dy * dy;
                if (dd >= 1 || y < 0) continue;
                var depth = 1 - dd;
                if (depth <= best) continue;
                best = depth;
                n = new Vector3(dx, dy, MathF.Sqrt(1 - dd));
            }

            if (best <= 0) return 0;
            var s = 0.3f + 0.7f * MathF.Max(0, Vector3.Dot(n, light));
            if (variant == 2 && Gfx.Hash((int)(x * 4), (int)(y * 4), 5) < 0.06f) return Gfx.Rgb(255, 90, 90);
            return Cel(s, Gfx.Rgb(26, 92, 40), Gfx.Rgb(48, 144, 56), Gfx.Rgb(94, 196, 70), Gfx.Rgb(170, 234, 110));
        });
        return c.ToSprite(Gfx.Rgb(14, 52, 24));
    }

    private static readonly int[] FlagColors =
    {
        Gfx.Rgb(236, 40, 52), Gfx.Rgb(255, 208, 30), Gfx.Rgb(40, 120, 240), Gfx.Rgb(60, 200, 90),
        Gfx.Rgb(170, 80, 230)
    };

    private static Sprite FlagPole(float ppu, int variant, int frame)
    {
        var c = new Canvas2D(12, 30, ppu);
        var col = FlagColors[variant % FlagColors.Length];
        var phase = frame * MathF.Tau / 3f;
        c.Each((x, y) =>
        {
            if (x > -4.6f && x < -3.4f && y < 28) return x < -4 ? Gfx.Rgb(220, 220, 230) : Gfx.Rgb(140, 140, 156);
            if (y > 28 && y < 29.5f && x > -5 && x < -3) return Gfx.Rgb(255, 214, 60);
            var fx = x + 3.4f;
            if (fx >= 0 && fx < 9)
            {
                var wave = MathF.Sin(fx * 0.8f - phase) * 0.7f * (fx / 9f + 0.3f);
                var top = 27.5f + wave;
                var bottom = 21.5f + wave + fx * 0.25f;
                if (y < top && y > bottom)
                {
                    var shade = MathF.Cos(fx * 0.8f - phase) > 0 ? 1f : 0.78f;
                    return Gfx.Shade(col, shade);
                }
            }

            return 0;
        });
        return c.ToSprite(Gfx.Rgb(20, 20, 30));
    }

    private static Sprite Palm(float ppu, int variant)
    {
        var lean = variant switch { 0 => 1f, 1 => -1f, _ => 0.4f };
        var height = 30f + variant * 3;
        var c = new Canvas2D(30, height + 10, ppu);
        float TrunkX(float y) => lean * 4.5f * MathF.Sin(y / height * 1.7f);
        var topX = TrunkX(height);
        var leaf = new List<(float x, float y, float r, int col)>();
        for (var f = 0; f < 8; f++)
        {
            var a = f / 8f * MathF.Tau + variant;
            var len = 11f + (f % 3) * 1.5f;
            for (var t = 0f; t <= 1f; t += 0.04f)
            {
                var px = topX + MathF.Cos(a) * len * t;
                var py = height + MathF.Sin(a) * len * t * 0.45f + 2.2f * t - 7.5f * t * t;
                var col = MathF.Sin(a) > 0 ? Gfx.Rgb(110, 210, 80) : Gfx.Rgb(46, 150, 60);
                if (t > 0.8f) col = Gfx.Shade(col, 0.85f);
                leaf.Add((px, py, (1 - t) * 1.6f + 0.45f, col));
            }
        }

        c.Each((x, y) =>
        {
            for (var i = leaf.Count - 1; i >= 0; i--)
            {
                var l = leaf[i];
                if ((x - l.x) * (x - l.x) + (y - l.y) * (y - l.y) < l.r * l.r) return l.col;
            }

            var dxTop = x - topX;
            var dyTop = y - (height - 0.8f);
            if (dxTop * dxTop + dyTop * dyTop < 2.2f) return Gfx.Rgb(120, 76, 30);
            if (y > height) return 0;
            var tx = TrunkX(y);
            var w = 1.3f - y / height * 0.45f;
            if (MathF.Abs(x - tx) > w) return 0;
            var ring = (int)(y / 1.6f) % 2 == 0;
            var shade = x - tx < -w * 0.3f ? 1.15f : x - tx > w * 0.4f ? 0.75f : 1f;
            return Gfx.Shade(ring ? Gfx.Rgb(156, 108, 62) : Gfx.Rgb(124, 82, 44), shade);
        });
        return c.ToSprite(Gfx.Rgb(20, 40, 20));
    }

    private static Sprite Parasol(float ppu, int variant)
    {
        var c = new Canvas2D(14, 13, ppu);
        var stripe = variant switch { 0 => Gfx.Rgb(230, 40, 50), 1 => Gfx.Rgb(30, 120, 230), _ => Gfx.Rgb(250, 190, 30) };
        c.Each((x, y) =>
        {
            var dy = y - 9f;
            if (dy >= 0 && x * x / 42f + dy * dy / 10f <= 1)
            {
                var seg = (int)((MathF.Atan2(dy + 1, x) + MathF.PI) / MathF.PI * 8) % 2 == 0;
                var col = seg ? stripe : Gfx.Rgb(250, 250, 250);
                return dy < 0.8f ? Gfx.Shade(col, 0.75f) : col;
            }

            if (MathF.Abs(x) < 0.4f && y < 9) return Gfx.Rgb(220, 220, 220);
            return 0;
        });
        return c.ToSprite(Gfx.Rgb(30, 30, 40));
    }

    private static Sprite Cactus(float ppu, int variant)
    {
        var h = 16f + variant * 4;
        var c = new Canvas2D(14, h + 2, ppu);
        bool Col(float x, float y, float cx, float y0, float y1, float r) =>
            y >= y0 && y <= y1 + r && MathF.Abs(x - cx) < r &&
            (y <= y1 || (x - cx) * (x - cx) + (y - y1) * (y - y1) < r * r);
        c.Each((x, y) =>
        {
            float local;
            if (Col(x, y, 0, 0, h - 1.6f, 1.6f)) local = x;
            else if (Col(x, y, -4.2f, h * 0.45f, h * 0.72f, 1.1f) || x > -4.2f && x < 0 && MathF.Abs(y - h * 0.45f) < 1.1f)
                local = x + 4.2f;
            else if (variant != 1 && (Col(x, y, 4f, h * 0.55f, h * 0.8f, 1.1f) || x < 4f && x > 0 && MathF.Abs(y - h * 0.55f) < 1.1f))
                local = x - 4f;
            else return 0;
            var rib = MathF.Abs(local * 2.2f - MathF.Round(local * 2.2f)) < 0.18f;
            var col = local < -0.5f ? Gfx.Rgb(110, 196, 96) : local < 0.6f ? Gfx.Rgb(66, 156, 70) : Gfx.Rgb(40, 110, 52);
            return rib ? Gfx.Shade(col, 0.8f) : col;
        });
        return c.ToSprite(Gfx.Rgb(16, 50, 22));
    }

    private static Sprite Rock(float ppu, int variant, RockStyle style)
    {
        var scale = 1f + variant * 0.25f;
        var c = new Canvas2D(14 * scale, 10 * scale, ppu);
        var light = Vector3.Normalize(new Vector3(-0.55f, 0.7f, 0.45f));
        var blobs = new[] { (-2.5f, 3.4f, 4f), (2.8f, 3f, 3.6f), (0.2f, 5.2f, 4.2f) };
        var (dark, mid, lit, hi) = style switch
        {
            RockStyle.Snow => (Gfx.Rgb(70, 76, 96), Gfx.Rgb(110, 118, 140), Gfx.Rgb(150, 158, 178), Gfx.Rgb(250, 252, 255)),
            RockStyle.Asteroid => (Gfx.Rgb(50, 44, 50), Gfx.Rgb(96, 86, 90), Gfx.Rgb(138, 126, 124), Gfx.Rgb(186, 176, 168)),
            _ => (Gfx.Rgb(130, 56, 32), Gfx.Rgb(180, 90, 52), Gfx.Rgb(216, 130, 80), Gfx.Rgb(240, 178, 124))
        };
        c.Each((x, y) =>
        {
            x /= scale;
            y /= scale;
            var best = -1f;
            var n = Vector3.Zero;
            foreach (var (bx, by, br) in blobs)
            {
                var dx = (x - bx) / br;
                var dy = (y - by) / br;
                var dd = dx * dx + dy * dy;
                if (dd >= 1 || (style != RockStyle.Asteroid && y < 0)) continue;
                var depth = 1 - dd;
                if (depth <= best) continue;
                best = depth;
                n = new Vector3(dx, dy, MathF.Sqrt(1 - dd));
            }

            if (best <= 0) return 0;
            if (style == RockStyle.Snow && n.Y > 0.35f) return n.X < 0.2f ? hi : Gfx.Rgb(200, 214, 240);
            if (style == RockStyle.Asteroid && Gfx.Hash((int)(x * 1.5f), (int)(y * 1.5f), variant) < 0.07f)
                return dark;
            var facet = Gfx.Hash((int)(x * 1.2f), (int)(y * 1.2f), 3) * 0.2f;
            var s = 0.25f + 0.75f * MathF.Max(0, Vector3.Dot(n, light)) + facet - 0.1f;
            return Cel(s, dark, mid, lit, style == RockStyle.Snow ? lit : hi);
        });
        var spr = c.ToSprite(Gfx.Shade(dark, 0.4f));
        if (style == RockStyle.Asteroid) spr.AnchorY = spr.H / 2f;
        return spr;
    }

    private static Sprite LavaSpire(float ppu, int variant)
    {
        var h = 14f + variant * 5;
        var c = new Canvas2D(12, h + 1, ppu);
        c.Each((x, y) =>
        {
            var jag = MathF.Sin(y * 1.7f + variant) * 0.7f;
            var w = 4.6f * (1 - y / h) + jag * (1 - y / h);
            if (MathF.Abs(x) > w || y > h) return 0;
            var crack = MathF.Abs(MathF.Sin(x * 2.1f + y * 0.9f + variant) + MathF.Sin(y * 1.3f)) < 0.12f;
            if (crack) return y < h * 0.5f ? Gfx.Rgb(255, 150, 40) : Gfx.Rgb(220, 70, 20);
            return x < -w * 0.3f ? Gfx.Rgb(84, 70, 72) : x < w * 0.3f ? Gfx.Rgb(56, 46, 50) : Gfx.Rgb(36, 30, 34);
        });
        return c.ToSprite(Gfx.Rgb(14, 10, 12));
    }

    /// <summary>Double street lamp; the renderer adds the glow at <see cref="LampHeadZ"/>.</summary>
    public const float LampHeadZ = 21.5f;
    public const float LampHeadX = 3.6f;

    private static Sprite Lamp(float ppu)
    {
        var c = new Canvas2D(10, 24, ppu);
        c.Each((x, y) =>
        {
            if (MathF.Abs(x) < 0.45f && y < 22) return x < 0 ? Gfx.Rgb(150, 150, 170) : Gfx.Rgb(90, 90, 110);
            if (y is > 22 and < 22.8f && MathF.Abs(x) < 3.8f) return Gfx.Rgb(120, 120, 140);
            if (y is > 21 and < 22.4f && MathF.Abs(MathF.Abs(x) - LampHeadX) < 1.1f) return Gfx.Rgb(255, 246, 200);
            return 0;
        });
        return c.ToSprite(Gfx.Rgb(20, 20, 30));
    }

    private static readonly (int A, int B)[] BalloonColors =
    {
        (Gfx.Rgb(236, 50, 60), Gfx.Rgb(255, 214, 50)),
        (Gfx.Rgb(40, 130, 240), Gfx.Rgb(250, 250, 250)),
        (Gfx.Rgb(150, 70, 230), Gfx.Rgb(80, 220, 140))
    };

    private static Sprite Balloon(float ppu, int variant)
    {
        var c = new Canvas2D(16, 24, ppu);
        var (ca, cb) = BalloonColors[variant % BalloonColors.Length];
        c.Each((x, y) =>
        {
            var cy = 15.5f;
            var dy = y - cy;
            var halfW = dy > 0 ? 7.5f * MathF.Sqrt(MathF.Max(0, 1 - dy * dy / 64f)) : 7.5f * (1 + dy / 9.5f);
            if (y > 6 && MathF.Abs(x) < halfW && y < cy + 8)
            {
                var u = x / MathF.Max(0.5f, halfW);
                var stripe = (int)MathF.Floor((MathF.Asin(Math.Clamp(u, -1f, 1f)) / MathF.PI + 0.5f) * 6) & 1;
                var col = stripe == 0 ? ca : cb;
                var shade = 1.15f - (u + 1) * 0.28f;
                if (y < 7.5f) col = Gfx.Rgb(120, 70, 40);
                return Gfx.Shade(col, shade);
            }

            if (y is > 1.5f and < 4 && MathF.Abs(x) < 1.8f) return y > 3.4f ? Gfx.Rgb(170, 120, 70) : Gfx.Rgb(120, 80, 44);
            if (y is >= 4 and <= 6.5f && MathF.Abs(MathF.Abs(x) - (1.3f + (y - 4) * 0.5f)) < 0.25f) return Gfx.Rgb(80, 60, 40);
            return 0;
        });
        return c.ToSprite(Gfx.Rgb(30, 20, 30));
    }

    private static Sprite Gull(float ppu, int frame)
    {
        var c = new Canvas2D(6, 3, ppu);
        c.Each((x, y) =>
        {
            var ax = MathF.Abs(x);
            var wingY = frame == 0 ? 1.2f + ax * 0.45f : 1.4f - ax * 0.25f;
            if (ax < 2.8f && MathF.Abs(y - wingY) < 0.35f) return ax > 2.2f ? Gfx.Rgb(70, 70, 80) : Gfx.Rgb(250, 250, 250);
            if (ax < 0.5f && MathF.Abs(y - 1.2f) < 0.45f) return Gfx.Rgb(240, 240, 240);
            return 0;
        });
        var s = c.ToSprite(0);
        s.AnchorY = s.H / 2f;
        return s;
    }

    private static Sprite Crystal(float ppu, int frame)
    {
        var c = new Canvas2D(7, 8, ppu);
        var a = frame / 16f * MathF.PI;
        var split = MathF.Sin(a * 2) * 0.75f;
        var hue = frame * 22.5f;
        c.Each((x, y) =>
        {
            var u = x / 3.2f;
            var v = (y - 4f) / 3.8f;
            var d = MathF.Abs(u) + MathF.Abs(v);
            if (d > 1) return 0;
            if (d > 0.84f) return Gfx.Rgb(255, 255, 255);
            var leftFacet = u < split;
            var col = leftFacet ? Gfx.Hsv(hue, 0.5f, 1f) : Gfx.Hsv(hue + 50, 0.75f, 0.85f);
            if (v > 0) col = Gfx.Add(col, 30);
            else col = Gfx.Shade(col, 0.82f);
            var sx = u + 0.28f;
            var sy = v - 0.32f;
            if (MathF.Abs(sx) < 0.07f && MathF.Abs(sy) < 0.25f || MathF.Abs(sy) < 0.07f && MathF.Abs(sx) < 0.22f)
                return Gfx.Rgb(255, 255, 255);
            return col;
        });
        return c.ToSprite(Gfx.Rgb(30, 20, 60));
    }

    private static Sprite GemSprite(float ppu, int frame)
    {
        var c = new Canvas2D(3.2f, 4f, ppu);
        var split = MathF.Sin(frame / 8f * MathF.Tau) * 0.7f;
        c.Each((x, y) =>
        {
            var u = x / 1.5f;
            var v = (y - 2f) / 1.9f;
            if (MathF.Abs(u) + MathF.Abs(v) > 1) return 0;
            var col = u < split ? Gfx.Rgb(140, 255, 236) : Gfx.Rgb(20, 190, 200);
            return v > 0.2f ? Gfx.Add(col, 40) : col;
        });
        return c.ToSprite(Gfx.Rgb(6, 60, 70));
    }

}
