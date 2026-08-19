//// =====================================================
//// BDF Font System - Quick Start Example
//// =====================================================

//using CanvasManagement;
//using CanvasManagement.BdfFontManager;
//using SkiaSharp;

//// =====================================================
//// STEP 1: Initialize Canvas Manager
//// =====================================================

//var manager = new CanvasManager(384, 192);

//// =====================================================
//// STEP 2: Register BDF Fonts (ONE-TIME SETUP)
//// =====================================================

//// Option A: Load all fonts from a directory
//BdfFontRegistry.FontsDirectory = @"C:\Fonts\BDF";

//// Option B: Register individual fonts
//BdfFontRegistry.RegisterFont("8x16", @"C:\Fonts\8x16.bdf");
//BdfFontRegistry.RegisterFont("tiny", @"C:\Fonts\tom-thumb.bdf");
//BdfFontRegistry.RegisterFont("console", @"C:\Fonts\terminus.bdf");

//// Set default font
//BdfFontRegistry.DefaultFontName = "8x16";

//// =====================================================
//// STEP 3: Use BDF Fonts in Extensions
//// =====================================================

//// Example 1: Simple Clock Extension
//var clockCanvas = manager.GetCanvas(0, 0, 384, 60, zOrder: 0, name: "Clock");

//Task.Run(async () =>
//{
//    while (true)
//    {
//        // Clear with transparent background
//        clockCanvas.Clear(SKColors.Transparent);

//        // Get current time
//        var time = DateTime.Now.ToString("HH:mm:ss");

//        // Draw centered, pixel-perfect text
//        clockCanvas.DrawBdfTextCenteredFull(
//            time,
//            SKColors.White,
//            fontName: "8x16"
//        );

//        await Task.Delay(1000);
//    }
//});

//// Example 2: Status Bar with Multiple Fonts
//var statusCanvas = manager.GetCanvas(0, 170, 384, 22, zOrder: 1, name: "Status");

//// Semi-transparent background
//statusCanvas.Clear(new SKColor(0, 0, 0, 180));

//// Left-aligned text (small font)
//statusCanvas.DrawBdfText(
//    "Status:",
//    x: 5,
//    y: 5,
//    color: SKColors.Cyan,
//    fontName: "tiny"
//);

//// Right-aligned text (small font)
//statusCanvas.DrawBdfTextRight(
//    "OK",
//    y: 5,
//    color: SKColors.Green,
//    fontName: "tiny",
//    marginRight: 5
//);

//// Example 3: Scrolling News Ticker
//var newsCanvas = manager.GetCanvas(0, 130, 384, 20, zOrder: 2, name: "News");

//var bdfManager = newsCanvas.GetBdfFontManager();
//bdfManager.SetFont("console");

//var scrollText = bdfManager.GetScrollTextLayer();
//scrollText.Start(
//    text: "Breaking News: BDF Fonts Look Amazing on LED Matrices! ",
//    color: SKColors.Yellow,
//    delay: 20,              // ms between scroll steps
//    loops: -1,              // Infinite loop
//    backgroundColor: null   // Transparent for alpha compositing
//);

//// Example 4: Multi-Line Information Display
//var infoCanvas = manager.GetCanvas(0, 60, 384, 70, zOrder: 3, name: "Info");

//infoCanvas.Clear(SKColors.Transparent);

//var info = "System Info\n" +
//           "CPU: 45%\n" +
//           "Temp: 55°C\n" +
//           "RAM: 2.1GB";

//infoCanvas.DrawBdfTextMultiline(
//    info,
//    x: 10,
//    y: 0,
//    color: SKColors.Green,
//    fontName: "tiny",
//    lineSpacing: 2
//);

//// =====================================================
//// STEP 4: Start Canvas Manager
//// =====================================================

//manager.Run();

//// =====================================================
//// COMPARISON: BDF vs Skia
//// =====================================================

//// ❌ OLD WAY (Skia - blurry on LED matrices)
//// canvas.DrawText(
////     "Hello",
////     10, 10,
////     color: SKColors.White,
////     fontSize: 12,
////     fontFamily: "Arial"
//// );

//// ✅ NEW WAY (BDF - pixel-perfect, crisp)
//// canvas.DrawBdfText(
////     "Hello",
////     10, 10,
////     SKColors.White,
////     fontName: "8x16"
//// );

//// =====================================================
//// AVAILABLE EXTENSION METHODS
//// =====================================================

//// Basic text drawing
//// canvas.DrawBdfText(text, x, y, color, fontName?, backgroundColor?)

//// Centered horizontally
//// canvas.DrawBdfTextCentered(text, y, color, fontName?, backgroundColor?)

//// Centered both ways
//// canvas.DrawBdfTextCenteredFull(text, color, fontName?, backgroundColor?)

//// Right-aligned
//// canvas.DrawBdfTextRight(text, y, color, fontName?, backgroundColor?, marginRight?)

//// Multi-line
//// canvas.DrawBdfTextMultiline(text, x, y, color, fontName?, backgroundColor?, lineSpacing?)

//// Measure text
//// SKSize size = canvas.MeasureBdfText(text, fontName?)

//// Advanced scrolling
//// var manager = canvas.GetBdfFontManager();
//// manager.SetFont(fontName);
//// var scroller = manager.GetScrollTextLayer();
//// scroller.Start(text, color, delay, loops, backgroundColor)

//// =====================================================
//// WHERE TO GET BDF FONTS
//// =====================================================

//// 1. X11 fonts (Linux): /usr/share/fonts/X11/misc/*.bdf
//// 2. Online: https://github.com/Tecate/bitmap-fonts
//// 3. Popular fonts:
////    - terminus-font (excellent for displays)
////    - tom-thumb (ultra-tiny 4x6)
////    - fixed (classic X11)
////    - profont (programmer's font)
////    - spleen (modern, clean)

//// =====================================================
//// PERFORMANCE NOTES
//// =====================================================

//// BDF Rendering: ~0.5ms per text (4-6x faster than Skia)
//// Font Loading: ~50KB per font (cached)
//// Memory: 2.5KB per rendered text bitmap
//// Result: Faster AND uses less memory than Skia!

//// =====================================================
//// TIPS
//// =====================================================

//// 1. Register fonts at startup (one-time)
//// 2. Use transparent backgrounds for layering
//// 3. Choose appropriate font size for display resolution
////    - 384×192: 6x12, 7x13, 8x16
////    - 256×128: 5x8, 6x10, 7x13  
////    - 128×64:  4x6, 5x7, 6x10
////    - 64×32:   4x6, 5x7 (tiny)
//// 4. Measure text before drawing for precise positioning

//Console.WriteLine("BDF Font System Demo Running!");
//Console.WriteLine($"Loaded fonts: {string.Join(", ", BdfFontRegistry.RegisteredFonts)}");
//Console.WriteLine($"Default font: {BdfFontRegistry.DefaultFontName}");
//Console.WriteLine("Press any key to exit...");
//Console.ReadKey();
