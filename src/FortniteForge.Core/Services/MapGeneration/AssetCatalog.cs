using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Services.MapGeneration;

/// <summary>
/// Scans a "seed level" (.umap) and catalogs every placed actor by type.
/// The seed level is the user's template — they place one of each building,
/// prop, terrain piece, and device. The catalog records what's available
/// for map generation to clone from.
///
/// Categories:
///   - Buildings: parent actors with child components (walls, floors, etc.)
///   - Props: standalone static meshes (rocks, crates, vegetation)
///   - Terrain: terrain/landscape chunks
///   - Devices: Creative devices (spawners, zones, triggers)
///   - Foliage: trees, bushes, grass clusters
/// </summary>
public class AssetCatalog
{
    private readonly WellVersedConfig _config;
    private readonly ILogger<AssetCatalog> _logger;

    public Dictionary<string, CatalogEntry> Entries { get; private set; } = new();
    public string? SeedLevelPath { get; private set; }
    public bool IsLoaded { get; private set; }

    public AssetCatalog(WellVersedConfig config, ILogger<AssetCatalog> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Scans a seed level and builds the catalog.
    /// Every top-level actor becomes a catalog entry with a category tag.
    /// </summary>
    public CatalogScanResult ScanSeedLevel(string levelPath)
    {
        var result = new CatalogScanResult { LevelPath = levelPath };
        Entries.Clear();

        try
        {
            var asset = new UAsset(levelPath, EngineVersion.VER_UE5_4);
            SeedLevelPath = levelPath;

            // Find the LevelExport to get the actor list
            var levelExport = asset.Exports.OfType<LevelExport>().FirstOrDefault();
            if (levelExport == null)
            {
                result.Errors.Add("No LevelExport found in this .umap file.");
                return result;
            }

            // Process each actor in the level
            foreach (var actorRef in levelExport.Actors)
            {
                if (!actorRef.IsExport()) continue;

                var actorIndex = actorRef.Index - 1;
                if (actorIndex < 0 || actorIndex >= asset.Exports.Count) continue;

                var actorExport = asset.Exports[actorIndex];
                var actorName = actorExport.ObjectName?.ToString() ?? "";
                var className = actorExport.GetExportClassType()?.ToString() ?? "";

                // Skip engine internals
                if (IsEngineInternal(className)) continue;

                // Count child components
                var childCount = CountChildren(asset, actorIndex);

                // Categorize
                var category = CategorizeActor(className, actorName, childCount);

                // Extract transform from the actor or its root component
                var transform = ExtractTransform(asset, actorExport, actorIndex);

                // Extract the label if set
                var label = ExtractLabel(asset, actorExport);

                // Calculate bounding size estimate from child count
                var sizeEstimate = EstimateSize(category, childCount);

                var entry = new CatalogEntry
                {
                    Id = actorName,
                    ActorName = actorName,
                    ClassName = className,
                    Category = category,
                    Label = label ?? actorName,
                    ChildComponentCount = childCount,
                    Location = transform.Location,
                    Rotation = transform.Rotation,
                    Scale = transform.Scale,
                    EstimatedFootprint = sizeEstimate,
                    Tags = InferTags(className, actorName, label)
                };

                Entries[actorName] = entry;
                result.EntriesByCategory[category] = result.EntriesByCategory.GetValueOrDefault(category) + 1;
            }

            IsLoaded = true;
            result.TotalEntries = Entries.Count;
            result.Success = true;

            _logger.LogInformation("Cataloged {Count} actors from seed level {Path}", Entries.Count, levelPath);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to scan: {ex.Message}");
            _logger.LogError(ex, "Seed level scan failed");
        }

        return result;
    }

    /// <summary>
    /// Gets all entries of a specific category.
    /// </summary>
    public List<CatalogEntry> GetByCategory(ActorCategory category)
    {
        return Entries.Values.Where(e => e.Category == category).ToList();
    }

    /// <summary>
    /// Gets entries matching any of the given tags.
    /// </summary>
    public List<CatalogEntry> GetByTags(params string[] tags)
    {
        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        return Entries.Values
            .Where(e => e.Tags.Any(t => tagSet.Contains(t)))
            .ToList();
    }

    /// <summary>
    /// Gets a specific entry by actor name.
    /// </summary>
    public CatalogEntry? Get(string actorName)
    {
        Entries.TryGetValue(actorName, out var entry);
        return entry;
    }

    /// <summary>
    /// Saves the catalog to a JSON file for reuse without rescanning.
    /// </summary>
    public void SaveCatalog(string outputPath)
    {
        var json = JsonSerializer.Serialize(new
        {
            seedLevel = SeedLevelPath,
            scannedAt = DateTime.UtcNow,
            entries = Entries.Values
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, json);
    }

    /// <summary>
    /// Loads a previously saved catalog.
    /// </summary>
    public void LoadCatalog(string catalogPath)
    {
        var json = File.ReadAllText(catalogPath);
        var doc = JsonDocument.Parse(json);

        Entries.Clear();
        SeedLevelPath = doc.RootElement.GetProperty("seedLevel").GetString();

        foreach (var entryElement in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            var entry = JsonSerializer.Deserialize<CatalogEntry>(entryElement.GetRawText(),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

            if (entry != null)
                Entries[entry.ActorName] = entry;
        }

        IsLoaded = true;
    }

    // ========= Classification Logic =========

    private static ActorCategory CategorizeActor(string className, string actorName, int childCount)
    {
        var lower = className.ToLowerInvariant();
        var nameLower = actorName.ToLowerInvariant();

        // Devices — Creative gameplay devices
        if (lower.Contains("device") || lower.Contains("spawner") ||
            lower.Contains("trigger") || lower.Contains("mutator") ||
            lower.Contains("granter") || lower.Contains("barrier") ||
            lower.Contains("teleporter") || lower.Contains("zone") ||
            lower.Contains("capture") || lower.Contains("timer") ||
            lower.Contains("scoreboard") || lower.Contains("hud"))
            return ActorCategory.Device;

        // Buildings — actors with many child components (walls, floors, etc.)
        if (childCount >= 3 &&
            (lower.Contains("building") || lower.Contains("structure") ||
             lower.Contains("prefab") || lower.Contains("gallery") ||
             nameLower.Contains("building") || nameLower.Contains("house") ||
             nameLower.Contains("tower") || nameLower.Contains("wall")))
            return ActorCategory.Building;

        // Also categorize large component-heavy actors as buildings
        if (childCount >= 5)
            return ActorCategory.Building;

        // Terrain
        if (lower.Contains("landscape") || lower.Contains("terrain") ||
            lower.Contains("ground") || nameLower.Contains("terrain") ||
            nameLower.Contains("floor_large") || nameLower.Contains("ground"))
            return ActorCategory.Terrain;

        // Foliage — trees, bushes, grass
        if (lower.Contains("foliage") || lower.Contains("tree") ||
            lower.Contains("bush") || lower.Contains("grass") ||
            nameLower.Contains("tree") || nameLower.Contains("bush") ||
            nameLower.Contains("plant") || nameLower.Contains("cactus"))
            return ActorCategory.Foliage;

        // Everything else is a prop
        return ActorCategory.Prop;
    }

    private static List<string> InferTags(string className, string actorName, string? label)
    {
        var tags = new List<string>();
        var combined = $"{className} {actorName} {label}".ToLowerInvariant();

        // Biome hints
        if (combined.ContainsAny("desert", "sand", "mesa", "arid", "canyon"))
            tags.Add("desert");
        if (combined.ContainsAny("forest", "wood", "pine", "oak", "leaf"))
            tags.Add("forest");
        if (combined.ContainsAny("urban", "city", "concrete", "asphalt", "metal"))
            tags.Add("urban");
        if (combined.ContainsAny("snow", "ice", "frost", "winter", "arctic"))
            tags.Add("snow");
        if (combined.ContainsAny("tropical", "palm", "beach", "coral"))
            tags.Add("tropical");

        // Size hints
        if (combined.ContainsAny("large", "big", "tall", "huge"))
            tags.Add("large");
        if (combined.ContainsAny("small", "tiny", "mini"))
            tags.Add("small");

        // Type hints
        if (combined.ContainsAny("rock", "stone", "boulder"))
            tags.Add("rock");
        if (combined.ContainsAny("vehicle", "car", "truck"))
            tags.Add("vehicle");
        if (combined.ContainsAny("barrel", "crate", "box", "container"))
            tags.Add("container");
        if (combined.ContainsAny("fence", "wall", "railing"))
            tags.Add("barrier");
        if (combined.ContainsAny("light", "lamp", "lantern"))
            tags.Add("lighting");
        if (combined.ContainsAny("chest", "loot", "supply"))
            tags.Add("loot");

        return tags;
    }

    private static float EstimateSize(ActorCategory category, int childCount)
    {
        // Rough footprint estimates in UE units based on category
        return category switch
        {
            ActorCategory.Building => 500f + (childCount * 100f),
            ActorCategory.Terrain => 2000f,
            ActorCategory.Device => 200f,
            ActorCategory.Foliage => 150f,
            ActorCategory.Prop => 100f + (childCount * 50f),
            _ => 200f
        };
    }

    private static int CountChildren(UAsset asset, int parentIndex)
    {
        var parentRef = UAssetAPI.UnrealTypes.FPackageIndex.FromExport(parentIndex);
        int count = 0;
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].OuterIndex == parentRef)
                count++;
        }
        return count;
    }

    private static (Vector3Info Location, Vector3Info Rotation, Vector3Info Scale) ExtractTransform(
        UAsset asset, Export actorExport, int actorIndex)
    {
        var loc = new Vector3Info();
        var rot = new Vector3Info();
        var scale = new Vector3Info(1, 1, 1);

        // Check the actor's own properties first, then root component
        var exportsToCheck = new List<NormalExport>();

        if (actorExport is NormalExport normalActor)
            exportsToCheck.Add(normalActor);

        // Find root component
        var actorRef = UAssetAPI.UnrealTypes.FPackageIndex.FromExport(actorIndex);
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].OuterIndex == actorRef && asset.Exports[i] is NormalExport ne)
            {
                exportsToCheck.Add(ne);
                break; // First child is usually root component
            }
        }

        foreach (var export in exportsToCheck)
        {
            foreach (var prop in export.Data)
            {
                var name = prop.Name?.ToString() ?? "";
                if (name == "RelativeLocation" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData locStruct)
                {
                    loc = ExtractVectorFromStruct(locStruct);
                }
                else if (name == "RelativeRotation" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData rotStruct)
                {
                    rot = ExtractVectorFromStruct(rotStruct);
                }
                else if (name == "RelativeScale3D" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData scaleStruct)
                {
                    scale = ExtractVectorFromStruct(scaleStruct);
                }
            }
        }

        return (loc, rot, scale);
    }

    private static Vector3Info ExtractVectorFromStruct(UAssetAPI.PropertyTypes.Structs.StructPropertyData structProp)
    {
        var vec = new Vector3Info();
        foreach (var prop in structProp.Value)
        {
            var name = prop.Name?.ToString() ?? "";
            double val = 0;
            if (prop is UAssetAPI.PropertyTypes.Objects.FloatPropertyData fp)
                val = fp.Value;
            else if (prop is UAssetAPI.PropertyTypes.Objects.DoublePropertyData dp)
                val = dp.Value;

            switch (name)
            {
                case "X": case "Pitch": vec.X = (float)val; break;
                case "Y": case "Yaw": vec.Y = (float)val; break;
                case "Z": case "Roll": vec.Z = (float)val; break;
            }
        }
        return vec;
    }

    private static string? ExtractLabel(UAsset asset, Export export)
    {
        if (export is NormalExport ne)
        {
            var labelProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "ActorLabel");
            if (labelProp is UAssetAPI.PropertyTypes.Objects.StrPropertyData strProp)
                return strProp.Value?.ToString();
        }
        return null;
    }

    private static bool IsEngineInternal(string className)
    {
        var internals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WorldSettings", "Brush", "DefaultPhysicsVolume",
            "GameModeBase", "GameStateBase", "PlayerStart",
            "NavigationData", "AbstractNavData", "RecastNavMesh",
            "AtmosphericFog", "SkyAtmosphere", "DirectionalLight",
            "SkyLight", "ExponentialHeightFog", "VolumetricCloud",
            "PostProcessVolume", "LevelSequenceActor", "Note"
        };

        return internals.Any(i => className.Contains(i, StringComparison.OrdinalIgnoreCase));
    }
}

// ========= Models =========

public class CatalogEntry
{
    public string Id { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public ActorCategory Category { get; set; }
    public string Label { get; set; } = "";
    public int ChildComponentCount { get; set; }
    public Vector3Info Location { get; set; } = new();
    public Vector3Info Rotation { get; set; } = new();
    public Vector3Info Scale { get; set; } = new(1, 1, 1);
    public float EstimatedFootprint { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class CatalogScanResult
{
    public string LevelPath { get; set; } = "";
    public bool Success { get; set; }
    public int TotalEntries { get; set; }
    public Dictionary<ActorCategory, int> EntriesByCategory { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public enum ActorCategory
{
    Building,
    Prop,
    Terrain,
    Device,
    Foliage
}

// Extension helper
internal static class StringExtensions
{
    public static bool ContainsAny(this string str, params string[] values)
    {
        return values.Any(v => str.Contains(v, StringComparison.OrdinalIgnoreCase));
    }
}
