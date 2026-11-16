using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

internal enum ThemeKind { Stadium, Beach, Canyon, Autumn, Snow, City, Lava, Space }

/// <summary>Look and physics knobs shared by every track of one theme.</summary>
internal sealed class Theme
{
    public ThemeKind Kind;
    public int Fog;
    public float FogStart = 260, FogRange = 1500, FogMax = 0.92f;
    public float Ambient = 1f;
    public bool Walls = true;
    public float WallHeight = 5f;
    public bool Hazard;
    public float RoadGrip = 1f;
    public float OffroadSpeed = 0.48f;
    public float FacadeLat, FacadeHeight;
    public bool Stands, Flags, Lamps;
    public (SceneryKind Kind, float Weight)[] Scenery = Array.Empty<(SceneryKind, float)>();
    public int SceneryCount = 1100;
    public float SceneryMaxDist = float.MaxValue;
    public float SceneryZ;
    public int MiniColor;

    public bool HasFacades => FacadeHeight > 0;
}

internal sealed record TrackDef(string Name, ThemeKind ThemeKind, Vector2[] Control)
{
    public Theme Theme => Circuits.ThemeFor(ThemeKind);
}

internal sealed record Cup(string Name, int Emblem, TrackDef[] Tracks);

internal static class Circuits
{
    private static Vector2[] P(params int[] xy)
    {
        var v = new Vector2[xy.Length / 2];
        for (var i = 0; i < v.Length; i++) v[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
        return v;
    }

    public static readonly Cup[] Cups =
    {
        new("SUNRISE CUP", 0, new[]
        {
            new TrackDef("SUNSET STADIUM", ThemeKind.Stadium, P(
                760, 1725, 1100, 1730, 1480, 1705, 1760, 1580, 1840, 1330, 1720, 1120, 1470, 1070, 1310, 900,
                1400, 650, 1640, 500, 1620, 290, 1330, 220, 1030, 320, 840, 560, 620, 540, 400, 380, 240, 540,
                290, 820, 510, 960, 480, 1210, 300, 1390, 330, 1610, 480, 1715)),
            new TrackDef("PALM BEACH", ThemeKind.Beach, P(
                500, 1520, 900, 1650, 1300, 1600, 1650, 1450, 1780, 1150, 1650, 900, 1400, 850, 1200, 700,
                1250, 450, 1000, 300, 650, 350, 450, 600, 560, 850, 400, 1100, 330, 1320)),
            new TrackDef("CANYON RUN", ThemeKind.Canyon, P(
                400, 1700, 800, 1720, 1100, 1600, 1150, 1350, 950, 1200, 900, 950, 1150, 800, 1450, 900,
                1700, 750, 1750, 450, 1500, 300, 1150, 350, 850, 450, 550, 350, 300, 550, 350, 900, 550, 1100,
                450, 1420)),
            new TrackDef("MAPLE VALLEY", ThemeKind.Autumn, P(
                600, 1750, 1000, 1700, 1350, 1750, 1700, 1600, 1800, 1300, 1550, 1150, 1650, 900, 1800, 650,
                1600, 350, 1250, 300, 1050, 550, 800, 700, 550, 550, 300, 700, 350, 1000, 650, 1100, 700, 1350,
                420, 1520))
        }),
        new("MOONLIGHT CUP", 1, new[]
        {
            new TrackDef("FROST PEAK", ThemeKind.Snow, P(
                500, 1650, 950, 1750, 1400, 1700, 1750, 1500, 1700, 1150, 1350, 1050, 1100, 1250, 800, 1250,
                650, 1000, 900, 800, 1300, 800, 1650, 650, 1700, 350, 1300, 250, 800, 300, 400, 400, 280, 800,
                300, 1250)),
            new TrackDef("NEON CITY", ThemeKind.City, P(
                400, 1700, 1000, 1700, 1600, 1700, 1720, 1520, 1700, 1120, 1550, 1000, 1100, 1000, 980, 880,
                1000, 600, 1150, 470, 1600, 470, 1760, 360, 1620, 240, 700, 240, 450, 360, 400, 700, 600, 860,
                600, 1150, 400, 1330)),
            new TrackDef("LAVA KEEP", ThemeKind.Lava, P(
                500, 1700, 1000, 1650, 1300, 1750, 1650, 1650, 1750, 1350, 1500, 1200, 1600, 950, 1750, 700,
                1550, 400, 1200, 450, 1000, 750, 750, 800, 650, 550, 850, 350, 600, 230, 300, 400, 300, 800,
                500, 1050, 350, 1350)),
            new TrackDef("NEBULA LOOP", ThemeKind.Space, P(
                300, 1500, 700, 1750, 1200, 1700, 1500, 1450, 1400, 1150, 1000, 1050, 900, 800, 1200, 600,
                1600, 700, 1800, 500, 1600, 250, 1000, 250, 600, 400, 350, 700, 500, 1000, 300, 1250))
        })
    };

    public static IEnumerable<TrackDef> All => Cups.SelectMany(c => c.Tracks);

    private static readonly Dictionary<ThemeKind, Theme> Themes = new()
    {
        [ThemeKind.Stadium] = new Theme
        {
            Kind = ThemeKind.Stadium, Fog = Gfx.Rgb(168, 204, 238), Stands = true, Flags = true,
            Scenery = new[] { (SceneryKind.RoundTree, 0.42f), (SceneryKind.PineTree, 0.28f),
                (SceneryKind.BlossomTree, 0.12f), (SceneryKind.Bush, 0.18f) },
            MiniColor = Gfx.Rgb(150, 214, 255)
        },
        [ThemeKind.Beach] = new Theme
        {
            Kind = ThemeKind.Beach, Fog = Gfx.Rgb(186, 222, 244), WallHeight = 3.5f, Flags = true,
            OffroadSpeed = 0.55f, Scenery = new[] { (SceneryKind.Palm, 0.62f), (SceneryKind.Parasol, 0.18f),
                (SceneryKind.Bush, 0.2f) },
            SceneryMaxDist = 165, SceneryCount = 700, MiniColor = Gfx.Rgb(255, 226, 140)
        },
        [ThemeKind.Canyon] = new Theme
        {
            Kind = ThemeKind.Canyon, Fog = Gfx.Rgb(236, 200, 160), WallHeight = 4f, Flags = true,
            OffroadSpeed = 0.52f, FacadeLat = 118, FacadeHeight = 70,
            Scenery = new[] { (SceneryKind.Cactus, 0.5f), (SceneryKind.DesertRock, 0.5f) },
            SceneryCount = 900, MiniColor = Gfx.Rgb(255, 160, 90)
        },
        [ThemeKind.Autumn] = new Theme
        {
            Kind = ThemeKind.Autumn, Fog = Gfx.Rgb(214, 206, 214), Flags = true,
            Scenery = new[] { (SceneryKind.AutumnTree, 0.58f), (SceneryKind.PineTree, 0.24f),
                (SceneryKind.Bush, 0.18f) },
            MiniColor = Gfx.Rgb(255, 150, 60)
        },
        [ThemeKind.Snow] = new Theme
        {
            Kind = ThemeKind.Snow, Fog = Gfx.Rgb(222, 232, 248), RoadGrip = 0.55f, OffroadSpeed = 0.45f,
            Flags = true, Scenery = new[] { (SceneryKind.SnowPine, 0.75f), (SceneryKind.SnowRock, 0.25f) },
            MiniColor = Gfx.Rgb(200, 236, 255)
        },
        [ThemeKind.City] = new Theme
        {
            Kind = ThemeKind.City, Fog = Gfx.Rgb(26, 26, 58), FogStart = 180, FogRange = 1200, FogMax = 0.95f,
            Ambient = 0.82f, Lamps = true, FacadeLat = 100, FacadeHeight = 110, SceneryCount = 0,
            MiniColor = Gfx.Rgb(255, 80, 220)
        },
        [ThemeKind.Lava] = new Theme
        {
            Kind = ThemeKind.Lava, Fog = Gfx.Rgb(64, 22, 18), FogStart = 200, FogRange = 1300, Ambient = 0.9f,
            Walls = false, Hazard = true, FacadeLat = 98, FacadeHeight = 42,
            Scenery = new[] { (SceneryKind.LavaRock, 1f) }, SceneryCount = 500, MiniColor = Gfx.Rgb(255, 110, 40)
        },
        [ThemeKind.Space] = new Theme
        {
            Kind = ThemeKind.Space, Fog = Gfx.Rgb(8, 6, 22), FogStart = 500, FogRange = 1600, FogMax = 0.8f,
            Walls = false, Hazard = true, Scenery = new[] { (SceneryKind.Asteroid, 1f) }, SceneryCount = 260,
            SceneryZ = 1, MiniColor = Gfx.Rgb(170, 120, 255)
        }
    };

    public static Theme ThemeFor(ThemeKind kind) => Themes[kind];

    public static readonly int[] Points = { 15, 12, 10, 8, 6, 4, 2, 1 };
}
