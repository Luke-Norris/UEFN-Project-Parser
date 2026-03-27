using System.ComponentModel;
using System.Text.Json;
using WellVersed.Core.Models;
using WellVersed.Core.Services;
using ModelContextProtocol.Server;

namespace WellVersed.MCP.Tools;

[McpServerToolType]
public class VerseReferenceTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Searches the Verse reference library for files matching a keyword. " +
        "Searches across filenames, class names, function names, device types, properties, and full text. " +
        "Returns ranked results with match reasons.")]
    public static string search_verse_reference(
        VerseReferenceService service,
        [Description("Keyword to search for (e.g., 'tracker', 'HUD', 'round', 'tycoon')")] string keyword,
        [Description("Maximum results to return")] int maxResults = 20)
    {
        var results = service.SearchKeyword(keyword, maxResults);

        if (results.Count == 0)
            return JsonSerializer.Serialize(new { query = keyword, totalResults = 0, results = Array.Empty<object>() }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            query = keyword,
            totalResults = results.Count,
            results = results.Select(r => new
            {
                r.FileName,
                r.RelativePath,
                r.FilePath,
                matchReasons = r.MatchReasons,
                categories = r.Categories.Select(c => c.ToString()),
                devicesUsed = r.DeviceTypesUsed,
                classes = r.ClassNames,
                relevanceScore = r.Relevance
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Finds Verse files that use a specific UEFN device type. " +
        "Examples: 'tracker_device', 'hud_message_device', 'player_spawner_device', 'item_granter_device'")]
    public static string find_device_examples(
        VerseReferenceService service,
        [Description("Device type to search for (e.g., 'tracker_device')")] string deviceType,
        [Description("Maximum results to return")] int maxResults = 20)
    {
        var results = service.FindByDevice(deviceType, maxResults);

        if (results.Count == 0)
            return JsonSerializer.Serialize(new { deviceType, totalResults = 0, examples = Array.Empty<object>() }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            deviceType,
            totalResults = results.Count,
            examples = results.Select(r => new
            {
                r.FileName,
                r.RelativePath,
                r.FilePath,
                allDevicesInFile = r.DeviceTypesUsed,
                categories = r.Categories.Select(c => c.ToString()),
                classes = r.ClassNames
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Finds Verse files by category/pattern. " +
        "Categories: GameManagement, PlayerEconomy, UISystem, AbilitySystem, " +
        "TycoonBuilding, RankedProgression, Environment, Utility, Combat, Movement")]
    public static string find_verse_patterns(
        VerseReferenceService service,
        [Description("Category name (e.g., 'UISystem', 'GameManagement')")] string category,
        [Description("Maximum results to return")] int maxResults = 20)
    {
        if (!Enum.TryParse<VerseCategory>(category, ignoreCase: true, out var categoryEnum))
        {
            var valid = string.Join(", ", Enum.GetNames<VerseCategory>());
            return JsonSerializer.Serialize(new { error = $"Invalid category '{category}'", validCategories = valid }, JsonOpts);
        }

        var results = service.FindByCategory(categoryEnum, maxResults);

        return JsonSerializer.Serialize(new
        {
            category,
            totalResults = results.Count,
            files = results.Select(r => new
            {
                r.FileName,
                r.RelativePath,
                r.FilePath,
                allCategories = r.Categories.Select(c => c.ToString()),
                devicesUsed = r.DeviceTypesUsed,
                classes = r.ClassNames
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Reads the full source code of a specific Verse file from the reference library. " +
        "Use after finding a file via search to view its complete content.")]
    public static string get_verse_snippet(
        VerseReferenceService service,
        [Description("Full file path to the Verse file")] string filePath)
    {
        var content = service.GetFileContent(filePath);

        if (content == null)
            return JsonSerializer.Serialize(new { error = $"File not found: {filePath}" }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            filePath,
            fileName = Path.GetFileName(filePath),
            lineCount = content.Split('\n').Length,
            content
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets statistics about the Verse reference library: total files, lines, " +
        "top device types used, category breakdown, and most common imports.")]
    public static string verse_library_stats(VerseReferenceService service)
    {
        var stats = service.GetStats();

        return JsonSerializer.Serialize(new
        {
            summary = new
            {
                stats.TotalFiles,
                stats.TotalLines,
                totalSizeKB = Math.Round(stats.TotalSizeBytes / 1024.0, 1),
                stats.IndexedAt
            },
            stats.FilesByCategory,
            topDevices = stats.DeviceUsageCounts,
            stats.TopUsingStatements
        }, JsonOpts);
    }
}
