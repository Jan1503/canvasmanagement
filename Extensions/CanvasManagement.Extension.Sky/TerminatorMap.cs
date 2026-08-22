using System.Runtime.InteropServices;
using SkiaSharp;

namespace CanvasManagement.Extension.Sky;

/// <summary>
///     Schematic equirectangular land mask + day/night shading for the terminator view.
///     Polygons are original simplified coastlines (not a copyrighted bitmap).
/// </summary>
internal static class TerminatorMap
{
    public static byte[] BuildLand(int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var ring in Continents)
            DrawRing(c, fill, ring, w, h);

        // Antarctica as a polar cap (cylindrical maps stretch it — that's expected).
        var antY = LatToY(-61, h);
        c.DrawRect(0, antY, w, Math.Max(1, h - antY + 1), fill);

        var types = new byte[w * h];
        var ptr = bmp.GetPixels();
        var row = bmp.RowBytes;
        for (var y = 0; y < h; y++)
        {
            var lat = 90.0 - (y + 0.5) / h * 180.0;
            for (var x = 0; x < w; x++)
            {
                var a = Marshal.ReadByte(ptr, y * row + x * 4 + 3);
                if (a < 70) continue;
                var lon = (x + 0.5) / w * 360.0 - 180.0;
                types[y * w + x] = Classify(lat, lon);
            }
        }

        return types;
    }

    public static SKColor Shade(byte land, double alt, double lat, double lon, int x, int y)
    {
        byte Lerp(byte a, byte b, double t) => (byte)(a + (b - a) * Math.Clamp(t, 0, 1));
        SKColor Mix(SKColor a, SKColor b, double t) =>
            new(Lerp(a.Red, b.Red, t), Lerp(a.Green, b.Green, t), Lerp(a.Blue, b.Blue, t));

        var day = land switch
        {
            2 => new SKColor(228, 236, 244),
            3 => new SKColor(186, 150, 78),
            1 => Math.Abs(lat) > 55 ? new SKColor(90, 120, 88) : new SKColor(42, 108, 52),
            _ => OceanDay(lat)
        };
        var night = land switch
        {
            2 => new SKColor(28, 36, 52),
            3 => new SKColor(22, 16, 12),
            1 => new SKColor(8, 14, 16),
            _ => new SKColor(3, 7, 20)
        };

        SKColor color;
        if (alt > 8) color = day;
        else if (alt < -12) color = night;
        else if (alt < 0)
        {
            var t = (alt + 12) / 12.0;
            var dusk = new SKColor(70, 28, 78);
            color = Mix(night, dusk, t);
        }
        else
        {
            var t = alt / 8.0;
            var dawn = new SKColor(255, 118, 52);
            color = Mix(Mix(new SKColor(90, 36, 80), dawn, 0.55), day, t);
        }

        // Specular ocean near the subsolar point.
        if (land == 0 && alt > 25)
        {
            var glint = Math.Clamp((alt - 25) / 65.0, 0, 1);
            color = Mix(color, new SKColor(120, 190, 230), glint * 0.35);
        }

        // City lights on the night side of populated land.
        if (land is 1 or 3 && alt < -6 && CityLight(lat, lon, x, y))
            color = Mix(color, new SKColor(255, 210, 120), 0.55);

        // Sparse stars over night ocean.
        if (land == 0 && alt < -10 && Hash(x * 7349 + y * 9133) > 0.997)
            color = new SKColor(220, 230, 255);

        return color;
    }

    private static SKColor OceanDay(double lat)
    {
        var t = Math.Clamp((Math.Abs(lat) - 10) / 70.0, 0, 1);
        return new SKColor(
            (byte)(16 + 8 * t),
            (byte)(58 - 18 * t),
            (byte)(118 - 28 * t));
    }

    private static byte Classify(double lat, double lon)
    {
        if (lat > 72 || lat < -60) return 2;
        if (lat > 60 && lon is > -75 and < -10) return 2; // Greenland
        if (IsDesert(lat, lon)) return 3;
        return 1;
    }

    private static bool IsDesert(double lat, double lon) =>
        (lat is > 14 and < 32 && lon is > -17 and < 58) ||
        (lat is > 22 and < 42 && lon is > 44 and < 62) ||
        (lat is < -18 and > -32 && lon is > 114 and < 146);

    private static bool CityLight(double lat, double lon, int x, int y)
    {
        if (Math.Abs(lat) is < 18 or > 58) return false;
        var populated =
            (lon is > -90 and < -67 && lat is > 30 and < 48) ||   // US east
            (lon is > -125 and < -117 && lat is > 32 and < 49) || // US west
            (lon is > -10 and < 28 && lat is > 38 and < 56) ||    // Europe
            (lon is > 68 and < 90 && lat is > 8 and < 32) ||      // India
            (lon is > 103 and < 140 && lat is > 22 and < 42) ||   // East Asia
            (lon is > -50 and < -40 && lat is > -26 and < -12);   // SE Brazil
        if (!populated) return false;
        return Hash(x * 1931 + y * 6151) > 0.91;
    }

    private static double Hash(int n)
    {
        unchecked
        {
            var x = (uint)n;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x / (double)uint.MaxValue;
        }
    }

    private static void DrawRing(SKCanvas c, SKPaint paint, (float Lon, float Lat)[] ring, int w, int h)
    {
        using var path = new SKPath();
        var first = true;
        foreach (var (lon, lat) in ring)
        {
            var x = (float)((lon + 180) / 360.0 * w);
            var y = LatToY(lat, h);
            if (first) { path.MoveTo(x, y); first = false; }
            else path.LineTo(x, y);
        }

        path.Close();
        c.DrawPath(path, paint);
    }

    private static float LatToY(double lat, int h) => (float)((90 - lat) / 180.0 * h);

    // Simplified original coastlines — enough to read as a world map at LED resolution.
    private static readonly (float Lon, float Lat)[][] Continents =
    [
        // North America
        [
            (-168f, 66f), (-155f, 71f), (-141f, 70f), (-120f, 70f), (-105f, 73f), (-88f, 70f),
            (-82f, 63f), (-70f, 60f), (-55f, 51f), (-60f, 47f), (-67f, 44f), (-74f, 40f),
            (-76f, 35f), (-81f, 31f), (-80f, 25f), (-82f, 25f), (-87f, 21f), (-90f, 18f),
            (-97f, 16f), (-105f, 22f), (-110f, 23f), (-115f, 27f), (-117f, 32f), (-124f, 40f),
            (-125f, 49f), (-130f, 55f), (-140f, 60f), (-153f, 58f), (-165f, 55f), (-168f, 63f)
        ],
        // South America
        [
            (-81f, 12f), (-77f, 12f), (-70f, 12f), (-60f, 8f), (-52f, 4f), (-50f, 1f),
            (-38f, 0f), (-35f, -2f), (-35f, -8f), (-40f, -22f), (-48f, -28f), (-54f, -34f),
            (-58f, -39f), (-62f, -40f), (-63f, -50f), (-65f, -55f), (-68f, -56f), (-74f, -52f),
            (-75f, -47f), (-73f, -42f), (-71f, -30f), (-70f, -18f), (-77f, -8f), (-79f, -5f),
            (-80f, 1f), (-77f, 8f)
        ],
        // Greenland
        [
            (-73f, 76f), (-60f, 82f), (-40f, 83f), (-22f, 74f), (-20f, 70f), (-30f, 68f),
            (-44f, 60f), (-50f, 64f), (-65f, 69f)
        ],
        // Africa
        [
            (-17f, 21f), (-16f, 28f), (-13f, 32f), (-8f, 33f), (-5f, 36f), (0f, 37f),
            (10f, 37f), (11f, 33f), (25f, 32f), (32f, 31f), (36f, 28f), (43f, 12f),
            (51f, 12f), (49f, 4f), (43f, -1f), (42f, -8f), (40f, -16f), (35f, -20f),
            (32f, -26f), (29f, -32f), (20f, -35f), (18f, -34f), (14f, -27f), (12f, -18f),
            (13f, -6f), (9f, 1f), (8f, 4f), (4f, 5f), (-5f, 5f), (-10f, 6f), (-15f, 10f),
            (-17f, 14f)
        ],
        // Eurasia
        [
            (-10f, 36f), (-9f, 43f), (-5f, 48f), (-6f, 54f), (-1f, 58f), (8f, 58f),
            (12f, 55f), (18f, 70f), (28f, 71f), (44f, 68f), (60f, 70f), (90f, 75f),
            (130f, 72f), (170f, 70f), (180f, 68f), (180f, 42f), (145f, 42f), (142f, 35f),
            (128f, 33f), (120f, 20f), (109f, 20f), (100f, 8f), (78f, 8f), (73f, 19f),
            (68f, 22f), (60f, 25f), (48f, 30f), (44f, 36f), (40f, 36f), (36f, 36f),
            (32f, 36f), (28f, 40f), (20f, 36f), (10f, 36f)
        ],
        // UK + Ireland
        [(-10f, 52f), (-6f, 55f), (-2f, 58f), (1f, 53f), (-1f, 50f), (-5f, 50f), (-10f, 52f)],
        // Madagascar
        [(43f, -12f), (50f, -13f), (47f, -25f), (43f, -25f)],
        // India extra bulge (if Eurasia under-covers)
        [(72f, 22f), (88f, 22f), (80f, 8f), (73f, 15f)],
        // Australia
        [
            (114f, -22f), (114f, -34f), (122f, -35f), (130f, -32f), (137f, -36f), (140f, -38f),
            (146f, -39f), (150f, -37f), (153f, -28f), (150f, -22f), (145f, -15f), (136f, -12f),
            (129f, -14f), (121f, -18f)
        ],
        // New Zealand
        [(166f, -34f), (174f, -36f), (178f, -39f), (175f, -47f), (167f, -45f), (166f, -41f)],
        // Japan
        [(131f, 34f), (140f, 38f), (145f, 43f), (141f, 45f), (130f, 32f)],
        // Indonesia / PNG
        [
            (95f, 5f), (104f, 1f), (114f, -3f), (120f, -8f), (131f, -6f), (150f, -10f),
            (147f, -2f), (130f, 0f), (118f, 5f), (100f, 6f)
        ],
        // Iceland
        [(-24f, 64f), (-14f, 66f), (-13f, 64f), (-22f, 63f)]
    ];
}
