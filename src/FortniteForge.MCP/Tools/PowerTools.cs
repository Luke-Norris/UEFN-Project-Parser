using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// High-level MCP tools designed for Claude Code workflows.
/// These are the "do things" tools — configure devices, copy assets,
/// get project context for verse generation.
/// </summary>
[McpServerToolType]
public class PowerTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Get complete project context for the active UEFN project. Returns everything Claude needs " +
        "to make informed decisions: levels, device types with counts, verse files, asset definitions, " +
        "and configurable property names per device type. Use this at the START of any UEFN work session.")]
    public string get_project_context(
        ForgeConfig config,
        DeviceService deviceService,
        AssetService assetService,
        UefnDetector detector)
    {
        var status = detector.GetStatus();
        var levels = deviceService.FindLevels();
        var contentPath = config.ContentPath;

        // Collect verse files
        var verseFiles = new List<object>();
        if (Directory.Exists(contentPath))
        {
            foreach (var vf in Directory.EnumerateFiles(contentPath, "*.verse", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(vf);
                var lineCount = source.Split('\n').Length;
                verseFiles.Add(new
                {
                    Name = Path.GetFileNameWithoutExtension(vf),
                    Path = vf,
                    LineCount = lineCount,
                    Preview = source.Length > 500 ? source[..500] + "..." : source
                });
            }
        }

        // Collect user-created asset definitions
        var definitions = new List<object>();
        if (Directory.Exists(contentPath))
        {
            foreach (var f in Directory.EnumerateFiles(contentPath, "*.uasset", SearchOption.AllDirectories)
                .Where(f => !f.Contains("__External")).Take(50))
            {
                try
                {
                    var asset = new UAsset(f, EngineVersion.VER_UE5_4);
                    definitions.Add(new
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Class = asset.Exports.FirstOrDefault()?.GetExportClassType()?.ToString() ?? "Unknown",
                        RelativePath = Path.GetRelativePath(contentPath, f)
                    });
                }
                catch { }
            }
        }

        // Device types per level (sample from first level)
        var deviceSummary = new List<object>();
        if (levels.Count > 0)
        {
            try
            {
                var devices = deviceService.ListDevicesInLevel(levels[0]);
                var grouped = devices.GroupBy(d => d.DeviceType).OrderByDescending(g => g.Count());
                foreach (var g in grouped.Take(20))
                {
                    var sample = g.First();
                    deviceSummary.Add(new
                    {
                        Type = g.Key,
                        Count = g.Count(),
                        EditableProperties = sample.Properties.Select(p => p.Name).ToList()
                    });
                }
            }
            catch { }
        }

        return JsonSerializer.Serialize(new
        {
            project = new
            {
                Name = status.ProjectName,
                config.ProjectPath,
                config.ContentPath,
                config.IsUefnProject,
                status.Mode,
                status.IsUefnRunning,
                status.HasUrc
            },
            levels = levels.Select(l => new { Name = Path.GetFileNameWithoutExtension(l), Path = l }),
            deviceTypes = deviceSummary,
            verseFiles,
            definitions,
            summary = $"{status.ProjectName}: {levels.Count} level(s), {deviceSummary.Count} device types, {verseFiles.Count} verse files, {definitions.Count} asset definitions"
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Configure one or more properties on a device in the level. This is the primary way to " +
        "modify device settings. Finds the device by searching external actor files, sets properties, " +
        "and writes via the staged/direct safety system.\n\n" +
        "Example: configure_device(levelPath, \"VendingMachine\", '{\"CostOfFirstItem\": \"50\", \"First Item Resource Type\": \"GoldCurrency\"}')\n\n" +
        "Returns a diff of what changed. For Library projects, this will be blocked. " +
        "Changes are staged for review — approve in the WellVersed app before they're applied to project files.")]
    public string configure_device(
        ForgeConfig config,
        AssetGuard guard,
        SafeFileAccess fileAccess,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Device name or class to search for (partial match)")] string deviceSearch,
        [Description("JSON object of property names to new values, e.g. {\"CostOfFirstItem\": \"50\"}")] string propertyChanges)
    {
        // Parse property changes
        Dictionary<string, string> changes;
        try
        {
            changes = JsonSerializer.Deserialize<Dictionary<string, string>>(propertyChanges)
                      ?? throw new Exception("Empty changes");
        }
        catch (Exception ex)
        {
            return $"Error parsing property changes: {ex.Message}\nExpected JSON like: {{\"PropertyName\": \"NewValue\"}}";
        }

        // Find the level's external actors
        var contentDir = Path.GetDirectoryName(levelPath) ?? "";
        var levelName = Path.GetFileNameWithoutExtension(levelPath);
        var extActorsDir = Path.Combine(contentDir, "__ExternalActors__", levelName);

        if (!Directory.Exists(extActorsDir))
            return $"No external actors directory found for level {levelName}";

        // Search for matching device
        string? matchedFile = null;
        string? matchedExportName = null;
        string? matchedClassName = null;

        foreach (var file in Directory.EnumerateFiles(extActorsDir, "*.uasset", SearchOption.AllDirectories))
        {
            try
            {
                var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                foreach (var export in asset.Exports)
                {
                    var cls = export.GetExportClassType()?.ToString() ?? "";
                    var name = export.ObjectName?.ToString() ?? "";

                    if (cls.Contains(deviceSearch, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(deviceSearch, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedFile = file;
                        matchedExportName = name;
                        matchedClassName = cls;
                        break;
                    }
                }
                if (matchedFile != null) break;
            }
            catch { }
        }

        if (matchedFile == null)
            return $"No device matching '{deviceSearch}' found in level {levelName}";

        // Safety check
        var safety = guard.CanModify(matchedFile, fileAccess);
        if (!safety.IsAllowed)
            return $"BLOCKED: {string.Join("; ", safety.Reasons)}";

        // Open for write
        var (uasset, writePath) = fileAccess.OpenForWrite(matchedFile);

        // Find the export and apply changes
        var targetExport = uasset.Exports.OfType<NormalExport>()
            .FirstOrDefault(e => e.ObjectName?.ToString() == matchedExportName);

        if (targetExport == null)
            return $"Export '{matchedExportName}' not found in asset";

        var results = new List<string>();
        foreach (var (propName, newValue) in changes)
        {
            var prop = targetExport.Data.FirstOrDefault(p =>
                p.Name?.ToString()?.Contains(propName, StringComparison.OrdinalIgnoreCase) == true);

            if (prop == null)
            {
                // Search all exports for the property
                foreach (var exp in uasset.Exports.OfType<NormalExport>())
                {
                    prop = exp.Data.FirstOrDefault(p =>
                        p.Name?.ToString()?.Contains(propName, StringComparison.OrdinalIgnoreCase) == true);
                    if (prop != null) break;
                }
            }

            if (prop == null)
            {
                results.Add($"  SKIP: Property '{propName}' not found");
                continue;
            }

            var oldValue = prop.ToString() ?? "unknown";
            try
            {
                Core.Services.PropertyValueSetter.SetPropertyValue(uasset, prop, newValue);
                results.Add($"  SET: {prop.Name} = {newValue} (was: {oldValue})");
            }
            catch (Exception ex)
            {
                results.Add($"  ERROR: {propName}: {ex.Message}");
            }
        }

        uasset.Write(writePath);

        var modeLabel = writePath == matchedFile ? "DIRECT (with backup)" : "STAGED";
        var approvalNote = writePath != matchedFile
            ? "\n\nChanges are staged for review. Approve in WellVersed app to apply."
            : "";
        return $"Device: {matchedClassName} ({matchedExportName})\n" +
               $"Mode: {modeLabel}\n" +
               $"Write path: {writePath}\n" +
               $"Changes:\n{string.Join("\n", results)}" +
               approvalNote;
    }

    [McpServerTool, Description(
        "Copy a verse file from the library to the active project. " +
        "Use after finding a relevant file with search_library or list_verse_files. " +
        "Changes are staged for review — approve in the WellVersed app before they're applied to project files.")]
    public string copy_verse_to_project(
        ForgeConfig config,
        SafeFileAccess fileAccess,
        [Description("Full path to the source .verse file in the library")] string sourceVersePath,
        [Description("Optional: custom filename for the copied file (without .verse extension)")] string? newName = null)
    {
        if (!File.Exists(sourceVersePath))
            return $"Source file not found: {sourceVersePath}";

        if (!sourceVersePath.EndsWith(".verse", StringComparison.OrdinalIgnoreCase))
            return "Not a .verse file";

        if (config.ReadOnly)
            return "BLOCKED: Project is in read-only mode. Switch to a 'My Project' to copy files.";

        var contentPath = config.ContentPath;
        if (!Directory.Exists(contentPath))
            return $"Content directory not found: {contentPath}";

        var fileName = newName != null ? $"{newName}.verse" : Path.GetFileName(sourceVersePath);
        var targetPath = Path.Combine(contentPath, fileName);

        // Don't overwrite existing files
        if (File.Exists(targetPath))
            return $"File already exists at {targetPath}. Choose a different name with the newName parameter.";

        // Stage the copy instead of writing directly
        var stagedPath = fileAccess.GetStagedPath(targetPath);
        var stagedDir = Path.GetDirectoryName(stagedPath);
        if (!string.IsNullOrEmpty(stagedDir))
            Directory.CreateDirectory(stagedDir);
        File.Copy(sourceVersePath, stagedPath);

        // Read source for summary
        var source = File.ReadAllText(sourceVersePath);
        var lineCount = source.Split('\n').Length;

        return $"Verse file staged for review. Approve in WellVersed app to apply.\n\n" +
               $"File: {Path.GetFileName(sourceVersePath)} → {Path.GetRelativePath(config.ProjectPath, targetPath)}\n" +
               $"Lines: {lineCount}\n" +
               $"Staged at: {stagedPath}\n\n" +
               $"You may need to update module paths and imports to match your project structure.";
    }

    [McpServerTool, Description(
        "Copy a device configuration (external actor .uasset) from a library project to the active project's level. " +
        "This copies a placed device instance with all its property overrides. " +
        "Use after finding relevant device configurations via search_library. " +
        "Changes are staged for review — approve in the WellVersed app before they're applied to project files.")]
    public string copy_device_to_project(
        ForgeConfig config,
        SafeFileAccess fileAccess,
        [Description("Full path to the source external actor .uasset file")] string sourceActorPath,
        [Description("Path to the target level's __ExternalActors__ directory")] string targetExternalActorsDir)
    {
        if (!File.Exists(sourceActorPath))
            return $"Source file not found: {sourceActorPath}";

        if (config.ReadOnly)
            return "BLOCKED: Project is in read-only mode.";

        if (!Directory.Exists(targetExternalActorsDir))
            return $"Target directory not found: {targetExternalActorsDir}";

        // Generate a unique filename
        var newFileName = $"{Guid.NewGuid():N}.uasset";
        var targetPath = Path.Combine(targetExternalActorsDir, newFileName);

        // Stage the copy instead of writing directly
        var stagedPath = fileAccess.GetStagedPath(targetPath);
        var stagedDir = Path.GetDirectoryName(stagedPath);
        if (!string.IsNullOrEmpty(stagedDir))
            Directory.CreateDirectory(stagedDir);
        File.Copy(sourceActorPath, stagedPath);

        // Also copy .uexp if it exists
        var sourceUexp = Path.ChangeExtension(sourceActorPath, ".uexp");
        if (File.Exists(sourceUexp))
        {
            var stagedUexp = Path.ChangeExtension(stagedPath, ".uexp");
            File.Copy(sourceUexp, stagedUexp);
        }

        // Read the device info from the source
        try
        {
            var asset = new UAsset(sourceActorPath, EngineVersion.VER_UE5_4);
            var primaryClass = asset.Exports
                .FirstOrDefault(e => !e.GetExportClassType()?.ToString()?.Contains("Component") == true)
                ?.GetExportClassType()?.ToString() ?? "Unknown";

            return $"Device copy staged for review. Approve in WellVersed app to apply.\n\n" +
                   $"Device: {primaryClass}\n" +
                   $"Source: {sourceActorPath}\n" +
                   $"Target: {targetPath}\n" +
                   $"Staged at: {stagedPath}\n\n" +
                   $"NOTE: The device may need its transform (position) adjusted in UEFN after applying. " +
                   $"If it references user-created assets from the source project, those dependencies may need to be copied separately.";
        }
        catch
        {
            return $"Device copy staged for review. Approve in WellVersed app to apply.\n\n" +
                   $"Staged at: {stagedPath}\n" +
                   $"Could not parse device info from source file.";
        }
    }

    [McpServerTool, Description(
        "Get full verse generation context for the active project. Returns all existing verse source code, " +
        "device types and their editable properties, and asset definitions. Use this before generating " +
        "new verse code so you can integrate with existing systems, use correct device references, " +
        "and follow the project's coding patterns.")]
    public string get_verse_context(
        ForgeConfig config)
    {
        var contentPath = config.ContentPath;
        if (!Directory.Exists(contentPath))
            return "Content directory not found. Is a project active?";

        var output = new List<string> { $"=== Verse Context for {config.ProjectName} ===\n" };

        // All verse files with full source
        var verseFiles = Directory.EnumerateFiles(contentPath, "*.verse", SearchOption.AllDirectories).ToList();
        output.Add($"Verse files: {verseFiles.Count}\n");

        foreach (var vf in verseFiles)
        {
            var source = File.ReadAllText(vf);
            var relPath = Path.GetRelativePath(contentPath, vf);
            output.Add($"--- {relPath} ({source.Split('\n').Length} lines) ---");
            output.Add(source);
            output.Add("");
        }

        // Device types available in levels
        output.Add("=== Device Types in Level ===");
        var umaps = Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories).ToList();
        foreach (var umap in umaps.Take(1))
        {
            var extDir = Path.Combine(Path.GetDirectoryName(umap)!, "__ExternalActors__", Path.GetFileNameWithoutExtension(umap));
            if (!Directory.Exists(extDir)) continue;

            var deviceTypes = new Dictionary<string, List<string>>();
            foreach (var file in Directory.EnumerateFiles(extDir, "*.uasset", SearchOption.AllDirectories).Take(200))
            {
                try
                {
                    var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                    foreach (var export in asset.Exports.OfType<NormalExport>())
                    {
                        var cls = export.GetExportClassType()?.ToString() ?? "";
                        if (cls.Contains("Component") || string.IsNullOrEmpty(cls)) continue;

                        if (!deviceTypes.ContainsKey(cls))
                        {
                            var propNames = export.Data.Select(p => p.Name?.ToString() ?? "").Where(n => n != "None" && n != "").ToList();
                            deviceTypes[cls] = propNames;
                        }
                        break;
                    }
                }
                catch { }
            }

            foreach (var (cls, props) in deviceTypes.OrderBy(kv => kv.Key))
            {
                output.Add($"  {cls}: {string.Join(", ", props.Take(10))}{(props.Count > 10 ? $" (+{props.Count - 10} more)" : "")}");
            }
        }

        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Read a verse file from the active project. Returns the full source code.")]
    public string read_project_verse(
        ForgeConfig config,
        [Description("Verse file name (with or without .verse extension)")] string fileName)
    {
        var contentPath = config.ContentPath;
        if (!Directory.Exists(contentPath))
            return "Content directory not found.";

        if (!fileName.EndsWith(".verse")) fileName += ".verse";

        var matches = Directory.EnumerateFiles(contentPath, fileName, SearchOption.AllDirectories).ToList();
        if (matches.Count == 0)
            return $"No verse file named '{fileName}' found in project.";

        var source = File.ReadAllText(matches[0]);
        return $"// {Path.GetRelativePath(contentPath, matches[0])}\n\n{source}";
    }

    [McpServerTool, Description(
        "Write or update a verse file in the active project. Creates the file if it doesn't exist, " +
        "or overwrites if it does. Use get_verse_context first to understand existing code. " +
        "Changes are staged for review — approve in the WellVersed app before they're applied to project files.")]
    public string write_project_verse(
        ForgeConfig config,
        SafeFileAccess fileAccess,
        [Description("Filename (with or without .verse extension)")] string fileName,
        [Description("Full verse source code to write")] string sourceCode)
    {
        if (config.ReadOnly)
            return "BLOCKED: Project is in read-only mode.";

        var contentPath = config.ContentPath;
        if (!Directory.Exists(contentPath))
            return "Content directory not found.";

        if (!fileName.EndsWith(".verse")) fileName += ".verse";

        var targetPath = Path.Combine(contentPath, fileName);
        var existed = File.Exists(targetPath);

        // Stage the write instead of writing directly
        var stagedPath = fileAccess.GetStagedPath(targetPath);
        var stagedDir = Path.GetDirectoryName(stagedPath);
        if (!string.IsNullOrEmpty(stagedDir))
            Directory.CreateDirectory(stagedDir);
        File.WriteAllText(stagedPath, sourceCode);

        var lineCount = sourceCode.Split('\n').Length;

        return $"Verse file staged for review. Approve in WellVersed app to apply.\n\n" +
               $"{(existed ? "Update" : "New file")}: {fileName}\n" +
               $"Lines: {lineCount}\n" +
               $"Target: {targetPath}\n" +
               $"Staged at: {stagedPath}";
    }
}
