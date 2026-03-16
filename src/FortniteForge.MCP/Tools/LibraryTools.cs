using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for searching and browsing the UEFN asset library.
/// Enables Claude to find relevant verse files, materials, device configs,
/// and assets across a collection of UEFN projects.
/// </summary>
[McpServerToolType]
public class LibraryTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Searches the UEFN library for verse files, assets, and device configurations " +
        "matching a query. Use this when the user asks about existing systems, wants to " +
        "find similar implementations, or needs to know what's available. " +
        "Examples: 'ranked system', 'vending machine gold', 'damage volume', 'material glass'")]
    public string search_library(
        LibraryIndexer indexer,
        [Description("Search query — keywords describing what you're looking for")] string query)
    {
        var result = indexer.Search(query);

        if (result.VerseFiles.Count == 0 && result.Assets.Count == 0 && result.DeviceTypes.Count == 0)
            return $"No results found for '{query}'. Try different keywords. The library index may need to be rebuilt with build_library_index.";

        var output = new List<string> { $"Library search: \"{query}\"\n" };

        if (result.VerseFiles.Count > 0)
        {
            output.Add($"=== Verse Files ({result.VerseFiles.Count}) ===");
            foreach (var hit in result.VerseFiles.Take(10))
            {
                var vf = hit.Item;
                output.Add($"  [{hit.ProjectName}] {vf.Name} ({vf.LineCount} lines)");
                if (!string.IsNullOrEmpty(vf.Summary))
                    output.Add($"    {vf.Summary}");
                output.Add($"    Path: {vf.FilePath}");
            }
        }

        if (result.Assets.Count > 0)
        {
            output.Add($"\n=== Assets ({result.Assets.Count}) ===");
            foreach (var hit in result.Assets.Take(10))
                output.Add($"  [{hit.ProjectName}] {hit.Item.Name} ({hit.Item.AssetClass})");
        }

        if (result.DeviceTypes.Count > 0)
        {
            output.Add($"\n=== Device Types ({result.DeviceTypes.Count}) ===");
            foreach (var hit in result.DeviceTypes.Take(10))
                output.Add($"  [{hit.ProjectName}] {hit.Item.DisplayName} x{hit.Item.Count}");
        }

        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Lists all materials available in the UEFN library. " +
        "Use when the user asks 'what materials do I have access to' or needs to find a specific material type.")]
    public string list_materials(LibraryIndexer indexer)
    {
        var materials = indexer.GetMaterials();
        if (materials.Count == 0)
            return "No materials found in the library. Run build_library_index first.";

        var grouped = materials.GroupBy(m => m.AssetClass).OrderByDescending(g => g.Count());
        var output = new List<string> { $"Materials in library: {materials.Count}\n" };

        foreach (var group in grouped)
        {
            output.Add($"  {group.Key} ({group.Count()}):");
            foreach (var m in group.Take(20))
                output.Add($"    {m.Name} — {m.RelativePath}");
        }

        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Lists verse files in the library, optionally filtered by keyword. " +
        "Use when the user wants to see what verse code exists or find implementations of specific features.")]
    public string list_verse_files(
        LibraryIndexer indexer,
        [Description("Optional keyword filter (e.g., 'spawner', 'timer', 'ranked')")] string? filter = null)
    {
        var files = indexer.GetVerseFiles(filter);
        if (files.Count == 0)
            return filter != null
                ? $"No verse files matching '{filter}'. Try broader keywords."
                : "No verse files found. Run build_library_index first.";

        var output = new List<string> { $"Verse files{(filter != null ? $" matching '{filter}'" : "")}: {files.Count}\n" };

        foreach (var f in files.Take(30))
        {
            output.Add($"  [{f.ProjectName}] {f.File.Name} ({f.File.LineCount} lines)");
            if (!string.IsNullOrEmpty(f.File.Summary))
                output.Add($"    {f.File.Summary}");
        }

        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Reads the full source code of a verse file from the library. " +
        "Use after finding a relevant file via search_library or list_verse_files.")]
    public string get_verse_source(
        [Description("Full file path to the .verse file")] string filePath)
    {
        if (!File.Exists(filePath))
            return $"File not found: {filePath}";

        if (!filePath.EndsWith(".verse", StringComparison.OrdinalIgnoreCase))
            return "Not a .verse file.";

        var source = File.ReadAllText(filePath);
        var lineCount = source.Split('\n').Length;
        return $"// {Path.GetFileName(filePath)} ({lineCount} lines)\n\n{source}";
    }

    [McpServerTool, Description(
        "Builds or rebuilds the library index from a directory of UEFN projects. " +
        "This scans all projects, verse files, assets, and device types. " +
        "Run this once, then use search_library, list_materials, etc. to query.")]
    public string build_library_index(
        LibraryIndexer indexer,
        [Description("Path to directory containing UEFN projects (e.g., Z:\\UEFN_Resources\\mapContent\\map_resources)")] string libraryPath)
    {
        if (!Directory.Exists(libraryPath))
            return $"Directory not found: {libraryPath}";

        var index = indexer.BuildIndex(libraryPath);

        // Save to a standard location
        var savePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".fortniteforge", "library-index.json");
        indexer.SaveIndex(savePath);

        return $"Library indexed successfully!\n" +
               $"  Projects: {index.Projects.Count}\n" +
               $"  Verse files: {index.TotalVerseFiles}\n" +
               $"  Assets: {index.TotalAssets}\n" +
               $"  Device types: {index.TotalDeviceTypes}\n" +
               $"  Saved to: {savePath}";
    }
}
