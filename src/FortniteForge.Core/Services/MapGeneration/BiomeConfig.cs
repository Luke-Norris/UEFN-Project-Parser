using System.Text.Json;
using System.Text.Json.Serialization;

namespace WellVersed.Core.Services.MapGeneration;

/// <summary>
/// Defines a biome — the style, density, and composition rules for an environment.
/// Biomes control which catalog entries get selected and how they're distributed.
///
/// Built-in biomes: Desert, Forest, Urban, Snow, Tropical
/// Users can define custom biomes in JSON files.
/// </summary>
public class BiomeConfig
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// Tags that catalog entries should match for this biome.
    /// Entries with ANY of these tags are eligible.
    /// If empty, all entries are eligible.
    /// </summary>
    public List<string> PreferredTags { get; set; } = new();

    /// <summary>
    /// Tags that should be excluded from this biome.
    /// </summary>
    public List<string> ExcludedTags { get; set; } = new();

    /// <summary>
    /// How densely to scatter props (0.0 = barren, 1.0 = packed).
    /// </summary>
    public float PropDensity { get; set; } = 0.5f;

    /// <summary>
    /// How densely to scatter foliage (0.0 = barren, 1.0 = thick forest).
    /// </summary>
    public float FoliageDensity { get; set; } = 0.3f;

    /// <summary>
    /// How many buildings per POI (base count, scaled by POI size).
    /// </summary>
    public int BuildingsPerPoi { get; set; } = 5;

    /// <summary>
    /// Minimum spacing between buildings in a POI (UE units).
    /// </summary>
    public float BuildingSpacing { get; set; } = 1200f;

    /// <summary>
    /// Minimum spacing between props (UE units).
    /// </summary>
    public float PropSpacing { get; set; } = 300f;

    /// <summary>
    /// Minimum spacing between foliage instances (UE units).
    /// </summary>
    public float FoliageSpacing { get; set; } = 200f;

    /// <summary>
    /// Scale variation range for props and foliage [min, max].
    /// </summary>
    public float ScaleMin { get; set; } = 0.8f;
    public float ScaleMax { get; set; } = 1.2f;

    /// <summary>
    /// Rules for what devices to auto-place.
    /// </summary>
    public DevicePlacementRules Devices { get; set; } = new();

    /// <summary>
    /// POI composition templates for this biome.
    /// </summary>
    public List<PoiTemplate> PoiTemplates { get; set; } = new();

    // ========= Built-in Biome Presets =========

    public static BiomeConfig Desert => new()
    {
        Name = "Desert",
        Description = "Arid environment with sparse vegetation, sand terrain, and weathered structures.",
        PreferredTags = new() { "desert", "sand", "rock", "arid" },
        ExcludedTags = new() { "snow", "tropical", "forest" },
        PropDensity = 0.3f,
        FoliageDensity = 0.1f,
        BuildingsPerPoi = 4,
        BuildingSpacing = 1500f,
        PropSpacing = 500f,
        FoliageSpacing = 800f,
        ScaleMin = 0.7f,
        ScaleMax = 1.3f,
        Devices = new DevicePlacementRules
        {
            ChestsPerPoi = 3,
            SpawnPointsPerPoi = 2,
            PlaceStormController = true,
            PlaceMapBoundary = true
        },
        PoiTemplates = new()
        {
            PoiTemplate.SmallOutpost,
            PoiTemplate.AbandonedTown,
            PoiTemplate.Compound
        }
    };

    public static BiomeConfig Forest => new()
    {
        Name = "Forest",
        Description = "Dense woodland with clearings, log cabins, and natural cover.",
        PreferredTags = new() { "forest", "wood", "rock", "leaf" },
        ExcludedTags = new() { "desert", "snow", "urban" },
        PropDensity = 0.5f,
        FoliageDensity = 0.7f,
        BuildingsPerPoi = 3,
        BuildingSpacing = 1000f,
        PropSpacing = 250f,
        FoliageSpacing = 150f,
        ScaleMin = 0.6f,
        ScaleMax = 1.4f,
        Devices = new DevicePlacementRules
        {
            ChestsPerPoi = 3,
            SpawnPointsPerPoi = 2,
            PlaceStormController = true,
            PlaceMapBoundary = true
        },
        PoiTemplates = new()
        {
            PoiTemplate.ForestClearing,
            PoiTemplate.SmallOutpost,
            PoiTemplate.Campsite
        }
    };

    public static BiomeConfig Urban => new()
    {
        Name = "Urban",
        Description = "City environment with dense building clusters, streets, and infrastructure.",
        PreferredTags = new() { "urban", "city", "concrete", "metal", "vehicle" },
        ExcludedTags = new() { "forest", "tropical" },
        PropDensity = 0.6f,
        FoliageDensity = 0.1f,
        BuildingsPerPoi = 7,
        BuildingSpacing = 800f,
        PropSpacing = 200f,
        FoliageSpacing = 600f,
        ScaleMin = 0.9f,
        ScaleMax = 1.1f,
        Devices = new DevicePlacementRules
        {
            ChestsPerPoi = 4,
            SpawnPointsPerPoi = 3,
            PlaceStormController = true,
            PlaceMapBoundary = true
        },
        PoiTemplates = new()
        {
            PoiTemplate.CityBlock,
            PoiTemplate.Compound,
            PoiTemplate.AbandonedTown
        }
    };

    public static BiomeConfig Snow => new()
    {
        Name = "Snow",
        Description = "Frozen landscape with icy terrain, sparse trees, and lodge-style buildings.",
        PreferredTags = new() { "snow", "ice", "frost", "rock" },
        ExcludedTags = new() { "desert", "tropical" },
        PropDensity = 0.25f,
        FoliageDensity = 0.2f,
        BuildingsPerPoi = 4,
        BuildingSpacing = 1200f,
        PropSpacing = 400f,
        FoliageSpacing = 500f,
        ScaleMin = 0.8f,
        ScaleMax = 1.2f,
        Devices = new DevicePlacementRules
        {
            ChestsPerPoi = 3,
            SpawnPointsPerPoi = 2,
            PlaceStormController = true,
            PlaceMapBoundary = true
        },
        PoiTemplates = new()
        {
            PoiTemplate.SmallOutpost,
            PoiTemplate.Campsite,
            PoiTemplate.Compound
        }
    };

    public static Dictionary<string, BiomeConfig> BuiltInBiomes => new(StringComparer.OrdinalIgnoreCase)
    {
        ["desert"] = Desert,
        ["forest"] = Forest,
        ["urban"] = Urban,
        ["snow"] = Snow
    };

    public static BiomeConfig GetBiome(string name)
    {
        if (BuiltInBiomes.TryGetValue(name, out var biome))
            return biome;

        // Default: no tag preferences, moderate density
        return new BiomeConfig
        {
            Name = name,
            Description = $"Custom biome: {name}",
            PropDensity = 0.4f,
            FoliageDensity = 0.3f,
            BuildingsPerPoi = 5
        };
    }
}

public class DevicePlacementRules
{
    public int ChestsPerPoi { get; set; } = 3;
    public int SpawnPointsPerPoi { get; set; } = 2;
    public bool PlaceStormController { get; set; } = true;
    public bool PlaceMapBoundary { get; set; } = true;
    public int AmmoBoxesPerPoi { get; set; } = 2;
}

/// <summary>
/// Template for a Point of Interest — defines how buildings, props, and devices
/// are arranged relative to the POI center.
/// </summary>
public class PoiTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// Radius of this POI type (UE units from center).
    /// </summary>
    public float Radius { get; set; } = 3000f;

    /// <summary>
    /// How buildings are arranged: Cluster, Grid, Ring, Scattered.
    /// </summary>
    public LayoutPattern BuildingPattern { get; set; } = LayoutPattern.Cluster;

    /// <summary>
    /// Number of buildings (base, may be scaled by biome config).
    /// </summary>
    public int BuildingCount { get; set; } = 4;

    /// <summary>
    /// Whether props should be concentrated near buildings or spread evenly.
    /// </summary>
    public PropDistribution PropDistribution { get; set; } = PropDistribution.NearBuildings;

    /// <summary>
    /// How many props per building in this POI.
    /// </summary>
    public int PropsPerBuilding { get; set; } = 3;

    /// <summary>
    /// Where to place spawn points relative to the POI.
    /// </summary>
    public SpawnPlacement SpawnPlacement { get; set; } = SpawnPlacement.Edges;

    // ========= Built-in Templates =========

    public static PoiTemplate SmallOutpost => new()
    {
        Name = "Small Outpost",
        Description = "A few buildings clustered together with light cover.",
        Radius = 2000f,
        BuildingPattern = LayoutPattern.Cluster,
        BuildingCount = 3,
        PropDistribution = PropDistribution.NearBuildings,
        PropsPerBuilding = 2,
        SpawnPlacement = SpawnPlacement.Edges
    };

    public static PoiTemplate AbandonedTown => new()
    {
        Name = "Abandoned Town",
        Description = "Grid of buildings forming a small town with streets.",
        Radius = 3500f,
        BuildingPattern = LayoutPattern.Grid,
        BuildingCount = 6,
        PropDistribution = PropDistribution.NearBuildings,
        PropsPerBuilding = 4,
        SpawnPlacement = SpawnPlacement.Edges
    };

    public static PoiTemplate Compound => new()
    {
        Name = "Compound",
        Description = "Buildings arranged in a ring around a central courtyard.",
        Radius = 2500f,
        BuildingPattern = LayoutPattern.Ring,
        BuildingCount = 4,
        PropDistribution = PropDistribution.NearBuildings,
        PropsPerBuilding = 3,
        SpawnPlacement = SpawnPlacement.Center
    };

    public static PoiTemplate Campsite => new()
    {
        Name = "Campsite",
        Description = "Scattered small structures in a loose arrangement.",
        Radius = 1500f,
        BuildingPattern = LayoutPattern.Scattered,
        BuildingCount = 2,
        PropDistribution = PropDistribution.Even,
        PropsPerBuilding = 5,
        SpawnPlacement = SpawnPlacement.Edges
    };

    public static PoiTemplate ForestClearing => new()
    {
        Name = "Forest Clearing",
        Description = "Open area surrounded by dense foliage with a few structures.",
        Radius = 2000f,
        BuildingPattern = LayoutPattern.Scattered,
        BuildingCount = 2,
        PropDistribution = PropDistribution.Perimeter,
        PropsPerBuilding = 3,
        SpawnPlacement = SpawnPlacement.Edges
    };

    public static PoiTemplate CityBlock => new()
    {
        Name = "City Block",
        Description = "Dense grid of buildings resembling a city block.",
        Radius = 3000f,
        BuildingPattern = LayoutPattern.Grid,
        BuildingCount = 8,
        PropDistribution = PropDistribution.NearBuildings,
        PropsPerBuilding = 3,
        SpawnPlacement = SpawnPlacement.Edges
    };
}

public enum LayoutPattern
{
    Cluster,    // Buildings grouped close together, organic spacing
    Grid,       // Buildings on a grid with streets between
    Ring,       // Buildings around a central area
    Scattered   // Loosely spread out
}

public enum PropDistribution
{
    NearBuildings,  // Props concentrate near building entrances/sides
    Even,           // Props evenly spread across the POI area
    Perimeter,      // Props along the edges of the POI
    Center          // Props concentrated at the POI center
}

public enum SpawnPlacement
{
    Edges,      // Spawn points at the outer edges of the POI
    Center,     // Spawn points in the center
    Distributed // Spawn points spread across the POI
}
