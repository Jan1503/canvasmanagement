namespace CanvasManagement.Extension.TurboKart;

/// <summary>ARGB int helpers. Layout matches <c>Bgra8888</c> in memory on little-endian.</summary>
internal static class Gfx
{
    public static int Rgb(int r, int g, int b) =>
        unchecked((int)0xFF000000) | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);

    public static int Argb(int a, int r, int g, int b) =>
        (Clamp(a) << 24) | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);

    public static int A(int c) => (c >> 24) & 255;
    public static int R(int c) => (c >> 16) & 255;
    public static int G(int c) => (c >> 8) & 255;
    public static int B(int c) => c & 255;

    public static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    public static int Shade(int c, float f) =>
        Rgb((int)(R(c) * f), (int)(G(c) * f), (int)(B(c) * f));

    public static int Add(int c, int d) => Rgb(R(c) + d, G(c) + d, B(c) + d);

    public static int Lerp(int a, int b, float t)
    {
        if (t <= 0) return a | unchecked((int)0xFF000000);
        if (t >= 1) return b | unchecked((int)0xFF000000);
        return Rgb(
            (int)(R(a) + (R(b) - R(a)) * t),
            (int)(G(a) + (G(b) - G(a)) * t),
            (int)(B(a) + (B(b) - B(a)) * t));
    }

    /// <summary>Additive blend, <paramref name="k"/> 0..1.</summary>
    public static int AddBlend(int dst, int src, float k) =>
        Rgb((int)(R(dst) + R(src) * k), (int)(G(dst) + G(src) * k), (int)(B(dst) + B(src) * k));

    public static int Hsv(float h, float s, float v)
    {
        h = (h % 360 + 360) % 360;
        var c = v * s;
        var x = c * (1 - MathF.Abs(h / 60f % 2 - 1));
        var m = v - c;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Rgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    public static float Hash(int x, int y, int seed = 0)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    public static float ValueNoise(float x, float y, int seed = 0)
    {
        var ix = (int)MathF.Floor(x);
        var iy = (int)MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var a = Hash(ix, iy, seed);
        var b = Hash(ix + 1, iy, seed);
        var c = Hash(ix, iy + 1, seed);
        var d = Hash(ix + 1, iy + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    public static float Fbm(float x, float y, int octaves, int seed = 0)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (var i = 0; i < octaves; i++)
        {
            sum += ValueNoise(x, y, seed + i * 17) * amp;
            norm += amp;
            amp *= 0.5f;
            x *= 2.03f;
            y *= 2.03f;
        }

        return sum / norm;
    }

    public static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    public static float LerpAngle(float a, float b, float t) => a + WrapAngle(b - a) * t;
}

/// <summary>Billboard bitmap anchored at its ground contact point.</summary>
internal sealed class Sprite
{
    public int W, H;
    public int[] Px = Array.Empty<int>();
    public float AnchorX, AnchorY;
    public float WorldPerPixel = 1;
}

/// <summary>Texture for vertical world quads (walls, stands, arch).</summary>
internal sealed class Tex
{
    public int W, H;
    public int[] Px = Array.Empty<int>();
    public Tex(int w, int h) { W = w; H = h; Px = new int[w * h]; }
    public void Set(int x, int y, int c) { if ((uint)x < (uint)W && (uint)y < (uint)H) Px[y * W + x] = c; }
    public void Fill(int x0, int y0, int w, int h, int c)
    {
        for (var y = y0; y < y0 + h; y++)
        for (var x = x0; x < x0 + w; x++)
            Set(x, y, c);
    }
}
