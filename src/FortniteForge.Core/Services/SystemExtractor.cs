using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Extracts multi-device systems from reference projects by analyzing
/// device clusters, wiring connections, and spatial relationships.
///
/// A "system" is a group of devices that work together — connected by wiring,
/// spatially co-located, or functionally related. Examples:
///   - Capture point: Trigger + Timer + Score Manager + HUD
///   - Item shop: Vending Machine + Item Granter + VFX + Confirmation
///   - Spawn area: Multiple spawners + Barrier + Timer
///
/// Systems extracted from real maps serve as recipes for generation.
/// </summary>
public class SystemExtractor
{
    private readonly WellVersedConfig _config;
    private readonly DeviceService _deviceService;
    private readonly ILogger<SystemExtractor> _logger;

    public SystemExtractor(
        WellVersedConfig config,
        DeviceService deviceService,
        ILogger<SystemExtractor> logger)
    {
        _config = config;
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <summary>
    /// Extracts all identifiable systems from a single level.
    /// </summary>
    public LevelSystemAnalysis AnalyzeLevel(string levelPath)
    {
        var analysis = new LevelSystemAnalysis { LevelPath = levelPath };

        List<DeviceInfo> devices;
        try
        {
            devices = _deviceService.ListDevicesInLevel(levelPath);
        }
        catch (Exception ex)
        {
            analysis.Errors.Add($"Failed to parse level: {ex.Message}");
            _logger.LogDebug(ex, "Failed to parse level: {Path}", levelPath);
            return analysis;
        }

        if (devices.Count == 0)
        {
            analysis.Errors.Add("No devices found in level");
            return analysis;
        }

        analysis.TotalDevices = devices.Count;

        // Build wiring graph — which devices reference which
        var wiringGraph = BuildWiringGraph(devices);

        // Find connected components (groups of devices linked by wiring)
        var wiredSystems = FindConnectedComponents(devices, wiringGraph);

        // Find spatial clusters among unwired devices
        var wiredDeviceNames = wiredSystems.SelectMany(s => s).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unwiredDevices = devices.Where(d => !wiredDeviceNames.Contains(d.ActorName)).ToList();
        var spatialClusters = FindSpatialClusters(unwiredDevices, clusterRadius: 2000f);

        // Convert connected components to extracted systems
        foreach (var component in wiredSystems)
        {
            if (component.Count < 2) continue; // Single devices aren't systems

            var systemDevices = devices.Where(d => component.Contains(d.ActorName)).ToList();
            var system = ExtractSystem(systemDevices, wiringGraph);
            if (system != null)
                analysis.Systems.Add(system);
        }

        // Convert large spatial clusters to potential systems (unwired but co-located)
        foreach (var cluster in spatialClusters)
        {
            if (cluster.Count < 3) continue; // Need at least 3 co-located devices

            var system = ExtractSystem(cluster, wiringGraph);
            if (system != null)
            {
                system.DetectionMethod = "spatial_cluster";
                system.Confidence = 0.5f; // Lower confidence — no wiring proof
                analysis.Systems.Add(system);
            }
        }

        // Classify each system
        foreach (var system in analysis.Systems)
        {
            system.Category = ClassifySystem(system);
            system.SourceLevel = levelPath;
        }

        _logger.LogInformation(
            "Analyzed {Level}: {Devices} devices, {Systems} systems extracted",
            Path.GetFileName(levelPath), devices.Count, analysis.Systems.Count);

        return analysis;
    }

    /// <summary>
    /// Scans all levels in a project and extracts systems.
    /// </summary>
    public ProjectSystemAnalysis AnalyzeProject(string projectPath)
    {
        var analysis = new ProjectSystemAnalysis { ProjectPath = projectPath };

        var contentPath = Path.Combine(projectPath, "Content");
        if (!Directory.Exists(contentPath))
        {
            analysis.Errors.Add($"Content directory not found: {contentPath}");
            return analysis;
        }

        var levels = Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories).ToList();
        analysis.LevelCount = levels.Count;

        foreach (var level in levels)
        {
            try
            {
                var levelAnalysis = AnalyzeLevel(level);
                analysis.LevelAnalyses.Add(levelAnalysis);
                analysis.Systems.AddRange(levelAnalysis.Systems);
            }
            catch (Exception ex)
            {
                analysis.Errors.Add($"Failed to analyze {Path.GetFileName(level)}: {ex.Message}");
                _logger.LogDebug(ex, "Failed to analyze level: {Path}", level);
            }
        }

        // Deduplicate similar systems across levels
        analysis.UniqueSystems = DeduplicateSystems(analysis.Systems);

        _logger.LogInformation(
            "Analyzed project {Project}: {Levels} levels, {Total} systems found, {Unique} unique patterns",
            Path.GetFileName(projectPath), levels.Count, analysis.Systems.Count, analysis.UniqueSystems.Count);

        return analysis;
    }

    /// <summary>
    /// Scans all projects in a library directory and extracts systems.
    /// </summary>
    public LibrarySystemAnalysis AnalyzeLibrary(string libraryPath)
    {
        var analysis = new LibrarySystemAnalysis { LibraryPath = libraryPath };

        // Discover UEFN projects (directories containing .uefnproject)
        var projects = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(libraryPath, "*", SearchOption.AllDirectories))
            {
                if (Directory.EnumerateFiles(dir, "*.uefnproject").Any())
                    projects.Add(dir);
            }
        }
        catch (Exception ex)
        {
            analysis.Errors.Add($"Failed to scan library: {ex.Message}");
            return analysis;
        }

        analysis.ProjectCount = projects.Count;
        _logger.LogInformation("Found {Count} UEFN projects in library at {Path}", projects.Count, libraryPath);

        foreach (var project in projects)
        {
            try
            {
                var projectAnalysis = AnalyzeProject(project);
                analysis.ProjectAnalyses.Add(projectAnalysis);
                analysis.AllSystems.AddRange(projectAnalysis.Systems);
            }
            catch (Exception ex)
            {
                analysis.Errors.Add($"Failed to analyze {Path.GetFileName(project)}: {ex.Message}");
            }
        }

        // Deduplicate across all projects
        analysis.UniqueSystems = DeduplicateSystems(analysis.AllSystems);

        // Rank by frequency — systems that appear in multiple projects are more valuable
        foreach (var system in analysis.UniqueSystems)
        {
            system.Frequency = analysis.AllSystems.Count(s => AreSystemsSimilar(s, system));
        }

        analysis.UniqueSystems = analysis.UniqueSystems.OrderByDescending(s => s.Frequency).ToList();

        _logger.LogInformation(
            "Library analysis complete: {Projects} projects, {Total} systems, {Unique} unique patterns",
            projects.Count, analysis.AllSystems.Count, analysis.UniqueSystems.Count);

        return analysis;
    }

    // ========= Graph Construction =========

    private Dictionary<string, HashSet<string>> BuildWiringGraph(List<DeviceInfo> devices)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            if (!graph.ContainsKey(device.ActorName))
                graph[device.ActorName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var wire in device.Wiring)
            {
                if (string.IsNullOrEmpty(wire.TargetDevice)) continue;

                // Find target device by matching the reference
                var target = ResolveWiringTarget(wire.TargetDevice, devices);
                if (target == null) continue;

                graph[device.ActorName].Add(target.ActorName);

                // Bidirectional — if A wires to B, they're in the same system
                if (!graph.ContainsKey(target.ActorName))
                    graph[target.ActorName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                graph[target.ActorName].Add(device.ActorName);
            }
        }

        return graph;
    }

    private DeviceInfo? ResolveWiringTarget(string targetRef, List<DeviceInfo> devices)
    {
        // Try exact match first
        var exact = devices.FirstOrDefault(d =>
            d.ActorName.Equals(targetRef, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Try partial match (wiring references may be export indices or shortened names)
        return devices.FirstOrDefault(d =>
            targetRef.Contains(d.ActorName, StringComparison.OrdinalIgnoreCase) ||
            d.ActorName.Contains(targetRef, StringComparison.OrdinalIgnoreCase));
    }

    // ========= Connected Components (Wiring) =========

    private List<HashSet<string>> FindConnectedComponents(
        List<DeviceInfo> devices,
        Dictionary<string, HashSet<string>> graph)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new List<HashSet<string>>();

        foreach (var device in devices)
        {
            if (visited.Contains(device.ActorName)) continue;
            if (!graph.ContainsKey(device.ActorName) || graph[device.ActorName].Count == 0) continue;

            var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(device.ActorName);
            visited.Add(device.ActorName);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                if (!graph.TryGetValue(current, out var neighbors)) continue;

                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            components.Add(component);
        }

        return components;
    }

    // ========= Spatial Clustering (DBSCAN-inspired) =========

    /// <summary>
    /// DBSCAN-inspired spatial clustering with adaptive epsilon.
    /// Epsilon is computed from the bounding box diagonal of all devices,
    /// scaled to capture meaningful spatial groupings.
    /// </summary>
    private List<List<DeviceInfo>> FindSpatialClusters(List<DeviceInfo> devices, float clusterRadius)
    {
        if (devices.Count < 2)
            return devices.Count == 1 ? new List<List<DeviceInfo>> { new(devices) } : new();

        // Compute adaptive epsilon from bounding box
        var epsilon = ComputeAdaptiveEpsilon(devices, clusterRadius);
        const int minPoints = 2; // Minimum cluster size

        // DBSCAN labels: -1 = noise, -2 = unvisited, >= 0 = cluster id
        var labels = new int[devices.Count];
        Array.Fill(labels, -2); // All unvisited

        int clusterIndex = 0;

        for (int i = 0; i < devices.Count; i++)
        {
            if (labels[i] != -2) continue; // Already visited

            var neighbors = RangeQuery(devices, i, epsilon);

            if (neighbors.Count < minPoints)
            {
                labels[i] = -1; // Noise
                continue;
            }

            // Start a new cluster
            labels[i] = clusterIndex;
            var seedSet = new Queue<int>(neighbors.Where(n => n != i));

            while (seedSet.Count > 0)
            {
                var q = seedSet.Dequeue();

                if (labels[q] == -1)
                    labels[q] = clusterIndex; // Noise becomes border point

                if (labels[q] != -2) continue; // Already processed

                labels[q] = clusterIndex;

                var qNeighbors = RangeQuery(devices, q, epsilon);
                if (qNeighbors.Count >= minPoints)
                {
                    foreach (var n in qNeighbors)
                    {
                        if (labels[n] == -2 || labels[n] == -1)
                            seedSet.Enqueue(n);
                    }
                }
            }

            clusterIndex++;
        }

        // Group devices by cluster label (ignore noise = -1)
        var clusters = new List<List<DeviceInfo>>();
        for (int c = 0; c < clusterIndex; c++)
        {
            var cluster = new List<DeviceInfo>();
            for (int i = 0; i < devices.Count; i++)
            {
                if (labels[i] == c)
                    cluster.Add(devices[i]);
            }
            if (cluster.Count >= minPoints)
                clusters.Add(cluster);
        }

        // Also return single-device noise points as single-element clusters
        // (they may be standalone devices worth noting)
        for (int i = 0; i < devices.Count; i++)
        {
            if (labels[i] == -1)
                clusters.Add(new List<DeviceInfo> { devices[i] });
        }

        return clusters;
    }

    /// <summary>
    /// Returns indices of all devices within epsilon distance of devices[pointIndex].
    /// Uses full 3D distance.
    /// </summary>
    private static List<int> RangeQuery(List<DeviceInfo> devices, int pointIndex, float epsilon)
    {
        var neighbors = new List<int>();
        var point = devices[pointIndex];

        for (int i = 0; i < devices.Count; i++)
        {
            if (Distance3D(point.Location, devices[i].Location) <= epsilon)
                neighbors.Add(i);
        }

        return neighbors;
    }

    /// <summary>
    /// Computes an adaptive epsilon based on the bounding box of all device positions.
    /// Uses 5% of the bounding box diagonal, clamped between a reasonable min and max.
    /// Falls back to the provided clusterRadius if the bounding box is degenerate.
    /// </summary>
    private static float ComputeAdaptiveEpsilon(List<DeviceInfo> devices, float fallbackRadius)
    {
        if (devices.Count < 2) return fallbackRadius;

        var minX = devices.Min(d => d.Location.X);
        var maxX = devices.Max(d => d.Location.X);
        var minY = devices.Min(d => d.Location.Y);
        var maxY = devices.Max(d => d.Location.Y);
        var minZ = devices.Min(d => d.Location.Z);
        var maxZ = devices.Max(d => d.Location.Z);

        var dx = maxX - minX;
        var dy = maxY - minY;
        var dz = maxZ - minZ;
        var diagonal = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        if (diagonal < 100f) // Degenerate bounding box
            return fallbackRadius;

        // 5% of diagonal, clamped between 500 and 5000 Unreal units
        var epsilon = diagonal * 0.05f;
        return Math.Clamp(epsilon, 500f, 5000f);
    }

    // ========= System Extraction =========

    private ExtractedSystem? ExtractSystem(
        List<DeviceInfo> devices,
        Dictionary<string, HashSet<string>> wiringGraph)
    {
        if (devices.Count < 2) return null;

        var system = new ExtractedSystem
        {
            DetectionMethod = "wiring",
            Confidence = 0.9f,
            DeviceCount = devices.Count
        };

        // Compute center of the system
        var centerX = devices.Average(d => d.Location.X);
        var centerY = devices.Average(d => d.Location.Y);
        var centerZ = devices.Average(d => d.Location.Z);

        // Extract each device with relative offset from center
        foreach (var device in devices)
        {
            var entry = new ExtractedDevice
            {
                Role = InferRole(device),
                DeviceClass = device.DeviceClass,
                DeviceType = device.DeviceType,
                Label = device.Label,
                Offset = new Vector3Info(
                    device.Location.X - (float)centerX,
                    device.Location.Y - (float)centerY,
                    device.Location.Z - (float)centerZ),
                Rotation = device.Rotation,
                Scale = device.Scale,
                ActorName = device.ActorName
            };

            // Extract non-default properties (skip transform + common noise)
            foreach (var prop in device.Properties)
            {
                if (IsSignificantProperty(prop))
                {
                    entry.Properties.Add(new ExtractedProperty
                    {
                        Name = prop.Name,
                        Type = prop.Type,
                        Value = prop.Value
                    });
                }
            }

            system.Devices.Add(entry);
        }

        // Extract wiring connections
        foreach (var device in devices)
        {
            foreach (var wire in device.Wiring)
            {
                var target = ResolveWiringTarget(wire.TargetDevice, devices);
                if (target == null) continue;

                system.Wiring.Add(new ExtractedWiring
                {
                    SourceRole = InferRole(device),
                    SourceActor = device.ActorName,
                    OutputEvent = wire.OutputEvent,
                    TargetRole = InferRole(target),
                    TargetActor = target.ActorName,
                    InputAction = wire.InputAction,
                    Channel = wire.Channel
                });
            }
        }

        // Generate a descriptive name
        var deviceTypes = devices.Select(d => d.DeviceType).Distinct().OrderBy(t => t).ToList();
        system.Name = string.Join(" + ", deviceTypes.Take(3));
        if (deviceTypes.Count > 3)
            system.Name += $" +{deviceTypes.Count - 3} more";

        return system;
    }

    // ========= Classification =========

    private string ClassifySystem(ExtractedSystem system)
    {
        var types = system.Devices.Select(d => d.DeviceClass.ToLower()).ToHashSet();
        var roles = system.Devices.Select(d => d.Role.ToLower()).ToHashSet();

        if (types.Any(t => t.Contains("capture") || t.Contains("zone")))
            return "capture";
        if (types.Any(t => t.Contains("spawn")) && types.Count >= 3)
            return "spawning";
        if (types.Any(t => t.Contains("vending") || t.Contains("granter")))
            return "economy";
        if (types.Any(t => t.Contains("elimination") || t.Contains("combat")))
            return "combat";
        if (types.Any(t => t.Contains("timer") || t.Contains("score")))
            return "gameplay";
        if (types.Any(t => t.Contains("barrier") || t.Contains("wall")))
            return "environment";
        if (types.Any(t => t.Contains("trigger")))
            return "triggers";
        if (types.Any(t => t.Contains("hud") || t.Contains("widget") || t.Contains("billboard")))
            return "ui";

        return "custom";
    }

    private string InferRole(DeviceInfo device)
    {
        var type = device.DeviceType.ToLower();
        var className = device.DeviceClass.ToLower();

        if (className.Contains("trigger")) return "trigger";
        if (className.Contains("spawn")) return "spawner";
        if (className.Contains("timer")) return "timer";
        if (className.Contains("score") || className.Contains("tracker")) return "tracker";
        if (className.Contains("barrier") || className.Contains("wall")) return "barrier";
        if (className.Contains("vending")) return "vendor";
        if (className.Contains("granter")) return "granter";
        if (className.Contains("hud") || className.Contains("billboard")) return "display";
        if (className.Contains("zone") || className.Contains("volume")) return "zone";
        if (className.Contains("mutator")) return "mutator";
        if (className.Contains("teleport")) return "teleporter";
        if (className.Contains("button")) return "button";
        if (className.Contains("switch")) return "switch";
        if (className.Contains("elimination")) return "elimination_tracker";

        // Fall back to the pretty name
        return device.DeviceType.ToLower().Replace(" ", "_");
    }

    private static bool IsSignificantProperty(PropertyInfo prop)
    {
        // Skip transform/rendering noise
        var name = prop.Name.ToLower();
        return !name.Contains("relativelocation") &&
               !name.Contains("relativerotation") &&
               !name.Contains("relativescale") &&
               !name.Contains("rootcomponent") &&
               !name.Contains("attachparent") &&
               !name.Contains("creationmethod") &&
               !name.Contains("mobility") &&
               !name.Contains("renderstate") &&
               !name.Contains("hiddening") &&
               !name.Contains("replication") &&
               !name.Contains("componenttags") &&
               !name.Contains("detailmode") &&
               !name.Contains("cancharacter") &&
               !name.Contains("bvisible") &&
               !name.Contains("bhidden");
    }

    // ========= Deduplication =========

    private List<ExtractedSystem> DeduplicateSystems(List<ExtractedSystem> systems)
    {
        var unique = new List<ExtractedSystem>();

        foreach (var system in systems)
        {
            if (!unique.Any(u => AreSystemsSimilar(u, system)))
                unique.Add(system);
        }

        return unique;
    }

    private bool AreSystemsSimilar(ExtractedSystem a, ExtractedSystem b)
    {
        // Compare device type composition using Jaccard similarity
        var aTypeSet = new HashSet<string>(
            a.Devices.Select(d => d.DeviceClass.ToLowerInvariant()));
        var bTypeSet = new HashSet<string>(
            b.Devices.Select(d => d.DeviceClass.ToLowerInvariant()));

        var typeIntersection = aTypeSet.Intersect(bTypeSet).Count();
        var typeUnion = aTypeSet.Union(bTypeSet).Count();
        var typeJaccard = typeUnion > 0 ? (double)typeIntersection / typeUnion : 0;

        if (typeJaccard < 0.5)
            return false; // Device types are too different

        // Compare wiring TOPOLOGY: source_type.event -> target_type.action patterns
        var aWiringTopology = GetWiringTopology(a);
        var bWiringTopology = GetWiringTopology(b);

        // If neither has wiring, rely on type similarity alone
        if (aWiringTopology.Count == 0 && bWiringTopology.Count == 0)
            return typeJaccard >= 0.8;

        var wiringIntersection = aWiringTopology.Intersect(bWiringTopology).Count();
        var wiringUnion = aWiringTopology.Union(bWiringTopology).Count();
        var wiringJaccard = wiringUnion > 0 ? (double)wiringIntersection / wiringUnion : 0;

        // Combined similarity: weight type similarity and wiring topology
        var combined = (typeJaccard * 0.4) + (wiringJaccard * 0.6);
        return combined >= 0.5;
    }

    /// <summary>
    /// Extracts the wiring topology of a system as a set of
    /// "source_type.event -> target_type.action" strings.
    /// This captures the structural pattern regardless of specific actor names.
    /// </summary>
    private static HashSet<string> GetWiringTopology(ExtractedSystem system)
    {
        var topology = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build a lookup from actor name to device class
        var actorToClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in system.Devices)
        {
            actorToClass[device.ActorName] = device.DeviceClass.ToLowerInvariant();
        }

        foreach (var wire in system.Wiring)
        {
            var sourceClass = actorToClass.GetValueOrDefault(wire.SourceActor, wire.SourceRole);
            var targetClass = actorToClass.GetValueOrDefault(wire.TargetActor, wire.TargetRole);

            var pattern = $"{sourceClass}.{wire.OutputEvent.ToLowerInvariant()}" +
                          $"->{targetClass}.{wire.InputAction.ToLowerInvariant()}";
            topology.Add(pattern);
        }

        return topology;
    }

    /// <summary>
    /// Ranks extracted systems by complexity. More complex systems with
    /// more devices, wiring, and Verse integration rank higher.
    /// Score = device_count * 2 + wiring_count * 3 + verse_device_count * 5
    /// </summary>
    public static List<ExtractedSystem> RankSystemsByComplexity(List<ExtractedSystem> systems)
    {
        return systems
            .Select(s => new
            {
                System = s,
                ComplexityScore = s.DeviceCount * 2
                    + s.Wiring.Count * 3
                    + s.Devices.Count(d =>
                        d.DeviceClass.Contains("verse", StringComparison.OrdinalIgnoreCase) ||
                        d.Properties.Any(p =>
                            p.Name.Contains("verse", StringComparison.OrdinalIgnoreCase) ||
                            p.Name.Contains("script", StringComparison.OrdinalIgnoreCase))) * 5
            })
            .OrderByDescending(x => x.ComplexityScore)
            .Select(x => x.System)
            .ToList();
    }

    private static float Distance3D(Vector3Info a, Vector3Info b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

// ========= Analysis Result Models =========

public class LevelSystemAnalysis
{
    public string LevelPath { get; set; } = "";
    public int TotalDevices { get; set; }
    public List<ExtractedSystem> Systems { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class ProjectSystemAnalysis
{
    public string ProjectPath { get; set; } = "";
    public int LevelCount { get; set; }
    public List<LevelSystemAnalysis> LevelAnalyses { get; set; } = new();
    public List<ExtractedSystem> Systems { get; set; } = new();
    public List<ExtractedSystem> UniqueSystems { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class LibrarySystemAnalysis
{
    public string LibraryPath { get; set; } = "";
    public int ProjectCount { get; set; }
    public List<ProjectSystemAnalysis> ProjectAnalyses { get; set; } = new();
    public List<ExtractedSystem> AllSystems { get; set; } = new();
    public List<ExtractedSystem> UniqueSystems { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class ExtractedSystem
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string DetectionMethod { get; set; } = "wiring";
    public float Confidence { get; set; } = 1.0f;
    public int Frequency { get; set; } = 1;
    public int DeviceCount { get; set; }
    public string? SourceLevel { get; set; }
    public List<ExtractedDevice> Devices { get; set; } = new();
    public List<ExtractedWiring> Wiring { get; set; } = new();
}

public class ExtractedDevice
{
    public string Role { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string Label { get; set; } = "";
    public string ActorName { get; set; } = "";
    public Vector3Info Offset { get; set; } = new();
    public Vector3Info Rotation { get; set; } = new();
    public Vector3Info Scale { get; set; } = new(1, 1, 1);
    public List<ExtractedProperty> Properties { get; set; } = new();
}

public class ExtractedProperty
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
}

public class ExtractedWiring
{
    public string SourceRole { get; set; } = "";
    public string SourceActor { get; set; } = "";
    public string OutputEvent { get; set; } = "";
    public string TargetRole { get; set; } = "";
    public string TargetActor { get; set; } = "";
    public string InputAction { get; set; } = "";
    public string? Channel { get; set; }
}
