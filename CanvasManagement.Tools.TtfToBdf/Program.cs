using System.Text;
using SkiaSharp;

namespace CanvasManagement.Tools.TtfToBdf;

/// <summary>
///     TrueType to BDF Font Converter
///     Converts Windows TrueType fonts to BDF format for LED matrices
/// </summary>
internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   TrueType to BDF Font Converter                           ║");
        Console.WriteLine("║   For LED Matrix Displays                                  ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        if (args.Length > 0 && args[0] == "--list")
        {
            ListInstalledFonts();
            return;
        }

        if (args.Length > 0 && args[0] == "--help")
        {
            ShowHelp();
            return;
        }

        // Interactive mode
        Console.WriteLine("Available options:");
        Console.WriteLine("  1. Convert a specific font");
        Console.WriteLine("  2. List installed fonts");
        Console.WriteLine("  3. Batch convert popular fonts");
        Console.WriteLine();
        Console.Write("Choose option (1-3): ");

        var choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                ConvertInteractive();
                break;
            case "2":
                ListInstalledFonts();
                break;
            case "3":
                BatchConvertPopularFonts();
                break;
            default:
                Console.WriteLine("Invalid option");
                break;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  TtfToBdf                           - Interactive mode");
        Console.WriteLine("  TtfToBdf --list                    - List installed fonts");
        Console.WriteLine("  TtfToBdf <font> <size> [output]    - Convert specific font");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  TtfToBdf Arial 12 arial-12.bdf");
        Console.WriteLine("  TtfToBdf \"Comic Sans MS\" 16");
        Console.WriteLine();
    }

    private static void ListInstalledFonts()
    {
        Console.WriteLine("Scanning installed fonts...");
        Console.WriteLine();

        var fontManager = SKFontManager.Default;
        var familyCount = fontManager.FontFamilyCount;

        Console.WriteLine($"Found {familyCount} font families:");
        Console.WriteLine(new string('-', 60));

        for (var i = 0; i < familyCount; i++)
        {
            var familyName = fontManager.GetFamilyName(i);
            Console.WriteLine($"  {i + 1}. {familyName}");
        }

        Console.WriteLine();
        Console.WriteLine($"Total: {familyCount} fonts");
    }

    private static void ConvertInteractive()
    {
        Console.WriteLine();
        Console.Write("Font family name (e.g., Arial, Consolas): ");
        var fontFamily = Console.ReadLine() ?? "Arial";

        Console.Write("Font size in pixels (e.g., 8, 12, 16, 20): ");
        if (!int.TryParse(Console.ReadLine(), out var fontSize)) fontSize = 12;

        Console.Write("Output directory (default: ./BDF): ");
        var outputDir = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(outputDir)) outputDir = "./BDF";

        Console.Write("Include extended characters? (y/n, default: n): ");
        var extended = Console.ReadLine()?.ToLower() == "y";

        Console.WriteLine();
        Console.WriteLine("Converting...");

        try
        {
            var converter = new TtfToBdfConverter();
            var outputPath = converter.Convert(fontFamily, fontSize, outputDir, extended);

            Console.WriteLine();
            Console.WriteLine("✓ Conversion successful!");
            Console.WriteLine($"  Output: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"✗ Conversion failed: {ex.Message}");
        }
    }

    private static void BatchConvertPopularFonts()
    {
        Console.WriteLine();
        Console.WriteLine("Batch converting popular fonts for LED matrices...");
        Console.WriteLine();

        var fonts = new[]
        {
            ("Arial", new[] { 8, 10, 12, 14, 16, 20 }),
            ("Consolas", new[] { 8, 10, 12, 14, 16 }),
            ("Courier New", new[] { 8, 10, 12, 14, 16 }),
            ("Lucida Console", new[] { 8, 10, 12, 14 }),
            ("Terminal", new[] { 8, 10, 12 }),
            ("Fixedsys", new[] { 8, 12, 16 })
        };

        var outputDir = "./BDF";
        var converter = new TtfToBdfConverter();
        var successful = 0;
        var failed = 0;

        foreach (var (family, sizes) in fonts)
        foreach (var size in sizes)
            try
            {
                Console.Write($"Converting {family} {size}px... ");
                var output = converter.Convert(family, size, outputDir);
                Console.WriteLine("✓");
                successful++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ({ex.Message})");
                failed++;
            }

        Console.WriteLine();
        Console.WriteLine("Batch conversion complete:");
        Console.WriteLine($"  ✓ Successful: {successful}");
        Console.WriteLine($"  ✗ Failed: {failed}");
        Console.WriteLine($"  Output directory: {Path.GetFullPath(outputDir)}");
    }
}

/// <summary>
///     Converts TrueType fonts to BDF format
/// </summary>
public class TtfToBdfConverter
{
    /// <summary>
    ///     Convert a TrueType font to BDF format
    /// </summary>
    public string Convert(string fontFamily, int fontSize, string outputDirectory, bool includeExtended = false)
    {
        // Ensure output directory exists
        Directory.CreateDirectory(outputDirectory);

        // Create output filename
        var safeFamily = string.Join("_", fontFamily.Split(Path.GetInvalidFileNameChars()));
        var outputFile = Path.Combine(outputDirectory, $"{safeFamily}-{fontSize}.bdf");

        // Create typeface
        var typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        if (typeface == null) throw new Exception($"Font '{fontFamily}' not found");

        using var font = new SKFont(typeface, fontSize)
        {
            Subpixel = false,
            Edging = SKFontEdging.Alias
        };
        using var paint = new SKPaint
        {
            IsAntialias = false,
            Color = SKColors.White
        };

        // Determine character range
        var charStart = 32; // Space
        var charEnd = includeExtended ? 255 : 126; // ASCII or extended ASCII

        // Measure font metrics
        var metrics = font.Metrics;
        var ascent = (int)Math.Ceiling(Math.Abs(metrics.Ascent));
        var descent = (int)Math.Ceiling(Math.Abs(metrics.Descent));
        var height = ascent + descent;

        // Build BDF file
        var bdf = new StringBuilder();

        // BDF Header
        bdf.AppendLine("STARTFONT 2.1");
        bdf.AppendLine(
            $"FONT -{safeFamily}-Medium-R-Normal--{fontSize}-{fontSize * 10}-75-75-C-{fontSize * 10}-ISO8859-1");
        bdf.AppendLine($"SIZE {fontSize} 75 75");
        bdf.AppendLine($"FONTBOUNDINGBOX {fontSize} {height} 0 {-descent}");
        bdf.AppendLine("STARTPROPERTIES 2");
        bdf.AppendLine($"FONT_ASCENT {ascent}");
        bdf.AppendLine($"FONT_DESCENT {descent}");
        bdf.AppendLine("ENDPROPERTIES");

        // Count valid characters
        var validChars = new List<int>();
        for (var c = charStart; c <= charEnd; c++)
        {
            var ch = (char)c;
            var glyphId = typeface.GetGlyph(ch);
            if (glyphId != 0) validChars.Add(c);
        }

        bdf.AppendLine($"CHARS {validChars.Count}");

        // Generate glyphs
        foreach (var c in validChars)
        {
            var ch = (char)c;
            GenerateGlyph(bdf, ch, font, paint, ascent, descent, height);
        }

        bdf.AppendLine("ENDFONT");

        // Write to file
        File.WriteAllText(outputFile, bdf.ToString());

        return outputFile;
    }

    private void GenerateGlyph(StringBuilder bdf, char ch, SKFont font, SKPaint paint, int ascent, int descent, int height)
    {
        // Measure character
        var width = font.MeasureText(ch.ToString(), out var bounds);

        var glyphWidth = (int)Math.Ceiling(width);
        var glyphHeight = height;

        // Handle zero-width characters
        if (glyphWidth == 0) glyphWidth = 1;

        // Create bitmap for the glyph
        var bitmapWidth = glyphWidth + 4; // Extra padding
        var bitmapHeight = glyphHeight + 4;

        // IMPORTANT: Use RGBA format and render WHITE text on BLACK background
        using var surface = SKSurface.Create(new SKImageInfo(bitmapWidth, bitmapHeight, SKColorType.Rgba8888));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        // Create a temporary paint for WHITE text (so we can see it!)
        using var whitePaint = paint.Clone();
        whitePaint.Color = SKColors.White; // White text!

        // Draw character
        var x = 2f - bounds.Left;
        var y = 2f + ascent;
        canvas.DrawText(ch.ToString(), x, y, SKTextAlign.Left, font, whitePaint);

        // Get pixel data
        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();

        // Convert to BDF format
        bdf.AppendLine($"STARTCHAR {GetCharName(ch)}");
        bdf.AppendLine($"ENCODING {(int)ch}");
        bdf.AppendLine("SWIDTH 500 0");
        bdf.AppendLine($"DWIDTH {glyphWidth} 0");
        bdf.AppendLine($"BBX {glyphWidth} {glyphHeight} 0 {-descent}");
        bdf.AppendLine("BITMAP");

        // Extract bitmap data (RGBA format: 4 bytes per pixel)
        var bytes = pixmap.GetPixelSpan();
        var bytesPerPixel = 4; // RGBA

        for (var row = 0; row < glyphHeight; row++)
        {
            // Calculate bytes needed for this width
            var bytesNeeded = (glyphWidth + 7) / 8;
            var rowBytes = new byte[bytesNeeded];

            for (var col = 0; col < glyphWidth; col++)
            {
                var pixelX = col + 2;
                var pixelY = row + 2;

                if (pixelX < bitmapWidth && pixelY < bitmapHeight)
                {
                    var pixelIndex = (pixelY * bitmapWidth + pixelX) * bytesPerPixel;
                    if (pixelIndex + 3 < bytes.Length)
                    {
                        // RGBA format: [R, G, B, A]
                        var r = bytes[pixelIndex];
                        var g = bytes[pixelIndex + 1];
                        var b = bytes[pixelIndex + 2];
                        var a = bytes[pixelIndex + 3];

                        // Calculate grayscale value
                        var gray = (r + g + b) / 3;

                        // Threshold: if pixel is bright (white text), set bit
                        if (gray > 127)
                        {
                            // Set bit in the appropriate byte
                            var byteIndex = col / 8;
                            var bitIndex = 7 - col % 8; // MSB first
                            rowBytes[byteIndex] |= (byte)(1 << bitIndex);
                        }
                    }
                }
            }

            // Convert bytes to hex string
            var hexString = string.Join("", rowBytes.Select(b => b.ToString("X2")));
            bdf.AppendLine(hexString);
        }

        bdf.AppendLine("ENDCHAR");
    }

    private string GetCharName(char ch)
    {
        return ch switch
        {
            ' ' => "space",
            '!' => "exclam",
            '"' => "quotedbl",
            '#' => "numbersign",
            '$' => "dollar",
            '%' => "percent",
            '&' => "ampersand",
            '\'' => "quotesingle",
            '(' => "parenleft",
            ')' => "parenright",
            '*' => "asterisk",
            '+' => "plus",
            ',' => "comma",
            '-' => "minus",
            '.' => "period",
            '/' => "slash",
            ':' => "colon",
            ';' => "semicolon",
            '<' => "less",
            '=' => "equal",
            '>' => "greater",
            '?' => "question",
            '@' => "at",
            '[' => "bracketleft",
            '\\' => "backslash",
            ']' => "bracketright",
            '^' => "asciicircum",
            '_' => "underscore",
            '`' => "grave",
            '{' => "braceleft",
            '|' => "bar",
            '}' => "braceright",
            '~' => "asciitilde",
            _ => ch >= '0' && ch <= '9' ? $"digit{ch}" :
                ch >= 'A' && ch <= 'Z' ? ch.ToString() :
                ch >= 'a' && ch <= 'z' ? ch.ToString() :
                $"char{(int)ch}"
        };
    }
}