using CanvasManagement;
using System;

// Test extension discovery
Console.WriteLine("=== Testing Extension Discovery ===\n");

var extensions = Canvas.GetAvailableExtensionInfo();
var extensionList = extensions.ToList();

Console.WriteLine($"Found {extensionList.Count} extensions:\n");

foreach (var ext in extensionList)
{
    Console.WriteLine($"Extension: {ext.DisplayName}");
    Console.WriteLine($"  Type: {ext.Name}");
    Console.WriteLine($"  Category: {ext.Category}");
    Console.WriteLine($"  Description: {ext.Description}");
    Console.WriteLine($"  Method: {ext.ExtensionMethodName}");
    Console.WriteLine($"  Assembly: {ext.AssemblyName}");
    Console.WriteLine($"  Parameters: {ext.Parameters.Count}");
    Console.WriteLine();
}

if (extensionList.Count == 0)
{
    Console.WriteLine("No extensions found! Checking types...\n");
    
    var types = Canvas.GetAvailableExtensionTypes();
    Console.WriteLine($"Found {types.Count()} types with [ExtensionInfo]");
}
