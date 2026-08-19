//using CanvasManagement;
//using System;
//using System.Linq;

//// Example: Dynamic Extension Usage

//Console.WriteLine("=== Dynamic Extension System Demo ===\n");

//// Create canvas manager
//var canvasManager = new CanvasManager(384, 192);
//var canvas = canvasManager.GetCanvas(0, 0, 384, 192, 1);
//canvasManager.Run();

//// 1. Load extensions dynamically
//Console.WriteLine("Loading extensions...");
//Canvas.LoadExtensionAssemblies();

//// 2. Discover what's available
//var extensions = Canvas.GetAvailableExtensionInfo().ToList();

//Console.WriteLine($"\nFound {extensions.Count} extensions:");
//foreach (var ext in extensions)
//{
//    Console.WriteLine($"  [{ext.Category}] {ext.DisplayName}");
//}

//// 3. Create extension dynamically (no hardcoded GetXXX() calls!)
//Console.WriteLine("\n--- Creating Starfield Extension ---");

//var starfield = canvas.CreateDynamicExtensionByDisplayName("Starfield");

//if (starfield != null)
//{
//    Console.WriteLine($"Created: {starfield.Name}");
    
//    // 4. Configure dynamically
//    starfield.SetProperty("StarCount", 300);
//    starfield.SetProperty("ColoredStars", true);
//    starfield.SetProperty("MinSpeed", 2);
//    starfield.SetProperty("MaxSpeed", 6);
    
//    Console.WriteLine("Configuration:");
//    Console.WriteLine($"  StarCount: {starfield.GetProperty("StarCount")}");
//    Console.WriteLine($"  ColoredStars: {starfield.GetProperty("ColoredStars")}");
    
//    // 5. Start
//    Console.WriteLine("\nStarting extension...");
//    starfield.Start();
//    Console.WriteLine($"Is Running: {starfield.IsRunning}");
    
//    // 6. Let it run
//    Console.WriteLine("\nPress Enter to switch to Plasma...");
//    Console.ReadLine();
    
//    // 7. Stop and dispose
//    starfield.Stop();
//    starfield.Dispose();
//    Console.WriteLine("Starfield stopped");
//}

//// 8. Switch to different extension dynamically
//Console.WriteLine("\n--- Creating Plasma Extension ---");

//var plasma = canvas.CreateDynamicExtensionByDisplayName("Plasma");

//if (plasma != null)
//{
//    Console.WriteLine($"Created: {plasma.Name}");
    
//    plasma.SetProperty("Resolution", 2);
//    plasma.SetProperty("Speed", 0.1);
//    plasma.SetProperty("ColorShift", 360);
    
//    plasma.Start();
//    Console.WriteLine($"Is Running: {plasma.IsRunning}");
    
//    Console.WriteLine("\nPress Enter to stop...");
//    Console.ReadLine();
    
//    plasma.Stop();
//    plasma.Dispose();
//    Console.WriteLine("Plasma stopped");
//}

//canvasManager.Stop();

//Console.WriteLine("\n=== Demo Complete ===");
//Console.WriteLine("\nKey Points:");
//Console.WriteLine("  ✓ No hardcoded canvas.GetXXX() calls");
//Console.WriteLine("  ✓ Extensions created dynamically by name");
//Console.WriteLine("  ✓ Properties configured via reflection");
//Console.WriteLine("  ✓ Works exactly like filter system");
//Console.WriteLine("  ✓ Fully plugin-based architecture");
