using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using FortniteForge.Core.Safety;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Core.Services;

/// <summary>
/// Handles placing new actors in levels by cloning existing ones.
///
/// Strategy: Clone-and-Modify
///   1. Find a "template" actor already in the level (user places one manually)
///   2. Deep-clone its export + component exports
///   3. Modify transform (position, rotation, scale) on the clones
///   4. Register clones in LevelExport.Actors
///   5. Save
///
/// This avoids having to construct actors from scratch (fragile),
/// because all class refs, flags, templates, and dependencies are
/// copied from a known-good actor.
/// </summary>
public class ActorPlacementService
{
    private readonly ForgeConfig _config;
    private readonly AssetGuard _guard;
    private readonly BackupService _backupService;
    private readonly ILogger<ActorPlacementService> _logger;

    public ActorPlacementService(
        ForgeConfig config,
        AssetGuard guard,
        BackupService backupService,
        ILogger<ActorPlacementService> logger)
    {
        _config = config;
        _guard = guard;
        _backupService = backupService;
        _logger = logger;
    }

    /// <summary>
    /// Clones an existing actor in a level and places the clone at a new location.
    /// Returns a preview of what will happen.
    /// </summary>
    public PlacementPreview PreviewCloneActor(
        string levelPath,
        string sourceActorName,
        Vector3Info newLocation,
        Vector3Info? newRotation = null,
        Vector3Info? newScale = null)
    {
        var preview = new PlacementPreview { LevelPath = levelPath };

        // Safety check
        var safety = _guard.CanModify(levelPath);
        if (!safety.IsAllowed)
        {
            preview.IsBlocked = true;
            preview.BlockReason = string.Join("; ", safety.Reasons);
            return preview;
        }

        try
        {
            var asset = new UAsset(levelPath, EngineVersion.VER_FORTNITE_LATEST);

            // Find the source actor
            var (sourceExport, sourceIndex) = FindExportByName(asset, sourceActorName);
            if (sourceExport == null)
            {
                preview.IsBlocked = true;
                preview.BlockReason = $"Actor '{sourceActorName}' not found in level.";
                return preview;
            }

            // Find all child exports (components) owned by this actor
            var childExports = FindChildExports(asset, sourceIndex);

            preview.SourceActor = sourceActorName;
            preview.SourceClass = sourceExport.GetExportClassType()?.ToString() ?? "Unknown";
            preview.NewLocation = newLocation;
            preview.NewRotation = newRotation;
            preview.NewScale = newScale;
            preview.ExportsToClone = 1 + childExports.Count; // actor + components
            preview.Description = $"Clone '{sourceActorName}' ({preview.SourceClass}) " +
                                  $"with {childExports.Count} component(s) " +
                                  $"to location ({newLocation.X}, {newLocation.Y}, {newLocation.Z})";

            // Generate unique name
            preview.NewActorName = GenerateUniqueName(asset, sourceActorName);
        }
        catch (Exception ex)
        {
            preview.IsBlocked = true;
            preview.BlockReason = $"Error: {ex.Message}";
        }

        return preview;
    }

    /// <summary>
    /// Executes the clone operation. Call PreviewCloneActor first.
    /// </summary>
    public PlacementResult CloneActor(
        string levelPath,
        string sourceActorName,
        Vector3Info newLocation,
        Vector3Info? newRotation = null,
        Vector3Info? newScale = null)
    {
        var result = new PlacementResult { LevelPath = levelPath };

        // Safety check
        var safety = _guard.CanModify(levelPath);
        if (!safety.IsAllowed)
        {
            result.Success = false;
            result.Message = $"BLOCKED: {string.Join("; ", safety.Reasons)}";
            return result;
        }

        try
        {
            // Backup first
            if (_config.AutoBackup)
            {
                result.BackupPath = _backupService.CreateBackup(levelPath);
            }

            var asset = new UAsset(levelPath, EngineVersion.VER_FORTNITE_LATEST);

            // Find source actor and its children
            var (sourceExport, sourceIndex) = FindExportByName(asset, sourceActorName);
            if (sourceExport == null)
                throw new InvalidOperationException($"Actor '{sourceActorName}' not found.");

            var childExports = FindChildExports(asset, sourceIndex);

            // Generate unique names
            var newActorName = GenerateUniqueName(asset, sourceActorName);

            // Clone the actor export
            var clonedActor = CloneExport(asset, sourceExport);
            clonedActor.ObjectName = FName.FromString(asset, newActorName);

            // We'll add the actor first, then its components
            int newActorIndex = asset.Exports.Count;
            asset.Exports.Add(clonedActor);

            // Clone child exports (components) and fix their OuterIndex
            var componentIndexMap = new Dictionary<int, int>(); // old index -> new index
            foreach (var (childExport, childIndex) in childExports)
            {
                var clonedChild = CloneExport(asset, childExport);

                // Generate unique component name
                var origName = childExport.ObjectName?.ToString() ?? "Component";
                clonedChild.ObjectName = FName.FromString(asset, GenerateUniqueName(asset, origName));

                // Point component's Outer to the new actor
                clonedChild.OuterIndex = FPackageIndex.FromExport(newActorIndex);

                int newChildIndex = asset.Exports.Count;
                asset.Exports.Add(clonedChild);
                componentIndexMap[childIndex] = newChildIndex;
            }

            // Fix up the actor's property references to point to cloned components
            if (clonedActor is NormalExport normalActor)
            {
                FixupPropertyReferences(normalActor.Data, sourceIndex, newActorIndex, componentIndexMap, asset);
            }

            // Apply the new transform to the root component
            ApplyTransformToClone(asset, clonedActor, componentIndexMap, newLocation, newRotation, newScale);

            // Register in LevelExport.Actors
            var levelExport = FindLevelExport(asset);
            if (levelExport != null)
            {
                levelExport.Actors.Add(FPackageIndex.FromExport(newActorIndex));
            }
            else
            {
                _logger.LogWarning("Could not find LevelExport — actor created but not registered in level.");
            }

            // Save
            asset.Write(levelPath);

            result.Success = true;
            result.NewActorName = newActorName;
            result.ExportsCreated = 1 + componentIndexMap.Count;
            result.Message = $"Cloned '{sourceActorName}' as '{newActorName}' with " +
                             $"{componentIndexMap.Count} component(s) at " +
                             $"({newLocation.X}, {newLocation.Y}, {newLocation.Z})";

            _logger.LogInformation("Placed actor: {Result}", result.Message);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Failed: {ex.Message}";
            _logger.LogError(ex, "Actor cloning failed");
        }

        return result;
    }

    /// <summary>
    /// Scatter-places multiple clones of an actor across an area.
    /// Used for things like "generate a forest of 50 trees in a 5000x5000 area".
    /// </summary>
    public ScatterPlacementResult PreviewScatterPlace(
        string levelPath,
        string sourceActorName,
        Vector3Info centerLocation,
        float areaWidth,
        float areaLength,
        int count,
        float minSpacing = 100f,
        bool randomRotation = true,
        float scaleMin = 0.8f,
        float scaleMax = 1.2f)
    {
        var result = new ScatterPlacementResult
        {
            LevelPath = levelPath,
            SourceActor = sourceActorName,
            Center = centerLocation,
            Count = count
        };

        // Safety check
        var safety = _guard.CanModify(levelPath);
        if (!safety.IsAllowed)
        {
            result.IsBlocked = true;
            result.BlockReason = string.Join("; ", safety.Reasons);
            return result;
        }

        // Generate placement positions
        var random = new Random();
        var positions = new List<ScatterInstance>();

        for (int i = 0; i < count; i++)
        {
            int attempts = 0;
            Vector3Info pos;

            // Try to find a position that respects minimum spacing
            do
            {
                var offsetX = (float)(random.NextDouble() * areaWidth - areaWidth / 2);
                var offsetY = (float)(random.NextDouble() * areaLength - areaLength / 2);

                pos = new Vector3Info(
                    centerLocation.X + offsetX,
                    centerLocation.Y + offsetY,
                    centerLocation.Z
                );
                attempts++;
            }
            while (attempts < 20 && positions.Any(p =>
                Distance2D(p.Location, pos) < minSpacing));

            var rotation = randomRotation
                ? new Vector3Info(0, (float)(random.NextDouble() * 360), 0)
                : new Vector3Info();

            var scaleFactor = (float)(random.NextDouble() * (scaleMax - scaleMin) + scaleMin);
            var scale = new Vector3Info(scaleFactor, scaleFactor, scaleFactor);

            positions.Add(new ScatterInstance
            {
                Location = pos,
                Rotation = rotation,
                Scale = scale
            });
        }

        result.Instances = positions;
        result.Description = $"Scatter {count} copies of '{sourceActorName}' " +
                             $"across {areaWidth}x{areaLength} area " +
                             $"centered at ({centerLocation.X}, {centerLocation.Y}, {centerLocation.Z})";

        return result;
    }

    /// <summary>
    /// Executes a scatter placement (places all instances).
    /// </summary>
    public ScatterPlacementResult ApplyScatterPlace(
        string levelPath,
        string sourceActorName,
        List<ScatterInstance> instances)
    {
        var result = new ScatterPlacementResult
        {
            LevelPath = levelPath,
            SourceActor = sourceActorName,
            Instances = instances,
            Count = instances.Count
        };

        // Safety check
        var safety = _guard.CanModify(levelPath);
        if (!safety.IsAllowed)
        {
            result.IsBlocked = true;
            result.BlockReason = string.Join("; ", safety.Reasons);
            return result;
        }

        // Backup
        if (_config.AutoBackup)
        {
            result.BackupPath = _backupService.CreateBackup(levelPath);
        }

        int placed = 0;
        var errors = new List<string>();

        foreach (var instance in instances)
        {
            try
            {
                var cloneResult = CloneActor(
                    levelPath, sourceActorName,
                    instance.Location, instance.Rotation, instance.Scale);

                if (cloneResult.Success)
                {
                    placed++;
                    // Update source name for subsequent clones to avoid conflicts
                    // (the level file is re-loaded each time by CloneActor)
                }
                else
                {
                    errors.Add(cloneResult.Message);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        result.PlacedCount = placed;
        result.Errors = errors;
        result.Description = $"Placed {placed}/{instances.Count} instances. " +
                             (errors.Count > 0 ? $"{errors.Count} errors." : "No errors.");

        return result;
    }

    // ========= Internal Helpers =========

    private static (Export? Export, int Index) FindExportByName(UAsset asset, string name)
    {
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].ObjectName?.ToString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                return (asset.Exports[i], i);
        }
        return (null, -1);
    }

    private static List<(Export Export, int Index)> FindChildExports(UAsset asset, int parentIndex)
    {
        var children = new List<(Export, int)>();
        var parentRef = FPackageIndex.FromExport(parentIndex);

        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].OuterIndex == parentRef)
                children.Add((asset.Exports[i], i));
        }

        return children;
    }

    private static Export CloneExport(UAsset asset, Export source)
    {
        // Use MemberwiseClone via serialization round-trip for deep copy
        if (source is NormalExport normalSource)
        {
            var clone = new NormalExport(asset, normalSource.Extras ?? new byte[0]);
            clone.ObjectName = normalSource.ObjectName;
            clone.ClassIndex = normalSource.ClassIndex;
            clone.SuperIndex = normalSource.SuperIndex;
            clone.TemplateIndex = normalSource.TemplateIndex;
            clone.OuterIndex = normalSource.OuterIndex;
            clone.ObjectFlags = normalSource.ObjectFlags;
            clone.bForcedExport = normalSource.bForcedExport;
            clone.bNotForClient = normalSource.bNotForClient;
            clone.bNotForServer = normalSource.bNotForServer;
            clone.bNotAlwaysLoadedForEditorGame = normalSource.bNotAlwaysLoadedForEditorGame;
            clone.bIsAsset = normalSource.bIsAsset;
            clone.GeneratePublicHash = normalSource.GeneratePublicHash;

            // Deep copy property data
            clone.Data = ClonePropertyList(normalSource.Data, asset);

            // Copy dependency lists
            clone.SerializationBeforeSerializationDependencies =
                new List<FPackageIndex>(normalSource.SerializationBeforeSerializationDependencies);
            clone.CreateBeforeSerializationDependencies =
                new List<FPackageIndex>(normalSource.CreateBeforeSerializationDependencies);
            clone.SerializationBeforeCreateDependencies =
                new List<FPackageIndex>(normalSource.SerializationBeforeCreateDependencies);
            clone.CreateBeforeCreateDependencies =
                new List<FPackageIndex>(normalSource.CreateBeforeCreateDependencies);

            return clone;
        }

        if (source is LevelExport)
        {
            throw new InvalidOperationException("Cannot clone a LevelExport.");
        }

        // Fallback: basic export clone
        var basicClone = new NormalExport(asset, new byte[0]);
        basicClone.ObjectName = source.ObjectName;
        basicClone.ClassIndex = source.ClassIndex;
        basicClone.SuperIndex = source.SuperIndex;
        basicClone.TemplateIndex = source.TemplateIndex;
        basicClone.OuterIndex = source.OuterIndex;
        basicClone.ObjectFlags = source.ObjectFlags;
        return basicClone;
    }

    private static List<PropertyData> ClonePropertyList(List<PropertyData> source, UAsset asset)
    {
        var cloned = new List<PropertyData>(source.Count);
        foreach (var prop in source)
        {
            // PropertyData doesn't have a clean deep clone, so we copy by type
            cloned.Add(CloneProperty(prop, asset));
        }
        return cloned;
    }

    private static PropertyData CloneProperty(PropertyData source, UAsset asset)
    {
        // For most property types, the values are value types (int, float, bool)
        // or immutable (FName, FString), so a shallow copy is sufficient.
        // For container types (Struct, Array, Map), we need to recurse.

        switch (source)
        {
            case StructPropertyData structProp:
                var newStruct = new StructPropertyData(source.Name)
                {
                    StructType = structProp.StructType,
                    StructGuid = structProp.StructGuid,
                    SerializationControl = structProp.SerializationControl,
                    Value = ClonePropertyList(structProp.Value, asset)
                };
                return newStruct;

            case ArrayPropertyData arrayProp:
                var newArray = new ArrayPropertyData(source.Name)
                {
                    ArrayType = arrayProp.ArrayType
                };
                if (arrayProp.Value != null)
                {
                    newArray.Value = new PropertyData[arrayProp.Value.Length];
                    for (int i = 0; i < arrayProp.Value.Length; i++)
                        newArray.Value[i] = CloneProperty(arrayProp.Value[i], asset);
                }
                return newArray;

            case MapPropertyData mapProp:
                var newMap = new MapPropertyData(source.Name);
                if (mapProp.Value != null)
                {
                    foreach (var kvp in mapProp.Value)
                    {
                        newMap.Value[CloneProperty(kvp.Key, asset)] =
                            CloneProperty(kvp.Value, asset);
                    }
                }
                return newMap;

            default:
                // Value types and simple references — shallow copy is safe
                // PropertyData is not easily deep-cloned without serialization,
                // but for value types (Bool, Int, Float, Name, Object, Enum, etc.)
                // the values themselves are immutable or value-typed.
                return source;
        }
    }

    private static void FixupPropertyReferences(
        List<PropertyData> properties,
        int oldActorIndex,
        int newActorIndex,
        Dictionary<int, int> componentMap,
        UAsset asset)
    {
        foreach (var prop in properties)
        {
            if (prop is ObjectPropertyData objProp && objProp.Value.IsExport())
            {
                int oldRef = objProp.Value.Index - 1; // FPackageIndex to array index
                if (componentMap.TryGetValue(oldRef, out var newRef))
                {
                    objProp.Value = FPackageIndex.FromExport(newRef);
                }
            }
            else if (prop is StructPropertyData structProp)
            {
                FixupPropertyReferences(structProp.Value, oldActorIndex, newActorIndex, componentMap, asset);
            }
            else if (prop is ArrayPropertyData arrayProp && arrayProp.Value != null)
            {
                FixupPropertyReferences(arrayProp.Value.ToList(), oldActorIndex, newActorIndex, componentMap, asset);
            }
        }
    }

    private static void ApplyTransformToClone(
        UAsset asset,
        Export actorExport,
        Dictionary<int, int> componentMap,
        Vector3Info newLocation,
        Vector3Info? newRotation,
        Vector3Info? newScale)
    {
        // The transform is usually on the RootComponent (a child component export).
        // Find which component is the RootComponent by checking actor properties.
        NormalExport? rootComponent = null;

        if (actorExport is NormalExport normalActor)
        {
            var rootCompProp = normalActor.Data.FirstOrDefault(p =>
                p.Name?.ToString() == "RootComponent" && p is ObjectPropertyData);

            if (rootCompProp is ObjectPropertyData rootRef && rootRef.Value.IsExport())
            {
                int compIndex = rootRef.Value.Index - 1;
                if (compIndex >= 0 && compIndex < asset.Exports.Count)
                    rootComponent = asset.Exports[compIndex] as NormalExport;
            }

            // Fallback: check any component in the map
            if (rootComponent == null)
            {
                foreach (var newIdx in componentMap.Values)
                {
                    if (asset.Exports[newIdx] is NormalExport ne)
                    {
                        rootComponent = ne;
                        break;
                    }
                }
            }
        }

        if (rootComponent == null)
        {
            // Apply directly to actor if no component found
            if (actorExport is NormalExport na)
                SetTransformProperties(na.Data, asset, newLocation, newRotation, newScale);
            return;
        }

        SetTransformProperties(rootComponent.Data, asset, newLocation, newRotation, newScale);
    }

    private static void SetTransformProperties(
        List<PropertyData> properties,
        UAsset asset,
        Vector3Info location,
        Vector3Info? rotation,
        Vector3Info? scale)
    {
        SetOrCreateVectorProperty(properties, asset, "RelativeLocation", location);

        if (rotation != null)
            SetOrCreateVectorProperty(properties, asset, "RelativeRotation", rotation);

        if (scale != null)
            SetOrCreateVectorProperty(properties, asset, "RelativeScale3D", scale);
    }

    private static void SetOrCreateVectorProperty(
        List<PropertyData> properties,
        UAsset asset,
        string propName,
        Vector3Info value)
    {
        var existing = properties.FirstOrDefault(p => p.Name?.ToString() == propName);

        if (existing is StructPropertyData existingStruct)
        {
            // Update existing vector struct
            foreach (var component in existingStruct.Value)
            {
                var name = component.Name?.ToString() ?? "";
                if (component is DoublePropertyData dp)
                {
                    switch (name)
                    {
                        case "X": dp.Value = value.X; break;
                        case "Y": dp.Value = value.Y; break;
                        case "Z": dp.Value = value.Z; break;
                    }
                }
                else if (component is FloatPropertyData fp)
                {
                    switch (name)
                    {
                        case "X": fp.Value = value.X; break;
                        case "Y": fp.Value = value.Y; break;
                        case "Z": fp.Value = value.Z; break;
                    }
                }
            }
        }
        else
        {
            // Create new vector property — UE5 uses doubles for transforms
            var newProp = new StructPropertyData(FName.FromString(asset, propName))
            {
                StructType = FName.FromString(asset, "Vector"),
                Value = new List<PropertyData>
                {
                    new DoublePropertyData(FName.FromString(asset, "X")) { Value = value.X },
                    new DoublePropertyData(FName.FromString(asset, "Y")) { Value = value.Y },
                    new DoublePropertyData(FName.FromString(asset, "Z")) { Value = value.Z }
                }
            };

            // Replace if name already exists (different type), or add
            var idx = properties.FindIndex(p => p.Name?.ToString() == propName);
            if (idx >= 0)
                properties[idx] = newProp;
            else
                properties.Add(newProp);
        }
    }

    private static string GenerateUniqueName(UAsset asset, string baseName)
    {
        // Strip trailing numbers: "StaticMeshActor_3" -> "StaticMeshActor"
        var baseNameClean = baseName;
        var underscoreIdx = baseName.LastIndexOf('_');
        if (underscoreIdx > 0 && int.TryParse(baseName[(underscoreIdx + 1)..], out _))
        {
            baseNameClean = baseName[..underscoreIdx];
        }

        var existingNames = new HashSet<string>(
            asset.Exports.Select(e => e.ObjectName?.ToString() ?? ""),
            StringComparer.OrdinalIgnoreCase);

        int suffix = 0;
        string candidate;
        do
        {
            candidate = $"{baseNameClean}_{suffix}";
            suffix++;
        }
        while (existingNames.Contains(candidate));

        return candidate;
    }

    private static LevelExport? FindLevelExport(UAsset asset)
    {
        return asset.Exports.OfType<LevelExport>().FirstOrDefault();
    }

    private static float Distance2D(Vector3Info a, Vector3Info b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

// ========= Placement Models =========

public class PlacementPreview
{
    public string LevelPath { get; set; } = "";
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }
    public string SourceActor { get; set; } = "";
    public string SourceClass { get; set; } = "";
    public string NewActorName { get; set; } = "";
    public Vector3Info NewLocation { get; set; } = new();
    public Vector3Info? NewRotation { get; set; }
    public Vector3Info? NewScale { get; set; }
    public int ExportsToClone { get; set; }
    public string Description { get; set; } = "";
}

public class PlacementResult
{
    public string LevelPath { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? NewActorName { get; set; }
    public int ExportsCreated { get; set; }
    public string? BackupPath { get; set; }
}

public class ScatterPlacementResult
{
    public string LevelPath { get; set; } = "";
    public string SourceActor { get; set; } = "";
    public Vector3Info Center { get; set; } = new();
    public int Count { get; set; }
    public int PlacedCount { get; set; }
    public List<ScatterInstance> Instances { get; set; } = new();
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }
    public string? BackupPath { get; set; }
    public string Description { get; set; } = "";
    public List<string> Errors { get; set; } = new();
}

public class ScatterInstance
{
    public Vector3Info Location { get; set; } = new();
    public Vector3Info Rotation { get; set; } = new();
    public Vector3Info Scale { get; set; } = new(1, 1, 1);
}
