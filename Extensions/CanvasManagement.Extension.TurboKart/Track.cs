using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

internal enum SceneryKind
{
    RoundTree, PineTree, BlossomTree, Bush, FlagPole, Palm, Parasol, Cactus, DesertRock, AutumnTree, SnowPine,
    SnowRock, Lamp, LavaRock, Asteroid, Balloon, Gull
}

internal struct Scenery
{
    public Vector2 Pos;
    public SceneryKind Kind;
    public int Variant;
    public float Z;
}

/// <summary>Vertical textured quad standing on the ground between A and B.</summary>
internal sealed class Quad
{
    public Vector2 A, B;
    public float Z0, Z1;
    public float U0, U1;
    public Tex Tex = null!;
}

internal struct BoostPad
{
    public int Idx;
    public float Lat;
}

/// <summary>
///     One circuit. World unit = one floor texel; the road is 68 units wide, a kart is 7.
///     Texture + mips ≈ 22 MB, so only the current (and possibly the next) track is kept alive.
/// </summary>
internal sealed class Track
{
    public const int MapBits = 11;
    public const int MapSize = 1 << MapBits;
    public const int MipLevels = 5;
    public const float Step = 4f;
    public const float HalfWidth = 34f;
    public const float CurbOuter = 39f;
    public const float BandOuter = 41.5f;
    public const float WallDist = 64f;
    public const float PadLength = 28f;
    public const float PadHalfWidth = 9f;
    public const int RampSamples = 3;
    private const float RasterRadius = 104f;

    public const int BoostMarker = 0xFE;
    public const int WaterMarker = 0xFC;
    public const int LavaMarker = 0xFB;
    public const int StarMarker = 0xFA;

    public readonly TrackDef Def;
    public readonly Theme Theme;
    public readonly Vector2[] P;
    public readonly Vector2[] T;
    public readonly Vector2[] N;
    public readonly float[] Curv;
    public readonly int Count;
    public readonly float Length;

    /// <summary>Mip chain: level i is (MapSize &gt;&gt; i)² ARGB texels.</summary>
    public readonly int[][] Mip = new int[MipLevels][];

    public readonly List<Quad> Quads = new();
    public readonly List<Scenery> Scenery = new();
    public readonly List<BoostPad> Pads = new();
    public readonly List<int> Ramps = new();
    private readonly List<int> _itemRows = new();
    public readonly List<Vector2> ItemBoxes = new();
    public readonly List<Vector2> Gems = new();
    public readonly Vector2[] MiniMap;

    public float HazardDist => Theme.Kind == ThemeKind.Space ? BandOuter + 1f : BandOuter + 4f;

    public Track(TrackDef def)
    {
        Def = def;
        Theme = def.Theme;
        P = Centerline(def.Control, out Length);
        Count = P.Length;
        T = new Vector2[Count];
        N = new Vector2[Count];
        Curv = new float[Count];
        for (var i = 0; i < Count; i++)
        {
            var d = P[(i + 1) % Count] - P[(i - 1 + Count) % Count];
            T[i] = Vector2.Normalize(d);
            N[i] = new Vector2(-T[i].Y, T[i].X);
        }

        var raw = new float[Count];
        for (var i = 0; i < Count; i++)
        {
            var a = T[(i - 2 + Count) % Count];
            var b = T[(i + 2) % Count];
            var cross = a.X * b.Y - a.Y * b.X;
            raw[i] = MathF.Asin(Math.Clamp(cross, -1f, 1f)) / (4 * Step);
        }

        for (var i = 0; i < Count; i++)
        {
            float s = 0;
            for (var k = -6; k <= 6; k++) s += raw[(i + k + Count) % Count];
            Curv[i] = s / 13f;
        }

        PlacePads();
        PlaceItems();
        PlaceRamps();

        var dist = new float[MapSize * MapSize];
        var sArr = new float[MapSize * MapSize];
        var latArr = new float[MapSize * MapSize];
        Rasterize(dist, sArr, latArr);
        DistanceTransform(dist);
        Colorize(dist, sArr, latArr);
        BuildMips();
        if (Theme.Walls) BuildWalls(dist);
        if (Theme.HasFacades) BuildFacades(dist);
        BuildStart();
        BuildRamps();
        if (Theme.Stands) BuildStands();
        PlaceScenery(dist);
        PlaceFlyers(dist);
        MiniMap = P.Where((_, i) => i % 4 == 0).ToArray();
    }

    // ---------------------------------------------------------------- centerline

    public static Vector2[] Centerline(Vector2[] control, out float total)
    {
        var dense = new List<Vector2>();
        var n = control.Length;
        for (var i = 0; i < n; i++)
        {
            var p0 = control[(i - 1 + n) % n];
            var p1 = control[i];
            var p2 = control[(i + 1) % n];
            var p3 = control[(i + 2) % n];
            for (var k = 0; k < 120; k++)
                dense.Add(CatmullRom(p0, p1, p2, p3, k / 120f));
        }

        var cum = new float[dense.Count + 1];
        for (var i = 0; i < dense.Count; i++)
            cum[i + 1] = cum[i] + Vector2.Distance(dense[i], dense[(i + 1) % dense.Count]);
        total = cum[^1];
        var count = (int)(total / Step);
        var pts = new Vector2[count];
        var j = 0;
        for (var i = 0; i < count; i++)
        {
            var s = i * total / count;
            while (cum[j + 1] < s) j++;
            var t = (s - cum[j]) / Math.Max(1e-4f, cum[j + 1] - cum[j]);
            pts[i] = Vector2.Lerp(dense[j], dense[(j + 1) % dense.Count], t);
        }

        return pts;
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                       (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    public int Wrap(int i) => ((i % Count) + Count) % Count;

    /// <summary>Nearest centerline sample, searched around <paramref name="hint"/>.</summary>
    public int Nearest(Vector2 pos, int hint, int back = 10, int ahead = 20)
    {
        var best = hint;
        var bestD = float.MaxValue;
        for (var k = -back; k <= ahead; k++)
        {
            var i = Wrap(hint + k);
            var d = Vector2.DistanceSquared(pos, P[i]);
            if (d < bestD) { bestD = d; best = i; }
        }

        return best;
    }

    public Vector2 At(int idx, float lat) => P[Wrap(idx)] + N[Wrap(idx)] * lat;

    /// <summary>Signed turn over the next <paramref name="samples"/> samples (radians).</summary>
    public float TurnAhead(int idx, int samples)
    {
        float s = 0;
        for (var k = 0; k < samples; k++) s += Curv[Wrap(idx + k)] * Step;
        return s;
    }

    private int StraightestNear(int idx, int range, int lookAhead = 14)
    {
        var best = idx;
        var bestV = float.MaxValue;
        for (var k = -range; k <= range; k++)
        {
            var i = Wrap(idx + k);
            var v = 0f;
            for (var j = 0; j < lookAhead; j++) v = MathF.Max(v, MathF.Abs(Curv[Wrap(i + j)]));
            if (v < bestV) { bestV = v; best = i; }
        }

        return best;
    }

    // ---------------------------------------------------------------- gameplay placements

    private void PlacePads()
    {
        foreach (var (frac, lat) in new[] { (0.235f, -14f), (0.47f, 13f), (0.71f, -12f), (0.9f, 14f) })
        {
            var i = StraightestNear((int)(Count * frac), 60, 75);
            Pads.Add(new BoostPad { Idx = i, Lat = lat });
        }
    }

    private void PlaceItems()
    {
        foreach (var frac in new[] { 0.14f, 0.42f, 0.64f, 0.84f })
        {
            var i = StraightestNear((int)(Count * frac), 30);
            _itemRows.Add(i);
            foreach (var lat in new[] { -22f, -7.5f, 7.5f, 22f })
                ItemBoxes.Add(At(i, lat));
        }

        foreach (var frac in new[] { 0.06f, 0.3f, 0.55f, 0.77f })
        {
            var i0 = (int)(Count * frac);
            for (var k = 0; k < 6; k++)
                Gems.Add(At(i0 + k * 3, 18f * MathF.Sin(k * 0.7f + frac * 9)));
        }
    }

    private void PlaceRamps()
    {
        foreach (var frac in new[] { 0.33f, 0.78f })
        {
            var i = StraightestNear((int)(Count * frac), 50, 30);
            for (var tries = 0; tries < 6; tries++)
            {
                var clash = Pads.Any(p => Math.Abs(Wrap(i - p.Idx + Count / 2) - Count / 2) < 22) ||
                            _itemRows.Any(r => Math.Abs(Wrap(i - r + Count / 2) - Count / 2) < 18);
                if (!clash) break;
                i = Wrap(i + 20);
            }

            Ramps.Add(i);
        }
    }

    private bool RampAt(float sm, out float u)
    {
        foreach (var r in Ramps)
        {
            u = sm - r * Step;
            if (u < 0) u += Length;
            if (u < RampSamples * Step) return true;
        }

        u = 0;
        return false;
    }

    public bool OnPad(int idx, float lat)
    {
        foreach (var p in Pads)
        {
            var di = Wrap(idx - p.Idx);
            if (di <= PadLength / Step && MathF.Abs(lat - p.Lat) < PadHalfWidth) return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- floor texture

    private void Rasterize(float[] dist, float[] sArr, float[] latArr)
    {
        Array.Fill(dist, 1e9f);
        const int bands = 32;
        var bandH = MapSize / bands;
        Parallel.For(0, bands, band =>
        {
            var y0 = band * bandH;
            var y1 = y0 + bandH;
            for (var i = 0; i < Count; i++)
            {
                var a = P[i];
                var b = P[(i + 1) % Count];
                var minY = (int)MathF.Max(y0, MathF.Min(a.Y, b.Y) - RasterRadius);
                var maxY = (int)MathF.Min(y1 - 1, MathF.Max(a.Y, b.Y) + RasterRadius);
                if (minY > maxY) continue;
                var minX = (int)MathF.Max(0, MathF.Min(a.X, b.X) - RasterRadius);
                var maxX = (int)MathF.Min(MapSize - 1, MathF.Max(a.X, b.X) + RasterRadius);
                var ab = b - a;
                var len2 = ab.LengthSquared();
                var segLen = MathF.Sqrt(len2);
                var s0 = i * Length / Count;
                for (var y = minY; y <= maxY; y++)
                {
                    var row = y * MapSize;
                    for (var x = minX; x <= maxX; x++)
                    {
                        var px = x + 0.5f - a.X;
                        var py = y + 0.5f - a.Y;
                        var t = Math.Clamp((px * ab.X + py * ab.Y) / len2, 0f, 1f);
                        var dx = px - ab.X * t;
                        var dy = py - ab.Y * t;
                        var d = MathF.Sqrt(dx * dx + dy * dy);
                        var o = row + x;
                        if (d >= dist[o]) continue;
                        dist[o] = d;
                        sArr[o] = s0 + t * segLen;
                        latArr[o] = ab.X * py - ab.Y * px >= 0 ? d : -d;
                    }
                }
            }
        });
    }

    /// <summary>Two-pass chamfer so the far field (water, lava islands, scenery) knows its distance too.</summary>
    private static void DistanceTransform(float[] d)
    {
        const float diag = 1.4142f;
        const int n = MapSize;
        for (var y = 1; y < n; y++)
        for (var x = 1; x < n - 1; x++)
        {
            var o = y * n + x;
            var v = d[o];
            v = MathF.Min(v, d[o - 1] + 1);
            v = MathF.Min(v, d[o - n] + 1);
            v = MathF.Min(v, d[o - n - 1] + diag);
            v = MathF.Min(v, d[o - n + 1] + diag);
            d[o] = v;
        }

        for (var y = n - 2; y >= 0; y--)
        for (var x = n - 2; x >= 1; x--)
        {
            var o = y * n + x;
            var v = d[o];
            v = MathF.Min(v, d[o + 1] + 1);
            v = MathF.Min(v, d[o + n] + 1);
            v = MathF.Min(v, d[o + n + 1] + diag);
            v = MathF.Min(v, d[o + n - 1] + diag);
            d[o] = v;
        }
    }

    private void Colorize(float[] dist, float[] sArr, float[] latArr)
    {
        var tex = new int[MapSize * MapSize];
        Mip[0] = tex;
        Parallel.For(0, MapSize, y =>
        {
            for (var x = 0; x < MapSize; x++)
            {
                var o = y * MapSize + x;
                tex[o] = FloorColor(x, y, dist[o], sArr[o], latArr[o]);
            }
        });
    }

    private int FloorColor(int x, int y, float d, float s, float lat)
    {
        var n = Gfx.Hash(x, y, 7);
        if (d >= RasterRadius - 0.5f) return Ground(x, y, n, d, 0, 0, 0, false);

        var sm = s % Length;
        if (sm < 0) sm += Length;
        var idx = Math.Clamp((int)(sm / Step), 0, Count - 1);

        if (d < HalfWidth) return Road(x, y, n, d, sm, lat, idx);

        if (d < CurbOuter)
        {
            var first = ((int)(sm / 7f) & 1) == 0;
            var c = CurbColor(first, sm);
            if (d > CurbOuter - 0.9f) c = Gfx.Shade(c, 0.72f);
            if (d < HalfWidth + 0.7f) c = Gfx.Shade(c, 0.82f);
            return c;
        }

        if (d < BandOuter) return BandColor(d, sm);
        return Ground(x, y, n, d, sm, lat, idx, true);
    }

    private int Road(int x, int y, float n, float d, float sm, float lat, int idx)
    {
        if (sm < 9f)
        {
            var c = ((int)(sm / 3f) + (int)((lat + 90f) / 3f)) & 1;
            return c == 0 ? Gfx.Rgb(245, 245, 245) : Gfx.Rgb(24, 24, 30);
        }

        if (RampAt(sm, out var ru))
        {
            if (ru > RampSamples * Step - 1.5f) return Gfx.Rgb(250, 250, 250);
            var stripe = ((int)MathF.Floor((ru + MathF.Abs(lat) * 0.6f) / 2.5f) & 1) == 0;
            var shade = 0.7f + 0.3f * ru / (RampSamples * Step);
            return Gfx.Shade(stripe ? Gfx.Rgb(255, 200, 30) : Gfx.Rgb(40, 40, 50), shade);
        }

        foreach (var p in Pads)
        {
            var u = sm - p.Idx * Step;
            if (u < 0) u += Length;
            var v = lat - p.Lat;
            if (u >= PadLength || MathF.Abs(v) >= PadHalfWidth) continue;
            if (u < 1.2f || u > PadLength - 1.2f || MathF.Abs(v) > PadHalfWidth - 1.2f)
                return Gfx.Rgb(60, 50, 20);
            var phase = (u - MathF.Abs(v) * 0.9f) / 7f;
            var b = (int)((phase - MathF.Floor(phase)) * 255);
            return Gfx.Argb(BoostMarker, b, 0, 0);
        }

        var gridS = Length - sm;
        if (gridS > 8 && gridS < 120 && MathF.Abs(MathF.Abs(lat) - 12f) < 7f)
        {
            var slot = (gridS - 14f) % 20f;
            if (slot > -0.5f && slot < 1.3f) return Gfx.Rgb(220, 220, 225);
        }

        var kind = Theme.Kind;
        var bigN = Gfx.Fbm(x / 48f, y / 48f, 3, 3);
        var v0 = (int)(bigN * 20) + (int)(n * 12) - 6;
        var edge = d > HalfWidth - 3f && d < HalfWidth - 1.6f;
        var center = MathF.Abs(lat) < 0.8f && sm % 32f < 15f;

        switch (kind)
        {
            case ThemeKind.Space:
            {
                if (edge) return Gfx.Rgb(80, 240, 255);
                var gs = sm / 8f - MathF.Floor(sm / 8f);
                var gl = (lat + 200) / 8f - MathF.Floor((lat + 200) / 8f);
                if (gs < 0.09f || gl < 0.09f) return Gfx.Rgb(92, 64, 190);
                return Gfx.Rgb(30 + v0 / 3, 20 + v0 / 4, 58 + v0 / 2);
            }
            case ThemeKind.Lava:
            {
                if (edge) return Gfx.Rgb(255, 140, 40);
                var crack = MathF.Abs(Gfx.Fbm(x / 30f, y / 30f, 2, 21) - 0.5f);
                if (crack < 0.006f) return Gfx.Rgb(255, 120, 30);
                if (crack < 0.011f) return Gfx.Rgb(120, 40, 22);
                return Gfx.Rgb(70 + v0, 62 + v0, 66 + v0);
            }
            case ThemeKind.City:
            {
                if (edge) return Gfx.Rgb(230, 230, 236);
                var al = MathF.Abs(lat);
                if (al is > 0.5f and < 1.5f) return Gfx.Rgb(250, 200, 40);
                if (MathF.Abs(al - 17f) < 0.7f && sm % 24f < 12f) return Gfx.Rgb(210, 210, 220);
                var puddle = Gfx.Fbm(x / 22f, y / 22f, 2, 41) > 0.68f;
                var c = Gfx.Rgb(44 + v0 / 2, 46 + v0 / 2, 60 + v0 / 2);
                return puddle ? Gfx.Rgb(30, 34, 70) : c;
            }
            case ThemeKind.Snow:
            {
                if (edge) return Gfx.Rgb(236, 240, 250);
                if (d > HalfWidth - 12 && Gfx.Fbm(x / 10f, y / 10f, 3, 13) > 0.6f) return Gfx.Rgb(222, 230, 246);
                if (center) return Gfx.Rgb(200, 210, 226);
                return Gfx.Rgb(116 + v0, 126 + v0, 148 + v0);
            }
            case ThemeKind.Beach:
            {
                if (edge) return Gfx.Rgb(240, 240, 240);
                if (d > HalfWidth - 10 && Gfx.Fbm(x / 9f, y / 9f, 3, 17) > 0.62f) return Gfx.Rgb(228, 208, 158);
                if (center) return Gfx.Rgb(212, 210, 206);
                return Gfx.Rgb(132 + v0, 126 + v0, 122 + v0);
            }
            case ThemeKind.Canyon:
            {
                if (edge) return Gfx.Rgb(240, 236, 226);
                if (center) return Gfx.Rgb(220, 210, 196);
                return Gfx.Rgb(124 + v0, 106 + v0, 96 + v0);
            }
            case ThemeKind.Autumn:
            {
                if (edge) return Gfx.Rgb(236, 236, 236);
                if (center) return Gfx.Rgb(205, 205, 210);
                var leaf = Gfx.Hash(x, y, 55);
                if (leaf < 0.008f) return LeafColor(leaf * 500);
                return Gfx.Rgb(96 + v0, 96 + v0, 104 + v0);
            }
            default:
            {
                var baseV = 92 + v0;
                var asphalt = Gfx.Rgb(baseV, baseV + 2, baseV + 14);
                var racing = MathF.Abs(lat - Curv[idx] * 1800f);
                if (racing < 6f) asphalt = Gfx.Shade(asphalt, 0.9f + racing / 60f);
                if (edge) return Gfx.Rgb(236, 236, 236);
                if (center) return Gfx.Rgb(205, 205, 210);
                return asphalt;
            }
        }
    }

    private static int LeafColor(float t) => (int)t switch
    {
        0 => Gfx.Rgb(236, 120, 30),
        1 => Gfx.Rgb(200, 50, 30),
        2 => Gfx.Rgb(244, 200, 60),
        _ => Gfx.Rgb(150, 90, 40)
    };

    private int CurbColor(bool first, float sm) => Theme.Kind switch
    {
        ThemeKind.Beach => first ? Gfx.Rgb(30, 120, 230) : Gfx.Rgb(246, 246, 246),
        ThemeKind.Canyon => first ? Gfx.Rgb(240, 120, 30) : Gfx.Rgb(246, 240, 230),
        ThemeKind.Autumn => first ? Gfx.Rgb(220, 40, 30) : Gfx.Rgb(250, 210, 50),
        ThemeKind.Snow => first ? Gfx.Rgb(40, 110, 230) : Gfx.Rgb(246, 248, 255),
        ThemeKind.City => first ? Gfx.Rgb(255, 50, 190) : Gfx.Rgb(30, 30, 44),
        ThemeKind.Lava => first ? Gfx.Rgb(255, 120, 30) : Gfx.Rgb(30, 26, 30),
        ThemeKind.Space => first ? Gfx.Rgb(60, 230, 255) : Gfx.Rgb(20, 16, 60),
        _ => first ? Gfx.Rgb(228, 36, 48) : Gfx.Rgb(246, 246, 246)
    };

    private int BandColor(float d, float sm)
    {
        var inner = d < CurbOuter + 0.8f;
        return Theme.Kind switch
        {
            ThemeKind.Beach => inner ? Gfx.Rgb(30, 120, 230) : Gfx.Rgb(248, 248, 248),
            ThemeKind.Canyon => inner ? Gfx.Rgb(250, 250, 250) : Gfx.Rgb(110, 64, 40),
            ThemeKind.Autumn => inner ? Gfx.Rgb(250, 250, 250) : Gfx.Rgb(120, 70, 40),
            ThemeKind.Snow => inner ? Gfx.Rgb(250, 250, 255) : Gfx.Rgb(170, 200, 246),
            ThemeKind.City => inner ? Gfx.Rgb(20, 20, 30) : Gfx.Rgb(40, 240, 255),
            ThemeKind.Lava => inner ? Gfx.Rgb(255, 160, 60) : Gfx.Rgb(42, 36, 38),
            ThemeKind.Space => inner ? Gfx.Rgb(255, 80, 220) : Gfx.Rgb(150, 30, 150),
            _ => inner ? Gfx.Rgb(250, 250, 250) : Gfx.Rgb(36, 92, 222)
        };
    }

    private int Ground(int x, int y, float n, float d, float sm, float lat, int idx, bool near)
    {
        var wallShadow = Theme.Walls && near && d < WallDist + 1.5f && d > WallDist - 2.5f;
        switch (Theme.Kind)
        {
            case ThemeKind.Beach:
            {
                if (d < 150)
                {
                    if (wallShadow) return Gfx.Rgb(170, 150, 110);
                    var v = Gfx.Fbm(x / 30f, y / 30f, 3, 8);
                    var shell = Gfx.Hash(x, y, 61);
                    if (shell < 0.0015f) return shell < 0.0008f ? Gfx.Rgb(255, 250, 240) : Gfx.Rgb(250, 180, 190);
                    return Gfx.Add(Gfx.Lerp(Gfx.Rgb(212, 188, 136), Gfx.Rgb(246, 230, 186), v), (int)(n * 10 - 5));
                }

                if (d < 168) return Gfx.Lerp(Gfx.Rgb(200, 178, 132), Gfx.Rgb(170, 150, 110), (d - 150) / 18f);
                var depth = Math.Clamp((d - 168) / 240f, 0, 1);
                var phase = Gfx.Fbm(x / 26f, y / 26f, 2, 31) + d / 40f;
                return Gfx.Argb(WaterMarker, (int)((phase - MathF.Floor(phase)) * 255), (int)(depth * 255), 0);
            }
            case ThemeKind.Canyon:
            {
                if (wallShadow) return Gfx.Rgb(130, 66, 40);
                var v = Gfx.Fbm(x / 40f, y / 40f, 3, 9);
                var c = Gfx.Lerp(Gfx.Rgb(172, 88, 50), Gfx.Rgb(214, 128, 72), v);
                if (Gfx.Hash(x, y, 71) < 0.012f) c = Gfx.Rgb(120, 70, 50);
                var crack = MathF.Abs(Gfx.Fbm(x / 60f, y / 60f, 2, 12) - 0.5f) < 0.008f;
                if (crack && !near) c = Gfx.Rgb(110, 56, 34);
                return Gfx.Add(c, (int)(n * 12 - 6));
            }
            case ThemeKind.Autumn:
            {
                if (wallShadow) return Gfx.Rgb(70, 90, 40);
                var leaf = Gfx.Hash(x, y, 91);
                if (leaf < 0.05f) return LeafColor(leaf * 80);
                var light = near ? ((int)(sm / 20f) & 1) == 0 : (((x + y) >> 5) & 1) == 0;
                var patch = Gfx.Fbm(x / 60f, y / 60f, 3, 5);
                var v = (int)(n * 14 - 7 + patch * 22);
                return light ? Gfx.Rgb(134 + v, 168 + v, 66 + v / 3) : Gfx.Rgb(118 + v, 150 + v, 58 + v / 3);
            }
            case ThemeKind.Snow:
            {
                if (wallShadow) return Gfx.Rgb(170, 186, 220);
                if (Gfx.Hash(x, y, 33) < 0.004f) return Gfx.Rgb(255, 255, 255);
                var v = Gfx.Fbm(x / 36f, y / 36f, 3, 6);
                return Gfx.Lerp(Gfx.Rgb(190, 206, 236), Gfx.Rgb(244, 248, 255), v);
            }
            case ThemeKind.City:
            {
                if (near && d < WallDist + 3)
                {
                    if (wallShadow) return Gfx.Rgb(70, 70, 84);
                    var tile = x % 6 == 0 || y % 6 == 0;
                    return tile ? Gfx.Rgb(96, 96, 112) : Gfx.Add(Gfx.Rgb(138, 138, 152), (int)(n * 10 - 5));
                }

                var big = x % 16 == 0 || y % 16 == 0;
                return big ? Gfx.Rgb(40, 40, 52) : Gfx.Add(Gfx.Rgb(58, 60, 74), (int)(n * 8 - 4));
            }
            case ThemeKind.Lava:
            {
                if (d < HazardDist)
                {
                    if (d > HazardDist - 1) return Gfx.Rgb(220, 80, 20);
                    return Gfx.Add(Gfx.Rgb(54, 46, 48), (int)(n * 12 - 6));
                }

                var island = Gfx.Fbm(x / 110f, y / 110f, 3, 44);
                if (island > 0.64f) return Gfx.Add(Gfx.Rgb(50, 42, 44), (int)(n * 10 - 5));
                if (island > 0.625f) return Gfx.Rgb(230, 96, 24);
                var phase = Gfx.Fbm(x / 24f, y / 24f, 3, 18);
                var heat = Gfx.Fbm(x / 70f, y / 70f, 2, 19);
                return Gfx.Argb(LavaMarker, (int)(phase * 255), (int)(heat * 255), 0);
            }
            case ThemeKind.Space:
            {
                var star = Gfx.Hash(x, y, 77);
                if (star < 0.005f) return Gfx.Argb(StarMarker, (int)(star / 0.005f * 255), 0, 0);
                var neb = Gfx.Fbm(x / 220f, y / 220f, 4, 50);
                var t = Math.Clamp((neb - 0.45f) * 2.4f, 0, 1);
                return Gfx.Lerp(Gfx.Rgb(6, 4, 16), Gfx.Rgb(48, 18, 70), t * t);
            }
            default:
            {
                if (near)
                {
                    var outer = MathF.Sign(lat) == -MathF.Sign(Curv[idx]);
                    var sandAmt = outer ? Math.Clamp((MathF.Abs(Curv[idx]) - 0.0035f) / 0.004f, 0f, 1f) : 0f;
                    var edgeJitter = Gfx.ValueNoise(x / 6f, y / 6f, 11) * 5f;
                    if (sandAmt > 0 && d - BandOuter < sandAmt * (WallDist - BandOuter + 6) - edgeJitter)
                    {
                        var ripple = MathF.Sin((sm + lat * 0.3f) * 0.9f) * 6f;
                        var sv = (int)(n * 14 + ripple);
                        return Gfx.Rgb(226 + sv, 196 + sv, 128 + sv / 2);
                    }

                    if (wallShadow) return Gfx.Rgb(40, 96, 40);
                    return Grass(x, y, n, ((int)(sm / 20f) & 1) == 0);
                }

                return Grass(x, y, n, (((x + y) >> 5) & 1) == 0);
            }
        }
    }

    private static int Grass(int x, int y, float n, bool light)
    {
        var flower = Gfx.Hash(x, y, 91);
        if (flower < 0.0035f)
        {
            return (int)(flower * 1000) switch
            {
                0 => Gfx.Rgb(255, 240, 90),
                1 => Gfx.Rgb(255, 255, 255),
                2 => Gfx.Rgb(255, 120, 170),
                _ => Gfx.Rgb(255, 200, 60)
            };
        }

        var patch = Gfx.Fbm(x / 60f, y / 60f, 3, 5);
        var g = light ? 168 : 148;
        var v = (int)(n * 16 - 8 + patch * 26);
        return Gfx.Rgb(62 + v / 2, g + v, 54 + v / 3);
    }

    public static readonly int WaterShallow = Gfx.Rgb(70, 206, 214);
    public static readonly int WaterDeep = Gfx.Rgb(20, 84, 184);

    /// <summary>Static stand-in colour for animated texels (used for mips).</summary>
    public static int Resolve(int c) => ((c >> 24) & 255) switch
    {
        BoostMarker => Gfx.Rgb(255, 170, 40),
        WaterMarker => Gfx.Lerp(WaterShallow, WaterDeep, ((c >> 8) & 255) / 255f),
        LavaMarker => Gfx.Rgb(240, 90, 20),
        StarMarker => Gfx.Rgb(60, 60, 90),
        _ => c
    };

    private void BuildMips()
    {
        for (var l = 1; l < MipLevels; l++)
        {
            var src = Mip[l - 1];
            var sw = MapSize >> (l - 1);
            var dw = sw >> 1;
            var dst = new int[dw * dw];
            Parallel.For(0, dw, y =>
            {
                for (var x = 0; x < dw; x++)
                {
                    var o = y * 2 * sw + x * 2;
                    var a = Resolve(src[o]);
                    var b = Resolve(src[o + 1]);
                    var c = Resolve(src[o + sw]);
                    var d = Resolve(src[o + sw + 1]);
                    dst[y * dw + x] = Gfx.Rgb(
                        (Gfx.R(a) + Gfx.R(b) + Gfx.R(c) + Gfx.R(d)) >> 2,
                        (Gfx.G(a) + Gfx.G(b) + Gfx.G(c) + Gfx.G(d)) >> 2,
                        (Gfx.B(a) + Gfx.B(b) + Gfx.B(c) + Gfx.B(d)) >> 2);
                }
            });
            Mip[l] = dst;
        }
    }

    private static float DistAt(float[] dist, Vector2 p)
    {
        var x = (int)p.X;
        var y = (int)p.Y;
        if ((uint)x >= MapSize || (uint)y >= MapSize) return 0;
        return dist[y * MapSize + x];
    }

    // ---------------------------------------------------------------- 3D scenery

    private void SideQuads(float[] dist, float lat, float height, Tex tex, float density, bool flipRight)
    {
        const int every = 3;
        foreach (var side in new[] { -1f, 1f })
        {
            var u = 0f;
            var dir = flipRight && side > 0 ? -1f : 1f;
            for (var i = 0; i < Count; i += every)
            {
                var j = (i + every) % Count;
                var a = P[i] + N[i] * side * lat;
                var b = P[j] + N[j] * side * lat;
                var len = Vector2.Distance(a, b);
                var okA = DistAt(dist, a) > lat - 5f;
                var okB = DistAt(dist, b) > lat - 5f;
                if (okA && okB && len < Step * every * 2.2f)
                {
                    Quads.Add(new Quad
                    {
                        A = a, B = b, Z0 = 0, Z1 = height,
                        U0 = dir * u * density, U1 = dir * (u + len) * density, Tex = tex
                    });
                }

                u += len;
            }
        }
    }

    private void BuildWalls(float[] dist) =>
        SideQuads(dist, WallDist, Theme.WallHeight, Art.WallTexture(Theme.Kind), Art.WallDensity, false);

    private void BuildFacades(float[] dist) =>
        SideQuads(dist, Theme.FacadeLat, Theme.FacadeHeight, Art.FacadeTexture(Theme.Kind), Art.FacadeDensity, true);

    private void BuildStart()
    {
        var i0 = 2;
        var p = P[i0];
        var nrm = N[i0];
        const float span = HalfWidth + 6f;
        const float pillar = 8f;
        var pillarTex = Art.PillarTexture(Theme.Kind);
        var bannerTex = Art.BannerTexture(Theme.Kind, Def.Name);
        Quads.Add(new Quad
        {
            A = p - nrm * (span + pillar), B = p - nrm * span, Z0 = 0, Z1 = 34, U0 = 0, U1 = pillarTex.W,
            Tex = pillarTex
        });
        Quads.Add(new Quad
        {
            A = p + nrm * span, B = p + nrm * (span + pillar), Z0 = 0, Z1 = 34, U0 = 0, U1 = pillarTex.W,
            Tex = pillarTex
        });
        Quads.Add(new Quad
        {
            A = p - nrm * span, B = p + nrm * span, Z0 = 22, Z1 = 33, U0 = 0, U1 = bannerTex.W, Tex = bannerTex
        });

        if (Theme.Flags)
            for (var k = -60; k < 150; k += 10)
            foreach (var side in new[] { -1f, 1f })
                Scenery.Add(new Scenery
                {
                    Pos = At(k, side * (WallDist + 7f)), Kind = SceneryKind.FlagPole, Variant = (k / 10 + 20) % 5
                });

        if (Theme.Lamps)
            for (var k = 0; k < Count; k += 9)
            foreach (var side in new[] { -1f, 1f })
                Scenery.Add(new Scenery { Pos = At(k, side * (WallDist + 6f)), Kind = SceneryKind.Lamp });
    }

    private void BuildRamps()
    {
        var side = Art.RampSideTexture();
        var face = Art.RampFaceTexture();
        foreach (var r in Ramps)
        {
            var e = Wrap(r + RampSamples);
            foreach (var s in new[] { -1f, 1f })
                Quads.Add(new Quad
                {
                    A = At(r, s * HalfWidth), B = At(e, s * HalfWidth), Z0 = 0, Z1 = Race.RampHeight,
                    U0 = 0, U1 = side.W, Tex = side
                });
            Quads.Add(new Quad
            {
                A = At(e, -HalfWidth), B = At(e, HalfWidth), Z0 = 0, Z1 = Race.RampHeight, U0 = 0, U1 = face.W,
                Tex = face
            });
        }
    }

    private void PlaceFlyers(float[] dist)
    {
        var rnd = new Random(77 + (int)Theme.Kind);
        var balloons = Theme.Kind is ThemeKind.Stadium or ThemeKind.Autumn or ThemeKind.Canyon or ThemeKind.Beach;
        for (var n = 0; balloons && n < 14; n++)
        {
            var p = new Vector2(rnd.Next(100, MapSize - 100), rnd.Next(100, MapSize - 100));
            if (DistAt(dist, p) < 180) { n--; continue; }
            Scenery.Add(new Scenery { Pos = p, Kind = SceneryKind.Balloon, Variant = rnd.Next(3), Z = 60 + rnd.Next(90) });
        }

        if (Theme.Kind != ThemeKind.Beach) return;
        for (var n = 0; n < 14; n++)
        {
            var i = rnd.Next(Count);
            var p = At(i, (rnd.Next(2) == 0 ? -1 : 1) * (70 + rnd.Next(120)));
            Scenery.Add(new Scenery { Pos = p, Kind = SceneryKind.Gull, Variant = rnd.Next(2), Z = 24 + rnd.Next(26) });
        }
    }

    private void BuildStands()
    {
        var standTex = Art.StandTexture();
        foreach (var (side, from, to) in new[] { (1f, -70, 150), (-1f, -40, 120) })
        {
            var u = 0f;
            var dir = side > 0 ? -1f : 1f;
            for (var k = from; k < to; k += 3)
            {
                var a = At(k, side * 112f);
                var b = At(k + 3, side * 112f);
                var len = Vector2.Distance(a, b);
                Quads.Add(new Quad
                {
                    A = a, B = b, Z0 = 0, Z1 = 52,
                    U0 = dir * u * Art.StandDensity, U1 = dir * (u + len) * Art.StandDensity, Tex = standTex
                });
                u += len;
            }
        }
    }

    private bool NearStands(Vector2 p)
    {
        if (!Theme.Stands) return false;
        for (var k = -90; k < 170; k += 2)
            if (Vector2.DistanceSquared(p, P[Wrap(k)]) < 190f * 190f) return true;
        return false;
    }

    private void PlaceScenery(float[] dist)
    {
        if (Theme.SceneryCount <= 0 || Theme.Scenery.Length == 0) return;
        var rnd = new Random(1503 + (int)Theme.Kind * 31);
        const float minSpacing = 22f;
        var cell = new Dictionary<(int, int), List<Vector2>>();
        bool Free(Vector2 p)
        {
            var cx = (int)(p.X / minSpacing);
            var cy = (int)(p.Y / minSpacing);
            for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
                if (cell.TryGetValue((cx + ox, cy + oy), out var l))
                    foreach (var q in l)
                        if (Vector2.DistanceSquared(p, q) < minSpacing * minSpacing) return false;
            return true;
        }

        var minDist = Theme.Hazard ? BandOuter + 30f : WallDist + 18f;
        var total = Theme.Scenery.Sum(s => s.Weight);
        var placed = 0;
        for (var attempt = 0; attempt < 24000 && placed < Theme.SceneryCount; attempt++)
        {
            var p = new Vector2(rnd.Next(30, MapSize - 30), rnd.Next(30, MapSize - 30));
            var d = DistAt(dist, p);
            if (d < minDist || d > Theme.SceneryMaxDist) continue;
            if (d > 300 && rnd.NextDouble() < 0.6) continue;
            if (Theme.HasFacades && d > Theme.FacadeLat - 12 && d < Theme.FacadeLat + 60) continue;
            if (NearStands(p) || !Free(p)) continue;

            var roll = (float)rnd.NextDouble() * total;
            var kind = Theme.Scenery[0].Kind;
            foreach (var (k, w) in Theme.Scenery)
            {
                if (roll < w) { kind = k; break; }
                roll -= w;
            }

            if (kind == SceneryKind.Parasol && d > 125) kind = SceneryKind.Palm;
            var z = Theme.SceneryZ > 0 ? 6f + (float)rnd.NextDouble() * 70f : 0f;
            Scenery.Add(new Scenery { Pos = p, Kind = kind, Variant = rnd.Next(3), Z = z });
            var key = ((int)(p.X / minSpacing), (int)(p.Y / minSpacing));
            if (!cell.TryGetValue(key, out var list)) cell[key] = list = new List<Vector2>();
            list.Add(p);
            placed++;
        }
    }
}
