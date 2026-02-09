using FortniteForge.Core.Models;

namespace FortniteForge.Core.Services.MapGeneration;

/// <summary>
/// Represents the planned layout of a map before any actors are placed.
/// This is the "blueprint" that the user reviews before generation.
/// </summary>
public class MapLayout
{
    /// <summary>
    /// Name for this map generation (e.g., "Desert_Competitive_01").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Biome used for this layout.
    /// </summary>
    public string Biome { get; set; } = "";

    /// <summary>
    /// Map dimensions (width x length in UE units).
    /// </summary>
    public float MapWidth { get; set; }
    public float MapLength { get; set; }

    /// <summary>
    /// Center of the map in world space.
    /// </summary>
    public Vector3Info MapCenter { get; set; } = new();

    /// <summary>
    /// Planned POI positions and configurations.
    /// </summary>
    public List<PoiPlacement> Pois { get; set; } = new();

    /// <summary>
    /// Planned terrain fill areas.
    /// </summary>
    public List<TerrainFill> TerrainAreas { get; set; } = new();

    /// <summary>
    /// Planned scattered prop/foliage areas (between POIs).
    /// </summary>
    public List<ScatterArea> ScatterAreas { get; set; } = new();

    /// <summary>
    /// Planned global device placements (storm controller, boundary, etc.).
    /// </summary>
    public List<DevicePlacement> GlobalDevices { get; set; } = new();

    /// <summary>
    /// Estimated total actor count.
    /// </summary>
    public int EstimatedActorCount { get; set; }

    /// <summary>
    /// Generation seed for reproducibility.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// Human-readable description of the layout plan.
    /// </summary>
    public string Description { get; set; } = "";
}

public class PoiPlacement
{
    /// <summary>
    /// Display name (e.g., "Desert Outpost", "Abandoned Town").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Template used for this POI.
    /// </summary>
    public string TemplateName { get; set; } = "";

    /// <summary>
    /// Center position of the POI.
    /// </summary>
    public Vector3Info Center { get; set; } = new();

    /// <summary>
    /// Radius of the POI.
    /// </summary>
    public float Radius { get; set; }

    /// <summary>
    /// Planned building placements within this POI.
    /// </summary>
    public List<ActorPlacement> Buildings { get; set; } = new();

    /// <summary>
    /// Planned prop placements within this POI.
    /// </summary>
    public List<ActorPlacement> Props { get; set; } = new();

    /// <summary>
    /// Planned device placements within this POI.
    /// </summary>
    public List<DevicePlacement> Devices { get; set; } = new();
}

public class ActorPlacement
{
    /// <summary>
    /// Reference to the catalog entry to clone.
    /// </summary>
    public string CatalogId { get; set; } = "";

    public Vector3Info Location { get; set; } = new();
    public Vector3Info Rotation { get; set; } = new();
    public Vector3Info Scale { get; set; } = new(1, 1, 1);
}

public class DevicePlacement
{
    /// <summary>
    /// Reference to the catalog entry for this device.
    /// </summary>
    public string CatalogId { get; set; } = "";

    /// <summary>
    /// Device purpose (e.g., "player_spawn", "chest", "storm_controller").
    /// </summary>
    public string Purpose { get; set; } = "";

    public Vector3Info Location { get; set; } = new();
    public Vector3Info Rotation { get; set; } = new();
}

public class TerrainFill
{
    /// <summary>
    /// Catalog entry for the terrain piece to tile.
    /// </summary>
    public string CatalogId { get; set; } = "";

    /// <summary>
    /// Area to fill with terrain.
    /// </summary>
    public Vector3Info AreaCenter { get; set; } = new();
    public float AreaWidth { get; set; }
    public float AreaLength { get; set; }

    /// <summary>
    /// Grid spacing for tiling terrain pieces.
    /// </summary>
    public float TileSpacing { get; set; } = 2000f;
}

public class ScatterArea
{
    /// <summary>
    /// What to scatter: "props", "foliage", or "mixed".
    /// </summary>
    public string Type { get; set; } = "mixed";

    /// <summary>
    /// Catalog entries to randomly pick from for scattering.
    /// </summary>
    public List<string> CatalogIds { get; set; } = new();

    public Vector3Info AreaCenter { get; set; } = new();
    public float AreaWidth { get; set; }
    public float AreaLength { get; set; }
    public int Count { get; set; }
    public float MinSpacing { get; set; }
}

/// <summary>
/// Result of applying a map layout to a level.
/// </summary>
public class MapGenerationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? BackupPath { get; set; }
    public int ActorsPlaced { get; set; }
    public int BuildingsPlaced { get; set; }
    public int PropsPlaced { get; set; }
    public int DevicesPlaced { get; set; }
    public int FoliagePlaced { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Map size presets calibrated for Fortnite Creative competitive maps.
/// Based on standard Reload map proportions.
/// </summary>
public static class MapSize
{
    /// <summary>
    /// Small arena — roughly 1/4 of a Reload map. Good for 2v2 or FFA.
    /// About 8,000 x 8,000 UE units. 2-3 POIs.
    /// </summary>
    public static (float Width, float Length) Small => (8000f, 8000f);

    /// <summary>
    /// Medium — roughly half a Reload map. Good for 4v4.
    /// About 14,000 x 14,000 UE units. 3-4 POIs.
    /// </summary>
    public static (float Width, float Length) Medium => (14000f, 14000f);

    /// <summary>
    /// Large — full Reload map size. Good for 8+ players.
    /// About 22,000 x 22,000 UE units. 4-6 POIs.
    /// </summary>
    public static (float Width, float Length) Large => (22000f, 22000f);

    /// <summary>
    /// Determines POI count based on map size.
    /// </summary>
    public static int RecommendedPoiCount(float width, float length)
    {
        var area = width * length;
        return area switch
        {
            < 100_000_000f => 2,   // Small
            < 250_000_000f => 3,   // Medium
            < 500_000_000f => 4,   // Large
            _ => 5                  // Very large
        };
    }

    /// <summary>
    /// Gets a size preset by name.
    /// </summary>
    public static (float Width, float Length) GetSize(string sizeName)
    {
        return sizeName.ToLowerInvariant() switch
        {
            "small" or "tiny" or "compact" => Small,
            "medium" or "moderate" or "normal" => Medium,
            "large" or "big" => Large,
            _ => Medium
        };
    }
}
