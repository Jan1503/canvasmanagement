namespace CanvasManagement.Extension.PacMan;

/// <summary>Colours, 8×8 sprite masks and two pixel fonts. Everything is drawn 1:1 into an ARGB buffer.</summary>
internal static class Art
{
    public static uint Rgb(int r, int g, int b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;

    public static uint Scale(uint c, float k)
    {
        k = Math.Clamp(k, 0, 1);
        var r = (int)(((c >> 16) & 255) * k);
        var g = (int)(((c >> 8) & 255) * k);
        var b = (int)((c & 255) * k);
        return Rgb(r, g, b);
    }

    public static uint Mix(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        int Ch(int s) => (int)(((a >> s) & 255) + ((((b >> s) & 255) - (float)((a >> s) & 255)) * t));
        return Rgb(Ch(16), Ch(8), Ch(0));
    }

    public static readonly uint Black = Rgb(0, 0, 0);
    public static readonly uint White = Rgb(255, 255, 255);
    public static readonly uint Yellow = Rgb(255, 230, 0);
    public static readonly uint Dot = Rgb(255, 190, 160);
    public static readonly uint DoorPink = Rgb(255, 170, 215);
    public static readonly uint FrightBlue = Rgb(40, 50, 255);
    public static readonly uint FrightFace = Rgb(255, 190, 160);
    public static readonly uint BlinkWhite = Rgb(235, 235, 255);
    public static readonly uint BlinkFace = Rgb(255, 30, 40);
    public static readonly uint Pupil = Rgb(30, 40, 200);

    public static readonly uint[] Walls =
    {
        Rgb(40, 70, 255), Rgb(255, 80, 190), Rgb(0, 210, 200), Rgb(255, 140, 20),
        Rgb(50, 220, 90), Rgb(170, 90, 255), Rgb(235, 50, 50), Rgb(240, 200, 40)
    };

    public static readonly uint[] GhostColors =
    {
        Rgb(255, 20, 20), Rgb(255, 160, 215), Rgb(0, 225, 255), Rgb(255, 165, 50),
        Rgb(170, 90, 255), Rgb(60, 230, 100), Rgb(255, 90, 150), Rgb(140, 200, 255)
    };

    public static readonly string[] GhostBody =
    {
        "..####..",
        ".######.",
        "########",
        "########",
        "########",
        "########",
        "########"
    };

    public static readonly string[] Skirts = { "##.##.##", "#.#..#.#" };

    // Fruit, one per level band. Letters are palette keys.
    public static readonly string[][] Fruits =
    {
        new[] { "......g.", ".....g..", "....g.g.", "...g..g.", ".rr..rr.", "rrrrrrrr", "rwrr.rwr", ".rr...rr" },
        new[] { "...gg...", "..gggg..", ".rrrrrr.", "rrwrrwrr", "rrrrrrrr", ".rwrrwr.", "..rrrr..", "...rr..." },
        new[] { "...g....", "..gg....", ".oooooo.", "oowooooo", "oooooooo", "oooooooo", ".oooooo.", "..oooo.." },
        new[] { "....b...", "..g.b...", ".rrrbrr.", "rrwrrrrr", "rwrrrrrr", "rrrrrrrr", ".rrrrrr.", "..rr.rr." },
        new[] { "...gb...", "....b...", ".pp.pp..", "pwpppwp.", ".pppppp.", "..pp.pp.", "...ppp..", "....p..." },
        new[] { "....b...", "...gg...", "..ylly..", ".yllllyy", ".yllllly", "yylllyyy", ".yyyyyy.", "..yyyy.." },
        new[] { "......b.", ".....yb.", "....yy..", "...yyy..", "..yyy...", ".yyy....", "yyy.....", ".y......" },
        new[] { "..gggg..", ".gGgGgg.", "gGgGgGgg", "gggGgGgG", "gGgggGgg", "ggGgGggg", ".gggGgg.", "..gggg.." }
    };

    public static uint FruitColor(char c) => c switch
    {
        'r' => Rgb(235, 20, 30),
        'w' => Rgb(255, 230, 230),
        'g' => Rgb(40, 200, 60),
        'G' => Rgb(10, 120, 30),
        'o' => Rgb(255, 150, 20),
        'b' => Rgb(150, 90, 40),
        'p' => Rgb(160, 60, 220),
        'y' => Rgb(255, 220, 40),
        'l' => Rgb(200, 230, 60),
        _ => 0
    };

    // ------------------------------------------------------------------ fonts

    public const int BigW = 5, BigH = 7;

    private static readonly Dictionary<char, string[]> Big = Build(new()
    {
        ['A'] = ".###. #...# #...# ##### #...# #...# #...#",
        ['B'] = "####. #...# #...# ####. #...# #...# ####.",
        ['C'] = ".###. #...# #.... #.... #.... #...# .###.",
        ['D'] = "####. #...# #...# #...# #...# #...# ####.",
        ['E'] = "##### #.... #.... ####. #.... #.... #####",
        ['F'] = "##### #.... #.... ####. #.... #.... #....",
        ['G'] = ".###. #...# #.... #.### #...# #...# .####",
        ['H'] = "#...# #...# #...# ##### #...# #...# #...#",
        ['I'] = ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.",
        ['J'] = "..### ...#. ...#. ...#. #..#. #..#. .##..",
        ['K'] = "#...# #..#. #.#.. ##... #.#.. #..#. #...#",
        ['L'] = "#.... #.... #.... #.... #.... #.... #####",
        ['M'] = "#...# ##.## #.#.# #.#.# #...# #...# #...#",
        ['N'] = "#...# ##..# #.#.# #..## #...# #...# #...#",
        ['O'] = ".###. #...# #...# #...# #...# #...# .###.",
        ['P'] = "####. #...# #...# ####. #.... #.... #....",
        ['Q'] = ".###. #...# #...# #...# #.#.# #..#. .##.#",
        ['R'] = "####. #...# #...# ####. #.#.. #..#. #...#",
        ['S'] = ".#### #.... #.... .###. ....# ....# ####.",
        ['T'] = "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",
        ['U'] = "#...# #...# #...# #...# #...# #...# .###.",
        ['V'] = "#...# #...# #...# #...# #...# .#.#. ..#..",
        ['W'] = "#...# #...# #...# #.#.# #.#.# ##.## #...#",
        ['X'] = "#...# #...# .#.#. ..#.. .#.#. #...# #...#",
        ['Y'] = "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..",
        ['Z'] = "##### ....# ...#. ..#.. .#... #.... #####",
        ['0'] = ".###. #...# #..## #.#.# ##..# #...# .###.",
        ['1'] = "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.",
        ['2'] = ".###. #...# ....# ...#. ..#.. .#... #####",
        ['3'] = "####. ....# ....# .###. ....# ....# ####.",
        ['4'] = "...#. ..##. .#.#. #..#. ##### ...#. ...#.",
        ['5'] = "##### #.... ####. ....# ....# #...# .###.",
        ['6'] = ".###. #.... #.... ####. #...# #...# .###.",
        ['7'] = "##### ....# ...#. ..#.. .#... .#... .#...",
        ['8'] = ".###. #...# #...# .###. #...# #...# .###.",
        ['9'] = ".###. #...# #...# .#### ....# ....# .###.",
        ['!'] = "..#.. ..#.. ..#.. ..#.. ..#.. ..... ..#..",
        ['-'] = "..... ..... ..... .###. ..... ..... .....",
        [':'] = "..... ..#.. ..#.. ..... ..#.. ..#.. .....",
        ['.'] = "..... ..... ..... ..... ..... ..... ..#..",
        [' '] = "..... ..... ..... ..... ..... ..... ....."
    });

    // 3×5 digits for the little point pop-ups.
    private static readonly Dictionary<char, string[]> Small = Build(new()
    {
        ['0'] = "### #.# #.# #.# ###",
        ['1'] = ".#. ##. .#. .#. ###",
        ['2'] = "### ..# ### #.. ###",
        ['3'] = "### ..# .## ..# ###",
        ['4'] = "#.# #.# ### ..# ..#",
        ['5'] = "### #.. ### ..# ###",
        ['6'] = "### #.. ### #.# ###",
        ['7'] = "### ..# ..# .#. .#.",
        ['8'] = "### #.# ### #.# ###",
        ['9'] = "### #.# ### ..# ###"
    });

    private static Dictionary<char, string[]> Build(Dictionary<char, string> src) =>
        src.ToDictionary(kv => kv.Key, kv => kv.Value.Split(' '));

    public static int MeasureBig(string text, int scale = 1) => text.Length == 0 ? 0 : (text.Length * (BigW + 1) - 1) * scale;

    public static int MeasureSmall(string text) => text.Length == 0 ? 0 : text.Length * 4 - 1;

    public static void DrawBig(Frame f, string text, int x, int y, uint color, int scale = 1)
    {
        foreach (var ch in text.ToUpperInvariant())
        {
            if (Big.TryGetValue(ch, out var g))
                for (var gy = 0; gy < BigH; gy++)
                for (var gx = 0; gx < BigW; gx++)
                    if (g[gy][gx] == '#')
                        f.Rect(x + gx * scale, y + gy * scale, scale, scale, color);
            x += (BigW + 1) * scale;
        }
    }

    public static void DrawSmall(Frame f, string text, int x, int y, uint color)
    {
        foreach (var ch in text)
        {
            if (Small.TryGetValue(ch, out var g))
                for (var gy = 0; gy < 5; gy++)
                for (var gx = 0; gx < 3; gx++)
                    if (g[gy][gx] == '#')
                        f.Set(x + gx, y + gy, color);
            x += 4;
        }
    }
}

/// <summary>A plain ARGB pixel buffer with clipped writes.</summary>
internal sealed class Frame
{
    public readonly int W, H;
    public readonly uint[] Px;

    public Frame(int w, int h)
    {
        W = w;
        H = h;
        Px = new uint[w * h];
    }

    public void Clear(uint c) => Array.Fill(Px, c);

    public void Set(int x, int y, uint c)
    {
        if ((uint)x < W && (uint)y < H) Px[y * W + x] = c;
    }

    public void Rect(int x, int y, int w, int h, uint c)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
        for (var yy = y0; yy < y1; yy++)
            Array.Fill(Px, c, yy * W + x0, Math.Max(0, x1 - x0));
    }

    /// <summary>Darken a rectangle (text backdrops).</summary>
    public void Shade(int x, int y, int w, int h, float k)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
        for (var yy = y0; yy < y1; yy++)
        for (var xx = x0; xx < x1; xx++)
            Px[yy * W + xx] = Art.Scale(Px[yy * W + xx], k);
    }
}
