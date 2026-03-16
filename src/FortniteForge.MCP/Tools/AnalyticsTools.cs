using FortniteForge.Core.Config;
using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for level analytics — actor breakdowns, performance scoring,
/// duplicate detection, and project-wide analysis.
/// </summary>
[McpServerToolType]
public class AnalyticsTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Analyzes a level file for performance, complexity, actor breakdown, and potential issues. " +
        "Returns a comprehensive report including actor counts by class and category, " +
        "world bounds, performance estimate, and duplicate detection.")]
    public string analyze_level(
        LevelAnalyticsService analytics,
        [Description("Path to the .umap level file")] string levelPath)
    {
        var result = analytics.AnalyzeLevel(levelPath);

        return JsonSerializer.Serialize(new
        {
            result.LevelName,
            result.LevelPath,
            result.TotalActors,
            result.TotalExports,
            result.TotalImports,
            fileSizeMB = Math.Round(result.FileSizeBytes / (1024.0 * 1024.0), 2),
            result.DeviceCount,
            result.UniqueDeviceTypes,
            actorsByClass = result.ActorsByClass
                .OrderByDescending(kvp => kvp.Value)
                .Take(20)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            actorsByCategory = result.ActorsByCategory
                .OrderByDescending(kvp => kvp.Value),
            worldBounds = new
            {
                min = result.WorldBounds.Min.ToString(),
                max = result.WorldBounds.Max.ToString(),
                size = result.WorldBounds.Size.ToString(),
                center = result.WorldBounds.Center.ToString()
            },
            performance = new
            {
                result.Performance.ComplexityScore,
                result.Performance.Rating,
                result.Performance.EstimatedMemoryMB,
                result.Performance.Concerns,
                result.Performance.Optimizations
            },
            duplicates = result.Duplicates.Select(d => new
            {
                d.ClassName,
                d.Count,
                d.ActorNames,
                d.Suggestion
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Analyzes all levels in the project and returns a summary. " +
        "Useful for understanding project-wide complexity and identifying problem areas.")]
    public string analyze_project(LevelAnalyticsService analytics)
    {
        var results = analytics.AnalyzeProject();

        if (results.Count == 0)
            return "No .umap level files found in the project.";

        return JsonSerializer.Serialize(new
        {
            totalLevels = results.Count,
            totalActors = results.Sum(r => r.TotalActors),
            totalDevices = results.Sum(r => r.DeviceCount),
            levels = results.Select(r => new
            {
                r.LevelName,
                r.TotalActors,
                r.DeviceCount,
                r.Performance.ComplexityScore,
                r.Performance.Rating,
                fileSizeMB = Math.Round(r.FileSizeBytes / (1024.0 * 1024.0), 2),
                concerns = r.Performance.Concerns.Count,
                duplicateGroups = r.Duplicates.Count
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets a quick performance score and top concerns for a level. " +
        "Faster than full analysis — use this for quick checks.")]
    public string level_health_check(
        LevelAnalyticsService analytics,
        [Description("Path to the .umap level file")] string levelPath)
    {
        var result = analytics.AnalyzeLevel(levelPath);
        var p = result.Performance;

        var summary = $"Level: {result.LevelName}\n";
        summary += $"Score: {p.ComplexityScore}/10 ({p.Rating})\n";
        summary += $"Actors: {result.TotalActors} | Devices: {result.DeviceCount} | Size: {result.FileSizeBytes / 1024}KB\n";

        if (p.Concerns.Count > 0)
        {
            summary += "\nConcerns:\n";
            foreach (var concern in p.Concerns)
                summary += $"  - {concern}\n";
        }

        if (p.Optimizations.Count > 0)
        {
            summary += "\nSuggestions:\n";
            foreach (var opt in p.Optimizations)
                summary += $"  - {opt}\n";
        }

        return summary;
    }
}
