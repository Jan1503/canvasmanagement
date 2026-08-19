using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Filters;

/// <summary>
///     Spooky Halloween filter with dark atmosphere, fog, and eerie glow
/// </summary>
[FilterInfo("Halloween Spooky",
    "Creepy Halloween effect with dark atmosphere, swirling fog, and eerie orange-purple glow",
    "Seasonal",
    IconResourceName = "halloween.svg")]
public class HalloweenFilter : ICanvasFilter
{
    private readonly List<Bat> _bats = new();
    private readonly List<Ghost> _ghosts = new();
    private readonly Random _random = new();
    private int _frameCount;
    private bool _initialized;

    /// <summary>
    ///     Fog/mist density
    /// </summary>
    [FilterParameter("Fog Density", "Amount of swirling fog/mist", MinValue = 0.0f, MaxValue = 1.0f,
        DefaultValue = 0.6f)]
    public float FogDensity { get; set; } = 0.6f;

    /// <summary>
    ///     Eerie glow intensity (orange and purple)
    /// </summary>
    [FilterParameter("Eerie Glow", "Spooky glow intensity", MinValue = 0.0f, MaxValue = 1.0f, DefaultValue = 0.7f)]
    public float EerieGlow { get; set; } = 0.7f;

    /// <summary>
    ///     Enable flying bats silhouettes
    /// </summary>
    [FilterParameter("Flying Bats", "Enable spooky bat silhouettes")]
    public bool EnableBats { get; set; } = true;

    /// <summary>
    ///     Enable ghostly apparitions
    /// </summary>
    [FilterParameter("Ghost Apparitions", "Enable floating ghostly figures")]
    public bool EnableGhosts { get; set; } = true;

    /// <summary>
    ///     Enable pulsing jack-o-lantern glow
    /// </summary>
    [FilterParameter("Pumpkin Glow", "Enable jack-o-lantern glow effect")]
    public bool EnablePumpkinGlow { get; set; } = true;

    public string Name => "Halloween Spooky";
    public float Intensity { get; set; } = 0.8f;
    public bool Enabled { get; set; } = true;

    public SKBitmap Apply(SKBitmap source, bool inPlace = true)
    {
        if (!Enabled || Intensity <= 0) return source;

        var bitmap = inPlace ? source : source.Copy();

        if (!_initialized)
        {
            InitializeCreatures(bitmap.Width, bitmap.Height);
            _initialized = true;
        }

        // Apply dark, desaturated look
        ApplyHalloweenColorGrade(bitmap);

        // Add eerie glow
        AddEerieGlow(bitmap);

        // Add swirling fog
        AddSwirlingFog(bitmap);

        // Add occasional flickers/lightning
        AddFlickers(bitmap);

        // Add pumpkin glow (bottom corners)
        if (EnablePumpkinGlow) AddPumpkinGlow(bitmap);

        // Add ghostly apparitions
        if (EnableGhosts)
        {
            DrawGhosts(bitmap);
            UpdateGhosts(bitmap.Width, bitmap.Height);
        }

        // Add flying bats
        if (EnableBats)
        {
            DrawBats(bitmap);
            UpdateBats(bitmap.Width, bitmap.Height);
        }

        // Occasional creepy vignette pulse
        if (_frameCount % 150 < 10) AddCreepyVignette(bitmap);

        _frameCount++;

        return bitmap;
    }

    private void InitializeCreatures(int width, int height)
    {
        // Initialize bats
        for (var i = 0; i < 8; i++)
            _bats.Add(new Bat
            {
                X = _random.Next(width),
                Y = _random.Next(height / 2),
                Speed = 2 + (float)_random.NextDouble() * 3,
                Size = 8 + _random.Next(8),
                WingPhase = (float)_random.NextDouble() * 360
            });

        // Initialize ghosts
        for (var i = 0; i < 3; i++)
            _ghosts.Add(new Ghost
            {
                X = _random.Next(width),
                Y = _random.Next(height),
                FloatPhase = (float)_random.NextDouble() * 360,
                Opacity = 0.2f + (float)_random.NextDouble() * 0.3f,
                Size = 20 + _random.Next(20)
            });
    }

    private void ApplyHalloweenColorGrade(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var pixelCount = bitmap.Width * bitmap.Height;

            for (var i = 0; i < pixelCount; i++)
            {
                var pixel = pixels[i];
                var a = (byte)((pixel >> 24) & 0xFF);
                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                // Darken overall
                r = (byte)(r * (0.7f - Intensity * 0.2f));
                g = (byte)(g * (0.7f - Intensity * 0.2f));
                b = (byte)(b * (0.7f - Intensity * 0.2f));

                // Desaturate
                var gray = (byte)(r * 0.299 + g * 0.587 + b * 0.114);
                r = (byte)((r + gray) / 2);
                g = (byte)((g + gray) / 2);
                b = (byte)((b + gray) / 2);

                // Add slight purple/orange tint
                var tintAmount = Intensity * 0.3f;
                r = (byte)Math.Min(255, r + (int)(20 * tintAmount)); // Orange tint
                b = (byte)Math.Min(255, b + (int)(30 * tintAmount)); // Purple tint

                pixels[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    private void AddEerieGlow(SKBitmap bitmap)
    {
        var glowStrength = EerieGlow * Intensity;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var brightness = GetBrightness(pixels[idx]);

                // Add glow to edges and bright areas
                if (brightness > 80 || IsEdgePixel(pixels, x, y, width, height))
                {
                    var pixel = pixels[idx];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Alternate between orange and purple glow
                    var phase = (x + y + _frameCount) % 100;
                    if (phase < 50)
                    {
                        // Orange glow
                        r = (byte)Math.Min(255, r + (int)(40 * glowStrength));
                        g = (byte)Math.Min(255, g + (int)(15 * glowStrength));
                    }
                    else
                    {
                        // Purple glow
                        r = (byte)Math.Min(255, r + (int)(25 * glowStrength));
                        b = (byte)Math.Min(255, b + (int)(40 * glowStrength));
                    }

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void AddSwirlingFog(SKBitmap bitmap)
    {
        var fogStrength = FogDensity * Intensity;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            // Draw fog in waves
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Swirling fog pattern using sine waves
                var fogValue = Math.Sin(x * 0.02 + _frameCount * 0.05) *
                               Math.Sin(y * 0.03 - _frameCount * 0.03) *
                               fogStrength;

                if (fogValue > 0.3)
                {
                    var idx = y * width + x;
                    var pixel = pixels[idx];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Grayish-purple fog
                    var fogAmount = (byte)(fogValue * 40);
                    r = (byte)Math.Min(255, r + fogAmount);
                    g = (byte)Math.Min(255, g + fogAmount * 0.8f);
                    b = (byte)Math.Min(255, b + fogAmount * 1.2f);

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void AddFlickers(SKBitmap bitmap)
    {
        // Random flicker effect (like lightning)
        if (_random.Next(100) < 2) // 2% chance per frame
        {
            var flickerStrength = (float)_random.NextDouble() * 0.4f * Intensity;

            unsafe
            {
                var pixels = (uint*)bitmap.GetPixels().ToPointer();
                var pixelCount = bitmap.Width * bitmap.Height;

                for (var i = 0; i < pixelCount; i++)
                {
                    var pixel = pixels[i];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Brief bright flash with purple tint
                    r = (byte)Math.Min(255, r + (int)(100 * flickerStrength));
                    g = (byte)Math.Min(255, g + (int)(80 * flickerStrength));
                    b = (byte)Math.Min(255, b + (int)(120 * flickerStrength));

                    pixels[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void AddPumpkinGlow(SKBitmap bitmap)
    {
        var glowIntensity = (float)Math.Sin(_frameCount * 0.05) * 0.3f + 0.7f;

        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            // Bottom corners glow (like jack-o-lanterns)
            for (var corner = 0; corner < 2; corner++)
            {
                var centerX = corner == 0 ? width / 6 : width * 5 / 6;
                var centerY = height * 5 / 6;

                for (var dy = -30; dy <= 30; dy++)
                for (var dx = -30; dx <= 30; dx++)
                {
                    var x = centerX + dx;
                    var y = centerY + dy;

                    if (x < 0 || x >= width || y < 0 || y >= height) continue;

                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance > 30) continue;

                    var idx = y * width + x;
                    var pixel = pixels[idx];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    var falloff = (1.0f - (float)distance / 30) * glowIntensity * Intensity;
                    r = (byte)Math.Min(255, r + (int)(80 * falloff)); // Orange glow
                    g = (byte)Math.Min(255, g + (int)(40 * falloff));

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void DrawGhosts(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var ghost in _ghosts)
            {
                var floatOffset = (int)(Math.Sin(ghost.FloatPhase * Math.PI / 180) * 5);
                var y = (int)ghost.Y + floatOffset;

                // Draw wispy ghost shape
                for (var dy = -ghost.Size; dy <= ghost.Size; dy++)
                for (var dx = -ghost.Size / 2; dx <= ghost.Size / 2; dx++)
                {
                    var gx = (int)ghost.X + dx;
                    var gy = y + dy;

                    if (gx < 0 || gx >= width || gy < 0 || gy >= height) continue;

                    // Wispy, ethereal shape
                    var distance = Math.Sqrt(dx * dx * 4 + dy * dy) / ghost.Size;
                    if (distance > 1) continue;

                    var wispiness = (float)Math.Sin((dx + dy + _frameCount) * 0.3) * 0.3f + 0.7f;
                    var alpha = (byte)((1.0f - distance) * ghost.Opacity * 255 * wispiness * Intensity);

                    var idx = gy * width + gx;
                    var pixel = pixels[idx];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Pale ghostly white with slight cyan
                    var blend = alpha / 255f;
                    r = (byte)Math.Min(255, r + (220 - r) * blend);
                    g = (byte)Math.Min(255, g + (230 - g) * blend);
                    b = (byte)Math.Min(255, b + (240 - b) * blend);

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private void UpdateGhosts(int width, int height)
    {
        foreach (var ghost in _ghosts)
        {
            ghost.FloatPhase += 2;
            ghost.X += (float)Math.Sin(ghost.FloatPhase * 0.1) * 0.5f;

            if (ghost.X < -ghost.Size)
                ghost.X = width + ghost.Size;
            if (ghost.X > width + ghost.Size)
                ghost.X = -ghost.Size;
        }
    }

    private void DrawBats(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;

            foreach (var bat in _bats)
            {
                var wingFlap = Math.Sin(bat.WingPhase * Math.PI / 180);
                var wingSpread = (int)(bat.Size * (0.7 + wingFlap * 0.3));

                // Draw bat silhouette
                var x = (int)bat.X;
                var y = (int)bat.Y;

                // Body
                for (var dy = -bat.Size / 4; dy <= bat.Size / 4; dy++)
                for (var dx = -bat.Size / 6; dx <= bat.Size / 6; dx++)
                {
                    var bx = x + dx;
                    var by = y + dy;

                    if (bx >= 0 && bx < width && by >= 0 && by < height)
                    {
                        var idx = by * width + bx;
                        pixels[idx] = 0xFF000000u; // Pure black silhouette
                    }
                }

                // Wings
                for (var wx = -wingSpread; wx <= wingSpread; wx++)
                {
                    var wingY = y + (int)(Math.Abs(wx) * 0.3);
                    var bx = x + wx;

                    if (bx >= 0 && bx < width && wingY >= 0 && wingY < height)
                    {
                        var idx = wingY * width + bx;
                        pixels[idx] = 0xFF000000u;
                    }
                }
            }
        }
    }

    private void UpdateBats(int width, int height)
    {
        foreach (var bat in _bats)
        {
            bat.X += bat.Speed;
            bat.WingPhase += 15;

            if (bat.X > width + bat.Size)
            {
                bat.X = -bat.Size;
                bat.Y = _random.Next(height / 2);
            }
        }
    }

    private void AddCreepyVignette(SKBitmap bitmap)
    {
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels().ToPointer();
            var width = bitmap.Width;
            var height = bitmap.Height;
            var centerX = width / 2f;
            var centerY = height / 2f;
            var maxDist = (float)Math.Sqrt(centerX * centerX + centerY * centerY);

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var dx = x - centerX;
                var dy = y - centerY;
                var distance = (float)Math.Sqrt(dx * dx + dy * dy);
                var vignette = Math.Min(1.0f, distance / maxDist);

                if (vignette > 0.5f)
                {
                    var idx = y * width + x;
                    var pixel = pixels[idx];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    var darken = (vignette - 0.5f) * 2 * Intensity * 0.5f;
                    r = (byte)(r * (1 - darken));
                    g = (byte)(g * (1 - darken));
                    b = (byte)(b * (1 - darken));

                    pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    private unsafe bool IsEdgePixel(uint* pixels, int x, int y, int width, int height)
    {
        if (x == 0 || x == width - 1 || y == 0 || y == height - 1) return false;

        var center = GetBrightness(pixels[y * width + x]);

        var neighbors = new[]
        {
            GetBrightness(pixels[(y - 1) * width + x]),
            GetBrightness(pixels[(y + 1) * width + x]),
            GetBrightness(pixels[y * width + (x - 1)]),
            GetBrightness(pixels[y * width + x + 1])
        };

        return neighbors.Any(n => Math.Abs(n - center) > 30);
    }

    private int GetBrightness(uint pixel)
    {
        var r = (pixel >> 16) & 0xFF;
        var g = (pixel >> 8) & 0xFF;
        var b = pixel & 0xFF;
        return (int)(r * 0.299 + g * 0.587 + b * 0.114);
    }

    private class Bat
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Speed { get; set; }
        public int Size { get; set; }
        public float WingPhase { get; set; }
    }

    private class Ghost
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float FloatPhase { get; set; }
        public float Opacity { get; set; }
        public int Size { get; set; }
    }
}