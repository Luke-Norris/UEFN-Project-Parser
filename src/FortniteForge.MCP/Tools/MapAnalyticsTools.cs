using WellVersed.Core.Config;
using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for map analytics — profiling, library comparison, insights,
/// and similarity search across the UEFN reference library.
/// </summary>
[McpServerToolType]
public class MapAnalyticsTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Generate a comprehensive profile of a UEFN map project. " +
        "Returns device counts by type, verse file stats, widget count, " +
        "genre classification (Battle Royale, Tycoon, Parkour, etc.), " +
        "and scores for complexity, polish, and gameplay variety. " +
        "Use this to understand what a map contains and how it compares structurally.")]
    public string profile_map(
        MapAnalytics analytics,
        WellVersedConfig config,
        [Description("Path to the UEFN project root. If omitted, uses the configured project.")] string? projectPath = null)
    {
        var path = projectPath ?? config.ProjectPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return $"Project path not found: {path}";

        var profile = analytics.ProfileMap(path);

        return JsonSerializer.Serialize(new
        {
            profile.ProjectName,
            profile.MapClassification,
            profile.OverallRating,
            scores = new
            {
                profile.ComplexityScore,
                profile.PolishScore,
                profile.GameplayVarietyScore
            },
            counts = new
            {
                profile.TotalDevices,
                profile.TotalActors,
                profile.TotalWirings,
                profile.VerseFileCount,
                profile.VerseTotalLines,
                profile.WidgetCount,
                profile.LevelCount,
                profile.UserAssetCount
            },
            topDevices = profile.DevicesByType
                .OrderByDescending(kv => kv.Value)
                .Take(15)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            actorCategories = profile.ActorsByCategory,
            verseClasses = profile.VerseClasses
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Compare a map against the full reference library of UEFN projects. " +
        "Returns percentile rankings (e.g., 'more devices than 73% of maps'), " +
        "the most similar map by device composition, missing features that " +
        "successful maps have, and strengths. " +
        "Requires the library index to be built first (use build_library_index).")]
    public string compare_to_library(
        MapAnalytics analytics,
        WellVersedConfig config,
        [Description("Path to the UEFN project root. If omitted, uses the configured project.")] string? projectPath = null)
    {
        var path = projectPath ?? config.ProjectPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return $"Project path not found: {path}";

        var comparison = analytics.CompareToLibrary(path);

        var output = new List<string>
        {
            $"=== Library Comparison: {comparison.ProjectName} ===\n",
            "Percentile Rankings:",
            $"  Device Count: Top {100 - comparison.DeviceCountPercentile}% (more than {comparison.DeviceCountPercentile}% of maps)",
            $"  Wiring Complexity: Top {100 - comparison.WiringComplexityPercentile}% (more than {comparison.WiringComplexityPercentile}% of maps)",
            $"  Verse Usage: Top {100 - comparison.VerseUsagePercentile}% (more than {comparison.VerseUsagePercentile}% of maps)",
            $"  Actor Count: Top {100 - comparison.ActorCountPercentile}% (more than {comparison.ActorCountPercentile}% of maps)",
            $"  Widget Usage: Top {100 - comparison.WidgetUsagePercentile}% (more than {comparison.WidgetUsagePercentile}% of maps)"
        };

        if (!string.IsNullOrEmpty(comparison.MostSimilarMap))
        {
            output.Add($"\nMost Similar Map: {comparison.MostSimilarMap} ({comparison.SimilarityScore:P0} match)");
        }

        if (comparison.Strengths.Count > 0)
        {
            output.Add("\nStrengths:");
            foreach (var s in comparison.Strengths)
                output.Add($"  + {s}");
        }

        if (comparison.MissingFeatures.Count > 0)
        {
            output.Add("\nMissing Features (common in library but absent here):");
            foreach (var f in comparison.MissingFeatures)
                output.Add($"  - {f}");
        }

        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Get aggregate statistics across all projects in the UEFN reference library. " +
        "Returns average device counts, most common device types, feature adoption rates, " +
        "genre distribution, verse/widget usage percentages, and more. " +
        "Requires the library index to be built first (use build_library_index).")]
    public string get_library_insights(MapAnalytics analytics)
    {
        var insights = analytics.GetLibraryInsights();

        if (insights.TotalProjects == 0)
            return "No library data available. Run build_library_index first to scan the reference library.";

        var output = new List<string>
        {
            $"=== Library Insights ({insights.TotalProjects} projects) ===\n",
            "Averages:",
            $"  Devices per map: {insights.AverageDeviceCount:F0}",
            $"  Actors per map: {insights.AverageActorCount:F0}",
            $"  Verse files per map: {insights.AverageVerseFileCount:F1}",
            $"  Verse lines per map: {insights.AverageVerseLines:F0}",
            $"  Wiring connections per map: {insights.AverageWiringCount:F0}",
            "",
            $"Median device count: {insights.MedianDeviceCount}",
            $"Max device count: {insights.MaxDeviceCount} ({insights.MaxDeviceProject})",
            "",
            $"Maps with Verse code: {insights.PercentWithVerse}%",
            $"Maps with UI Widgets: {insights.PercentWithWidgets}%"
        };

        if (insights.TopDeviceTypes.Count > 0)
        {
            output.Add("\nTop Device Types:");
            foreach (var dt in insights.TopDeviceTypes.Take(15))
                output.Add($"  {dt.DisplayName}: {dt.TotalInstances} total across {dt.ProjectCount} projects");
        }

        if (insights.FeatureAdoption.Count > 0)
        {
            output.Add("\nFeature Adoption (% of maps using each):");
            foreach (var (feature, percent) in insights.FeatureAdoption.Take(15))
                output.Add($"  {feature}: {percent}%");
        }

        if (insights.GenreDistribution.Count > 0)
        {
            output.Add("\nGenre Distribution:");
            foreach (var (genre, count) in insights.GenreDistribution.OrderByDescending(kv => kv.Value))
                output.Add($"  {genre}: {count} maps");
        }

        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Find the most similar maps in the reference library to a target project, " +
        "ranked by cosine similarity of device composition. " +
        "Returns shared device types, unique device types in similar maps, " +
        "and similarity scores. Useful for finding inspiration or reference implementations. " +
        "Requires the library index to be built first (use build_library_index).")]
    public string find_similar_maps(
        MapAnalytics analytics,
        WellVersedConfig config,
        [Description("Path to the UEFN project root. If omitted, uses the configured project.")] string? projectPath = null,
        [Description("Number of similar maps to return. Default: 5")] int topN = 5)
    {
        var path = projectPath ?? config.ProjectPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return $"Project path not found: {path}";

        var results = analytics.FindSimilarMaps(path, topN);

        if (results.Count == 0)
            return "No similar maps found. Ensure the library index is built (use build_library_index).";

        var output = new List<string> { $"=== Top {results.Count} Similar Maps ===\n" };

        for (int i = 0; i < results.Count; i++)
        {
            var m = results[i];
            output.Add($"{i + 1}. {m.ProjectName} ({m.SimilarityScore:P0} similarity)");
            output.Add($"   Genre: {m.Classification} | Devices: {m.DeviceCount} | Verse Files: {m.VerseFileCount}");

            if (m.SharedDeviceTypes.Count > 0)
                output.Add($"   Shared: {string.Join(", ", m.SharedDeviceTypes.Take(8))}");
            if (m.UniqueDeviceTypes.Count > 0)
                output.Add($"   They have (you don't): {string.Join(", ", m.UniqueDeviceTypes.Take(8))}");
            output.Add("");
        }

        return string.Join("\n", output);
    }
}
