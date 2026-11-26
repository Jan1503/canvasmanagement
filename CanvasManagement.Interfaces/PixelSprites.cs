using System.Runtime.CompilerServices;
using SkiaSharp;

namespace CanvasManagement.Interfaces;

/// <summary>
///     Pre-rendered pixel shapes for <see cref="PixelArt"/> and <see cref="GameFx"/>: drawing one image is a
///     single Skia call, drawing the same shape pixel by pixel is hundreds. Shapes are cached once per size
///     (alpha masks, coloured by the paint at draw time); character sprites once per colour set.
///     Thread-safe; images are immutable and may be drawn from any thread.
/// </summary>
internal static class PixelSprites
{
    private static readonly object MaskLock = new();
    private static readonly Dictionary<(int Kind, int Size), SKImage> Masks = new();
    private const int MaxMasks = 512;

    private const int DiscKind = 0, RingKind = 1, GlowKind = 2;

    /// <summary>Filled disc of radius <paramref name="r"/>, (2r+1)² pixels.</summary>
    public static SKImage Disc(int r) => Mask(DiscKind, r, BuildDisc);

    /// <summary>1px ring of radius <paramref name="r"/>.</summary>
    public static SKImage Ring(int r) => Mask(RingKind, r, BuildRing);

    /// <summary>Soft glow, alpha (1-d)² at full strength; radius in quarter pixels.</summary>
    public static SKImage Glow(int quarterRadius) => Mask(GlowKind, quarterRadius, BuildGlow);

    private static SKImage Mask(int kind, int size, Func<int, SKImage> build)
    {
        lock (MaskLock)
        {
            if (Masks.TryGetValue((kind, size), out var img)) return img;
            // Shapes come from a handful of sizes; the cap only guards against a caller animating sizes freely.
            if (Masks.Count >= MaxMasks) Masks.Clear(); // images are not disposed: another thread may be drawing one
            img = build(size);
            Masks[(kind, size)] = img;
            return img;
        }
    }

    private static SKImage BuildDisc(int r) => BuildMask(r, (x, y) => x * x + y * y <= r * r ? (byte)255 : (byte)0);

    private static SKImage BuildRing(int r)
    {
        var r2 = r * r;
        var i2 = (r - 1) * (r - 1);
        return BuildMask(r, (x, y) =>
        {
            var d = x * x + y * y;
            return d <= r2 && d > i2 ? (byte)255 : (byte)0;
        });
    }

    private static SKImage BuildGlow(int quarterRadius)
    {
        var radius = quarterRadius / 4f;
        var r = (int)MathF.Ceiling(radius);
        return BuildMask(r, (x, y) =>
        {
            var d = MathF.Sqrt(x * x + y * y) / radius;
            if (d > 1) return 0;
            return (byte)Math.Clamp((1 - d) * (1 - d) * 255, 0, 255);
        });
    }

    private static SKImage BuildMask(int r, Func<int, int, byte> alpha)
    {
        var size = 2 * r + 1;
        var data = new byte[size * size];
        for (var y = -r; y <= r; y++)
        for (var x = -r; x <= r; x++)
            data[(y + r) * size + x + r] = alpha(x, y);
        var info = new SKImageInfo(size, size, SKColorType.Alpha8, SKAlphaType.Premul);
        return SKImage.FromPixelCopy(info, data, size);
    }

    // ------------------------------------------------------------------ character sprites

    /// <summary>The last few colourings of one sprite (palettes animate, so a sprite has a few variants).</summary>
    private sealed class Variants
    {
        public readonly List<(uint[] Colors, SKImage Image, long Used)> Items = new();
    }

    private const int MaxVariants = 12;
    private static readonly ConditionalWeakTable<string[], Variants> Sprites = new();
    private static long _clock;

    /// <summary>
    ///     The sprite <paramref name="rows"/> coloured by <paramref name="palette"/>, 1 pixel per character.
    ///     The palette is evaluated on every call (it may depend on time); an image is only built when that
    ///     colouring has not been seen recently.
    /// </summary>
    public static SKImage? Sprite(string[] rows, Func<char, SKColor> palette, bool flipX)
    {
        var h = rows.Length;
        var w = rows[0].Length;
        if (w == 0) return null;
        var colors = new uint[w * h + 1];
        colors[^1] = flipX ? 1u : 0u;
        var any = false;
        for (var y = 0; y < h; y++)
        {
            var row = rows[y];
            for (var x = 0; x < w; x++)
            {
                var c = x < row.Length ? (uint)palette(row[x]) : 0u;
                if ((c >> 24) == 0) c = 0;
                any |= c != 0;
                colors[y * w + (flipX ? w - 1 - x : x)] = c;
            }
        }
        if (!any) return null;

        var variants = Sprites.GetValue(rows, _ => new Variants());
        lock (variants)
        {
            var now = Interlocked.Increment(ref _clock);
            for (var i = 0; i < variants.Items.Count; i++)
            {
                var v = variants.Items[i];
                if (!v.Colors.AsSpan().SequenceEqual(colors)) continue;
                variants.Items[i] = (v.Colors, v.Image, now);
                return v.Image;
            }

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var img = SKImage.FromPixelCopy(info, (ReadOnlySpan<byte>)System.Runtime.InteropServices.MemoryMarshal
                .AsBytes(colors.AsSpan(0, w * h)), w * 4);
            if (variants.Items.Count >= MaxVariants)
            {
                var oldest = 0;
                for (var i = 1; i < variants.Items.Count; i++)
                    if (variants.Items[i].Used < variants.Items[oldest].Used) oldest = i;
                variants.Items.RemoveAt(oldest); // not disposed: another thread may be drawing it; the GC frees it
            }
            variants.Items.Add((colors, img, now));
            return img;
        }
    }
}
