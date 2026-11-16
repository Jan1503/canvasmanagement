using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

/// <summary>Pixel primitives for menus, drawn straight into the frame buffer.</summary>
internal sealed class Ui
{
    public readonly int[] Px;
    public readonly int W, H, S;

    public Ui(int[] px, int w, int h)
    {
        Px = px;
        W = w;
        H = h;
        S = Math.Max(1, (int)MathF.Round(h / 128f));
    }

    public void FillRect(int x0, int y0, int w, int h, int c, float alpha = 1f)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(H, y0 + h); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(W, x0 + w); x++)
        {
            var i = y * W + x;
            Px[i] = alpha >= 1 ? c : Gfx.Lerp(Px[i], c, alpha);
        }
    }

    public void Frame(int x0, int y0, int w, int h, int c, int t = 1)
    {
        FillRect(x0, y0, w, t, c);
        FillRect(x0, y0 + h - t, w, t, c);
        FillRect(x0, y0, t, h, c);
        FillRect(x0 + w - t, y0, t, h, c);
    }

    public void Dim(float alpha, int color = 0) => FillRect(0, 0, W, H, color == 0 ? Gfx.Rgb(6, 8, 30) : color, alpha);

    public void Gradient(int top, int bottom)
    {
        for (var y = 0; y < H; y++)
        {
            var c = Gfx.Lerp(top, bottom, y / (float)Math.Max(1, H - 1));
            Array.Fill(Px, c, y * W, W);
        }
    }

    /// <summary>Rounded panel: translucent fill, 1px corner cut, border.</summary>
    public void Panel(int x, int y, int w, int h, int fill, float alpha, int border)
    {
        FillRect(x + 1, y, w - 2, h, fill, alpha);
        FillRect(x, y + 1, 1, h - 2, fill, alpha);
        FillRect(x + w - 1, y + 1, 1, h - 2, fill, alpha);
        FillRect(x + 1, y, w - 2, S, border);
        FillRect(x + 1, y + h - S, w - 2, S, border);
        FillRect(x, y + 1, S, h - 2, border);
        FillRect(x + w - S, y + 1, S, h - 2, border);
    }

    public void Blit(string[] rows, int x0, int y0, int px, Func<char, int> palette)
    {
        for (var y = 0; y < rows.Length; y++)
        for (var x = 0; x < rows[y].Length; x++)
        {
            var c = palette(rows[y][x]);
            if (c == 0) continue;
            FillRect(x0 + x * px, y0 + y * px, px, px, c);
        }
    }

    public void Dot(float x, float y, int r, int c) => FillRect((int)x - r, (int)y - r, 2 * r + 1, 2 * r + 1, c);

    /// <summary>Track outline from normalised (0..1) points, fitted and centred into the rectangle.</summary>
    public void Thumb(Vector2[] pts, int x, int y, int w, int h, int color, bool thick)
    {
        if (pts.Length < 2) return;
        var pad = 3 * S;
        var sc = MathF.Min(w - 2 * pad, h - 2 * pad);
        var ox = x + (w - sc) / 2f;
        var oy = y + (h - sc) / 2f;
        for (var pass = 0; pass < 2; pass++)
        {
            var r = pass == 0 ? (thick ? 2 * S : S) : (thick ? S : 0);
            var c = pass == 0 ? Gfx.Rgb(10, 12, 30) : color;
            for (var i = 0; i < pts.Length; i++)
            {
                var a = new Vector2(ox + pts[i].X * sc, oy + pts[i].Y * sc);
                var b = new Vector2(ox + pts[(i + 1) % pts.Length].X * sc, oy + pts[(i + 1) % pts.Length].Y * sc);
                var steps = (int)MathF.Ceiling(Vector2.Distance(a, b)) + 1;
                for (var k = 0; k <= steps; k++)
                {
                    var q = Vector2.Lerp(a, b, k / (float)steps);
                    Dot(q.X, q.Y, r, c);
                }
            }
        }

        var s0 = new Vector2(ox + pts[0].X * sc, oy + pts[0].Y * sc);
        Dot(s0.X, s0.Y, S, Gfx.Rgb(255, 255, 255));
    }

    public void Sprite(Sprite s, float cx, float bottom)
    {
        var left = (int)MathF.Round(cx - s.AnchorX);
        var top = (int)MathF.Round(bottom - s.AnchorY);
        for (var y = 0; y < s.H; y++)
        for (var x = 0; x < s.W; x++)
        {
            var c = s.Px[y * s.W + x];
            if (c == 0) continue;
            var tx = left + x;
            var ty = top + y;
            if ((uint)tx < (uint)W && (uint)ty < (uint)H) Px[ty * W + tx] = c;
        }
    }

    public void Glow(float cx, float cy, float radius, int color, float strength)
    {
        for (var y = Math.Max(0, (int)(cy - radius)); y <= Math.Min(H - 1, (int)(cy + radius)); y++)
        for (var x = Math.Max(0, (int)(cx - radius)); x <= Math.Min(W - 1, (int)(cx + radius)); x++)
        {
            var d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / radius;
            if (d > 1) continue;
            var i = y * W + x;
            Px[i] = Gfx.AddBlend(Px[i], color, (1 - d) * (1 - d) * strength);
        }
    }

    // ------------------------------------------------------------------ icons

    public static readonly string[] Sun =
    {
        ".....y.....",
        "..y..y..y..",
        "...yyyyy...",
        "..yoooooy..",
        ".yoooooooy.",
        "yyoooooooyy",
        ".yoooooooy.",
        "..yoooooy..",
        "...yyyyy...",
        "..y..y..y..",
        ".....y....."
    };

    public static readonly string[] Moon =
    {
        "...wwww....",
        "..wwww.....",
        ".wwwg......",
        "wwwg.......",
        "wwwg.......",
        "wwwg.......",
        "wwwg.......",
        ".wwwg......",
        "..wwww.....",
        "...wwwww...",
        "....wwww..."
    };

    public static readonly string[] Trophy =
    {
        ".kkkkkkkkkkk.",
        "kk.yyyyyyy.kk",
        "k.kywwyyyyk.k",
        "k.kywyyyyok.k",
        ".kkyyyyyyokk.",
        "...kyyyyok...",
        "....kyyok....",
        ".....kyk.....",
        ".....kyk.....",
        "....kyyyk....",
        "...koooook...",
        "..kkkkkkkkk.."
    };

    public static int IconColor(char ch) => ch switch
    {
        'y' => Gfx.Rgb(255, 214, 40),
        'o' => Gfx.Rgb(255, 150, 20),
        'w' => Gfx.Rgb(240, 244, 255),
        'g' => Gfx.Rgb(150, 160, 200),
        'k' => Gfx.Rgb(30, 20, 10),
        _ => 0
    };
}
