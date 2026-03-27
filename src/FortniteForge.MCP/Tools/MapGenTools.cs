using WellVersed.Core.Models;
using WellVersed.Core.Services.MapGeneration;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for procedural map generation.
///
/// Workflow:
///   1. scan_seed_level — catalog available building/prop/device templates
///   2. generate_map_layout — create a layout plan (preview)
///   3. apply_map_layout — place all actors in the target level
///
/// Example Claude conversation:
///   User: "Generate me a desert map, not too large, couple POIs"
///   Claude: scan_seed_level → generate_map_layout("desert", "small", 2) → show plan → apply_map_layout
/// </summary>
[McpServerToolType]
public class MapGenTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool, Description(
        "STEP 1 for map generation: Scans a seed level to catalog all available building, " +
        "prop, terrain, and device templates. The seed level should have one of each " +
        "building type, prop, and device that you want available for map generation. " +
        "Returns what was found organized by category.")]
    public string scan_seed_level(
        AssetCatalog catalog,
        [Description("Path to the seed .umap level file containing template actors")] string seedLevelPath)
    {
        var result = catalog.ScanSeedLevel(seedLevelPath);

        return JsonSerializer.Serialize(new
        {
            result.Success,
            result.TotalEntries,
            result.EntriesByCategory,
            result.Errors,
            entries = catalog.Entries.Values.Select(e => new
            {
                e.ActorName,
                e.ClassName,
                category = e.Category.ToString(),
                e.Label,
                e.ChildComponentCount,
                e.Tags,
                location = e.Location.ToString(),
                footprint = $"{e.EstimatedFootprint:F0} units"
            }),
            nextStep = result.Success
                ? "Catalog loaded. Use generate_map_layout to create a map plan."
                : "Fix errors above and re-scan."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "STEP 2 for map generation: Generates a map layout plan based on biome, size, " +
        "and POI count. This is a PREVIEW — nothing is placed yet. " +
        "Returns the full plan with positions for buildings, props, devices, and scatter areas. " +
        "Available biomes: desert, forest, urban, snow. " +
        "Available sizes: small (2-3 POIs), medium (3-4 POIs), large (4-6 POIs).")]
    public string generate_map_layout(
        MapGenerator generator,
        AssetCatalog catalog,
        [Description("Biome name: 'desert', 'forest', 'urban', or 'snow'")] string biome,
        [Description("Map size: 'small', 'medium', or 'large'")] string size = "small",
        [Description("Number of POIs (auto-calculated from size if not specified)")] int? poiCount = null,
        [Description("Optional center X coordinate")] float centerX = 0,
        [Description("Optional center Y coordinate")] float centerY = 0,
        [Description("Optional center Z coordinate")] float centerZ = 0,
        [Description("Optional random seed for reproducible layouts")] int? seed = null)
    {
        if (!catalog.IsLoaded)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Asset catalog not loaded. Call scan_seed_level first."
            }, JsonOpts);
        }

        var center = new Vector3Info(centerX, centerY, centerZ);
        var layout = generator.GenerateLayout(biome, size, poiCount, center, seed);

        return JsonSerializer.Serialize(new
        {
            plan = new
            {
                layout.Name,
                layout.Biome,
                mapDimensions = $"{layout.MapWidth:F0} x {layout.MapLength:F0} UE units",
                center = layout.MapCenter.ToString(),
                layout.EstimatedActorCount,
                layout.Seed,
                layout.Description
            },
            pois = layout.Pois.Select(p => new
            {
                p.Name,
                p.TemplateName,
                center = p.Center.ToString(),
                p.Radius,
                buildingCount = p.Buildings.Count,
                propCount = p.Props.Count,
                deviceCount = p.Devices.Count,
                buildings = p.Buildings.Select(b => new
                {
                    template = b.CatalogId,
                    location = b.Location.ToString(),
                    rotation = b.Rotation.Y
                }),
                devices = p.Devices.Select(d => new
                {
                    d.Purpose,
                    template = d.CatalogId,
                    location = d.Location.ToString()
                })
            }),
            scatterAreas = layout.ScatterAreas.Select(s => new
            {
                s.Type,
                s.Count,
                area = $"{s.AreaWidth:F0} x {s.AreaLength:F0}",
                center = s.AreaCenter.ToString(),
                templateCount = s.CatalogIds.Count
            }),
            globalDevices = layout.GlobalDevices.Select(d => new
            {
                d.Purpose,
                location = d.Location.ToString()
            }),
            nextStep = "Review the plan above. Call apply_map_layout to place all actors, " +
                       "or call generate_map_layout again with different parameters to regenerate."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "STEP 3 for map generation: Applies a generated layout to a target level file. " +
        "Clones all planned actors from the seed level into the target level. " +
        "Creates a backup before any changes. This is the step that actually modifies files. " +
        "Changes are staged for review — approve in the WellVersed app before they're applied to project files.")]
    public string apply_map_layout(
        MapGenerator generator,
        AssetCatalog catalog,
        [Description("Path to the target .umap level to place actors in")] string targetLevelPath,
        [Description("Biome name (must match previous generate_map_layout call)")] string biome,
        [Description("Map size")] string size = "small",
        [Description("Number of POIs")] int? poiCount = null,
        [Description("Center X")] float centerX = 0,
        [Description("Center Y")] float centerY = 0,
        [Description("Center Z")] float centerZ = 0,
        [Description("Random seed (use the seed from generate_map_layout for same layout)")] int? seed = null)
    {
        if (!catalog.IsLoaded)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Asset catalog not loaded. Call scan_seed_level first."
            }, JsonOpts);
        }

        // Regenerate the same layout using the seed
        var center = new Vector3Info(centerX, centerY, centerZ);
        var layout = generator.GenerateLayout(biome, size, poiCount, center, seed);

        // Apply it
        var result = generator.ApplyLayout(layout, targetLevelPath);

        return JsonSerializer.Serialize(new
        {
            result.Success,
            result.Message,
            result.BackupPath,
            result.ActorsPlaced,
            result.BuildingsPlaced,
            result.PropsPlaced,
            result.FoliagePlaced,
            result.DevicesPlaced,
            errors = result.Errors,
            warnings = result.Warnings
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Lists available built-in biome presets with their configurations.")]
    public string list_biomes()
    {
        var biomes = BiomeConfig.BuiltInBiomes;
        return JsonSerializer.Serialize(new
        {
            count = biomes.Count,
            biomes = biomes.Select(b => new
            {
                name = b.Key,
                b.Value.Description,
                b.Value.PropDensity,
                b.Value.FoliageDensity,
                b.Value.BuildingsPerPoi,
                b.Value.BuildingSpacing,
                preferredTags = b.Value.PreferredTags,
                poiTemplates = b.Value.PoiTemplates.Select(t => t.Name)
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Lists available map size presets. Use these names in generate_map_layout.")]
    public string list_map_sizes()
    {
        return JsonSerializer.Serialize(new
        {
            sizes = new[]
            {
                new { name = "small", width = MapSize.Small.Width, length = MapSize.Small.Length,
                      recommendedPois = 2, description = "~1/4 Reload map. Good for 2v2 or FFA." },
                new { name = "medium", width = MapSize.Medium.Width, length = MapSize.Medium.Length,
                      recommendedPois = 3, description = "~1/2 Reload map. Good for 4v4." },
                new { name = "large", width = MapSize.Large.Width, length = MapSize.Large.Length,
                      recommendedPois = 5, description = "Full Reload map. Good for 8+ players." }
            },
            note = "These are estimates. Actual in-game feel depends on building density and cover placement. " +
                   "The 'small' size matches your request for '1/4 of a Reload Venture map'."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets the current asset catalog contents. Shows what templates are available " +
        "for map generation after scanning a seed level.")]
    public string get_catalog(
        AssetCatalog catalog,
        [Description("Optional category filter: 'Building', 'Prop', 'Terrain', 'Device', 'Foliage'")] string? category = null)
    {
        if (!catalog.IsLoaded)
            return "Catalog not loaded. Call scan_seed_level first.";

        IEnumerable<CatalogEntry> entries = catalog.Entries.Values;

        if (!string.IsNullOrEmpty(category) &&
            Enum.TryParse<ActorCategory>(category, true, out var cat))
        {
            entries = entries.Where(e => e.Category == cat);
        }

        return JsonSerializer.Serialize(new
        {
            seedLevel = catalog.SeedLevelPath,
            totalEntries = catalog.Entries.Count,
            filtered = entries.Select(e => new
            {
                e.ActorName,
                category = e.Category.ToString(),
                e.Label,
                e.ClassName,
                e.Tags,
                e.ChildComponentCount,
                footprint = $"{e.EstimatedFootprint:F0}"
            })
        }, JsonOpts);
    }
}
