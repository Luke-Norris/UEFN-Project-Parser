using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Safety;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;

namespace WellVersed.Core.Services;

/// <summary>
/// Imports extracted multi-device systems into target levels.
///
/// Takes an ExtractedSystem (from SystemExtractor) and recreates it in a
/// different project/level using the clone-and-modify pattern:
///   1. Find a matching template actor in the target level for each device class
///   2. Clone the template via ActorPlacementService at the offset position
///   3. Apply extracted property overrides via ModificationService
///   4. Wire devices together via ModificationService
///
/// This enables cross-map system copying — extract a capture point from one
/// project and import it into another with a single operation.
/// </summary>
public class SystemImporter
{
    private readonly WellVersedConfig _config;
    private readonly ActorPlacementService _placementService;
    private readonly ModificationService _modificationService;
    private readonly SystemExtractor _systemExtractor;
    private readonly AssetValidator _validator;
    private readonly DeviceService _deviceService;
    private readonly AssetGuard _guard;
    private readonly SafeFileAccess _fileAccess;
    private readonly BackupService _backupService;
    private readonly ILogger<SystemImporter> _logger;

    private static readonly JsonSerializerOptions RecipeJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SystemImporter(
        WellVersedConfig config,
        ActorPlacementService placementService,
        ModificationService modificationService,
        SystemExtractor systemExtractor,
        AssetValidator validator,
        DeviceService deviceService,
        AssetGuard guard,
        SafeFileAccess fileAccess,
        BackupService backupService,
        ILogger<SystemImporter> logger)
    {
        _config = config;
        _placementService = placementService;
        _modificationService = modificationService;
        _systemExtractor = systemExtractor;
        _validator = validator;
        _deviceService = deviceService;
        _guard = guard;
        _fileAccess = fileAccess;
        _backupService = backupService;
        _logger = logger;
    }

    /// <summary>
    /// Previews what will happen when importing a system into a target level.
    /// Does not modify any files. Checks for missing template actors.
    /// </summary>
    /// <param name="system">The extracted system to import.</param>
    /// <param name="targetLevelPath">Path to the target .umap level file.</param>
    /// <param name="centerPosition">World position to center the imported system at.</param>
    public ImportPreview PreviewImport(
        ExtractedSystem system,
        string targetLevelPath,
        Vector3Info centerPosition)
    {
        var preview = new ImportPreview
        {
            SystemName = system.Name,
            SourceLevel = system.SourceLevel ?? "(unknown)",
            TargetLevel = targetLevelPath,
            DeviceCount = system.Devices.Count,
            WiringCount = system.Wiring.Count,
            HasVerse = false // Verse detection from system properties
        };

        // Safety check — can we modify the target level?
        var safety = _guard.CanModify(targetLevelPath, _fileAccess);
        if (!safety.IsAllowed)
        {
            preview.CanImport = false;
            preview.Warnings.Add($"Target level is not modifiable: {string.Join("; ", safety.Reasons)}");
            return preview;
        }

        // Scan target level for existing device actors to use as templates
        List<DeviceInfo> targetDevices;
        try
        {
            targetDevices = _deviceService.ListDevicesInLevel(targetLevelPath);
        }
        catch (Exception ex)
        {
            preview.CanImport = false;
            preview.Warnings.Add($"Failed to scan target level: {ex.Message}");
            return preview;
        }

        // Build a map: device class -> template actor name in target level
        var templateMap = BuildTemplateMap(system.Devices, targetDevices);

        // Check for device classes with no matching template
        foreach (var device in system.Devices)
        {
            if (!templateMap.ContainsKey(device.DeviceClass))
            {
                if (!preview.MissingTemplates.Contains(device.DeviceClass))
                    preview.MissingTemplates.Add(device.DeviceClass);
            }
        }

        // Build device preview entries
        foreach (var device in system.Devices)
        {
            var placementPos = new Vector3Info(
                centerPosition.X + device.Offset.X,
                centerPosition.Y + device.Offset.Y,
                centerPosition.Z + device.Offset.Z);

            var templateActor = templateMap.TryGetValue(device.DeviceClass, out var tmpl)
                ? tmpl
                : "(no template found)";

            preview.Devices.Add(new ImportDevicePreview
            {
                DeviceClass = device.DeviceClass,
                Role = device.Role,
                TemplateActor = templateActor,
                PlacementPosition = placementPos,
                PropertyOverrideCount = device.Properties.Count
            });
        }

        preview.CanImport = preview.MissingTemplates.Count == 0;

        if (preview.MissingTemplates.Count > 0)
        {
            preview.Warnings.Add(
                "Place one instance of each missing device class in the target level " +
                "(in UEFN) to use as a template for cloning. Missing: " +
                string.Join(", ", preview.MissingTemplates));
        }

        if (system.Wiring.Count > 0)
        {
            preview.Warnings.Add(
                $"{system.Wiring.Count} wiring connection(s) will be recreated between placed devices. " +
                "Verify wiring in UEFN after import.");
        }

        _logger.LogInformation(
            "Import preview: {System} -> {Target}, {Devices} devices, {Missing} missing templates",
            system.Name, Path.GetFileName(targetLevelPath),
            system.Devices.Count, preview.MissingTemplates.Count);

        return preview;
    }

    /// <summary>
    /// Imports an extracted system into a target level.
    /// Places devices, applies property overrides, and creates wiring.
    /// </summary>
    /// <param name="system">The extracted system to import.</param>
    /// <param name="targetLevelPath">Path to the target .umap level file.</param>
    /// <param name="centerPosition">World position to center the imported system at.</param>
    public ImportResult ImportSystem(
        ExtractedSystem system,
        string targetLevelPath,
        Vector3Info centerPosition)
    {
        var result = new ImportResult();

        // Safety check
        var safety = _guard.CanModify(targetLevelPath, _fileAccess);
        if (!safety.IsAllowed)
        {
            result.Success = false;
            result.Message = $"BLOCKED: {string.Join("; ", safety.Reasons)}";
            return result;
        }

        // Scan target level for templates
        List<DeviceInfo> targetDevices;
        try
        {
            targetDevices = _deviceService.ListDevicesInLevel(targetLevelPath);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Failed to scan target level: {ex.Message}";
            result.Errors.Add(ex.Message);
            return result;
        }

        var templateMap = BuildTemplateMap(system.Devices, targetDevices);

        // Check for missing templates
        var missingClasses = system.Devices
            .Select(d => d.DeviceClass)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(dc => !templateMap.ContainsKey(dc))
            .ToList();

        if (missingClasses.Count > 0)
        {
            result.Success = false;
            result.Message = "Cannot import: missing template actors for device classes. " +
                             "Place one instance of each in the target level first.";
            result.Errors.Add($"Missing templates: {string.Join(", ", missingClasses)}");
            return result;
        }

        // Create backup before modifying
        if (_config.AutoBackup && safety.OperationMode == OperationMode.Direct)
        {
            try
            {
                result.BackupPath = _backupService.CreateBackup(targetLevelPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create backup for {Path}", targetLevelPath);
            }
        }

        // Phase 1: Place all devices via clone-and-modify
        // Maps original actor name -> new actor name (for wiring fixup)
        var actorNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in system.Devices)
        {
            var templateActor = templateMap[device.DeviceClass];
            var placementPos = new Vector3Info(
                centerPosition.X + device.Offset.X,
                centerPosition.Y + device.Offset.Y,
                centerPosition.Z + device.Offset.Z);

            try
            {
                var cloneResult = _placementService.CloneActor(
                    targetLevelPath,
                    templateActor,
                    placementPos,
                    device.Rotation,
                    device.Scale.X != 1 || device.Scale.Y != 1 || device.Scale.Z != 1
                        ? device.Scale
                        : null);

                if (cloneResult.Success && cloneResult.NewActorName != null)
                {
                    actorNameMap[device.ActorName] = cloneResult.NewActorName;
                    result.CreatedActors.Add(cloneResult.NewActorName);
                    result.DevicesPlaced++;

                    _logger.LogInformation(
                        "Placed {Role} ({Class}) as '{NewName}' at {Pos}",
                        device.Role, device.DeviceClass,
                        cloneResult.NewActorName, placementPos);
                }
                else
                {
                    result.Errors.Add(
                        $"Failed to place {device.Role} ({device.DeviceClass}): {cloneResult.Message}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(
                    $"Error placing {device.Role} ({device.DeviceClass}): {ex.Message}");
                _logger.LogError(ex, "Failed to clone device {Class}", device.DeviceClass);
            }
        }

        // Phase 2: Apply property overrides to placed devices
        foreach (var device in system.Devices)
        {
            if (!actorNameMap.TryGetValue(device.ActorName, out var newActorName))
                continue; // Device wasn't placed successfully

            foreach (var prop in device.Properties)
            {
                try
                {
                    var modRequest = new ModificationRequest
                    {
                        Type = ModificationType.SetProperty,
                        AssetPath = targetLevelPath,
                        TargetObject = newActorName,
                        PropertyName = prop.Name,
                        NewValue = prop.Value
                    };

                    var preview = _modificationService.PreviewModification(modRequest);
                    if (preview.IsSafe)
                    {
                        var modResult = _modificationService.ApplyModification(preview.RequestId);
                        if (!modResult.Success)
                        {
                            _logger.LogWarning(
                                "Property override failed: {Actor}.{Prop} = {Value}: {Error}",
                                newActorName, prop.Name, prop.Value, modResult.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Property override failures are non-fatal — log and continue
                    _logger.LogWarning(ex,
                        "Failed to set property {Prop} on {Actor}",
                        prop.Name, newActorName);
                }
            }
        }

        // Phase 3: Recreate wiring between placed devices
        foreach (var wire in system.Wiring)
        {
            // Resolve source and target actor names to their new names in the target level
            if (!actorNameMap.TryGetValue(wire.SourceActor, out var newSourceActor))
            {
                result.Errors.Add(
                    $"Wiring skipped: source actor '{wire.SourceActor}' was not placed successfully.");
                continue;
            }

            if (!actorNameMap.TryGetValue(wire.TargetActor, out var newTargetActor))
            {
                result.Errors.Add(
                    $"Wiring skipped: target actor '{wire.TargetActor}' was not placed successfully.");
                continue;
            }

            try
            {
                var wireRequest = new ModificationRequest
                {
                    Type = ModificationType.WireDevices,
                    AssetPath = targetLevelPath,
                    SourceDevice = newSourceActor,
                    OutputEvent = wire.OutputEvent,
                    TargetDevice = newTargetActor,
                    InputAction = wire.InputAction
                };

                var preview = _modificationService.PreviewModification(wireRequest);
                if (preview.IsSafe)
                {
                    var wireResult = _modificationService.ApplyModification(preview.RequestId);
                    if (wireResult.Success)
                    {
                        result.WiresCreated++;
                    }
                    else
                    {
                        result.Errors.Add(
                            $"Wiring failed: {newSourceActor}.{wire.OutputEvent} -> " +
                            $"{newTargetActor}.{wire.InputAction}: {wireResult.Message}");
                    }
                }
                else
                {
                    result.Errors.Add(
                        $"Wiring blocked: {string.Join("; ", preview.BlockReasons)}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(
                    $"Wiring error: {newSourceActor} -> {newTargetActor}: {ex.Message}");
                _logger.LogError(ex, "Failed to wire devices");
            }
        }

        // Generate verse stub if the system had verse-scripted devices
        var verseDevices = system.Devices.Where(d =>
            d.Properties.Any(p =>
                p.Name.Contains("Verse", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("Script", StringComparison.OrdinalIgnoreCase)));

        if (verseDevices.Any())
        {
            try
            {
                result.VerseFilePath = GenerateVerseStub(system, targetLevelPath, actorNameMap);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Verse generation failed: {ex.Message}");
                _logger.LogWarning(ex, "Failed to generate verse stub for imported system");
            }
        }

        result.Success = result.DevicesPlaced > 0;
        result.Message = result.Success
            ? $"Imported '{system.Name}': {result.DevicesPlaced}/{system.Devices.Count} devices placed, " +
              $"{result.WiresCreated}/{system.Wiring.Count} wires created" +
              (result.Errors.Count > 0 ? $" ({result.Errors.Count} non-fatal errors)" : "")
            : $"Import failed: no devices could be placed. {string.Join("; ", result.Errors)}";

        _logger.LogInformation("System import complete: {Message}", result.Message);
        return result;
    }

    /// <summary>
    /// Serializes an extracted system as a shareable JSON recipe.
    /// The recipe is self-contained and can be imported into any project.
    /// </summary>
    public string ExportSystemToJson(ExtractedSystem system)
    {
        var recipe = new SystemRecipe
        {
            FormatVersion = 1,
            Name = system.Name,
            Category = system.Category,
            SourceLevel = system.SourceLevel,
            DetectionMethod = system.DetectionMethod,
            Confidence = system.Confidence,
            Devices = system.Devices.Select(d => new RecipeDevice
            {
                Role = d.Role,
                DeviceClass = d.DeviceClass,
                DeviceType = d.DeviceType,
                Label = d.Label,
                OriginalActorName = d.ActorName,
                Offset = d.Offset,
                Rotation = d.Rotation,
                Scale = d.Scale,
                Properties = d.Properties
            }).ToList(),
            Wiring = system.Wiring
        };

        return JsonSerializer.Serialize(recipe, RecipeJsonOpts);
    }

    /// <summary>
    /// Imports a system from a JSON recipe file into a target level.
    /// </summary>
    /// <param name="json">JSON content of the recipe file.</param>
    /// <param name="targetLevelPath">Path to the target .umap level file.</param>
    /// <param name="centerPosition">World position to center the imported system at.</param>
    public ImportResult ImportSystemFromJson(
        string json,
        string targetLevelPath,
        Vector3Info centerPosition)
    {
        SystemRecipe? recipe;
        try
        {
            recipe = JsonSerializer.Deserialize<SystemRecipe>(json, RecipeJsonOpts);
        }
        catch (Exception ex)
        {
            return new ImportResult
            {
                Success = false,
                Message = $"Failed to parse recipe JSON: {ex.Message}",
                Errors = { ex.Message }
            };
        }

        if (recipe == null)
        {
            return new ImportResult
            {
                Success = false,
                Message = "Recipe JSON deserialized to null.",
                Errors = { "Invalid recipe format." }
            };
        }

        // Convert recipe back to ExtractedSystem for import
        var system = new ExtractedSystem
        {
            Name = recipe.Name,
            Category = recipe.Category,
            SourceLevel = recipe.SourceLevel,
            DetectionMethod = recipe.DetectionMethod,
            Confidence = recipe.Confidence,
            DeviceCount = recipe.Devices.Count,
            Devices = recipe.Devices.Select(d => new ExtractedDevice
            {
                Role = d.Role,
                DeviceClass = d.DeviceClass,
                DeviceType = d.DeviceType,
                Label = d.Label,
                ActorName = d.OriginalActorName,
                Offset = d.Offset,
                Rotation = d.Rotation,
                Scale = d.Scale,
                Properties = d.Properties
            }).ToList(),
            Wiring = recipe.Wiring
        };

        return ImportSystem(system, targetLevelPath, centerPosition);
    }

    // ========= Internal Helpers =========

    /// <summary>
    /// Builds a map from device class -> template actor name in the target level.
    /// Picks the first matching actor for each device class.
    /// </summary>
    private Dictionary<string, string> BuildTemplateMap(
        List<ExtractedDevice> neededDevices,
        List<DeviceInfo> availableActors)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in neededDevices)
        {
            if (map.ContainsKey(device.DeviceClass))
                continue; // Already have a template for this class

            // Find a matching actor in the target level by device class
            var template = availableActors.FirstOrDefault(a =>
                a.DeviceClass.Equals(device.DeviceClass, StringComparison.OrdinalIgnoreCase));

            if (template != null)
            {
                map[device.DeviceClass] = template.ActorName;
            }
            else
            {
                // Try fuzzy match — strip common prefixes/suffixes and compare
                var normalizedNeeded = NormalizeClassName(device.DeviceClass);
                template = availableActors.FirstOrDefault(a =>
                    NormalizeClassName(a.DeviceClass)
                        .Equals(normalizedNeeded, StringComparison.OrdinalIgnoreCase));

                if (template != null)
                {
                    map[device.DeviceClass] = template.ActorName;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Normalizes a device class name for fuzzy matching.
    /// Strips BP_, PBWA_, B_ prefixes and _C suffix.
    /// </summary>
    private static string NormalizeClassName(string className)
    {
        var name = className;
        if (name.StartsWith("BP_", StringComparison.OrdinalIgnoreCase))
            name = name[3..];
        else if (name.StartsWith("PBWA_", StringComparison.OrdinalIgnoreCase))
            name = name[5..];
        else if (name.StartsWith("B_", StringComparison.OrdinalIgnoreCase))
            name = name[2..];

        if (name.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
            name = name[..^2];

        return name;
    }

    /// <summary>
    /// Generates a verse stub file describing the imported system's wiring
    /// and device references. This gives the creator a starting point for
    /// adding verse logic to the imported system.
    /// </summary>
    private string? GenerateVerseStub(
        ExtractedSystem system,
        string targetLevelPath,
        Dictionary<string, string> actorNameMap)
    {
        var projectRoot = FindProjectRoot(targetLevelPath);
        if (projectRoot == null)
        {
            _logger.LogWarning("Could not find project root for verse stub generation");
            return null;
        }

        // Determine verse output path
        var safeName = system.Name
            .Replace(" ", "_")
            .Replace("+", "And")
            .Replace("(", "")
            .Replace(")", "");
        var versePath = Path.Combine(projectRoot, "Content", $"imported_{safeName}.verse");

        // Generate a simple verse stub with device references
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Imported System: {system.Name}");
        sb.AppendLine($"# Category: {system.Category}");
        sb.AppendLine($"# Source: {system.SourceLevel ?? "unknown"}");
        sb.AppendLine($"# Devices: {system.Devices.Count}, Wiring: {system.Wiring.Count}");
        sb.AppendLine();
        sb.AppendLine("using { /Fortnite.com/Devices }");
        sb.AppendLine("using { /Verse.org/Simulation }");
        sb.AppendLine();
        sb.AppendLine($"imported_{safeName}_controller := class(creative_device):");
        sb.AppendLine();
        sb.AppendLine("    # Device references (wire these up in UEFN)");

        foreach (var device in system.Devices)
        {
            var fieldName = $"{device.Role}_{device.DeviceType.Replace(" ", "")}"
                .ToLower()
                .Replace(" ", "_");
            sb.AppendLine($"    @editable {fieldName} : creative_device = creative_device{{}}");
        }

        sb.AppendLine();
        sb.AppendLine("    OnBegin<override>()<suspends>:void =");
        sb.AppendLine($"        # TODO: Add logic for {system.Name}");
        sb.AppendLine("        Print(\"System initialized\")");

        File.WriteAllText(versePath, sb.ToString());

        _logger.LogInformation("Generated verse stub at {Path}", versePath);
        return versePath;
    }

    /// <summary>
    /// Walks up from a level path to find the project root (directory with .uefnproject).
    /// </summary>
    private static string? FindProjectRoot(string levelPath)
    {
        var dir = Path.GetDirectoryName(levelPath);
        while (dir != null)
        {
            if (Directory.EnumerateFiles(dir, "*.uefnproject").Any())
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}

// ========= Recipe Serialization Models =========

/// <summary>
/// Self-contained recipe format for sharing extracted systems as JSON.
/// Includes all device configs, properties, wiring, and spatial layout.
/// </summary>
public class SystemRecipe
{
    /// <summary>
    /// Recipe format version for forward compatibility.
    /// </summary>
    public int FormatVersion { get; set; } = 1;

    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? SourceLevel { get; set; }
    public string DetectionMethod { get; set; } = "";
    public float Confidence { get; set; }
    public List<RecipeDevice> Devices { get; set; } = new();
    public List<ExtractedWiring> Wiring { get; set; } = new();
}

/// <summary>
/// Device entry within a recipe, including the original actor name
/// for wiring reference resolution during import.
/// </summary>
public class RecipeDevice
{
    public string Role { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string Label { get; set; } = "";
    public string OriginalActorName { get; set; } = "";
    public Vector3Info Offset { get; set; } = new();
    public Vector3Info Rotation { get; set; } = new();
    public Vector3Info Scale { get; set; } = new(1, 1, 1);
    public List<ExtractedProperty> Properties { get; set; } = new();
}
