using CanvasManagement.Tools.TtfToBdf;


//// Example 1: Convert Arial 12px
//var converter = new TtfToBdfConverter();
//var output = converter.Convert("Arial", 12, "./BDF");
//Console.WriteLine($"Created: {output}");

//// Example 2: Convert multiple sizes
//var sizes = new[] { 8, 10, 12, 14, 16, 20 };
//foreach (var size in sizes)
//{
//    var file = converter.Convert("Consolas", size, "./BDF");
//    Console.WriteLine($"Created: {file}");
//}

//// Example 3: Convert with extended ASCII
//var extended = converter.Convert("Comic Sans MS", 16, "./BDF", includeExtended: true);
//Console.WriteLine($"Created with extended chars: {extended}");

//// Example 4: Popular monospace fonts
//var fonts = new[] { "Consolas", "Courier New", "Lucida Console" };
//foreach (var font in fonts)
//{
//    try
//    {
//        var file = converter.Convert(font, 12, "./BDF");
//        Console.WriteLine($"✓ {font}: {file}");
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"✗ {font}: {ex.Message}");
//    }
//}
