using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for reading and inspecting UEFN project assets.
/// These are read-only operations — safe to call anytime.
/// </summary>
[McpServerToolType]
public class AssetTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Lists all assets in the UEFN project's Content directory. " +
        "Returns asset name, class type, file size, and whether it's modifiable. " +
        "Use subfolder to narrow the search, filterByClass to filter by asset type, " +
        "or searchName to find assets by name.")]
    public string list_assets(
        AssetService assetService,
        [Description("Optional subfolder within Content/ to search (e.g., 'MyMaps', 'Devices')")] string? subfolder = null,
        [Description("Filter by asset class (e.g., 'Blueprint', 'World', 'StaticMesh')")] string? filterByClass = null,
        [Description("Search assets by name (partial match)")] string? searchName = null)
    {
        var assets = assetService.ListAssets(subfolder, filterByClass, searchName);

        if (assets.Count == 0)
            return "No assets found matching the criteria.";

        // Return concise summary to save tokens
        var summary = assets.Select(a => new
        {
            a.Name,
            a.RelativePath,
            a.AssetClass,
            Size = FormatSize(a.FileSize),
            a.IsModifiable,
            a.IsCooked,
            a.Summary
        });

        return JsonSerializer.Serialize(new
        {
            count = assets.Count,
            assets = summary
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Inspects a specific asset file in detail. Returns all exports, imports, " +
        "and properties. Use this to drill into an asset after finding it with list_assets.")]
    public string inspect_asset(
        AssetService assetService,
        [Description("Full path or path relative to Content/ of the .uasset or .umap file")] string assetPath)
    {
        var resolvedPath = ResolveAssetPath(assetService, assetPath);
        var detail = assetService.InspectAsset(resolvedPath);
        return JsonSerializer.Serialize(detail, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets a quick summary of an asset without full details. " +
        "Faster than inspect_asset — use this when you just need to know what an asset is.")]
    public string asset_summary(
        AssetService assetService,
        [Description("Full path or path relative to Content/ of the asset file")] string assetPath)
    {
        var resolvedPath = ResolveAssetPath(assetService, assetPath);
        var summary = assetService.GetAssetSummary(resolvedPath);
        return JsonSerializer.Serialize(summary, JsonOpts);
    }

    [McpServerTool, Description(
        "Searches for assets across the project by name, class type, or path. " +
        "Broader than list_assets — searches all fields.")]
    public string search_assets(
        AssetService assetService,
        [Description("Search query (matches against name, class, path, and summary)")] string query)
    {
        var results = assetService.SearchAssets(query);

        if (results.Count == 0)
            return $"No assets found matching '{query}'.";

        var summary = results.Select(a => new
        {
            a.Name,
            a.RelativePath,
            a.AssetClass,
            a.IsModifiable,
            a.Summary
        });

        return JsonSerializer.Serialize(new
        {
            query,
            count = results.Count,
            results = summary
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets project information — configured paths, content directory structure, " +
        "and overall asset counts. Good starting point for understanding a project.")]
    public string get_project_info(AssetService assetService)
    {
        var assets = assetService.ListAssets();

        var byClass = assets
            .GroupBy(a => a.AssetClass)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count);

        var byFolder = assets
            .Select(a => Path.GetDirectoryName(a.RelativePath) ?? "root")
            .GroupBy(f => f)
            .Select(g => new { Folder = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count);

        return JsonSerializer.Serialize(new
        {
            totalAssets = assets.Count,
            modifiableAssets = assets.Count(a => a.IsModifiable),
            cookedAssets = assets.Count(a => a.IsCooked),
            byType = byClass,
            byFolder = byFolder
        }, JsonOpts);
    }

    private static string ResolveAssetPath(AssetService assetService, string path)
    {
        if (File.Exists(path))
            return path;

        // Try as relative to Content/
        var assets = assetService.ListAssets();
        var match = assets.FirstOrDefault(a =>
            a.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase));

        if (match != null)
            return match.FilePath;

        return path; // Let it fail with a clear error from UAssetAPI
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
        };
    }
}
