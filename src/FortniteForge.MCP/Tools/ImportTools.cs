using WellVersed.Core.Models;
using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for cross-map system import.
/// Extract device systems from one project/level and import them into another.
/// Uses the clone-and-modify pattern — requires template actors in the target level.
/// </summary>
[McpServerToolType]
public class ImportTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Preview importing a multi-device system from one level into another. " +
        "Extracts the system from the source level, then checks if the target level " +
        "has matching template actors for each device class. " +
        "Returns what will be created WITHOUT modifying anything.")]
    public string preview_system_import(
        SystemExtractor extractor,
        SystemImporter importer,
        [Description("Path to the source .umap level containing the system to extract.")] string sourceLevelPath,
        [Description("Path to the target .umap level to import into.")] string targetLevelPath,
        [Description("Category filter (capture, spawning, economy, combat, gameplay, etc.). Omit for first system found.")] string? category,
        [Description("X coordinate to center the imported system at.")] float centerX,
        [Description("Y coordinate to center the imported system at.")] float centerY,
        [Description("Z coordinate to center the imported system at.")] float centerZ)
    {
        // Extract systems from source level
        var analysis = extractor.AnalyzeLevel(sourceLevelPath);

        if (analysis.Systems.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                error = "No systems found in source level.",
                totalDevices = analysis.TotalDevices,
                errors = analysis.Errors
            }, JsonOpts);
        }

        // Filter by category if specified
        var systems = analysis.Systems.AsEnumerable();
        if (!string.IsNullOrEmpty(category))
            systems = systems.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var system = systems.FirstOrDefault();
        if (system == null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"No system matching category '{category}' found.",
                availableCategories = analysis.Systems.Select(s => s.Category).Distinct()
            }, JsonOpts);
        }

        // Preview the import
        var preview = importer.PreviewImport(
            system, targetLevelPath,
            new Vector3Info(centerX, centerY, centerZ));

        return JsonSerializer.Serialize(new
        {
            preview.SystemName,
            preview.SourceLevel,
            preview.TargetLevel,
            preview.DeviceCount,
            preview.WiringCount,
            preview.HasVerse,
            preview.CanImport,
            missingTemplates = preview.MissingTemplates,
            devices = preview.Devices.Select(d => new
            {
                d.DeviceClass,
                d.Role,
                d.TemplateActor,
                position = $"({d.PlacementPosition.X:F0}, {d.PlacementPosition.Y:F0}, {d.PlacementPosition.Z:F0})",
                d.PropertyOverrideCount
            }),
            warnings = preview.Warnings,
            nextStep = preview.CanImport
                ? "Call import_system to execute the import."
                : "Place template actors for missing device classes in the target level first."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Import a multi-device system from one level into another. " +
        "Extracts the system from the source, clones template actors in the target, " +
        "applies property overrides, and recreates wiring. " +
        "Creates a backup before modifying. " +
        "The target level must have at least one instance of each device class as a template.")]
    public string import_system(
        SystemExtractor extractor,
        SystemImporter importer,
        [Description("Path to the source .umap level containing the system to extract.")] string sourceLevelPath,
        [Description("Path to the target .umap level to import into.")] string targetLevelPath,
        [Description("Category filter (capture, spawning, economy, combat, gameplay, etc.). Omit for first system found.")] string? category,
        [Description("X coordinate to center the imported system at.")] float centerX,
        [Description("Y coordinate to center the imported system at.")] float centerY,
        [Description("Z coordinate to center the imported system at.")] float centerZ)
    {
        // Extract systems from source level
        var analysis = extractor.AnalyzeLevel(sourceLevelPath);

        if (analysis.Systems.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "No systems found in source level.",
                totalDevices = analysis.TotalDevices
            }, JsonOpts);
        }

        // Filter by category if specified
        var systems = analysis.Systems.AsEnumerable();
        if (!string.IsNullOrEmpty(category))
            systems = systems.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var system = systems.FirstOrDefault();
        if (system == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"No system matching category '{category}' found.",
                availableCategories = analysis.Systems.Select(s => s.Category).Distinct()
            }, JsonOpts);
        }

        // Execute the import
        var result = importer.ImportSystem(
            system, targetLevelPath,
            new Vector3Info(centerX, centerY, centerZ));

        return JsonSerializer.Serialize(new
        {
            result.Success,
            result.Message,
            result.DevicesPlaced,
            result.WiresCreated,
            result.VerseFilePath,
            result.BackupPath,
            createdActors = result.CreatedActors,
            errors = result.Errors
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Export an extracted system as a shareable JSON recipe file. " +
        "The recipe is self-contained — includes all device configs, properties, " +
        "wiring, and spatial layout. Can be shared and imported into any project.")]
    public string export_system_recipe(
        SystemExtractor extractor,
        SystemImporter importer,
        [Description("Path to the .umap level containing the system to export.")] string levelPath,
        [Description("Category filter (capture, spawning, economy, combat, etc.). Omit for first system found.")] string? category,
        [Description("Path where the recipe JSON file will be saved.")] string outputPath)
    {
        // Extract systems from level
        var analysis = extractor.AnalyzeLevel(levelPath);

        if (analysis.Systems.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "No systems found in level.",
                totalDevices = analysis.TotalDevices
            }, JsonOpts);
        }

        // Filter by category if specified
        var systems = analysis.Systems.AsEnumerable();
        if (!string.IsNullOrEmpty(category))
            systems = systems.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var system = systems.FirstOrDefault();
        if (system == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"No system matching category '{category}' found.",
                availableCategories = analysis.Systems.Select(s => s.Category).Distinct()
            }, JsonOpts);
        }

        // Serialize to JSON recipe
        var recipeJson = importer.ExportSystemToJson(system);

        // Write to output file
        try
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            File.WriteAllText(outputPath, recipeJson);

            return JsonSerializer.Serialize(new
            {
                success = true,
                systemName = system.Name,
                category = system.Category,
                deviceCount = system.DeviceCount,
                wiringCount = system.Wiring.Count,
                outputPath,
                fileSizeBytes = new FileInfo(outputPath).Length,
                message = $"Recipe for '{system.Name}' saved. Share this file or use import_system_recipe to import it."
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Failed to write recipe file: {ex.Message}"
            }, JsonOpts);
        }
    }

    [McpServerTool, Description(
        "Import a system from a previously exported JSON recipe file. " +
        "The target level must have template actors for each device class in the recipe. " +
        "Creates a backup before modifying.")]
    public string import_system_recipe(
        SystemImporter importer,
        [Description("Path to the recipe JSON file to import.")] string recipePath,
        [Description("Path to the target .umap level to import into.")] string targetLevelPath,
        [Description("X coordinate to center the imported system at.")] float centerX,
        [Description("Y coordinate to center the imported system at.")] float centerY,
        [Description("Z coordinate to center the imported system at.")] float centerZ)
    {
        // Read recipe file
        string json;
        try
        {
            json = File.ReadAllText(recipePath);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Failed to read recipe file: {ex.Message}"
            }, JsonOpts);
        }

        // Import from recipe
        var result = importer.ImportSystemFromJson(
            json, targetLevelPath,
            new Vector3Info(centerX, centerY, centerZ));

        return JsonSerializer.Serialize(new
        {
            result.Success,
            result.Message,
            result.DevicesPlaced,
            result.WiresCreated,
            result.VerseFilePath,
            result.BackupPath,
            createdActors = result.CreatedActors,
            errors = result.Errors
        }, JsonOpts);
    }
}
