using WellVersed.Core.Config;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WellVersed.Tests;

public class AnalyticsOutputTest
{
    private const string ProjectPath = @"C:\Users\Luke\Documents\Fortnite Projects\UEFN_AutoMation_Test";
    private const string LevelPath = ProjectPath + @"\Content\UEFN_Automation_Test.umap";
    private static bool ProjectExists => Directory.Exists(ProjectPath) && File.Exists(LevelPath);

    private readonly ITestOutputHelper _output;

    public AnalyticsOutputTest(ITestOutputHelper output) => _output = output;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [SkippableFact]
    public void AnalyzeProject_PrintOutput()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = new WellVersedConfig { ProjectPath = ProjectPath };
        var service = new LevelAnalyticsService(config, NullLogger<LevelAnalyticsService>.Instance);

        var results = service.AnalyzeProject();

        var json = JsonSerializer.Serialize(new
        {
            totalLevels = results.Count,
            totalActors = results.Sum(r => r.TotalActors),
            totalDevices = results.Sum(r => r.DeviceCount),
            levels = results.Select(r => new
            {
                r.LevelName,
                r.TotalActors,
                r.TotalExports,
                r.TotalImports,
                r.DeviceCount,
                r.UniqueDeviceTypes,
                fileSizeMB = Math.Round(r.FileSizeBytes / (1024.0 * 1024.0), 2),
                actorsByClass = r.ActorsByClass
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(20)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                actorsByCategory = r.ActorsByCategory
                    .OrderByDescending(kvp => kvp.Value),
                performance = new
                {
                    r.Performance.ComplexityScore,
                    r.Performance.Rating,
                    r.Performance.EstimatedMemoryMB,
                    r.Performance.Concerns,
                    r.Performance.Optimizations
                },
                duplicateGroups = r.Duplicates.Count
            })
        }, JsonOpts);

        _output.WriteLine(json);
    }

    [SkippableFact]
    public void AnalyzeLevel_PrintOutput()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = new WellVersedConfig { ProjectPath = ProjectPath };
        var service = new LevelAnalyticsService(config, NullLogger<LevelAnalyticsService>.Instance);

        var result = service.AnalyzeLevel(LevelPath);

        var json = JsonSerializer.Serialize(new
        {
            result.LevelName,
            result.TotalActors,
            result.TotalExports,
            result.TotalImports,
            fileSizeKB = result.FileSizeBytes / 1024,
            result.DeviceCount,
            result.UniqueDeviceTypes,
            actorsByClass = result.ActorsByClass
                .OrderByDescending(kvp => kvp.Value)
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
                actorNames = d.ActorNames.Take(5),
                d.Suggestion
            })
        }, JsonOpts);

        _output.WriteLine(json);
    }
}
