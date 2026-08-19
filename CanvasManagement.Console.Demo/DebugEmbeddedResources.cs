//using System;
//using System.Linq;
//using CanvasManagement;
//using CanvasManagement.Extension.Starfield;
//using CanvasManagement.Canvas.Extensions;

//// Debug script to check embedded resources and icon loading

//Console.WriteLine("=== Embedded Resources Debug ===\n");

//// Check Starfield assembly
//Console.WriteLine("1. Starfield Extension Assembly:");
//var starfieldAssembly = typeof(StarfieldExtension).Assembly;
//var starfieldResources = starfieldAssembly.GetManifestResourceNames();
//Console.WriteLine($"   Total resources: {starfieldResources.Length}");
//foreach (var res in starfieldResources)
//{
//    Console.WriteLine($"   - {res}");
//}

//if (starfieldResources.Length == 0)
//{
//    Console.WriteLine("   ❌ NO RESOURCES FOUND!");
//    Console.WriteLine("   → Missing <EmbeddedResource> in .csproj file");
//}

//Console.WriteLine();

//// Check Canvas.Extensions assembly  
//Console.WriteLine("2. Canvas.Extensions Assembly:");
//var canvasExtAssembly = typeof(PlasmaExtension).Assembly;
//var canvasExtResources = canvasExtAssembly.GetManifestResourceNames();
//Console.WriteLine($"   Total resources: {canvasExtResources.Length}");
//foreach (var res in canvasExtResources)
//{
//    Console.WriteLine($"   - {res}");
//}

//if (canvasExtResources.Length == 0)
//{
//    Console.WriteLine("   ❌ NO RESOURCES FOUND!");
//    Console.WriteLine("   → Missing <EmbeddedResource> in .csproj file");
//}

//Console.WriteLine();

//// Check extension discovery
//Console.WriteLine("3. Extension Discovery:");
//var extensions = Canvas.GetAvailableExtensionInfo().ToList();
//Console.WriteLine($"   Found {extensions.Count} extensions");

//foreach (var ext in extensions)
//{
//    Console.WriteLine($"\n   Extension: {ext.DisplayName}");
//    Console.WriteLine($"   - Category: {ext.Category}");
//    Console.WriteLine($"   - Assembly: {ext.AssemblyName}");
//    Console.WriteLine($"   - IconData: {(string.IsNullOrEmpty(ext.IconData) ? "❌ EMPTY" : $"✅ {ext.IconData.Length} chars")}");
    
//    // Check attribute
//    var attr = ext.Type.GetCustomAttributes(typeof(CanvasManagement.Interfaces.ExtensionInfoAttribute), false)
//        .FirstOrDefault() as CanvasManagement.Interfaces.ExtensionInfoAttribute;
    
//    if (attr != null)
//    {
//        Console.WriteLine($"   - IconResourceName: {attr.IconResourceName ?? "(null)"}");
//        Console.WriteLine($"   - IconData (attr): {(string.IsNullOrEmpty(attr.IconData) ? "(null)" : $"{attr.IconData.Length} chars")}");
//    }
//}

//Console.WriteLine("\n=== Test Complete ===");
//Console.WriteLine("\nIf resources are EMPTY:");
//Console.WriteLine("1. Edit .csproj files to add:");
//Console.WriteLine("   <ItemGroup>");
//Console.WriteLine("     <EmbeddedResource Include=\"Icons\\*.svg\" />");
//Console.WriteLine("   </ItemGroup>");
//Console.WriteLine("2. Rebuild solution");
//Console.WriteLine("3. Run this test again");
