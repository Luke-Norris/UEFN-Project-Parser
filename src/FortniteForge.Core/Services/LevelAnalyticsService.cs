using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Core.Services;

/// <summary>
/// Analyzes level files to provide performance insights, complexity scoring,
/// actor breakdowns, duplicate detection, and wiring analysis.
/// </summary>
public class LevelAnalyticsService
{
    private readonly ForgeConfig _config;
    private readonly ILogger<LevelAnalyticsService> _logger;

    // Device class patterns (same as DeviceService)
    private static readonly string[] DevicePatterns = {
        "device", "spawner", "trigger", "mutator", "granter", "barrier",
        "teleporter", "zone", "volume", "prop_mover", "BP_", "PBWA_"
    };

    public LevelAnalyticsService(ForgeConfig config, ILogger<LevelAnalyticsService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Performs comprehensive analysis of a level file.
    /// </summary>
    public LevelAnalytics AnalyzeLevel(string levelPath)
    {
        var analytics = new LevelAnalytics
        {
            LevelPath = levelPath,
            LevelName = Path.GetFileNameWithoutExtension(levelPath),
            FileSizeBytes = new FileInfo(levelPath).Length
        };

        try
        {
            var asset = new UAsset(levelPath, EngineVersion.VER_UE5_4);
            analytics.TotalExports = asset.Exports.Count;
            analytics.TotalImports = asset.Imports.Count;

            // Analyze actors
            var levelExport = asset.Exports.OfType<LevelExport>().FirstOrDefault();
            if (levelExport != null)
            {
                analytics.TotalActors = levelExport.Actors.Count;
                AnalyzeActors(asset, levelExport, analytics);
            }

            // Analyze exports for additional detail
            AnalyzeExports(asset, analytics);

            // Calculate performance estimate
            analytics.Performance = EstimatePerformance(analytics);

            // Analyze external actors if present
            AnalyzeExternalActors(levelPath, analytics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze level {Path}", levelPath);
            analytics.Performance.Concerns.Add($"Analysis error: {ex.Message}");
        }

        return analytics;
    }

    /// <summary>
    /// Quick summary of all levels in the project.
    /// </summary>
    public List<LevelAnalytics> AnalyzeProject()
    {
        var results = new List<LevelAnalytics>();
        var contentPath = _config.ContentPath;

        if (!Directory.Exists(contentPath))
            return results;

        var mapFiles = Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories);
        foreach (var mapFile in mapFiles)
        {
            try
            {
                results.Add(AnalyzeLevel(mapFile));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping level: {Path}", mapFile);
            }
        }

        return results;
    }

    private void AnalyzeActors(UAsset asset, LevelExport levelExport, LevelAnalytics analytics)
    {
        var minX = float.MaxValue; var minY = float.MaxValue; var minZ = float.MaxValue;
        var maxX = float.MinValue; var maxY = float.MinValue; var maxZ = float.MinValue;

        foreach (var actorRef in levelExport.Actors)
        {
            if (!actorRef.IsExport()) continue;

            var exportIdx = actorRef.Index - 1; // FPackageIndex is 1-based for exports
            if (exportIdx < 0 || exportIdx >= asset.Exports.Count) continue;

            var export = asset.Exports[exportIdx];
            var className = export.GetExportClassType()?.ToString() ?? "Unknown";

            // Count by class
            analytics.ActorsByClass.TryGetValue(className, out var count);
            analytics.ActorsByClass[className] = count + 1;

            // Categorize
            var category = CategorizeActor(className);
            analytics.ActorsByCategory.TryGetValue(category, out var catCount);
            analytics.ActorsByCategory[category] = catCount + 1;

            if (IsDevice(className))
                analytics.DeviceCount++;

            // Extract location for bounds
            if (export is NormalExport normalExport)
            {
                var loc = ExtractLocation(normalExport);
                if (loc != null)
                {
                    minX = Math.Min(minX, loc.X); maxX = Math.Max(maxX, loc.X);
                    minY = Math.Min(minY, loc.Y); maxY = Math.Max(maxY, loc.Y);
                    minZ = Math.Min(minZ, loc.Z); maxZ = Math.Max(maxZ, loc.Z);
                }
            }
        }

        // Set bounds
        if (minX < float.MaxValue)
        {
            analytics.WorldBounds = new BoundsInfo
            {
                Min = new Vector3Info(minX, minY, minZ),
                Max = new Vector3Info(maxX, maxY, maxZ),
                Size = new Vector3Info(maxX - minX, maxY - minY, maxZ - minZ),
                Center = new Vector3Info((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2)
            };
        }

        analytics.UniqueDeviceTypes = analytics.ActorsByClass
            .Where(kvp => IsDevice(kvp.Key))
            .Count();
    }

    private void AnalyzeExports(UAsset asset, LevelAnalytics analytics)
    {
        // Find duplicates — actors with the same class at very similar positions
        var actorPositions = new Dictionary<string, List<(string Name, Vector3Info? Pos)>>();

        foreach (var export in asset.Exports)
        {
            var className = export.GetExportClassType()?.ToString() ?? "Unknown";
            var name = export.ObjectName?.ToString() ?? "";

            if (!actorPositions.ContainsKey(className))
                actorPositions[className] = new();

            Vector3Info? pos = null;
            if (export is NormalExport ne)
                pos = ExtractLocation(ne);

            actorPositions[className].Add((name, pos));
        }

        // Detect potential duplicates
        foreach (var (className, instances) in actorPositions)
        {
            if (instances.Count < 3) continue;
            if (className is "Unknown" or "SceneComponent" or "StaticMeshComponent") continue;

            // Check for actors stacked at same position
            var posGroups = instances
                .Where(i => i.Pos != null)
                .GroupBy(i => $"{Math.Round(i.Pos!.X / 100) * 100},{Math.Round(i.Pos!.Y / 100) * 100},{Math.Round(i.Pos!.Z / 100) * 100}")
                .Where(g => g.Count() > 1)
                .ToList();

            if (posGroups.Count > 0)
            {
                analytics.Duplicates.Add(new DuplicateGroup
                {
                    ClassName = className,
                    Count = posGroups.Sum(g => g.Count()),
                    ActorNames = posGroups.SelectMany(g => g.Select(i => i.Name)).Take(10).ToList(),
                    Suggestion = $"{posGroups.Sum(g => g.Count())} instances of {className} stacked at similar positions — possible unintentional duplicates."
                });
            }
        }
    }

    private void AnalyzeExternalActors(string levelPath, LevelAnalytics analytics)
    {
        // UEFN uses __ExternalActors__ folder for actor streaming
        var contentDir = Path.GetDirectoryName(levelPath);
        if (contentDir == null) return;

        var levelName = Path.GetFileNameWithoutExtension(levelPath);
        var externalActorsDir = Path.Combine(contentDir, "__ExternalActors__", levelName);

        if (Directory.Exists(externalActorsDir))
        {
            var externalAssets = Directory.EnumerateFiles(externalActorsDir, "*.uasset", SearchOption.AllDirectories).ToList();
            analytics.TotalActors += externalAssets.Count;

            // Sample a few to categorize
            var sampled = 0;
            foreach (var extAsset in externalAssets.Take(50))
            {
                try
                {
                    var asset = new UAsset(extAsset, EngineVersion.VER_UE5_4);
                    foreach (var export in asset.Exports)
                    {
                        var className = export.GetExportClassType()?.ToString() ?? "Unknown";
                        if (className == "Unknown") continue;

                        analytics.ActorsByClass.TryGetValue(className, out var cnt);
                        analytics.ActorsByClass[className] = cnt + 1;

                        var category = CategorizeActor(className);
                        analytics.ActorsByCategory.TryGetValue(category, out var catCnt);
                        analytics.ActorsByCategory[category] = catCnt + 1;

                        if (IsDevice(className))
                            analytics.DeviceCount++;
                    }
                    sampled++;
                }
                catch
                {
                    // Skip unparseable external actors
                }
            }

            if (externalAssets.Count > 50)
            {
                analytics.Performance.Concerns.Add(
                    $"{externalAssets.Count} external actors found (sampled {sampled}). " +
                    "Full analysis may differ from sampled results.");
            }
        }
    }

    private PerformanceEstimate EstimatePerformance(LevelAnalytics analytics)
    {
        var perf = new PerformanceEstimate();

        // Complexity scoring
        var score = 0;
        score += analytics.TotalActors switch
        {
            < 50 => 1,
            < 150 => 2,
            < 500 => 4,
            < 1000 => 6,
            < 5000 => 8,
            _ => 10
        };

        score += analytics.DeviceCount switch
        {
            < 10 => 0,
            < 30 => 1,
            < 100 => 2,
            _ => 3
        };

        // File size factor
        var sizeMB = analytics.FileSizeBytes / (1024.0 * 1024.0);
        score += sizeMB switch
        {
            < 1 => 0,
            < 10 => 1,
            < 50 => 2,
            _ => 3
        };

        perf.ComplexityScore = Math.Min(10, score);
        perf.Rating = perf.ComplexityScore switch
        {
            <= 2 => "Simple",
            <= 4 => "Moderate",
            <= 6 => "Complex",
            <= 8 => "Heavy",
            _ => "Very Heavy"
        };

        // Memory estimate (rough)
        perf.EstimatedMemoryMB = (int)(analytics.FileSizeBytes / (1024.0 * 1024.0))
            + analytics.TotalActors / 10
            + analytics.DeviceCount;

        // Generate concerns
        if (analytics.TotalActors > 500)
            perf.Concerns.Add($"High actor count ({analytics.TotalActors}). May affect load times.");

        if (analytics.DeviceCount > 100)
            perf.Concerns.Add($"Many devices ({analytics.DeviceCount}). Watch for device tick overhead.");

        if (analytics.Duplicates.Count > 0)
            perf.Concerns.Add($"{analytics.Duplicates.Count} potential duplicate groups detected.");

        var worldSize = analytics.WorldBounds.Size;
        if (worldSize.X > 100000 || worldSize.Y > 100000)
            perf.Concerns.Add($"Very large world bounds ({worldSize.X:N0} x {worldSize.Y:N0} units). Consider streaming or level partitioning.");

        // Optimizations
        if (analytics.TotalActors > 200)
            perf.Optimizations.Add("Consider using HLOD layers to reduce draw calls for distant actors.");

        if (analytics.DeviceCount > 50)
            perf.Optimizations.Add("Review device placement — disable devices when not in active gameplay area.");

        if (analytics.ActorsByCategory.TryGetValue("StaticMesh", out var meshCount) && meshCount > 300)
            perf.Optimizations.Add($"Many static meshes ({meshCount}). Consider merging nearby meshes or using instanced rendering.");

        if (perf.Concerns.Count == 0)
            perf.Optimizations.Add("Level looks good! No major performance concerns detected.");

        return perf;
    }

    private static string CategorizeActor(string className)
    {
        var lower = className.ToLowerInvariant();
        if (IsDevice(className)) return "Device";
        if (lower.Contains("light")) return "Light";
        if (lower.Contains("staticmesh") || lower.Contains("mesh")) return "StaticMesh";
        if (lower.Contains("landscape") || lower.Contains("terrain")) return "Terrain";
        if (lower.Contains("foliage") || lower.Contains("tree") || lower.Contains("grass")) return "Foliage";
        if (lower.Contains("volume")) return "Volume";
        if (lower.Contains("camera")) return "Camera";
        if (lower.Contains("player") || lower.Contains("spawn")) return "PlayerStart";
        return "Other";
    }

    private static bool IsDevice(string className)
    {
        var lower = className.ToLowerInvariant();
        return DevicePatterns.Any(p => lower.Contains(p.ToLowerInvariant()));
    }

    private static Vector3Info? ExtractLocation(NormalExport export)
    {
        foreach (var prop in export.Data)
        {
            if (prop is StructPropertyData structProp &&
                prop.Name?.ToString() is "RelativeLocation" or "RootComponent")
            {
                var x = structProp.Value.OfType<FloatPropertyData>().FirstOrDefault(p => p.Name?.ToString() == "X");
                var y = structProp.Value.OfType<FloatPropertyData>().FirstOrDefault(p => p.Name?.ToString() == "Y");
                var z = structProp.Value.OfType<FloatPropertyData>().FirstOrDefault(p => p.Name?.ToString() == "Z");

                if (x != null && y != null && z != null)
                    return new Vector3Info(x.Value, y.Value, z.Value);
            }
        }
        return null;
    }
}
