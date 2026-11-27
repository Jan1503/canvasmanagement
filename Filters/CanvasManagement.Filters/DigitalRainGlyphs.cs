namespace CanvasManagement.Filters;

/// <summary>
///     The symbols of <see cref="DigitalRainVisionFilter" />: half-width katakana (shown mirrored, as in the
///     classic digital rain), digits and a few signs. Drawn here as 5×7 pixel bitmaps for LED walls; a 3×5 set
///     is derived from them for fine streams. Masks are 8 bits per row: bit (y * 8 + x).
/// </summary>
internal static class DigitalRainGlyphs
{
    // 'X' = lit; rows separated by spaces
    private static readonly string[] Katakana =
    [
        "XXXXX ....X ..X.X ..XX. ..X.. .X... X....", // a
        "....X ...X. ..XX. .X.X. X..X. ...X. ...X.", // i
        "..X.. XXXXX X...X ....X ...X. ..X.. XX...", // u
        "..... XXXXX ..X.. ..X.. ..X.. ..X.. XXXXX", // e
        "...X. XXXXX ...X. ..XX. .X.X. X..X. ..XX.", // o
        ".X... XXXXX .X..X .X..X .X..X X...X X.XX.", // ka
        "..X.. XXXXX ..X.. XXXXX ..X.. ..X.. ..X..", // ki
        ".X... .XXXX X..X. ...X. ...X. ..X.. .X...", // ke
        "..... XXXXX ....X ....X ....X ....X XXXXX", // ko
        ".X.X. XXXXX .X.X. .X.X. ...X. ..X.. .X...", // sa
        "X.... .X..X X...X .X..X ....X ...X. XXX..", // shi
        "..... XXXXX ....X ...X. ..X.. .X.X. X...X", // su
        ".X... .X... XXXXX .X..X .X.X. .X... .XXXX", // se
        "X...X X...X .X..X ....X ...X. ..X.. XX...", // so
        ".XXXX .X..X X.X.X ...X. ..X.. .X... X....", // ta
        "...XX XXX.. ..X.. XXXXX ..X.. ..X.. .X...", // chi
        "X.X.X X.X.X X.X.X ....X ...X. ..X.. XX...", // tsu
        "XXXXX ..... XXXXX ..X.. ..X.. ..X.. .X...", // te
        "..X.. ..X.. XXXXX ..X.. ..X.. ..X.. .X...", // na
        "..... .XXX. ..... ..... ..... XXXXX .....", // ni
        "..... XXXXX ....X .X.X. ..X.. .X.X. X....", // nu
        "..X.. XXXXX ...X. ..X.. .XXX. X.X.X ..X..", // ne
        "....X ....X ...X. ...X. ..X.. .X... X....", // no
        "..... ..X.. .X.X. .X..X X...X X...X .....", // ha
        "X.... X.... X..XX XXX.. X.... X.... .XXXX", // hi
        "..... XXXXX ....X ....X ...X. ..X.. XX...", // fu
        "..... .X... X.X.. ...X. ....X ..... .....", // he
        "..X.. XXXXX ..X.. X.X.X X.X.X ..X.. ..X..", // ho
        "XXXXX ....X ...X. X.X.. .X... ..X.. .....", // ma
        ".XXX. ....X .XXX. ....X XXX.. ...XX .....", // mi
        "..X.. ..X.. .X... .X... X..X. X...X XXXXX", // mu
        "....X X...X .X.X. ..X.. .X.X. X.... .....", // me
        "XXXXX ..X.. XXXXX ..X.. ..X.. ..X.. ...XX", // mo
        ".X... XXXXX .X..X .X.X. .X... ..X.. ..X..", // ya
        "..... XXXX. ...X. ...X. ...X. XXXXX .....", // yu
        "XXXXX ....X ....X XXXXX ....X ....X XXXXX", // yo
        "XXXXX ..... XXXXX ....X ...X. ..X.. XX...", // ra
        "X...X X...X X...X X...X ....X ...X. ..X..", // ri
        ".X.X. .X.X. .X.X. .X.X. .X.X. X..XX X..X.", // ru
        "X.... X.... X.... X.... X...X X..X. XXX..", // re
        "..... XXXXX X...X X...X X...X XXXXX .....", // ro
        "XXXXX X...X X...X ....X ...X. ..X.. XX...", // wa
        "X.... .X..X ....X ....X ...X. ..X.. XX...", // n
    ];

    private static readonly string[] Digits =
    [
        ".XXX. X...X X..XX X.X.X XX..X X...X .XXX.", // 0
        "..X.. .XX.. ..X.. ..X.. ..X.. ..X.. .XXX.", // 1
        ".XXX. X...X ....X ...X. ..X.. .X... XXXXX", // 2
        "XXXXX ...X. ..X.. ...X. ....X X...X .XXX.", // 3
        "...X. ..XX. .X.X. X..X. XXXXX ...X. ...X.", // 4
        "XXXXX X.... XXXX. ....X ....X X...X .XXX.", // 5
        "XXXXX ....X ...X. ..X.. .X... .X... .X...", // 7
        ".XXX. X...X X...X .XXX. X...X X...X .XXX.", // 8
        ".XXX. X...X X...X .XXXX ....X ...X. .XX..", // 9
        "XXXXX ....X ...X. ..X.. .X... X.... XXXXX", // Z
    ];

    private static readonly string[] Signs =
    [
        "..... ..X.. ..X.. ..... ..X.. ..X.. .....", // :
        "..... ..... XXXXX ..... XXXXX ..... .....", // =
        "..... ..X.. ..X.. XXXXX ..X.. ..X.. .....", // +
        "..... X.X.X .XXX. XXXXX .XXX. X.X.X .....", // *
        "...X. ..X.. .X... X.... .X... ..X.. ...X.", // <
        "..X.. ..X.. ..X.. ..... ..X.. ..X.. ..X..", // ¦
        ".X.X. .X.X. .X.X. ..... ..... ..... .....", // "
    ];

    /// <summary>All symbols, katakana first, then digits and signs.</summary>
    public static int Count => Katakana.Length + Digits.Length + Signs.Length;

    /// <summary>Number of katakana at the start of the set.</summary>
    public static int KatakanaCount => Katakana.Length;

    /// <summary>The set as 5×7 masks (katakana mirrored) or, with <paramref name="small" />, as 3×5 masks.</summary>
    public static ulong[] Build(bool small)
    {
        var all = new List<ulong>(Count);
        foreach (var k in Katakana) all.Add(Parse(k, mirror: true));
        foreach (var d in Digits) all.Add(Parse(d, mirror: false));
        foreach (var s in Signs) all.Add(Parse(s, mirror: false));
        if (small)
            for (var i = 0; i < all.Count; i++) all[i] = Shrink(all[i]);
        return [.. all];
    }

    /// <summary>
    ///     A pick table that makes katakana the most common symbols (about three in four), digits next and signs
    ///     rare, like the classic look.
    /// </summary>
    public static byte[] PickTable()
    {
        var table = new List<byte>();
        for (var i = 0; i < Katakana.Length; i++) for (var n = 0; n < 4; n++) table.Add((byte)i);
        for (var i = 0; i < Digits.Length; i++) for (var n = 0; n < 4; n++) table.Add((byte)(Katakana.Length + i));
        for (var i = 0; i < Signs.Length; i++) for (var n = 0; n < 2; n++) table.Add((byte)(Katakana.Length + Digits.Length + i));
        return [.. table];
    }

    private static ulong Parse(string glyph, bool mirror)
    {
        var rows = glyph.Split(' ');
        ulong mask = 0;
        for (var y = 0; y < rows.Length; y++)
        for (var x = 0; x < rows[y].Length; x++)
            if (rows[y][x] == 'X')
                mask |= 1UL << (y * 8 + (mirror ? 4 - x : x));
        return mask;
    }

    // 5x7 -> 3x5: column groups {0} {1,2,3} {4}, row groups {0} {1,2} {3} {4,5} {6}; a small cell is lit when
    // its group holds enough of the stroke, so the shapes stay recognisable
    private static readonly (int From, int To)[] ColGroups = [(0, 0), (1, 3), (4, 4)];
    private static readonly (int From, int To)[] RowGroups = [(0, 0), (1, 2), (3, 3), (4, 5), (6, 6)];

    private static ulong Shrink(ulong big)
    {
        ulong small = 0;
        for (var sy = 0; sy < 5; sy++)
        for (var sx = 0; sx < 3; sx++)
        {
            int lit = 0, all = 0;
            for (var y = RowGroups[sy].From; y <= RowGroups[sy].To; y++)
            for (var x = ColGroups[sx].From; x <= ColGroups[sx].To; x++)
            {
                all++;
                if (((big >> (y * 8 + x)) & 1) != 0) lit++;
            }
            if (lit * 3 >= all) small |= 1UL << (sy * 8 + sx);
        }
        return small;
    }
}
