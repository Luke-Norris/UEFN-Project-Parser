using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for extracting and analyzing multi-device systems from UEFN projects.
/// Systems are groups of devices that work together (connected by wiring or spatial proximity).
/// </summary>
[McpServerToolType]
public class SystemTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Analyzes a single level and extracts multi-device systems. " +
        "Identifies device clusters connected by wiring or spatial proximity. " +
        "Returns systems with device roles, configs, wiring patterns, and spatial layout.")]
    public string analyze_level_systems(
        SystemExtractor extractor,
        [Description("Path to the .umap level file to analyze.")] string levelPath)
    {
        var analysis = extractor.AnalyzeLevel(levelPath);

        if (analysis.Systems.Count == 0)
        {
            var msg = $"No multi-device systems found in level ({analysis.TotalDevices} devices total).";
            if (analysis.Errors.Any())
                msg += $"\nErrors: {string.Join("; ", analysis.Errors)}";
            return msg;
        }

        return JsonSerializer.Serialize(new
        {
            levelPath = Path.GetFileName(analysis.LevelPath),
            totalDevices = analysis.TotalDevices,
            systemsFound = analysis.Systems.Count,
            systems = analysis.Systems.Select(s => new
            {
                s.Name,
                s.Category,
                s.DetectionMethod,
                s.Confidence,
                s.DeviceCount,
                devices = s.Devices.Select(d => new
                {
                    d.Role,
                    d.DeviceClass,
                    d.DeviceType,
                    d.Label,
                    offset = $"({d.Offset.X:F0}, {d.Offset.Y:F0}, {d.Offset.Z:F0})",
                    propertyCount = d.Properties.Count,
                    significantProperties = d.Properties.Take(5).Select(p => $"{p.Name}={p.Value}")
                }),
                wiring = s.Wiring.Select(w => new
                {
                    connection = $"{w.SourceRole}.{w.OutputEvent} → {w.TargetRole}.{w.InputAction}",
                    w.Channel
                })
            }),
            errors = analysis.Errors
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Analyzes all levels in a project and extracts multi-device systems. " +
        "Deduplicates similar systems across levels. " +
        "Use this to understand what device patterns a project uses.")]
    public string analyze_project_systems(
        SystemExtractor extractor,
        [Description("Path to the UEFN project root (directory with .uefnproject).")] string projectPath)
    {
        var analysis = extractor.AnalyzeProject(projectPath);

        return JsonSerializer.Serialize(new
        {
            projectPath = Path.GetFileName(analysis.ProjectPath),
            levelsScanned = analysis.LevelCount,
            totalSystems = analysis.Systems.Count,
            uniquePatterns = analysis.UniqueSystems.Count,
            systems = analysis.UniqueSystems.Select(s => new
            {
                s.Name,
                s.Category,
                s.DetectionMethod,
                s.Confidence,
                s.DeviceCount,
                sourceLevel = Path.GetFileName(s.SourceLevel ?? ""),
                deviceTypes = s.Devices.Select(d => d.DeviceType).Distinct(),
                wiringConnections = s.Wiring.Count,
                propertyOverrides = s.Devices.Sum(d => d.Properties.Count)
            }),
            errors = analysis.Errors
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Scans the entire reference library (~92 projects) and extracts all device systems. " +
        "Ranks by frequency — systems appearing in multiple projects are more valuable as recipes. " +
        "WARNING: This can take several minutes on a large library.")]
    public string analyze_library_systems(
        SystemExtractor extractor,
        [Description("Path to the library root directory containing UEFN projects.")] string libraryPath)
    {
        var analysis = extractor.AnalyzeLibrary(libraryPath);

        return JsonSerializer.Serialize(new
        {
            libraryPath,
            projectsScanned = analysis.ProjectCount,
            totalSystems = analysis.AllSystems.Count,
            uniquePatterns = analysis.UniqueSystems.Count,
            topSystems = analysis.UniqueSystems.Take(20).Select(s => new
            {
                s.Name,
                s.Category,
                s.Frequency,
                s.Confidence,
                s.DeviceCount,
                deviceTypes = s.Devices.Select(d => d.DeviceType).Distinct(),
                wiringConnections = s.Wiring.Count
            }),
            categoryCounts = analysis.UniqueSystems
                .GroupBy(s => s.Category)
                .Select(g => new { category = g.Key, count = g.Count() })
                .OrderByDescending(c => c.count),
            errors = analysis.Errors.Take(10)
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets the full recipe for a specific system extracted from the library. " +
        "Returns complete device configs, properties, wiring, and spatial layout — " +
        "everything needed to recreate this system in a new project.")]
    public string get_system_recipe(
        SystemExtractor extractor,
        [Description("Path to the level containing the system.")] string levelPath,
        [Description("Category filter (capture, spawning, economy, combat, gameplay, etc.).")] string? category = null)
    {
        var analysis = extractor.AnalyzeLevel(levelPath);

        var systems = analysis.Systems.AsEnumerable();
        if (!string.IsNullOrEmpty(category))
            systems = systems.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var matches = systems.ToList();
        if (matches.Count == 0)
            return $"No systems found matching category '{category ?? "any"}' in this level.";

        return JsonSerializer.Serialize(matches.Select(s => new
        {
            s.Name,
            s.Category,
            s.Confidence,
            devices = s.Devices.Select(d => new
            {
                d.Role,
                d.DeviceClass,
                d.DeviceType,
                d.Label,
                offset = new { x = d.Offset.X, y = d.Offset.Y, z = d.Offset.Z },
                rotation = new { pitch = d.Rotation.X, yaw = d.Rotation.Y, roll = d.Rotation.Z },
                scale = new { x = d.Scale.X, y = d.Scale.Y, z = d.Scale.Z },
                properties = d.Properties.Select(p => new { p.Name, p.Type, p.Value })
            }),
            wiring = s.Wiring.Select(w => new
            {
                w.SourceRole,
                w.OutputEvent,
                w.TargetRole,
                w.InputAction,
                w.Channel
            })
        }), JsonOpts);
    }
}
