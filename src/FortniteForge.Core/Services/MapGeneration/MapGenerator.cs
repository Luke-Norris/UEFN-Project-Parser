using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using Microsoft.Extensions.Logging;

namespace FortniteForge.Core.Services.MapGeneration;

/// <summary>
/// Orchestrates map generation from a high-level prompt to a fully placed level.
///
/// Flow:
///   1. User: "Generate a desert map, not too large, couple POIs"
///   2. GenerateLayout() → MapLayout (plan with positions, reviewed by Claude/user)
///   3. ApplyLayout() → places all actors via ActorPlacementService
///
/// The generator uses:
///   - AssetCatalog: knows what building/prop/device templates are available
///   - BiomeConfig: controls density, spacing, and style
///   - PoiTemplates: defines building arrangement patterns
///   - ActorPlacementService: actually clones actors into the level
/// </summary>
public class MapGenerator
{
    private readonly ForgeConfig _config;
    private readonly AssetCatalog _catalog;
    private readonly ActorPlacementService _placementService;
    private readonly BackupService _backupService;
    private readonly ILogger<MapGenerator> _logger;

    public MapGenerator(
        ForgeConfig config,
        AssetCatalog catalog,
        ActorPlacementService placementService,
        BackupService backupService,
        ILogger<MapGenerator> logger)
    {
        _config = config;
        _catalog = catalog;
        _placementService = placementService;
        _backupService = backupService;
        _logger = logger;
    }

    /// <summary>
    /// Generates a map layout plan from high-level parameters.
    /// This is the PLANNING step — nothing is written to disk.
    /// </summary>
    public MapLayout GenerateLayout(
        string biomeName,
        string mapSize = "small",
        int? poiCount = null,
        Vector3Info? centerOverride = null,
        int? seed = null)
    {
        var biome = BiomeConfig.GetBiome(biomeName);
        var (width, length) = MapSize.GetSize(mapSize);
        var actualPoiCount = poiCount ?? MapSize.RecommendedPoiCount(width, length);
        var rng = new Random(seed ?? Environment.TickCount);
        var center = centerOverride ?? new Vector3Info(0, 0, 0);

        var layout = new MapLayout
        {
            Name = $"{biome.Name}_{mapSize}_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            Biome = biome.Name,
            MapWidth = width,
            MapLength = length,
            MapCenter = center,
            Seed = seed ?? rng.Next()
        };

        // Step 1: Plan POI positions (spaced apart, within map bounds)
        var poiPositions = GeneratePoiPositions(center, width, length, actualPoiCount, rng);
        for (int i = 0; i < poiPositions.Count; i++)
        {
            var template = PickPoiTemplate(biome, rng);
            var poi = PlanPoi(
                $"POI_{i + 1}_{template.Name.Replace(" ", "")}",
                template, biome, poiPositions[i], rng);
            layout.Pois.Add(poi);
        }

        // Step 2: Plan scatter areas between POIs
        var scatterAreas = PlanScatterAreas(layout, biome, rng);
        layout.ScatterAreas.AddRange(scatterAreas);

        // Step 3: Plan global devices
        layout.GlobalDevices.AddRange(PlanGlobalDevices(layout, biome));

        // Step 4: Calculate estimates
        layout.EstimatedActorCount =
            layout.Pois.Sum(p => p.Buildings.Count + p.Props.Count + p.Devices.Count) +
            layout.ScatterAreas.Sum(s => s.Count) +
            layout.GlobalDevices.Count;

        layout.Description = $"{biome.Name} map ({mapSize}): " +
                             $"{width:F0}x{length:F0} units, " +
                             $"{layout.Pois.Count} POIs, " +
                             $"~{layout.EstimatedActorCount} actors total. " +
                             $"POIs: {string.Join(", ", layout.Pois.Select(p => p.Name))}";

        return layout;
    }

    /// <summary>
    /// Applies a layout to a target level file.
    /// This is the EXECUTION step — writes actors to disk.
    /// </summary>
    public MapGenerationResult ApplyLayout(MapLayout layout, string targetLevelPath)
    {
        var result = new MapGenerationResult();

        if (!_catalog.IsLoaded)
        {
            result.Success = false;
            result.Message = "Asset catalog not loaded. Scan a seed level first.";
            return result;
        }

        if (string.IsNullOrEmpty(_catalog.SeedLevelPath))
        {
            result.Success = false;
            result.Message = "No seed level set. Scan a seed level first.";
            return result;
        }

        try
        {
            // Backup target level
            if (_config.AutoBackup && File.Exists(targetLevelPath))
            {
                result.BackupPath = _backupService.CreateBackup(targetLevelPath);
            }

            // Place buildings in each POI
            foreach (var poi in layout.Pois)
            {
                foreach (var building in poi.Buildings)
                {
                    var placed = PlaceActor(targetLevelPath, building);
                    if (placed) result.BuildingsPlaced++;
                    else result.Warnings.Add($"Failed to place building {building.CatalogId} at {building.Location}");
                }

                foreach (var prop in poi.Props)
                {
                    var placed = PlaceActor(targetLevelPath, prop);
                    if (placed) result.PropsPlaced++;
                }

                foreach (var device in poi.Devices)
                {
                    var placed = PlaceDevice(targetLevelPath, device);
                    if (placed) result.DevicesPlaced++;
                }
            }

            // Place scatter areas (props and foliage between POIs)
            foreach (var scatter in layout.ScatterAreas)
            {
                var scatterResult = PlaceScatterArea(targetLevelPath, scatter);
                result.PropsPlaced += scatterResult.Placed;
                result.FoliagePlaced += scatterResult.FoliagePlaced;
                result.Errors.AddRange(scatterResult.Errors);
            }

            // Place global devices
            foreach (var device in layout.GlobalDevices)
            {
                var placed = PlaceDevice(targetLevelPath, device);
                if (placed) result.DevicesPlaced++;
            }

            result.ActorsPlaced = result.BuildingsPlaced + result.PropsPlaced +
                                  result.DevicesPlaced + result.FoliagePlaced;
            result.Success = true;
            result.Message = $"Map generated: {result.ActorsPlaced} actors placed " +
                             $"({result.BuildingsPlaced} buildings, {result.PropsPlaced} props, " +
                             $"{result.FoliagePlaced} foliage, {result.DevicesPlaced} devices). " +
                             $"Backup at: {result.BackupPath ?? "N/A"}";

            _logger.LogInformation("Map generation complete: {Message}", result.Message);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Map generation failed: {ex.Message}";
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Map generation failed");
        }

        return result;
    }

    // ========= Layout Planning =========

    private List<Vector3Info> GeneratePoiPositions(
        Vector3Info center, float mapWidth, float mapLength, int count, Random rng)
    {
        var positions = new List<Vector3Info>();
        var minPoiDistance = Math.Min(mapWidth, mapLength) * 0.3f; // POIs at least 30% of map size apart
        var margin = Math.Min(mapWidth, mapLength) * 0.15f; // Keep away from edges

        for (int i = 0; i < count; i++)
        {
            int attempts = 0;
            Vector3Info pos;

            do
            {
                // Generate position within map bounds (minus margin)
                var x = center.X + (float)(rng.NextDouble() * (mapWidth - 2 * margin) - (mapWidth - 2 * margin) / 2);
                var y = center.Y + (float)(rng.NextDouble() * (mapLength - 2 * margin) - (mapLength - 2 * margin) / 2);
                pos = new Vector3Info(x, y, center.Z);
                attempts++;
            }
            while (attempts < 50 && positions.Any(p => Distance2D(p, pos) < minPoiDistance));

            positions.Add(pos);
        }

        return positions;
    }

    private PoiPlacement PlanPoi(
        string name, PoiTemplate template, BiomeConfig biome,
        Vector3Info center, Random rng)
    {
        var poi = new PoiPlacement
        {
            Name = name,
            TemplateName = template.Name,
            Center = center,
            Radius = template.Radius
        };

        // Get available buildings and props from catalog
        var buildings = _catalog.GetByCategory(ActorCategory.Building);
        var props = _catalog.GetByCategory(ActorCategory.Prop);
        var foliage = _catalog.GetByCategory(ActorCategory.Foliage);

        // Filter by biome tags if available
        if (biome.PreferredTags.Count > 0)
        {
            var tagged = _catalog.GetByTags(biome.PreferredTags.ToArray());
            if (tagged.Any(t => t.Category == ActorCategory.Building))
                buildings = tagged.Where(t => t.Category == ActorCategory.Building).ToList();
            if (tagged.Any(t => t.Category == ActorCategory.Prop))
                props = tagged.Where(t => t.Category == ActorCategory.Prop).ToList();
        }

        // Plan building positions based on template pattern
        var buildingCount = Math.Min(template.BuildingCount, buildings.Count);
        var buildingPositions = GeneratePatternPositions(
            center, template.Radius * 0.6f, buildingCount,
            template.BuildingPattern, biome.BuildingSpacing, rng);

        for (int i = 0; i < buildingPositions.Count && buildings.Count > 0; i++)
        {
            var building = buildings[rng.Next(buildings.Count)];
            poi.Buildings.Add(new ActorPlacement
            {
                CatalogId = building.ActorName,
                Location = buildingPositions[i],
                Rotation = new Vector3Info(0, rng.Next(0, 4) * 90f, 0), // 90° snapped rotation
                Scale = new Vector3Info(1, 1, 1)
            });
        }

        // Plan props near buildings
        if (props.Count > 0)
        {
            var propsToPlace = template.PropsPerBuilding * buildingCount;
            for (int i = 0; i < propsToPlace; i++)
            {
                var prop = props[rng.Next(props.Count)];
                var propPos = GetPropPosition(template.PropDistribution, center,
                    template.Radius, buildingPositions, biome.PropSpacing, rng);

                poi.Props.Add(new ActorPlacement
                {
                    CatalogId = prop.ActorName,
                    Location = propPos,
                    Rotation = new Vector3Info(0, (float)(rng.NextDouble() * 360), 0),
                    Scale = RandomScale(biome.ScaleMin, biome.ScaleMax, rng)
                });
            }
        }

        // Plan devices
        var devices = _catalog.GetByCategory(ActorCategory.Device);
        PlanPoiDevices(poi, devices, biome.Devices, center, template, rng);

        return poi;
    }

    private List<Vector3Info> GeneratePatternPositions(
        Vector3Info center, float radius, int count,
        LayoutPattern pattern, float spacing, Random rng)
    {
        return pattern switch
        {
            LayoutPattern.Grid => GenerateGridPositions(center, radius, count, spacing),
            LayoutPattern.Ring => GenerateRingPositions(center, radius, count),
            LayoutPattern.Cluster => GenerateClusterPositions(center, radius, count, spacing, rng),
            LayoutPattern.Scattered => GenerateScatteredPositions(center, radius, count, spacing, rng),
            _ => GenerateClusterPositions(center, radius, count, spacing, rng)
        };
    }

    private static List<Vector3Info> GenerateGridPositions(
        Vector3Info center, float radius, int count, float spacing)
    {
        var positions = new List<Vector3Info>();
        var cols = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling((double)count / cols);
        var startX = center.X - (cols - 1) * spacing / 2;
        var startY = center.Y - (rows - 1) * spacing / 2;

        for (int r = 0; r < rows && positions.Count < count; r++)
        {
            for (int c = 0; c < cols && positions.Count < count; c++)
            {
                positions.Add(new Vector3Info(startX + c * spacing, startY + r * spacing, center.Z));
            }
        }
        return positions;
    }

    private static List<Vector3Info> GenerateRingPositions(
        Vector3Info center, float radius, int count)
    {
        var positions = new List<Vector3Info>();
        for (int i = 0; i < count; i++)
        {
            var angle = (float)(2 * Math.PI * i / count);
            positions.Add(new Vector3Info(
                center.X + radius * MathF.Cos(angle),
                center.Y + radius * MathF.Sin(angle),
                center.Z));
        }
        return positions;
    }

    private static List<Vector3Info> GenerateClusterPositions(
        Vector3Info center, float radius, int count, float spacing, Random rng)
    {
        var positions = new List<Vector3Info>();
        for (int i = 0; i < count; i++)
        {
            int attempts = 0;
            Vector3Info pos;
            do
            {
                var r = (float)(rng.NextDouble() * radius * 0.7);
                var angle = (float)(rng.NextDouble() * 2 * Math.PI);
                pos = new Vector3Info(
                    center.X + r * MathF.Cos(angle),
                    center.Y + r * MathF.Sin(angle),
                    center.Z);
                attempts++;
            }
            while (attempts < 30 && positions.Any(p => Distance2D(p, pos) < spacing));

            positions.Add(pos);
        }
        return positions;
    }

    private static List<Vector3Info> GenerateScatteredPositions(
        Vector3Info center, float radius, int count, float spacing, Random rng)
    {
        var positions = new List<Vector3Info>();
        for (int i = 0; i < count; i++)
        {
            int attempts = 0;
            Vector3Info pos;
            do
            {
                pos = new Vector3Info(
                    center.X + (float)(rng.NextDouble() * radius * 2 - radius),
                    center.Y + (float)(rng.NextDouble() * radius * 2 - radius),
                    center.Z);
                attempts++;
            }
            while (attempts < 30 && positions.Any(p => Distance2D(p, pos) < spacing));

            positions.Add(pos);
        }
        return positions;
    }

    private static Vector3Info GetPropPosition(
        PropDistribution distribution, Vector3Info poiCenter,
        float poiRadius, List<Vector3Info> buildingPositions, float spacing, Random rng)
    {
        switch (distribution)
        {
            case PropDistribution.NearBuildings when buildingPositions.Count > 0:
                var nearBuilding = buildingPositions[rng.Next(buildingPositions.Count)];
                return new Vector3Info(
                    nearBuilding.X + (float)(rng.NextDouble() * spacing * 2 - spacing),
                    nearBuilding.Y + (float)(rng.NextDouble() * spacing * 2 - spacing),
                    nearBuilding.Z);

            case PropDistribution.Perimeter:
                var angle = (float)(rng.NextDouble() * 2 * Math.PI);
                var dist = poiRadius * (0.7f + (float)rng.NextDouble() * 0.3f);
                return new Vector3Info(
                    poiCenter.X + dist * MathF.Cos(angle),
                    poiCenter.Y + dist * MathF.Sin(angle),
                    poiCenter.Z);

            case PropDistribution.Center:
                return new Vector3Info(
                    poiCenter.X + (float)(rng.NextDouble() * poiRadius * 0.4 - poiRadius * 0.2),
                    poiCenter.Y + (float)(rng.NextDouble() * poiRadius * 0.4 - poiRadius * 0.2),
                    poiCenter.Z);

            default: // Even
                return new Vector3Info(
                    poiCenter.X + (float)(rng.NextDouble() * poiRadius * 2 - poiRadius),
                    poiCenter.Y + (float)(rng.NextDouble() * poiRadius * 2 - poiRadius),
                    poiCenter.Z);
        }
    }

    private void PlanPoiDevices(
        PoiPlacement poi, List<CatalogEntry> devices,
        DevicePlacementRules rules, Vector3Info center, PoiTemplate template, Random rng)
    {
        // Find device types in catalog
        var spawners = devices.Where(d =>
            d.ClassName.Contains("Spawner", StringComparison.OrdinalIgnoreCase) ||
            d.ClassName.Contains("PlayerStart", StringComparison.OrdinalIgnoreCase)).ToList();
        var chests = devices.Where(d =>
            d.Tags.Contains("loot") ||
            d.ClassName.Contains("Chest", StringComparison.OrdinalIgnoreCase)).ToList();

        // Place spawn points
        if (spawners.Count > 0)
        {
            for (int i = 0; i < rules.SpawnPointsPerPoi; i++)
            {
                var pos = template.SpawnPlacement switch
                {
                    SpawnPlacement.Edges => GetEdgePosition(center, template.Radius, rng),
                    SpawnPlacement.Center => new Vector3Info(
                        center.X + (float)(rng.NextDouble() * 400 - 200),
                        center.Y + (float)(rng.NextDouble() * 400 - 200),
                        center.Z),
                    _ => GetEdgePosition(center, template.Radius * 0.5f, rng)
                };

                poi.Devices.Add(new DevicePlacement
                {
                    CatalogId = spawners[rng.Next(spawners.Count)].ActorName,
                    Purpose = "player_spawn",
                    Location = pos
                });
            }
        }

        // Place chests near buildings
        if (chests.Count > 0 && poi.Buildings.Count > 0)
        {
            for (int i = 0; i < rules.ChestsPerPoi && i < poi.Buildings.Count; i++)
            {
                var nearBuilding = poi.Buildings[i % poi.Buildings.Count];
                poi.Devices.Add(new DevicePlacement
                {
                    CatalogId = chests[rng.Next(chests.Count)].ActorName,
                    Purpose = "chest",
                    Location = new Vector3Info(
                        nearBuilding.Location.X + (float)(rng.NextDouble() * 300 - 150),
                        nearBuilding.Location.Y + (float)(rng.NextDouble() * 300 - 150),
                        nearBuilding.Location.Z)
                });
            }
        }
    }

    private List<ScatterArea> PlanScatterAreas(MapLayout layout, BiomeConfig biome, Random rng)
    {
        var areas = new List<ScatterArea>();
        var props = _catalog.GetByCategory(ActorCategory.Prop);
        var foliage = _catalog.GetByCategory(ActorCategory.Foliage);

        if (props.Count == 0 && foliage.Count == 0)
            return areas;

        // Create scatter areas in the spaces between POIs
        var halfW = layout.MapWidth / 2;
        var halfL = layout.MapLength / 2;

        // Prop scatter across the map (avoiding POI centers)
        if (props.Count > 0)
        {
            var propCount = (int)(layout.MapWidth * layout.MapLength / 1_000_000 * biome.PropDensity * 10);
            areas.Add(new ScatterArea
            {
                Type = "props",
                CatalogIds = props.Select(p => p.ActorName).ToList(),
                AreaCenter = layout.MapCenter,
                AreaWidth = layout.MapWidth * 0.9f,
                AreaLength = layout.MapLength * 0.9f,
                Count = propCount,
                MinSpacing = biome.PropSpacing
            });
        }

        // Foliage scatter
        if (foliage.Count > 0)
        {
            var foliageCount = (int)(layout.MapWidth * layout.MapLength / 1_000_000 * biome.FoliageDensity * 15);
            areas.Add(new ScatterArea
            {
                Type = "foliage",
                CatalogIds = foliage.Select(f => f.ActorName).ToList(),
                AreaCenter = layout.MapCenter,
                AreaWidth = layout.MapWidth * 0.95f,
                AreaLength = layout.MapLength * 0.95f,
                Count = foliageCount,
                MinSpacing = biome.FoliageSpacing
            });
        }

        return areas;
    }

    private List<DevicePlacement> PlanGlobalDevices(MapLayout layout, BiomeConfig biome)
    {
        var devices = new List<DevicePlacement>();
        var catalogDevices = _catalog.GetByCategory(ActorCategory.Device);

        if (biome.Devices.PlaceMapBoundary)
        {
            var boundary = catalogDevices.FirstOrDefault(d =>
                d.ClassName.Contains("Barrier", StringComparison.OrdinalIgnoreCase) ||
                d.ClassName.Contains("Boundary", StringComparison.OrdinalIgnoreCase));

            if (boundary != null)
            {
                devices.Add(new DevicePlacement
                {
                    CatalogId = boundary.ActorName,
                    Purpose = "map_boundary",
                    Location = layout.MapCenter
                });
            }
        }

        return devices;
    }

    // ========= Placement Execution =========

    private bool PlaceActor(string levelPath, ActorPlacement placement)
    {
        if (_catalog.SeedLevelPath == null) return false;

        try
        {
            var result = _placementService.CloneActor(
                levelPath,
                placement.CatalogId,
                placement.Location,
                placement.Rotation,
                placement.Scale.X != 1 || placement.Scale.Y != 1 || placement.Scale.Z != 1
                    ? placement.Scale : null);

            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to place {Id}", placement.CatalogId);
            return false;
        }
    }

    private bool PlaceDevice(string levelPath, DevicePlacement device)
    {
        return PlaceActor(levelPath, new ActorPlacement
        {
            CatalogId = device.CatalogId,
            Location = device.Location,
            Rotation = device.Rotation
        });
    }

    private (int Placed, int FoliagePlaced, List<string> Errors) PlaceScatterArea(
        string levelPath, ScatterArea scatter)
    {
        int placed = 0;
        int foliagePlaced = 0;
        var errors = new List<string>();
        var rng = new Random();

        for (int i = 0; i < scatter.Count && scatter.CatalogIds.Count > 0; i++)
        {
            var catalogId = scatter.CatalogIds[rng.Next(scatter.CatalogIds.Count)];
            var x = scatter.AreaCenter.X + (float)(rng.NextDouble() * scatter.AreaWidth - scatter.AreaWidth / 2);
            var y = scatter.AreaCenter.Y + (float)(rng.NextDouble() * scatter.AreaLength - scatter.AreaLength / 2);

            try
            {
                var result = _placementService.CloneActor(
                    levelPath, catalogId,
                    new Vector3Info(x, y, scatter.AreaCenter.Z),
                    new Vector3Info(0, (float)(rng.NextDouble() * 360), 0));

                if (result.Success)
                {
                    if (scatter.Type == "foliage") foliagePlaced++;
                    else placed++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Scatter place failed: {ex.Message}");
            }
        }

        return (placed, foliagePlaced, errors);
    }

    // ========= Helpers =========

    private static PoiTemplate PickPoiTemplate(BiomeConfig biome, Random rng)
    {
        if (biome.PoiTemplates.Count > 0)
            return biome.PoiTemplates[rng.Next(biome.PoiTemplates.Count)];
        return PoiTemplate.SmallOutpost;
    }

    private static Vector3Info GetEdgePosition(Vector3Info center, float radius, Random rng)
    {
        var angle = (float)(rng.NextDouble() * 2 * Math.PI);
        return new Vector3Info(
            center.X + radius * MathF.Cos(angle),
            center.Y + radius * MathF.Sin(angle),
            center.Z);
    }

    private static Vector3Info RandomScale(float min, float max, Random rng)
    {
        var s = min + (float)rng.NextDouble() * (max - min);
        return new Vector3Info(s, s, s);
    }

    private static float Distance2D(Vector3Info a, Vector3Info b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
