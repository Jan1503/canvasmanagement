namespace CanvasManagement.Extension.TurboKart;

/// <summary>5×7 glyphs for painting signage into textures (banners, stands). HUD text uses BDF.</summary>
internal static class TinyFont
{
    public const int GlyphW = 5, GlyphH = 7;

    private static readonly Dictionary<char, string[]> Glyphs = Build(new()
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
        ['.'] = "..... ..... ..... ..... ..... ..... ..#..",
        [' '] = "..... ..... ..... ..... ..... ..... ....."
    });

    private static Dictionary<char, string[]> Build(Dictionary<char, string> src) =>
        src.ToDictionary(kv => kv.Key, kv => kv.Value.Split(' '));

    public static bool Pixel(char c, int x, int y)
    {
        if (!Glyphs.TryGetValue(char.ToUpperInvariant(c), out var g)) return false;
        return y < g.Length && x < g[y].Length && g[y][x] == '#';
    }

    public static int Measure(string text, int scale) =>
        text.Length == 0 ? 0 : (text.Length * (GlyphW + 1) - 1) * scale;

    /// <summary>Paints <paramref name="text"/> into an ARGB texel array; optional 1-texel dark outline.</summary>
    public static void Paint(int[] px, int texW, int texH, string text, int x0, int y0, int scale, int color,
        int outline = 0)
    {
        for (var pass = outline != 0 ? 0 : 1; pass < 2; pass++)
        for (var ci = 0; ci < text.Length; ci++)
        {
            var gx = x0 + ci * (GlyphW + 1) * scale;
            for (var y = 0; y < GlyphH; y++)
            for (var x = 0; x < GlyphW; x++)
            {
                if (!Pixel(text[ci], x, y)) continue;
                for (var sy = 0; sy < scale; sy++)
                for (var sx = 0; sx < scale; sx++)
                {
                    var tx = gx + x * scale + sx;
                    var ty = y0 + y * scale + sy;
                    if (pass == 0)
                    {
                        for (var oy = -1; oy <= 1; oy++)
                        for (var ox = -1; ox <= 1; ox++)
                            Put(px, texW, texH, tx + ox, ty + oy, outline);
                    }
                    else
                    {
                        Put(px, texW, texH, tx, ty, color);
                    }
                }
            }
        }
    }

    private static void Put(int[] px, int w, int h, int x, int y, int c)
    {
        if ((uint)x < (uint)w && (uint)y < (uint)h) px[y * w + x] = c;
    }
}
