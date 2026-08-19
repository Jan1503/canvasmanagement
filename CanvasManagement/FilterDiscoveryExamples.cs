//using CanvasManagement;
//using CanvasManagement.Filters;

//namespace CanvasManagement.Examples;

///// <summary>
///// Examples demonstrating the filter discovery and management API
///// </summary>
//public static class FilterDiscoveryExamples
//{
//    /// <summary>
//    /// Example 1: Discover all available filter types at runtime
//    /// </summary>
//    public static void DiscoverAllFilters()
//    {
//        Console.WriteLine("=== Discovering All Available Filters ===\n");
        
//        var filterTypes = CanvasManager.GetAvailableFilterTypes();
        
//        foreach (var filterType in filterTypes)
//        {
//            Console.WriteLine($"Filter: {filterType.Name}");
//            Console.WriteLine($"  Full Name: {filterType.FullName}");
//            Console.WriteLine($"  Assembly: {filterType.Assembly.GetName().Name}");
//            Console.WriteLine();
//        }
//    }
    
//    /// <summary>
//    /// Example 2: Get detailed metadata about available filters
//    /// </summary>
//    public static void GetFilterMetadata()
//    {
//        Console.WriteLine("=== Filter Metadata ===\n");
        
//        var filterInfo = CanvasManager.GetAvailableFilterInfo();
        
//        foreach (var info in filterInfo)
//        {
//            Console.WriteLine($"Name: {info.Name}");
//            Console.WriteLine($"Type: {info.Type.Name}");
//            Console.WriteLine($"Assembly: {info.AssemblyName}");
//            Console.WriteLine($"ToString: {info}");
//            Console.WriteLine();
//        }
//    }
    
//    /// <summary>
//    /// Example 3: Create filters dynamically by name
//    /// </summary>
//    public static void CreateFiltersByName(CanvasManager canvasManager)
//    {
//        Console.WriteLine("=== Creating Filters Dynamically ===\n");
        
//        // Create filters by simple name
//        var neoFilter = CanvasManager.CreateFilter("NeoCodeVisionFilter");
//        if (neoFilter != null)
//        {
//            neoFilter.Intensity = 0.8f;
//            canvasManager.AddFilter(neoFilter);
//            Console.WriteLine($"Created and added: {neoFilter.Name}");
//        }
        
//        // Create by full type name
//        var comicFilter = CanvasManager.CreateFilter("CanvasManagement.Filters.ComicBookFilter");
//        if (comicFilter != null)
//        {
//            comicFilter.Intensity = 0.9f;
//            canvasManager.AddFilter(comicFilter);
//            Console.WriteLine($"Created and added: {comicFilter.Name}");
//        }
        
//        // Handle missing filter
//        var missingFilter = CanvasManager.CreateFilter("NonExistentFilter");
//        if (missingFilter == null)
//        {
//            Console.WriteLine("NonExistentFilter not found (expected)");
//        }
//    }
    
//    /// <summary>
//    /// Example 4: Query current filter pipeline
//    /// </summary>
//    public static void QueryFilterPipeline(CanvasManager canvasManager)
//    {
//        Console.WriteLine("=== Querying Filter Pipeline ===\n");
        
//        // Get filter count
//        var count = canvasManager.GetFilterCount();
//        Console.WriteLine($"Total filters in pipeline: {count}");
        
//        // Get all filters
//        var filters = canvasManager.GetFilters();
//        Console.WriteLine($"\nAll filters:");
//        for (int i = 0; i < filters.Count; i++)
//        {
//            var filter = filters[i];
//            Console.WriteLine($"  [{i}] {filter.Name} - Enabled: {filter.Enabled}, Intensity: {filter.Intensity:F2}");
//        }
        
//        // Get filter by index
//        var firstFilter = canvasManager.GetFilterAt(0);
//        if (firstFilter != null)
//        {
//            Console.WriteLine($"\nFirst filter: {firstFilter.Name}");
//        }
        
//        // Get filter by name
//        var neoFilter = canvasManager.GetFilterByName("Neo Code Vision");
//        if (neoFilter != null)
//        {
//            Console.WriteLine($"Found Neo Code Vision filter with intensity: {neoFilter.Intensity:F2}");
//        }
//    }
    
//    /// <summary>
//    /// Example 5: Type-specific filter queries
//    /// </summary>
//    public static void QueryFiltersByType(CanvasManager canvasManager)
//    {
//        Console.WriteLine("=== Querying Filters by Type ===\n");
        
//        // Check if specific filter type exists
//        if (canvasManager.HasFilterOfType<NeoCodeVisionFilter>())
//        {
//            Console.WriteLine("Neo Code Vision filter is in the pipeline");
//        }
        
//        if (canvasManager.HasFilterOfType<ComicBookFilter>())
//        {
//            Console.WriteLine("Comic Book filter is in the pipeline");
//        }
        
//        // Get all filters of a specific type
//        var matrixFilters = canvasManager.GetFiltersOfType<MatrixCodeRainFilter>();
//        Console.WriteLine($"\nFound {matrixFilters.Count()} Matrix Code Rain filters");
        
//        foreach (var filter in matrixFilters)
//        {
//            Console.WriteLine($"  - Image Retention: {filter.ImageRetention:F2}");
//            Console.WriteLine($"  - Fall Speed: {filter.FallSpeed}");
//        }
//    }
    
//    /// <summary>
//    /// Example 6: Build a filter pipeline from configuration
//    /// </summary>
//    public static void BuildPipelineFromConfig(CanvasManager canvasManager)
//    {
//        Console.WriteLine("=== Building Pipeline from Configuration ===\n");
        
//        // Simulated configuration (could come from JSON, XML, database, etc.)
//        var config = new[]
//        {
//            new { Type = "NeoCodeVisionFilter", Intensity = 0.9f },
//            new { Type = "VignetteFilter", Intensity = 0.5f },
//            new { Type = "GrainFilter", Intensity = 0.2f }
//        };
        
//        canvasManager.ClearFilters();
        
//        foreach (var filterConfig in config)
//        {
//            var filter = CanvasManager.CreateFilter(filterConfig.Type);
//            if (filter != null)
//            {
//                filter.Intensity = filterConfig.Intensity;
//                canvasManager.AddFilter(filter);
//                Console.WriteLine($"Added {filter.Name} with intensity {filter.Intensity:F2}");
//            }
//            else
//            {
//                Console.WriteLine($"Warning: Could not create filter {filterConfig.Type}");
//            }
//        }
//    }
    
//    /// <summary>
//    /// Example 7: Dynamic filter selection UI (console-based)
//    /// </summary>
//    public static void InteractiveFilterSelection(CanvasManager canvasManager)
//    {
//        Console.WriteLine("=== Interactive Filter Selection ===\n");
        
//        var availableFilters = CanvasManager.GetAvailableFilterInfo().ToList();
        
//        Console.WriteLine("Available filters:");
//        for (int i = 0; i < availableFilters.Count; i++)
//        {
//            Console.WriteLine($"{i + 1}. {availableFilters[i].Name}");
//        }
        
//        Console.Write("\nEnter filter number to add (0 to cancel): ");
//        if (int.TryParse(Console.ReadLine(), out int selection) && 
//            selection > 0 && selection <= availableFilters.Count)
//        {
//            var selectedFilter = availableFilters[selection - 1];
//            var filter = CanvasManager.CreateFilter(selectedFilter.Type.Name);
            
//            if (filter != null)
//            {
//                Console.Write("Enter intensity (0.0 - 1.0): ");
//                if (float.TryParse(Console.ReadLine(), out float intensity))
//                {
//                    filter.Intensity = Math.Clamp(intensity, 0f, 1f);
//                }
                
//                canvasManager.AddFilter(filter);
//                Console.WriteLine($"\n? Added {filter.Name} with intensity {filter.Intensity:F2}");
//            }
//        }
//    }
    
//    /// <summary>
//    /// Example 8: Filter pipeline inspection and debugging
//    /// </summary>
//    public static void InspectPipeline(CanvasManager canvasManager)
//    {
//        Console.WriteLine("=== Filter Pipeline Inspection ===\n");
        
//        var filters = canvasManager.GetFilters();
        
//        Console.WriteLine($"Pipeline contains {filters.Count} filter(s)");
//        Console.WriteLine($"Active filters: {filters.Count(f => f.Enabled)}");
//        Console.WriteLine($"Disabled filters: {filters.Count(f => !f.Enabled)}");
        
//        Console.WriteLine("\nDetailed breakdown:");
//        Console.WriteLine("????????????????????????????????????????????????????????");
//        Console.WriteLine("? Idx ? Filter Name              ? Enabled ? Intensity ?");
//        Console.WriteLine("????????????????????????????????????????????????????????");
        
//        for (int i = 0; i < filters.Count; i++)
//        {
//            var filter = filters[i];
//            var name = filter.Name.Length > 24 ? filter.Name.Substring(0, 21) + "..." : filter.Name.PadRight(24);
//            var enabled = filter.Enabled ? "  ?   " : "  ?   ";
//            var intensity = $"{filter.Intensity:F2}".PadLeft(9);
            
//            Console.WriteLine($"? {i,3} ? {name} ? {enabled} ? {intensity} ?");
//        }
        
//        Console.WriteLine("????????????????????????????????????????????????????????");
        
//        // Type distribution
//        Console.WriteLine("\nFilter type distribution:");
//        var typeGroups = filters.GroupBy(f => f.GetType().Name);
//        foreach (var group in typeGroups)
//        {
//            Console.WriteLine($"  {group.Key}: {group.Count()}");
//        }
//    }
//}
