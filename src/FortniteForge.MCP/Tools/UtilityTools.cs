using FortniteForge.Core.Config;
using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP utility tools — project health checks, JSON export for debugging,
/// digest file management, and config inspection.
/// </summary>
[McpServerToolType]
public class UtilityTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Checks project health — validates config, verifies paths exist, " +
        "checks digest files are loadable, and reports any issues. " +
        "Run this first when starting a session to make sure everything is configured.")]
    public string check_project_health(
        ForgeConfig config,
        DigestService digestService)
    {
        var issues = config.Validate();
        var checks = new List<object>();

        // Config validation
        checks.Add(new
        {
            check = "Configuration",
            status = issues.Count == 0 ? "PASS" : "FAIL",
            details = issues.Count == 0
                ? $"Config loaded. Project: {config.ProjectPath}"
                : string.Join("; ", issues)
        });

        // Content directory
        var contentExists = Directory.Exists(config.ContentPath);
        checks.Add(new
        {
            check = "Content Directory",
            status = contentExists ? "PASS" : "FAIL",
            details = contentExists
                ? $"Found at: {config.ContentPath}"
                : $"Missing: {config.ContentPath}"
        });

        // Digest files
        try
        {
            digestService.LoadDigests();
            var types = digestService.ListDeviceTypes();
            checks.Add(new
            {
                check = "Digest Files",
                status = types.Count > 0 ? "PASS" : "WARN",
                details = types.Count > 0
                    ? $"Loaded {types.Count} device schemas from digest files."
                    : "No device schemas found. Digest files may be missing."
            });
        }
        catch (Exception ex)
        {
            checks.Add(new
            {
                check = "Digest Files",
                status = "FAIL",
                details = $"Failed to load: {ex.Message}"
            });
        }

        // Asset count
        if (contentExists)
        {
            try
            {
                var uassetCount = Directory.EnumerateFiles(config.ContentPath, "*.uasset", SearchOption.AllDirectories).Count();
                var umapCount = Directory.EnumerateFiles(config.ContentPath, "*.umap", SearchOption.AllDirectories).Count();
                checks.Add(new
                {
                    check = "Project Assets",
                    status = "PASS",
                    details = $"{uassetCount} .uasset files, {umapCount} .umap files"
                });
            }
            catch (Exception ex)
            {
                checks.Add(new
                {
                    check = "Project Assets",
                    status = "WARN",
                    details = $"Could not scan: {ex.Message}"
                });
            }
        }

        // Backup directory
        var backupDir = Path.Combine(config.ProjectPath, config.BackupDirectory);
        var backupExists = Directory.Exists(backupDir);
        checks.Add(new
        {
            check = "Backup Directory",
            status = "PASS",
            details = backupExists
                ? $"Exists at: {backupDir}"
                : $"Will be created at: {backupDir} (on first backup)"
        });

        // Build configuration
        checks.Add(new
        {
            check = "Build Config",
            status = string.IsNullOrEmpty(config.Build.BuildCommand) ? "WARN" : "PASS",
            details = string.IsNullOrEmpty(config.Build.BuildCommand)
                ? "Build command not configured. Set build.buildCommand in forge.config.json for build integration."
                : $"Build command: {config.Build.BuildCommand}"
        });

        // Safety settings
        checks.Add(new
        {
            check = "Safety Settings",
            status = "PASS",
            details = $"DryRun: {config.RequireDryRun}, AutoBackup: {config.AutoBackup}, " +
                      $"ReadOnly folders: [{string.Join(", ", config.ReadOnlyFolders)}]"
        });

        var overallPass = checks.All(c => ((dynamic)c).status != "FAIL");

        return JsonSerializer.Serialize(new
        {
            healthy = overallPass,
            checks
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Exports an asset's full internal structure as JSON. " +
        "Extremely useful for understanding how UE serializes actors, components, and properties. " +
        "Use this to study the structure of an actor before cloning or modifying it.")]
    public string export_asset_json(
        [Description("Path to the .uasset or .umap file")] string assetPath,
        [Description("Only export a specific export by index (omit for all)")] int? exportIndex = null)
    {
        var asset = new UAsset(assetPath, EngineVersion.VER_UE5_4);

        if (exportIndex.HasValue)
        {
            if (exportIndex.Value < 0 || exportIndex.Value >= asset.Exports.Count)
                return $"Export index {exportIndex.Value} out of range (0-{asset.Exports.Count - 1}).";

            var export = asset.Exports[exportIndex.Value];
            return JsonSerializer.Serialize(new
            {
                index = exportIndex.Value,
                objectName = export.ObjectName?.ToString(),
                className = export.GetExportClassType()?.ToString(),
                outerIndex = export.OuterIndex.Index,
                classIndex = export.ClassIndex.Index,
                templateIndex = export.TemplateIndex.Index,
                objectFlags = export.ObjectFlags.ToString(),
                serialSize = export.SerialSize,
                data = export is UAssetAPI.ExportTypes.NormalExport ne
                    ? ne.Data.Select(p => new
                    {
                        name = p.Name?.ToString(),
                        type = p.PropertyType?.ToString() ?? p.GetType().Name,
                        value = p.ToString()
                    }).ToList()
                    : null
            }, JsonOpts);
        }

        // Full asset dump — summarized to avoid token explosion
        return JsonSerializer.Serialize(new
        {
            filePath = assetPath,
            engineVersion = asset.ObjectVersionUE5.ToString(),
            packageFlags = asset.PackageFlags,
            nameCount = asset.GetNameMapIndexList().Count,
            importCount = asset.Imports.Count,
            exportCount = asset.Exports.Count,
            imports = asset.Imports.Select((imp, i) => new
            {
                index = i,
                objectName = imp.ObjectName?.ToString(),
                className = imp.ClassName?.ToString(),
                outerIndex = imp.OuterIndex.Index
            }),
            exports = asset.Exports.Select((exp, i) => new
            {
                index = i,
                objectName = exp.ObjectName?.ToString(),
                className = exp.GetExportClassType()?.ToString(),
                outerIndex = exp.OuterIndex.Index,
                classIndex = exp.ClassIndex.Index,
                templateIndex = exp.TemplateIndex.Index,
                serialSize = exp.SerialSize,
                propertyCount = exp is UAssetAPI.ExportTypes.NormalExport ne2 ? ne2.Data.Count : 0
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets the current FortniteForge configuration. " +
        "Shows all configured paths, safety settings, and build config.")]
    public string get_config(ForgeConfig config)
    {
        return JsonSerializer.Serialize(new
        {
            config.ProjectPath,
            contentPath = config.ContentPath,
            config.UefnInstallPath,
            config.ModifiableFolders,
            config.ReadOnlyFolders,
            config.BackupDirectory,
            config.MaxBackupsPerFile,
            config.RequireDryRun,
            config.AutoBackup,
            config.VerseErrorForwardPath,
            build = new
            {
                config.Build.BuildCommand,
                config.Build.BuildArguments,
                config.Build.LogDirectory,
                config.Build.TimeoutSeconds
            }
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Reloads digest files (.digest) from the project. " +
        "Use this after UEFN updates to pick up new device definitions.")]
    public string reload_digests(DigestService digestService)
    {
        try
        {
            digestService.LoadDigests();
            var types = digestService.ListDeviceTypes();
            return JsonSerializer.Serialize(new
            {
                success = true,
                deviceTypesLoaded = types.Count,
                message = $"Loaded {types.Count} device schemas from digest files."
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, JsonOpts);
        }
    }
}
