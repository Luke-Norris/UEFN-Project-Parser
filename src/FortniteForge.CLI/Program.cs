using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Safety;
using WellVersed.Core.Services;
using WellVersed.Core.Services.MapGeneration;
using Microsoft.Extensions.Logging;

namespace WellVersed.CLI;

/// <summary>
/// WellVersed CLI — manual interface for testing, debugging, and standalone use.
///
/// Usage:
///   wellversed status
///   wellversed list [--folder path] [--class type]
///   wellversed inspect asset-path
///   wellversed devices level-path
///   wellversed device device-name [--level path]
///   wellversed audit [--level path]
///   wellversed staged [--apply | --discard]
///   wellversed build
///   wellversed build-log
///   wellversed backups [--asset path]
///   wellversed schema device-type
///   wellversed init project-path
/// </summary>
public class Program
{
    // Compact JSON for NDJSON sidecar protocol (one response per line)
    private static readonly JsonSerializerOptions SidecarJsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Global config option shared by all commands
    private static readonly Option<string?> ConfigOption = new("--config", "Path to forge.config.json");
    static Program() { ConfigOption.AddAlias("-c"); }

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("WellVersed — AI-powered UEFN project tools")
        {
            BuildStatusCommand(),
            BuildListCommand(),
            BuildInspectCommand(),
            BuildDevicesCommand(),
            BuildDeviceCommand(),
            BuildAuditCommand(),
            BuildStagedCommand(),
            BuildBuildCommand(),
            BuildBuildLogCommand(),
            BuildBackupsCommand(),
            BuildSchemaCommand(),
            BuildInitCommand(),
            BuildSidecarCommand()
        };
        rootCommand.AddGlobalOption(ConfigOption);

        var result = await rootCommand.InvokeAsync(args);
        _lastServices?.Dispose();
        return result;
    }

    // Track the last created service bundle for cleanup
    private static ServiceBundle? _lastServices;

    private static (WellVersedConfig Config, ServiceBundle Services, UefnDetector Detector) LoadServices(string? configPath = null)
    {
        // Priority: explicit arg > env var > walk parent dirs
        configPath ??= Environment.GetEnvironmentVariable("WELLVERSED_CONFIG")
                       ?? FindConfigFile();
        WellVersedConfig config;

        if (configPath != null && File.Exists(configPath))
        {
            config = WellVersedConfig.Load(configPath);
        }
        else
        {
            Console.Error.WriteLine("Warning: No forge.config.json found. Run 'wellversed init' to create one.");
            config = new WellVersedConfig();
        }

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var detector = new UefnDetector(config, loggerFactory.CreateLogger<UefnDetector>());
        var fileAccess = new SafeFileAccess(config, detector, loggerFactory.CreateLogger<SafeFileAccess>());
        var guard = new AssetGuard(config, detector, loggerFactory.CreateLogger<AssetGuard>());
        var assetService = new AssetService(config, guard, fileAccess, loggerFactory.CreateLogger<AssetService>());
        var backupService = new BackupService(config, loggerFactory.CreateLogger<BackupService>());
        var digestService = new DigestService(config, loggerFactory.CreateLogger<DigestService>());
        var deviceService = new DeviceService(config, assetService, digestService, loggerFactory.CreateLogger<DeviceService>());
        var auditService = new AuditService(config, deviceService, assetService, digestService, guard, loggerFactory.CreateLogger<AuditService>());
        var assetValidator = new AssetValidator(loggerFactory.CreateLogger<AssetValidator>());
        var placementService = new ActorPlacementService(config, guard, fileAccess, backupService, assetValidator, loggerFactory.CreateLogger<ActorPlacementService>());
        var modService = new ModificationService(config, assetService, backupService, guard, fileAccess, digestService, placementService, assetValidator, loggerFactory.CreateLogger<ModificationService>());
        var buildService = new BuildService(config, loggerFactory.CreateLogger<BuildService>());
        var catalog = new AssetCatalog(config, loggerFactory.CreateLogger<AssetCatalog>());
        var mapGenerator = new MapGenerator(config, catalog, placementService, backupService, loggerFactory.CreateLogger<MapGenerator>());

        var bundle = new ServiceBundle(assetService, deviceService, auditService, modService, buildService, backupService, digestService, placementService, fileAccess, catalog, mapGenerator);
        _lastServices = bundle;
        return (config, bundle, detector);
    }

    // ========= Status Line =========

    private static void PrintStatusLine(WellVersedConfig config, UefnDetector detector)
    {
        var status = detector.GetStatus();
        var isConfigured = !string.IsNullOrEmpty(config.ProjectPath) && Directory.Exists(config.ProjectPath);

        // Colors
        const string reset = "\x1b[0m";
        const string dim = "\x1b[2m";
        const string bold = "\x1b[1m";
        const string green = "\x1b[32m";
        const string yellow = "\x1b[33m";
        const string red = "\x1b[31m";
        const string cyan = "\x1b[36m";
        const string magenta = "\x1b[35m";

        // Top border
        Console.Error.WriteLine($"{dim}{"".PadRight(60, '\u2500')}{reset}");

        // Project line
        if (isConfigured)
        {
            var projectType = config.IsUefnProject ? "UEFN" : "UE";
            Console.Error.WriteLine($"{bold}{cyan}  WellVersed{reset}  {status.ProjectName} {dim}({projectType}){reset}");
        }
        else
        {
            Console.Error.WriteLine($"{bold}{cyan}  WellVersed{reset}  {yellow}No project configured{reset}");
        }

        // Status indicators
        var uefnIndicator = status.IsUefnRunning
            ? $"{green}\u25cf Running{reset} {dim}(PID {status.UefnPid}){reset}"
            : $"{dim}\u25cb Not running{reset}";

        var modeColor = status.Mode switch
        {
            OperationMode.ReadOnly => red,
            OperationMode.Staged => yellow,
            OperationMode.Direct => green,
            _ => dim
        };
        var modeLabel = status.Mode switch
        {
            OperationMode.ReadOnly => "Read-Only",
            OperationMode.Staged => "Staged",
            OperationMode.Direct => "Direct",
            _ => "Unknown"
        };

        Console.Error.WriteLine($"  UEFN: {uefnIndicator}    Mode: {modeColor}{bold}{modeLabel}{reset}");

        // Third line: URC + counts
        var parts = new List<string>();

        if (status.HasUrc)
        {
            var urcColor = status.UrcActive ? yellow : dim;
            parts.Add($"URC: {urcColor}{(status.UrcActive ? "Active" : "Present")}{reset}");
        }

        if (isConfigured && Directory.Exists(config.ContentPath))
        {
            try
            {
                var assetCount = Directory.EnumerateFiles(config.ContentPath, "*.uasset", SearchOption.AllDirectories).Count()
                    + Directory.EnumerateFiles(config.ContentPath, "*.umap", SearchOption.AllDirectories).Count();
                parts.Add($"Assets: {magenta}{assetCount}{reset}");
            }
            catch { }

            try
            {
                var verseCount = Directory.EnumerateFiles(config.ContentPath, "*.verse", SearchOption.AllDirectories).Count();
                if (verseCount > 0)
                    parts.Add($"Verse: {magenta}{verseCount}{reset}");
            }
            catch { }
        }

        if (status.StagedFileCount > 0)
            parts.Add($"Staged: {yellow}{status.StagedFileCount}{reset}");

        if (parts.Count > 0)
            Console.Error.WriteLine($"  {string.Join($"  {dim}|{reset}  ", parts)}");

        // Bottom border
        Console.Error.WriteLine($"{dim}{"".PadRight(60, '\u2500')}{reset}");
    }

    // ========= Commands =========

    private static Command BuildStatusCommand()
    {
        var cmd = new Command("status", "Show project status and UEFN environment");
        cmd.SetHandler((string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);

            var status = detector.GetStatus();
            Console.WriteLine(JsonSerializer.Serialize(status, JsonOpts));
        }, ConfigOption);

        return cmd;
    }

    private static Command BuildListCommand()
    {
        var folderOption = new Option<string?>("--folder", "Subfolder within Content/ to search");
        var classOption = new Option<string?>("--class", "Filter by asset class");
        var nameOption = new Option<string?>("--name", "Search by name");

        var cmd = new Command("list", "List project assets") { folderOption, classOption, nameOption };
        cmd.SetHandler((string? folder, string? assetClass, string? name, string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);
            var assets = services.Asset.ListAssets(folder, assetClass, name);
            Console.WriteLine(JsonSerializer.Serialize(assets, JsonOpts));
        }, folderOption, classOption, nameOption, ConfigOption);

        return cmd;
    }

    private static Command BuildInspectCommand()
    {
        var pathArg = new Argument<string>("asset-path", "Path to the asset file");
        var cmd = new Command("inspect", "Inspect an asset in detail") { pathArg };
        cmd.SetHandler((string path, string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);
            var detail = services.Asset.InspectAsset(path);
            Console.WriteLine(JsonSerializer.Serialize(detail, JsonOpts));
        }, pathArg, ConfigOption);

        return cmd;
    }

    private static Command BuildDevicesCommand()
    {
        var levelArg = new Argument<string>("level-path", "Path to the .umap level file");
        var cmd = new Command("devices", "List devices in a level") { levelArg };
        cmd.SetHandler((string level, string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);
            var devices = services.Device.ListDevicesInLevel(level);
            Console.WriteLine(JsonSerializer.Serialize(devices, JsonOpts));
        }, levelArg, ConfigOption);

        return cmd;
    }

    private static Command BuildDeviceCommand()
    {
        var nameArg = new Argument<string>("device-name", "Actor name of the device");
        var levelOption = new Option<string?>("--level", "Level path (searches all levels if not specified)");
        var cmd = new Command("device", "Inspect a specific device") { nameArg, levelOption };
        cmd.SetHandler((string name, string? level, string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);
            if (level != null)
            {
                var device = services.Device.GetDevice(level, name);
                Console.WriteLine(device != null
                    ? JsonSerializer.Serialize(device, JsonOpts)
                    : $"Device '{name}' not found.");
            }
            else
            {
                var (device, foundLevel) = services.Device.FindDeviceByName(name);
                if (device != null)
                {
                    Console.WriteLine($"Found in: {foundLevel}");
                    Console.WriteLine(JsonSerializer.Serialize(device, JsonOpts));
                }
                else
                {
                    Console.WriteLine($"Device '{name}' not found in any level.");
                }
            }
        }, nameArg, levelOption, ConfigOption);

        return cmd;
    }

    private static Command BuildAuditCommand()
    {
        var levelOption = new Option<string?>("--level", "Audit a specific level (audits entire project if not specified)");
        var cmd = new Command("audit", "Audit project or level for issues") { levelOption };
        cmd.SetHandler((string? level, string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);
            var result = level != null
                ? services.Audit.AuditLevel(level)
                : services.Audit.AuditProject();
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
        }, levelOption, ConfigOption);

        return cmd;
    }

    private static Command BuildStagedCommand()
    {
        var applyOption = new Option<bool>("--apply", "Apply all staged changes (requires UEFN to be closed)");
        var discardOption = new Option<bool>("--discard", "Discard all staged changes");

        var cmd = new Command("staged", "Manage staged modifications") { applyOption, discardOption };
        cmd.SetHandler((bool apply, bool discard, string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);

            if (apply)
            {
                var results = services.FileAccess.ApplyAllStaged(services.Backup);
                foreach (var r in results)
                {
                    var color = r.Success ? "\x1b[32m" : "\x1b[31m";
                    Console.Error.WriteLine($"  {color}{(r.Success ? "\u2713" : "\u2717")}\x1b[0m {r.Message}");
                }
                if (results.Count == 0)
                    Console.Error.WriteLine("  No staged files to apply.");
            }
            else if (discard)
            {
                services.FileAccess.DiscardAllStaged();
                Console.Error.WriteLine("  All staged files discarded.");
            }
            else
            {
                // List staged files
                var staged = services.FileAccess.ListStagedFiles();
                if (staged.Count == 0)
                {
                    Console.Error.WriteLine("  No staged modifications.");
                }
                else
                {
                    Console.Error.WriteLine($"  {staged.Count} staged file(s):");
                    foreach (var s in staged)
                    {
                        var existsLabel = s.Exists ? "update" : "new";
                        Console.Error.WriteLine($"    [{existsLabel}] {s.RelativePath} ({s.Size:N0} bytes, staged {s.StagedAt:g})");
                    }
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("  Use --apply to write to project (UEFN must be closed)");
                    Console.Error.WriteLine("  Use --discard to remove all staged changes");
                }
            }
        }, applyOption, discardOption, ConfigOption);

        return cmd;
    }

    private static Command BuildBuildCommand()
    {
        var cmd = new Command("build", "Trigger a UEFN build");
        cmd.SetHandler(async (string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);
            Console.WriteLine("Starting build...");
            var result = await services.Build.TriggerBuildAsync();
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
        }, ConfigOption);

        return cmd;
    }

    private static Command BuildBuildLogCommand()
    {
        var cmd = new Command("build-log", "Read the latest build log");
        cmd.SetHandler((string? cfgPath) =>
        {
            var (_, services, _) = LoadServices(cfgPath);
            Console.WriteLine(services.Build.GetBuildSummary());
        }, ConfigOption);

        return cmd;
    }

    private static Command BuildBackupsCommand()
    {
        var assetOption = new Option<string?>("--asset", "Filter backups for a specific asset");
        var cmd = new Command("backups", "List available backups") { assetOption };
        cmd.SetHandler((string? asset, string? cfgPath) =>
        {
            var (config, services, detector) = LoadServices(cfgPath);
            PrintStatusLine(config, detector);
            var backups = services.Backup.ListBackups(asset);
            Console.WriteLine(JsonSerializer.Serialize(backups, JsonOpts));
        }, assetOption, ConfigOption);

        return cmd;
    }

    private static Command BuildSchemaCommand()
    {
        var typeArg = new Argument<string>("device-type", "Device class name to look up");
        var cmd = new Command("schema", "Get device schema from digest files") { typeArg };
        cmd.SetHandler((string deviceType, string? cfgPath) =>
        {
            var (_, services, _) = LoadServices(cfgPath);
            services.Digest.LoadDigests();
            var schema = services.Digest.GetDeviceSchema(deviceType);
            Console.WriteLine(schema != null
                ? JsonSerializer.Serialize(schema, JsonOpts)
                : $"No schema found for '{deviceType}'.");
        }, typeArg, ConfigOption);

        return cmd;
    }

    private static Command BuildInitCommand()
    {
        var projectPathArg = new Argument<string>("project-path", "Path to your UEFN project root");
        var readOnlyOption = new Option<bool>("--read-only", "Start in read-only mode (recommended for active projects)");
        var cmd = new Command("init", "Create a forge.config.json in the current directory") { projectPathArg, readOnlyOption };
        cmd.SetHandler((string projectPath, bool readOnly) =>
        {
            var fullPath = Path.GetFullPath(projectPath);
            var config = new WellVersedConfig
            {
                ProjectPath = fullPath,
                ReadOnly = readOnly,
                ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" }
            };

            var issues = config.Validate();
            if (issues.Count > 0)
            {
                Console.Error.WriteLine("Warnings:");
                foreach (var issue in issues)
                    Console.Error.WriteLine($"  - {issue}");
            }

            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "forge.config.json");
            config.Save(configPath);

            Console.Error.WriteLine($"\x1b[32m\u2713\x1b[0m Created: {configPath}");
            Console.Error.WriteLine($"  Project: {config.ProjectName}");
            Console.Error.WriteLine($"  Type: {(config.IsUefnProject ? "UEFN" : "Unreal")}");
            Console.Error.WriteLine($"  Content: {config.ContentPath}");
            Console.Error.WriteLine($"  Mode: {(readOnly ? "Read-Only" : "Auto (staged when UEFN running)")}");

            if (config.IsUefnProject)
                Console.Error.WriteLine($"  URC: {(config.HasUrc ? "Detected" : "Not found")}");
        }, projectPathArg, readOnlyOption);

        return cmd;
    }

    // ========= Sidecar Command (Electron bridge) =========

    // Sidecar-scoped project manager for persistent project list
    private static SidecarProjectManager? _sidecarProjects;
    // Sidecar-scoped library manager for persistent library list (separate from projects)
    private static SidecarLibraryManager? _sidecarLibraries;
    // CUE4Parse asset preview service (optional — only if Fortnite is installed)
    private static AssetPreviewService? _previewService;
    // Mesh export service for GLB extraction from PAK files
    private static MeshExportService? _meshExportService;
    // UEFN Bridge — HTTP client connecting to the Python bridge inside UEFN
    private static UefnBridge? _uefnBridge;

    private static Command BuildSidecarCommand()
    {
        var cmd = new Command("sidecar", "Run as Electron sidecar — reads JSON requests from stdin, writes responses to stdout");
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var configPath = ctx.ParseResult.GetValueForOption(ConfigOption);
            WellVersedConfig? config = null;
            ServiceBundle? services = null;

            _sidecarProjects = new SidecarProjectManager();
            _sidecarLibraries = new SidecarLibraryManager();

            try
            {
                // Try loading from active project if one exists
                var active = _sidecarProjects.GetActiveProject();
                if (active != null)
                {
                    config = new WellVersedConfig { ProjectPath = active.ProjectPath, ReadOnly = active.Type == "Library" };
                    // Don't call LoadServices(null) — it would overwrite config with an empty one.
                    // Services are built on-demand by BuildActiveProjectServices() for each request.
                }
                else if (configPath != null)
                {
                    var loaded = LoadServices(configPath);
                    config = loaded.Config;
                    services = loaded.Services;
                }
                else
                {
                    config = new WellVersedConfig();
                }
            }
            catch
            {
                config = new WellVersedConfig();
            }

            // Signal ready
            Console.Out.WriteLine(JsonSerializer.Serialize(new SidecarResponse("ready", new { status = "ok" }), SidecarJsonOpts));
            Console.Out.Flush();

            // NDJSON request loop
            string? line;
            while ((line = await Console.In.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var request = JsonSerializer.Deserialize<SidecarRequest>(line, JsonOpts);
                    if (request == null) continue;

                    var response = HandleSidecarRequest(request, config, services);
                    Console.Out.WriteLine(JsonSerializer.Serialize(response, SidecarJsonOpts));
                    Console.Out.Flush();
                }
                catch (Exception ex)
                {
                    var errResponse = new SidecarResponse("unknown", Error: new SidecarError("INTERNAL", ex.Message));
                    Console.Out.WriteLine(JsonSerializer.Serialize(errResponse, SidecarJsonOpts));
                    Console.Out.Flush();
                }
            }

            services?.Dispose();
        });
        return cmd;
    }

    private static SidecarResponse HandleSidecarRequest(SidecarRequest req, WellVersedConfig config, ServiceBundle? services)
    {
        try
        {
            return req.Method switch
            {
                "validate-spec" => HandleValidateSpec(req),
                "build-uasset" => HandleBuildUasset(req, config),
                "generate-verse" => HandleGenerateVerse(req),
                "ping" => new SidecarResponse(req.Id, new { pong = true }),
                "status" => HandleStatus(req),
                "list-projects" => HandleListProjects(req),
                "add-project" => HandleAddProject(req),
                "remove-project" => HandleRemoveProject(req),
                "activate-project" => HandleActivateProject(req),
                "scan-projects" => HandleScanProjects(req),
                "list-levels" => HandleListLevels(req),
                "audit" => HandleAudit(req, services),
                "browse-content" => HandleBrowseContent(req),
                "inspect-asset" => HandleInspectAsset(req),
                "list-devices" => HandleListDevices(req),
                "inspect-device" => HandleInspectDevice(req),
                "list-user-assets" => HandleListUserAssets(req),
                "list-epic-assets" => HandleListEpicAssets(req),
                "read-verse" => HandleReadVerse(req),
                "list-staged" => HandleListStaged(req),
                // Library management (separate from projects)
                "list-libraries" => HandleListLibraries(req),
                "add-library" => HandleAddLibrary(req),
                "remove-library" => HandleRemoveLibrary(req),
                "activate-library" => HandleActivateLibrary(req),
                "index-library" => HandleIndexLibrary(req),
                "get-library-verse-files" => HandleGetLibraryVerseFiles(req),
                "get-library-assets-by-type" => HandleGetLibraryAssetsByType(req),
                "browse-library-dir" => HandleBrowseLibraryDir(req),
                "search-library-index" => HandleSearchLibraryIndex(req),
                // Widget parsing
                "list-project-widgets" => HandleListProjectWidgets(req),
                "parse-widget" => HandleParseWidget(req),
                "widget-texture" => HandleWidgetTexture(req, config),
                "list-library-widgets" => HandleListLibraryWidgets(req),
                // CUE4Parse asset preview
                "preview-init" => HandlePreviewInit(req),
                "preview-search" => HandlePreviewSearch(req),
                "preview-texture" => HandlePreviewTexture(req),
                "preview-mesh-info" => HandlePreviewMeshInfo(req),
                "preview-export-mesh" => HandlePreviewExportMesh(req),
                "preview-export-mesh-batch" => HandlePreviewExportMeshBatch(req),
                "preview-status" => HandlePreviewStatus(req),
                // System extraction
                "analyze-level-systems" => HandleAnalyzeLevelSystems(req),
                "analyze-project-systems" => HandleAnalyzeProjectSystems(req),
                // Project diff / snapshots
                "take-snapshot" => HandleTakeSnapshot(req),
                "list-snapshots" => HandleListSnapshots(req),
                "compare-snapshot" => HandleCompareSnapshot(req),
                // Device Encyclopedia
                "encyclopedia-search" => HandleEncyclopediaSearch(req),
                "encyclopedia-device-reference" => HandleEncyclopediaDeviceReference(req),
                "encyclopedia-common-configs" => HandleEncyclopediaCommonConfigs(req),
                "encyclopedia-list-devices" => HandleEncyclopediaListDevices(req),
                // File watcher integration (called from Rust side)
                "watch-project" => HandleWatchProject(req),
                "unwatch-project" => HandleUnwatchProject(req),
                // UEFN Bridge passthrough
                "bridge-connect" => HandleBridgeConnect(req),
                "bridge-status" => HandleBridgeStatus(req),
                "bridge-command" => HandleBridgeCommand(req),
                // Device Behavior Simulator
                "simulate-game-loop" => HandleSimulateGameLoop(req),
                "simulate-event" => HandleSimulateEvent(req),
                _ => new SidecarResponse(req.Id, Error: new SidecarError("UNKNOWN_METHOD", $"Unknown method: {req.Method}"))
            };
        }
        catch (Exception ex)
        {
            return new SidecarResponse(req.Id, Error: new SidecarError("HANDLER_ERROR", ex.Message));
        }
    }

    // ========= Sidecar Project Management Handlers =========

    private static SidecarResponse HandleStatus(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();

        if (active == null)
            return new SidecarResponse(req.Id, new
            {
                projectName = "No Project",
                mode = "None",
                isConfigured = false,
                assetCount = 0,
                verseCount = 0,
                levelCount = 0
            });

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath, ReadOnly = true, StagingDirectory = Path.Combine(active.ProjectPath, ".wellversed", "staged") };
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.None));
        var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
        var status = detector.GetStatus();

        int assetCount = 0, verseCount = 0, levelCount = 0, defCount = 0;
        if (Directory.Exists(cfg.ContentPath))
        {
            try { assetCount = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories).Count()
                              + Directory.EnumerateFiles(cfg.ContentPath, "*.umap", SearchOption.AllDirectories).Count(); } catch { }
            try { defCount = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories).Count(f => !f.Contains("__External")); } catch { }
            try { verseCount = Directory.EnumerateFiles(cfg.ContentPath, "*.verse", SearchOption.AllDirectories).Count(); } catch { }
            try { levelCount = Directory.EnumerateFiles(cfg.ContentPath, "*.umap", SearchOption.AllDirectories).Count(); } catch { }
        }

        return new SidecarResponse(req.Id, new
        {
            isConfigured = true,
            projectName = active.Name,
            projectPath = active.ProjectPath,
            projectType = active.Type,
            isUefnProject = active.IsUefnProject,
            contentPath = cfg.ContentPath,
            isUefnRunning = status.IsUefnRunning,
            uefnPid = status.UefnPid,
            hasUrc = status.HasUrc,
            urcActive = status.UrcActive,
            mode = status.Mode.ToString(),
            modeReason = status.ModeReason,
            stagedFileCount = status.StagedFileCount,
            assetCount,
            definitionCount = defCount,
            verseCount,
            levelCount,
            readOnly = cfg.ReadOnly
        });
    }

    private static SidecarResponse HandleListProjects(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        return new SidecarResponse(req.Id, new
        {
            activeProjectId = pm.GetActiveProject()?.Id,
            projects = pm.ListProjects()
        });
    }

    private static SidecarResponse HandleAddProject(SidecarRequest req)
    {
        var path = req.Params?.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter");
        var type = req.Params?.GetProperty("type").GetString() ?? "MyProject";

        var pm = _sidecarProjects!;
        var entry = pm.AddProject(path, type);

        // Create .wellversed/ directory structure at project root
        try
        {
            var wsRoot = Path.Combine(path, ".wellversed");
            Directory.CreateDirectory(Path.Combine(wsRoot, "staged"));
            Directory.CreateDirectory(Path.Combine(wsRoot, "workspace", "unpacked"));
            Directory.CreateDirectory(Path.Combine(wsRoot, "workspace", "cache"));
            Directory.CreateDirectory(Path.Combine(wsRoot, "recipes"));
        }
        catch { /* non-fatal — directory creation may fail on read-only volumes */ }

        return new SidecarResponse(req.Id, entry);
    }

    private static SidecarResponse HandleRemoveProject(SidecarRequest req)
    {
        var id = req.Params?.GetProperty("id").GetString()
            ?? throw new ArgumentException("Missing 'id' parameter");
        var pm = _sidecarProjects!;
        var removed = pm.RemoveProject(id);
        return new SidecarResponse(req.Id, new { removed });
    }

    private static SidecarResponse HandleActivateProject(SidecarRequest req)
    {
        var id = req.Params?.GetProperty("id").GetString()
            ?? throw new ArgumentException("Missing 'id' parameter");
        var pm = _sidecarProjects!;
        var project = pm.SetActive(id);
        return project != null
            ? new SidecarResponse(req.Id, project)
            : new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Project '{id}' not found"));
    }

    private static SidecarResponse HandleScanProjects(SidecarRequest req)
    {
        var path = req.Params?.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter");

        if (!Directory.Exists(path))
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", "Directory not found"));

        var pm = _sidecarProjects!;
        var discovered = pm.ScanDirectory(path);
        return new SidecarResponse(req.Id, discovered);
    }

    private static SidecarResponse HandleListLevels(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
        if (!Directory.Exists(cfg.ContentPath))
            return new SidecarResponse(req.Id, Array.Empty<object>());

        var levels = Directory.EnumerateFiles(cfg.ContentPath, "*.umap", SearchOption.AllDirectories)
            .Select(l => new
            {
                filePath = l,
                relativePath = Path.GetRelativePath(cfg.ContentPath, l),
                name = Path.GetFileNameWithoutExtension(l)
            })
            .ToList();

        return new SidecarResponse(req.Id, levels);
    }

    private static SidecarResponse HandleAudit(SidecarRequest req, ServiceBundle? services)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        // Build services for the active project if we don't have them or they're for a different project
        if (services == null)
        {
            var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath, ReadOnly = true, StagingDirectory = Path.Combine(active.ProjectPath, ".wellversed", "staged") };
            var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
            var fileAccess = new SafeFileAccess(cfg, detector, loggerFactory.CreateLogger<SafeFileAccess>());
            var guard = new AssetGuard(cfg, detector, loggerFactory.CreateLogger<AssetGuard>());
            var assetService = new AssetService(cfg, guard, fileAccess, loggerFactory.CreateLogger<AssetService>());
            var digestService = new DigestService(cfg, loggerFactory.CreateLogger<DigestService>());
            var deviceService = new DeviceService(cfg, assetService, digestService, loggerFactory.CreateLogger<DeviceService>());
            var auditService = new AuditService(cfg, deviceService, assetService, digestService, guard, loggerFactory.CreateLogger<AuditService>());

            string? levelPath = null;
            if (req.Params?.TryGetProperty("level", out var levelEl) == true)
                levelPath = levelEl.GetString();

            var result = levelPath != null
                ? auditService.AuditLevel(levelPath)
                : auditService.AuditProject();

            fileAccess.Dispose();
            return new SidecarResponse(req.Id, result);
        }
        else
        {
            string? levelPath = null;
            if (req.Params?.TryGetProperty("level", out var levelEl) == true)
                levelPath = levelEl.GetString();

            var result = levelPath != null
                ? services.Audit.AuditLevel(levelPath)
                : services.Audit.AuditProject();

            return new SidecarResponse(req.Id, result);
        }
    }

    // ========= Sidecar Content Browsing Handlers =========

    /// <summary>
    /// Builds lightweight services for the active project on demand.
    /// Returns (config, assetService, deviceService, fileAccess) or an error response.
    /// The caller must dispose fileAccess when done.
    /// </summary>
    private static (WellVersedConfig Cfg, AssetService Asset, DeviceService Device, SafeFileAccess FileAccess)?
        BuildActiveProjectServices(out SidecarResponse? errorResponse, string requestId)
    {
        errorResponse = null;
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
        {
            errorResponse = new SidecarResponse(requestId, Error: new SidecarError("NO_PROJECT", "No active project"));
            return null;
        }

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath, ReadOnly = true, StagingDirectory = Path.Combine(active.ProjectPath, ".wellversed", "staged") };
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
        var fileAccess = new SafeFileAccess(cfg, detector, loggerFactory.CreateLogger<SafeFileAccess>());
        var guard = new AssetGuard(cfg, detector, loggerFactory.CreateLogger<AssetGuard>());
        var assetService = new AssetService(cfg, guard, fileAccess, loggerFactory.CreateLogger<AssetService>());
        var digestService = new DigestService(cfg, loggerFactory.CreateLogger<DigestService>());
        var deviceService = new DeviceService(cfg, assetService, digestService, loggerFactory.CreateLogger<DeviceService>());

        return (cfg, assetService, deviceService, fileAccess);
    }

    private static SidecarResponse HandleBrowseContent(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
        var basePath = cfg.ContentPath;

        // Optional subfolder param
        string? subPath = null;
        if (req.Params?.TryGetProperty("path", out var pathEl) == true)
            subPath = pathEl.GetString();

        var targetPath = string.IsNullOrEmpty(subPath)
            ? basePath
            : Path.Combine(basePath, subPath);

        if (!Directory.Exists(targetPath))
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Directory not found: {targetPath}"));

        // Ensure the target is within the content path (prevent directory traversal)
        var fullTarget = Path.GetFullPath(targetPath);
        var fullBase = Path.GetFullPath(basePath);
        if (!fullTarget.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            return new SidecarResponse(req.Id, Error: new SidecarError("INVALID_PATH", "Path must be within project content directory"));

        var entries = new List<object>();

        // Folders first
        foreach (var dir in Directory.EnumerateDirectories(targetPath).OrderBy(d => d))
        {
            var di = new DirectoryInfo(dir);
            entries.Add(new
            {
                name = di.Name,
                path = Path.GetRelativePath(basePath, dir),
                type = "folder",
                size = 0L,
                lastModified = di.LastWriteTimeUtc.ToString("o")
            });
        }

        // Then files
        foreach (var file in Directory.EnumerateFiles(targetPath).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
            var ext = fi.Extension.ToLowerInvariant();
            var fileType = ext switch
            {
                ".uasset" => "uasset",
                ".umap" => "umap",
                ".verse" => "verse",
                ".uexp" => "uexp",
                _ => "other"
            };

            entries.Add(new
            {
                name = fi.Name,
                path = Path.GetRelativePath(basePath, file),
                type = fileType,
                size = fi.Length,
                lastModified = fi.LastWriteTimeUtc.ToString("o")
            });
        }

        return new SidecarResponse(req.Id, new { entries });
    }

    private static SidecarResponse HandleInspectAsset(SidecarRequest req)
    {
        var assetPath = req.Params?.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter");

        // Resolve relative paths against active project's content directory
        if (!Path.IsPathRooted(assetPath))
        {
            var active2 = _sidecarProjects!.GetActiveProject();
            if (active2 != null)
            {
                var projCfg = new WellVersedConfig { ProjectPath = active2.ProjectPath };
                var resolved = Path.Combine(projCfg.ContentPath, assetPath);
                if (File.Exists(resolved))
                    assetPath = resolved;
            }
        }

        if (!File.Exists(assetPath))
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"File not found: {assetPath}"));

        var result = BuildActiveProjectServices(out var errorResponse, req.Id);
        if (result == null) return errorResponse!;

        var (cfg, assetService, _, fileAccess) = result.Value;
        try
        {
            var detail = assetService.InspectAsset(assetPath);

            // Build response with property cap of 50 per export
            var exports = detail.Exports.Select(e => new
            {
                className = e.ClassName,
                objectName = e.ObjectName,
                propertyCount = e.Properties.Count,
                properties = e.Properties.Take(50).Select(p => new
                {
                    name = p.Name,
                    value = p.Value,
                    type = p.Type
                })
            });

            return new SidecarResponse(req.Id, new
            {
                name = detail.Name,
                assetClass = detail.AssetClass,
                fileSize = detail.FileSize,
                exportCount = detail.ExportCount,
                importCount = detail.ImportCount,
                exports
            });
        }
        finally
        {
            fileAccess.Dispose();
        }
    }

    // Cache for device scan results — keyed by levelPath
    private static readonly Dictionary<string, (DateTime scannedAt, object result)> _deviceCache = new();

    private static SidecarResponse HandleListDevices(SidecarRequest req)
    {
        var levelPath = req.Params?.GetProperty("levelPath").GetString()
            ?? throw new ArgumentException("Missing 'levelPath' parameter");

        if (!File.Exists(levelPath))
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Level not found: {levelPath}"));

        // Return cached result if scanned within the last 5 minutes
        var noCache = req.Params?.TryGetProperty("noCache", out var nc) == true && nc.GetBoolean();
        if (!noCache && _deviceCache.TryGetValue(levelPath, out var cached) && (DateTime.UtcNow - cached.scannedAt).TotalMinutes < 5)
        {
            return new SidecarResponse(req.Id, cached.result);
        }

        var result = BuildActiveProjectServices(out var errorResponse, req.Id);
        if (result == null) return errorResponse!;

        var (cfg, assetService, deviceService, fileAccess) = result.Value;
        try
        {
            // First try DeviceService (scans .umap exports)
            var devices = deviceService.ListDevicesInLevel(levelPath);

            // If empty, scan __ExternalActors__ directory (UEFN stores actors as separate files)
            if (devices.Count == 0)
            {
                var levelName = Path.GetFileNameWithoutExtension(levelPath);
                var contentDir = Path.GetDirectoryName(levelPath) ?? "";
                var externalActorsDir = Path.Combine(
                    Path.GetDirectoryName(contentDir) ?? contentDir,
                    "__ExternalActors__",
                    Path.GetRelativePath(Path.GetDirectoryName(contentDir) ?? contentDir, contentDir),
                    levelName);

                // Also try directly under Content/__ExternalActors__/<levelName>
                if (!Directory.Exists(externalActorsDir))
                {
                    var pluginContentDir = cfg.ContentPath;
                    if (!string.IsNullOrEmpty(pluginContentDir))
                    {
                        // Search for __ExternalActors__ containing the level name
                        var candidates = Directory.EnumerateDirectories(pluginContentDir, "__ExternalActors__", SearchOption.AllDirectories);
                        foreach (var candidate in candidates)
                        {
                            var sub = Directory.EnumerateDirectories(candidate, levelName, SearchOption.AllDirectories).FirstOrDefault();
                            if (sub != null) { externalActorsDir = sub; break; }
                        }
                    }
                }

                if (Directory.Exists(externalActorsDir))
                {
                    var actorFiles = Directory.EnumerateFiles(externalActorsDir, "*.uasset", SearchOption.AllDirectories).ToList();
                    foreach (var actorFile in actorFiles)
                    {
                        try
                        {
                            var asset = assetService.OpenAsset(actorFile);
                            // Only look at root-level exports (OuterIndex == 0 means top-level actor)
                            var rootExport = asset.Exports.FirstOrDefault(e =>
                                e.OuterIndex.Index == 0 || !asset.Exports.Any(parent =>
                                    asset.Exports.IndexOf(e) != asset.Exports.IndexOf(parent) &&
                                    e.OuterIndex.Index == asset.Exports.IndexOf(parent) + 1));

                            if (rootExport == null) rootExport = asset.Exports.FirstOrDefault();
                            if (rootExport == null) continue;

                            var className = rootExport.GetExportClassType()?.ToString() ?? "";
                            if (string.IsNullOrEmpty(className)) continue;

                            // Extract transform — search ALL exports for RelativeLocation (usually on a component)
                            float x = 0, y = 0, z = 0;
                            string label = "";

                            foreach (var export in asset.Exports)
                            {
                                if (export is not UAssetAPI.ExportTypes.NormalExport ne) continue;
                                foreach (var prop in ne.Data)
                                {
                                    var propName = prop.Name.ToString();
                                    if (propName == "RelativeLocation" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData locStruct)
                                    {
                                        foreach (var sub in locStruct.Value)
                                        {
                                            var sn = sub.Name.ToString();
                                            var subType = sub.GetType().Name;
                                            // UE5.4 may use VectorPropertyData which has X/Y/Z directly
                                            if (sub is UAssetAPI.PropertyTypes.Structs.VectorPropertyData vecProp)
                                            {
                                                x = (float)vecProp.Value.X;
                                                y = (float)vecProp.Value.Y;
                                                z = (float)vecProp.Value.Z;
                                            }
                                            else if (sn == "X") { if (sub is UAssetAPI.PropertyTypes.Objects.FloatPropertyData f) x = f.Value; else if (sub is UAssetAPI.PropertyTypes.Objects.DoublePropertyData d) x = (float)d.Value; }
                                            else if (sn == "Y") { if (sub is UAssetAPI.PropertyTypes.Objects.FloatPropertyData f) y = f.Value; else if (sub is UAssetAPI.PropertyTypes.Objects.DoublePropertyData d) y = (float)d.Value; }
                                            else if (sn == "Z") { if (sub is UAssetAPI.PropertyTypes.Objects.FloatPropertyData f) z = f.Value; else if (sub is UAssetAPI.PropertyTypes.Objects.DoublePropertyData d) z = (float)d.Value; }
                                        }
                                        // empty — debug removed
                                    }
                                    else if (propName == "ActorLabel" && prop is UAssetAPI.PropertyTypes.Objects.StrPropertyData strProp)
                                    {
                                        label = strProp.Value?.ToString() ?? "";
                                    }
                                }
                                // Stop once we found a position
                                if (x != 0 || y != 0 || z != 0) break;
                            }

                            var prettyType = className
                                .Replace("BP_", "").Replace("PBWA_", "").Replace("_C", "")
                                .Replace("B_", "");

                            var actorName = !string.IsNullOrEmpty(label) ? label
                                : rootExport.ObjectName?.ToString()
                                ?? Path.GetFileNameWithoutExtension(actorFile);

                            devices.Add(new DeviceInfo
                            {
                                ActorName = actorName,
                                DeviceClass = className,
                                DeviceType = prettyType,
                                Label = label,
                                Location = new Vector3Info(x, y, z),
                                LevelPath = actorFile,
                            });
                        }
                        catch { /* skip unparseable actors */ }
                    }
                }
            }

            var deviceList = devices.Select(d => new
            {
                name = !string.IsNullOrEmpty(d.Label) ? d.Label : d.ActorName,
                filePath = d.LevelPath ?? levelPath,
                deviceType = !string.IsNullOrEmpty(d.DeviceType) ? d.DeviceType : d.DeviceClass,
                deviceClass = d.DeviceClass,
                position = new { x = (double)d.Location.X, y = (double)d.Location.Y, z = (double)d.Location.Z },
            }).ToList();

            var responseData = new { levelPath, devices = deviceList };
            _deviceCache[levelPath] = (DateTime.UtcNow, responseData);
            return new SidecarResponse(req.Id, responseData);
        }
        finally
        {
            fileAccess.Dispose();
        }
    }

    private static SidecarResponse HandleInspectDevice(SidecarRequest req)
    {
        var devicePath = req.Params?.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter");

        if (!File.Exists(devicePath))
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"File not found: {devicePath}"));

        var result = BuildActiveProjectServices(out var errorResponse, req.Id);
        if (result == null) return errorResponse!;

        var (cfg, assetService, _, fileAccess) = result.Value;
        try
        {
            // Use SafeFileAccess (via AssetService) to parse the external actor
            var asset = assetService.OpenAsset(devicePath);
            string? actorClass = null;
            string? displayName = null;
            var properties = new List<object>();
            double x = 0, y = 0, z = 0;
            double rotYaw = 0;

            foreach (var export in asset.Exports)
            {
                if (export is not UAssetAPI.ExportTypes.NormalExport ne) continue;

                var className = ne.GetExportClassType()?.ToString() ?? "";
                var objName = ne.ObjectName?.ToString() ?? "";

                // Identify the primary actor
                if (!className.Contains("Component") && !className.Contains("Model")
                    && !className.Contains("HLODLayer") && !className.Contains("MetaData")
                    && !className.Contains("Level") && !className.Contains("Brush")
                    && actorClass == null)
                {
                    actorClass = className;
                    displayName = CleanActorNameSimple(objName, className);
                }

                // Extract properties from each export
                foreach (var prop in ne.Data)
                {
                    var name = prop.Name?.ToString();
                    if (string.IsNullOrEmpty(name) || name == "None") continue;

                    // Extract position
                    if (name == "RelativeLocation" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData locStruct)
                    {
                        foreach (var c in locStruct.Value)
                        {
                            var cn = c.Name?.ToString() ?? "";
                            if (c is UAssetAPI.PropertyTypes.Objects.DoublePropertyData d)
                            {
                                switch (cn) { case "X": x = d.Value; break; case "Y": y = d.Value; break; case "Z": z = d.Value; break; }
                            }
                            else if (c is UAssetAPI.PropertyTypes.Objects.FloatPropertyData f)
                            {
                                switch (cn) { case "X": x = f.Value; break; case "Y": y = f.Value; break; case "Z": z = f.Value; break; }
                            }
                        }
                    }

                    // Extract rotation yaw
                    if (name == "RelativeRotation" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData rotStruct)
                    {
                        foreach (var c in rotStruct.Value)
                        {
                            if (c.Name?.ToString() == "Y" || c.Name?.ToString() == "Yaw")
                            {
                                if (c is UAssetAPI.PropertyTypes.Objects.DoublePropertyData d) rotYaw = d.Value;
                                else if (c is UAssetAPI.PropertyTypes.Objects.FloatPropertyData f) rotYaw = f.Value;
                            }
                        }
                    }

                    // Convert property to a simple representation
                    var value = SafePropertyValue(prop);
                    var isEditable = prop is UAssetAPI.PropertyTypes.Objects.BoolPropertyData
                        or UAssetAPI.PropertyTypes.Objects.IntPropertyData
                        or UAssetAPI.PropertyTypes.Objects.FloatPropertyData
                        or UAssetAPI.PropertyTypes.Objects.DoublePropertyData
                        or UAssetAPI.PropertyTypes.Objects.StrPropertyData
                        or UAssetAPI.PropertyTypes.Objects.NamePropertyData
                        or UAssetAPI.PropertyTypes.Objects.EnumPropertyData
                        or UAssetAPI.PropertyTypes.Objects.BytePropertyData;

                    properties.Add(new
                    {
                        name,
                        value,
                        type = prop.PropertyType?.ToString() ?? prop.GetType().Name,
                        componentClass = className,
                        isEditable
                    });
                }
            }

            return new SidecarResponse(req.Id, new
            {
                className = actorClass ?? "Unknown",
                displayName = displayName ?? Path.GetFileNameWithoutExtension(devicePath),
                properties,
                position = new { x, y, z },
                rotationYaw = rotYaw
            });
        }
        finally
        {
            fileAccess.Dispose();
        }
    }

    private static SidecarResponse HandleListUserAssets(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
        if (!Directory.Exists(cfg.ContentPath))
            return new SidecarResponse(req.Id, new { assets = Array.Empty<object>() });

        var files = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
            .Where(f => !f.Contains("__External"))
            .ToList();

        var assets = new List<object>();
        foreach (var file in files)
        {
            var fi = new FileInfo(file);
            string assetClass = "Unknown";
            try
            {
                // Lightweight parse for class name only — using UAssetAPI directly here is
                // acceptable because user-created assets in the Plugins/Content folder are
                // small definition files. We do NOT use SafeFileAccess for the listing scan
                // to avoid copying potentially hundreds of files to temp.
                // For actual inspection, always use inspect-asset which goes through SafeFileAccess.
                var asset = new UAssetAPI.UAsset(file, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                assetClass = asset.Exports.FirstOrDefault()?.GetExportClassType()?.ToString() ?? "Unknown";
            }
            catch { }

            assets.Add(new
            {
                name = Path.GetFileNameWithoutExtension(file),
                path = fi.FullName,
                relativePath = Path.GetRelativePath(cfg.ContentPath, file),
                assetClass,
                fileSize = fi.Length
            });
        }

        return new SidecarResponse(req.Id, new { assets });
    }

    private static SidecarResponse HandleListEpicAssets(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
        if (!Directory.Exists(cfg.ContentPath))
            return new SidecarResponse(req.Id, new { types = Array.Empty<object>() });

        // Optional level filter
        string? levelFilter = null;
        if (req.Params?.TryGetProperty("levelPath", out var levelEl) == true)
            levelFilter = levelEl.GetString();

        // Find external actor directories
        var extDirs = Directory.EnumerateDirectories(cfg.ContentPath, "__ExternalActors__", SearchOption.AllDirectories).ToList();
        var classCounts = new Dictionary<string, (int Count, List<string> SamplePaths)>(StringComparer.OrdinalIgnoreCase);

        foreach (var extDir in extDirs)
        {
            // If level filter specified, only look at that level's external actors
            if (!string.IsNullOrEmpty(levelFilter))
            {
                var levelName = Path.GetFileNameWithoutExtension(levelFilter);
                var levelExtDir = Path.Combine(Path.GetDirectoryName(levelFilter) ?? "",
                    "__ExternalActors__", levelName);
                // Only process if this extDir matches
                if (!extDir.EndsWith(levelName, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var actorFiles = Directory.EnumerateFiles(extDir, "*.uasset", SearchOption.AllDirectories);
            foreach (var file in actorFiles)
            {
                try
                {
                    var asset = new UAssetAPI.UAsset(file, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                    foreach (var export in asset.Exports)
                    {
                        var cls = export.GetExportClassType()?.ToString() ?? "";
                        if (string.IsNullOrEmpty(cls) || cls.Contains("Component") || cls.Contains("Model")
                            || cls == "Level" || cls == "MetaData" || cls == "Brush") continue;

                        if (!classCounts.ContainsKey(cls))
                            classCounts[cls] = (0, new List<string>());

                        var entry = classCounts[cls];
                        classCounts[cls] = (entry.Count + 1, entry.SamplePaths);
                        if (entry.SamplePaths.Count < 5) // Keep a few sample file paths for inspection
                            entry.SamplePaths.Add(file);
                        break; // Only count primary export per file
                    }
                }
                catch { }
            }
        }

        var types = classCounts
            .Select(kv => new Dictionary<string, object>
            {
                ["className"] = kv.Key,
                ["displayName"] = CleanActorNameSimple(kv.Key, kv.Key),
                ["count"] = kv.Value.Count,
                ["isDevice"] = IsDeviceSimple(kv.Key),
                ["samplePaths"] = kv.Value.SamplePaths.ToArray()
            })
            .OrderByDescending(t => (int)t["count"])
            .ToList();

        return new SidecarResponse(req.Id, new { types });
    }

    private static SidecarResponse HandleReadVerse(SidecarRequest req)
    {
        var versePath = req.Params?.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter");

        // Resolve relative paths against active project's content directory
        if (!Path.IsPathRooted(versePath))
        {
            var pm = _sidecarProjects!;
            var active = pm.GetActiveProject();
            if (active != null)
            {
                var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
                var resolved = Path.Combine(cfg.ContentPath, versePath);
                if (File.Exists(resolved))
                    versePath = resolved;
            }
        }

        if (!File.Exists(versePath))
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"File not found: {versePath}"));

        if (!versePath.EndsWith(".verse", StringComparison.OrdinalIgnoreCase))
            return new SidecarResponse(req.Id, Error: new SidecarError("INVALID_TYPE", "File must be a .verse file"));

        var source = File.ReadAllText(versePath);
        var lineCount = source.Split('\n').Length;

        return new SidecarResponse(req.Id, new
        {
            name = Path.GetFileNameWithoutExtension(versePath),
            source,
            lineCount
        });
    }

    private static SidecarResponse HandleListStaged(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath, ReadOnly = true, StagingDirectory = Path.Combine(active.ProjectPath, ".wellversed", "staged") };
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
        var fileAccess = new SafeFileAccess(cfg, detector, loggerFactory.CreateLogger<SafeFileAccess>());

        try
        {
            var staged = fileAccess.ListStagedFiles();
            var files = staged.Select(s => new
            {
                path = s.StagedPath,
                relativePath = s.RelativePath,
                size = s.Size
            }).ToList();

            return new SidecarResponse(req.Id, new { files });
        }
        finally
        {
            fileAccess.Dispose();
        }
    }

    // ========= Simple Helpers for Sidecar Handlers (avoid Web dependency) =========

    /// <summary>
    /// Simplified actor name cleaning — mirrors DeviceClassifier.CleanActorName
    /// without taking a dependency on WellVersed.Web.
    /// </summary>
    private static string CleanActorNameSimple(string rawName, string className)
    {
        var name = className;
        name = name.Replace("Device_", "").Replace("_C", "");

        var uaidIdx = rawName.IndexOf("_UAID_", StringComparison.OrdinalIgnoreCase);
        if (uaidIdx > 0)
            name = rawName[..uaidIdx].Replace("Device_", "").TrimEnd('_');

        name = name.Replace("_V2", " V2").Replace("_V3", " V3")
                    .Replace("_", " ")
                    .Replace("  ", " ")
                    .Trim();

        if (name.EndsWith(" C")) name = name[..^2].Trim();
        return name;
    }

    /// <summary>
    /// Simplified device detection — mirrors DeviceClassifier.IsDevice
    /// without taking a dependency on WellVersed.Web.
    /// </summary>
    private static bool IsDeviceSimple(string className)
    {
        var notPrefixes = new[] { "Prop_", "Athena_Prop_", "CP_Prop_", "CP_Apollo_", "CP_Asteria_",
            "StaticMesh", "Material", "Texture", "Landscape", "Foliage", "HLOD", "LayerInfo" };
        if (notPrefixes.Any(p => className.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        var devicePrefixes = new[] { "Device_", "BP_Device_", "BP_Creative_" };
        if (devicePrefixes.Any(p => className.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        var keywords = new[] { "Spawner", "Timer", "Trigger", "Button", "Barrier",
            "Checkpoint", "VendingMachine", "ClassSelector", "ClassDesigner",
            "ItemSpawner", "ScoreManager", "Mutator", "Teleporter",
            "Sequencer", "Tracker", "Elimination", "StormController",
            "Transmitter", "Receiver", "creative_device" };
        return keywords.Any(kw => className.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Safe property value extraction — handles common types with try-catch fallback.
    /// </summary>
    private static string SafePropertyValue(UAssetAPI.PropertyTypes.Objects.PropertyData prop)
    {
        try
        {
            return prop switch
            {
                UAssetAPI.PropertyTypes.Objects.BoolPropertyData b => b.Value.ToString(),
                UAssetAPI.PropertyTypes.Objects.IntPropertyData i => i.Value.ToString(),
                UAssetAPI.PropertyTypes.Objects.FloatPropertyData f => f.Value.ToString("G"),
                UAssetAPI.PropertyTypes.Objects.DoublePropertyData d => d.Value.ToString("G"),
                UAssetAPI.PropertyTypes.Objects.StrPropertyData s => s.Value?.ToString() ?? "",
                UAssetAPI.PropertyTypes.Objects.NamePropertyData n => n.Value?.ToString() ?? "",
                UAssetAPI.PropertyTypes.Objects.TextPropertyData t => t.Value?.ToString() ?? "",
                UAssetAPI.PropertyTypes.Objects.EnumPropertyData e => e.Value?.ToString() ?? "",
                UAssetAPI.PropertyTypes.Objects.BytePropertyData bp => bp.EnumValue?.ToString() ?? bp.Value.ToString(),
                UAssetAPI.PropertyTypes.Objects.ObjectPropertyData o => o.Value?.ToString() ?? "",
                UAssetAPI.PropertyTypes.Objects.SoftObjectPropertyData so => so.Value.AssetPath.PackageName?.ToString() ?? "",
                UAssetAPI.PropertyTypes.Structs.StructPropertyData sp => $"{{{sp.Value?.Count ?? 0} fields}}",
                UAssetAPI.PropertyTypes.Objects.SetPropertyData set => $"[{set.Value?.Length ?? 0} items]",
                UAssetAPI.PropertyTypes.Objects.ArrayPropertyData arr => $"[{arr.Value?.Length ?? 0} items]",
                UAssetAPI.PropertyTypes.Objects.MapPropertyData map => $"[{map.Value?.Count ?? 0} entries]",
                _ => prop.ToString() ?? prop.GetType().Name
            };
        }
        catch
        {
            return $"[{prop.GetType().Name}]";
        }
    }

    private static SidecarResponse HandleValidateSpec(SidecarRequest req)
    {
        var specJson = req.Params?.GetProperty("spec").GetRawText()
            ?? throw new ArgumentException("Missing 'spec' parameter");

        var spec = WidgetSpecSerializer.FromJson(specJson);
        var errors = spec.Validate();

        return new SidecarResponse(req.Id, new
        {
            valid = !errors.Any(e => e.Severity == WidgetValidationSeverity.Error),
            errors = errors.Select(e => new { path = e.Path, severity = e.Severity.ToString(), message = e.Message })
        });
    }

    private static SidecarResponse HandleBuildUasset(SidecarRequest req, WellVersedConfig config)
    {
        var specJson = req.Params?.GetProperty("spec").GetRawText()
            ?? throw new ArgumentException("Missing 'spec' parameter");
        var outputDir = req.Params?.GetProperty("outputDir").GetString()
            ?? throw new ArgumentException("Missing 'outputDir' parameter");

        var spec = WidgetSpecSerializer.FromJson(specJson);

        // Apply variable overrides if provided
        if (req.Params?.TryGetProperty("variables", out var varsEl) == true)
        {
            var vars = JsonSerializer.Deserialize<Dictionary<string, string>>(varsEl.GetRawText()) ?? new();
            WidgetSpecSerializer.ApplyVariables(spec, vars);
        }

        // Find template
        var templateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
            "umg_widget_library", "original_UMG_uassets", "minimal");
        var templatePath = Directory.GetFiles(templateDir, "*.uasset").FirstOrDefault()
            ?? throw new FileNotFoundException("No minimal widget template found");

        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, spec.Name + ".uasset");

        var builder = new WidgetBlueprintBuilder();
        builder.Build(spec, templatePath, outputPath);

        // Also generate Verse controller
        var verseCode = WidgetSpecVerseGenerator.Generate(spec);
        var versePath = Path.Combine(outputDir, spec.Name + "_controller.verse");
        File.WriteAllText(versePath, verseCode);

        return new SidecarResponse(req.Id, new
        {
            success = true,
            uassetPath = outputPath,
            versePath = versePath
        });
    }

    private static SidecarResponse HandleGenerateVerse(SidecarRequest req)
    {
        var specJson = req.Params?.GetProperty("spec").GetRawText()
            ?? throw new ArgumentException("Missing 'spec' parameter");

        var spec = WidgetSpecSerializer.FromJson(specJson);
        var verseCode = WidgetSpecVerseGenerator.Generate(spec);

        return new SidecarResponse(req.Id, new { code = verseCode });
    }

    // ========= Widget Parsing Handlers =========

    private static SidecarResponse HandleListProjectWidgets(SidecarRequest req)
    {
        var pm = _sidecarProjects!;
        var active = pm.GetActiveProject();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
        if (!Directory.Exists(cfg.ContentPath))
            return new SidecarResponse(req.Id, new { widgets = Array.Empty<object>() });

        var widgets = new List<object>();
        var files = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
            .Where(f => !f.Contains("__External"));

        foreach (var file in files)
        {
            try
            {
                var asset = new UAssetAPI.UAsset(file, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                var summary = WidgetBlueprintParser.GetSummary(asset);
                if (summary != null)
                {
                    widgets.Add(new
                    {
                        name = summary.Value.name,
                        path = file,
                        widgetCount = summary.Value.widgetCount
                    });
                }
            }
            catch { /* Skip unparseable files */ }
        }

        return new SidecarResponse(req.Id, new { widgets });
    }

    private static SidecarResponse HandleParseWidget(SidecarRequest req)
    {
        var path = req.Params?.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter");

        if (!File.Exists(path))
            return new SidecarResponse(req.Id, Error: new SidecarError("FILE_NOT_FOUND", $"File not found: {path}"));

        // Build a temporary SafeFileAccess for copy-on-read
        var config = new WellVersedConfig { ProjectPath = Path.GetDirectoryName(path) ?? "", ReadOnly = true };
        var detector = new UefnDetector(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<UefnDetector>.Instance);
        using var fileAccess = new SafeFileAccess(config, detector,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SafeFileAccess>.Instance);

        // Try parsing with detailed error reporting
        UAssetAPI.UAsset asset;
        try
        {
            asset = fileAccess.OpenForRead(path);
        }
        catch (Exception ex)
        {
            return new SidecarResponse(req.Id, Error: new SidecarError("READ_FAILED",
                $"Could not read .uasset: {ex.Message}"));
        }

        var parser = new WidgetBlueprintParser();

        // Check if it's actually a widget blueprint
        if (!WidgetBlueprintParser.IsWidgetBlueprint(asset))
        {
            var exportTypes = string.Join(", ",
                asset.Exports.Select(e => e.GetExportClassType()?.ToString() ?? "null").Distinct().Take(10));
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_WIDGET",
                $"Not a widget blueprint. Export types: [{exportTypes}]"));
        }

        var spec = parser.ParseFromAsset(asset, Path.GetFileNameWithoutExtension(path));

        if (spec == null)
        {
            var diagnosis = parser.DiagnoseParseFailure(asset) ?? "ParseWidgetExport returned null";

            // Dump export info for debugging
            var exportDump = new List<object>();
            for (int i = 0; i < asset.Exports.Count; i++)
            {
                var exp = asset.Exports[i];
                var ne = exp as UAssetAPI.ExportTypes.NormalExport;
                exportDump.Add(new {
                    index = i,
                    type = exp.GetExportClassType()?.ToString() ?? "null",
                    name = exp.ObjectName?.ToString() ?? "null",
                    serialOffset = exp.SerialOffset,
                    serialSize = exp.SerialSize,
                    scriptStart = ne?.ScriptSerializationStartOffset ?? -1,
                    scriptEnd = ne?.ScriptSerializationEndOffset ?? -1,
                    propertyCount = ne?.Data?.Count ?? -1,
                    isNormalExport = ne != null,
                });
            }

            return new SidecarResponse(req.Id, Error: new SidecarError("PARSE_FAILED",
                $"Parse failed: {diagnosis}",
                new { exports = exportDump }));
        }

        var specJson = WidgetSpecSerializer.ToJson(spec);

        return new SidecarResponse(req.Id, new { spec = System.Text.Json.JsonDocument.Parse(specJson).RootElement });
    }

    private static SidecarResponse HandleWidgetTexture(SidecarRequest req, WellVersedConfig config)
    {
        var texRef = req.Params?.GetProperty("texturePath").GetString()
            ?? throw new ArgumentException("Missing 'texturePath' parameter");

        var projectPath = config.ProjectPath;
        if (string.IsNullOrEmpty(projectPath))
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

        // Resolve texture reference to file path
        // Format: /PluginName/path/to/texture.textureName  or  /path/to/texture.textureName
        var parts = texRef.TrimStart('/').Split('.');
        var assetPath = parts[0]; // e.g., "lwky_2v2v2/icons_imgs/CustomWidgetImgs/blueTeamIcon"

        // Try multiple resolution strategies
        var candidates = new[]
        {
            Path.Combine(projectPath, "Content", assetPath.Replace('/', Path.DirectorySeparatorChar) + ".uasset"),
            Path.Combine(projectPath, "Plugins", assetPath.Replace('/', Path.DirectorySeparatorChar).Insert(assetPath.IndexOf('/'), "/Content") + ".uasset"),
        };

        string? foundPath = null;
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) { foundPath = candidate; break; }
        }

        if (foundPath == null)
        {
            // Try searching Content directory recursively for the asset name
            var assetName = Path.GetFileName(assetPath) + ".uasset";
            var contentDir = Path.Combine(projectPath, "Content");
            if (Directory.Exists(contentDir))
            {
                var found = Directory.GetFiles(contentDir, assetName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) foundPath = found;
            }
        }

        if (foundPath == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Texture not found: {texRef}"));

        try
        {
            // Read the .uasset to find original source file path
            var asset = new UAssetAPI.UAsset(foundPath, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);

            // Check InterchangeAssetImportData for source file path
            foreach (var export in asset.Exports)
            {
                if (export is not UAssetAPI.ExportTypes.NormalExport ne) continue;
                var nodeId = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "NodeUniqueID")
                    as UAssetAPI.PropertyTypes.Objects.StrPropertyData;
                if (nodeId?.Value != null)
                {
                    // Format: "Factory_C:/Users/Luke/Downloads/blueTeamIcon.png"
                    var sourcePath = nodeId.Value.ToString().Replace("Factory_", "");
                    if (File.Exists(sourcePath))
                    {
                        var bytes = File.ReadAllBytes(sourcePath);
                        var ext = Path.GetExtension(sourcePath).ToLower();
                        var mime = ext switch
                        {
                            ".png" => "image/png",
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".tga" => "image/tga",
                            _ => "application/octet-stream"
                        };

                        // Check size — warn if > 5MB
                        var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                        return new SidecarResponse(req.Id, new
                        {
                            found = true,
                            dataUrl,
                            sourcePath,
                            sizeBytes = bytes.Length,
                            warning = bytes.Length > 5_000_000 ? $"Large texture: {bytes.Length / 1_000_000}MB" : (string?)null,
                        });
                    }
                }
            }

            // Source file not found — check for PNG/JPG next to the .uasset
            var dir = Path.GetDirectoryName(foundPath)!;
            var baseName = Path.GetFileNameWithoutExtension(foundPath);
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tga" })
            {
                var imgPath = Path.Combine(dir, baseName + ext);
                if (File.Exists(imgPath))
                {
                    var bytes = File.ReadAllBytes(imgPath);
                    var mime = ext == ".png" ? "image/png" : "image/jpeg";
                    return new SidecarResponse(req.Id, new
                    {
                        found = true,
                        dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}",
                        sourcePath = imgPath,
                        sizeBytes = bytes.Length,
                    });
                }
            }

            // No source image found — return dimensions from the Texture2D export
            var tex2d = asset.Exports.FirstOrDefault(e => e.GetExportClassType()?.ToString() == "Texture2D") as UAssetAPI.ExportTypes.NormalExport;
            int texW = 0, texH = 0;
            if (tex2d != null)
            {
                var source = tex2d.Data.FirstOrDefault(p => p.Name?.ToString() == "Source") as UAssetAPI.PropertyTypes.Structs.StructPropertyData;
                if (source?.Value != null)
                {
                    var sizeX = source.Value.FirstOrDefault(p => p.Name?.ToString() == "SizeX") as UAssetAPI.PropertyTypes.Objects.IntPropertyData;
                    var sizeY = source.Value.FirstOrDefault(p => p.Name?.ToString() == "SizeY") as UAssetAPI.PropertyTypes.Objects.IntPropertyData;
                    texW = sizeX?.Value ?? 0;
                    texH = sizeY?.Value ?? 0;
                }
            }

            return new SidecarResponse(req.Id, new
            {
                found = false,
                assetPath = foundPath,
                width = texW,
                height = texH,
                message = "Texture data is Oodle-compressed in .uasset; source image file not found",
            });
        }
        catch (Exception ex)
        {
            return new SidecarResponse(req.Id, Error: new SidecarError("READ_FAILED", ex.Message));
        }
    }

    private static SidecarResponse HandleListLibraryWidgets(SidecarRequest req)
    {
        var lm = _sidecarLibraries!;
        var active = lm.GetActiveLibrary();
        if (active == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_LIBRARY", "No active library"));

        var widgets = new List<object>();

        // Scan library path for widget blueprints
        if (Directory.Exists(active.Path))
        {
            var files = Directory.EnumerateFiles(active.Path, "*.uasset", SearchOption.AllDirectories)
                .Where(f => !f.Contains("__External"));

            foreach (var file in files)
            {
                try
                {
                    var asset = new UAssetAPI.UAsset(file, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                    var summary = WidgetBlueprintParser.GetSummary(asset);
                    if (summary != null)
                    {
                        widgets.Add(new
                        {
                            name = summary.Value.name,
                            path = file,
                            widgetCount = summary.Value.widgetCount
                        });
                    }
                }
                catch { }
            }
        }

        return new SidecarResponse(req.Id, new { widgets });
    }

    // ========= Sidecar Library Management Handlers =========

    private static SidecarResponse HandleListLibraries(SidecarRequest req)
    {
        var lm = _sidecarLibraries!;
        return new SidecarResponse(req.Id, new
        {
            activeLibraryId = lm.GetActiveLibrary()?.Id,
            libraries = lm.ListLibraries()
        });
    }

    private static SidecarResponse HandleAddLibrary(SidecarRequest req)
    {
        var path = req.Params?.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter");

        var lm = _sidecarLibraries!;
        var entry = lm.AddLibrary(path);
        return new SidecarResponse(req.Id, entry);
    }

    private static SidecarResponse HandleRemoveLibrary(SidecarRequest req)
    {
        var id = req.Params?.GetProperty("id").GetString()
            ?? throw new ArgumentException("Missing 'id' parameter");

        var lm = _sidecarLibraries!;
        var removed = lm.RemoveLibrary(id);
        return new SidecarResponse(req.Id, new { removed });
    }

    private static SidecarResponse HandleActivateLibrary(SidecarRequest req)
    {
        var id = req.Params?.GetProperty("id").GetString()
            ?? throw new ArgumentException("Missing 'id' parameter");

        var lm = _sidecarLibraries!;
        var entry = lm.SetActive(id);
        if (entry == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Library not found: {id}"));
        return new SidecarResponse(req.Id, entry);
    }

    private static SidecarResponse HandleIndexLibrary(SidecarRequest req)
    {
        var lm = _sidecarLibraries!;

        // Use explicit id or fall back to active library
        string? id = null;
        if (req.Params?.TryGetProperty("id", out var idEl) == true)
            id = idEl.GetString();

        var lib = id != null ? lm.ListLibraries().FirstOrDefault(l => l.Id == id) : lm.GetActiveLibrary();
        if (lib == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_LIBRARY", "No active library"));

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var indexer = new LibraryIndexer(loggerFactory.CreateLogger<LibraryIndexer>());

        var index = indexer.BuildIndex(lib.Path);

        // Save index to ~/.wellversed/library-index-{id}.json
        var indexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wellversed", $"library-index-{lib.Id}.json");
        indexer.SaveIndex(indexPath);

        // Update library entry stats
        lm.UpdateStats(lib.Id, index.TotalVerseFiles, index.TotalAssets, DateTime.UtcNow);

        return new SidecarResponse(req.Id, new
        {
            libraryId = lib.Id,
            libraryName = lib.Name,
            indexPath,
            totalProjects = index.Projects.Count,
            totalVerseFiles = index.TotalVerseFiles,
            totalAssets = index.TotalAssets,
            totalDeviceTypes = index.TotalDeviceTypes,
            indexedAt = index.IndexedAt.ToString("o")
        });
    }

    private static SidecarResponse HandleGetLibraryVerseFiles(SidecarRequest req)
    {
        var lm = _sidecarLibraries!;
        var lib = lm.GetActiveLibrary();
        if (lib == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_LIBRARY", "No active library"));

        var indexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wellversed", $"library-index-{lib.Id}.json");

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var indexer = new LibraryIndexer(loggerFactory.CreateLogger<LibraryIndexer>());
        var index = indexer.LoadIndex(indexPath);
        if (index == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INDEXED", "Library not indexed yet. Call index-library first."));

        string? filter = null;
        if (req.Params?.TryGetProperty("filter", out var filterEl) == true)
            filter = filterEl.GetString();

        var verseFiles = indexer.GetVerseFiles(filter);

        return new SidecarResponse(req.Id, new
        {
            verseFiles = verseFiles.Select(vf => new
            {
                name = vf.File.Name,
                filePath = vf.File.FilePath,
                lineCount = vf.File.LineCount,
                classes = vf.File.Classes,
                functions = vf.File.Functions,
                deviceReferences = vf.File.DeviceReferences,
                imports = vf.File.Imports,
                summary = vf.File.Summary,
                projectName = vf.ProjectName
            }).ToList()
        });
    }

    private static SidecarResponse HandleGetLibraryAssetsByType(SidecarRequest req)
    {
        var lm = _sidecarLibraries!;
        var lib = lm.GetActiveLibrary();
        if (lib == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_LIBRARY", "No active library"));

        var indexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wellversed", $"library-index-{lib.Id}.json");

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var indexer = new LibraryIndexer(loggerFactory.CreateLogger<LibraryIndexer>());
        var index = indexer.LoadIndex(indexPath);
        if (index == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INDEXED", "Library not indexed yet. Call index-library first."));

        // Group all assets by class across all projects
        var allAssets = index.Projects.SelectMany(p =>
            p.Assets.Select(a => new { asset = a, projectName = p.Name }));

        var grouped = allAssets
            .GroupBy(a => a.asset.AssetClass)
            .Select(g => new
            {
                assetClass = g.Key,
                count = g.Count(),
                assets = g.Select(a => new
                {
                    name = a.asset.Name,
                    filePath = a.asset.FilePath,
                    assetClass = a.asset.AssetClass,
                    fileSize = a.asset.FileSize,
                    projectName = a.projectName
                }).ToList()
            })
            .OrderByDescending(g => g.count)
            .ToList();

        return new SidecarResponse(req.Id, new { groups = grouped });
    }

    private static SidecarResponse HandleBrowseLibraryDir(SidecarRequest req)
    {
        var lm = _sidecarLibraries!;
        var lib = lm.GetActiveLibrary();
        if (lib == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_LIBRARY", "No active library"));

        var basePath = lib.Path;

        string? subPath = null;
        if (req.Params?.TryGetProperty("path", out var pathEl) == true)
            subPath = pathEl.GetString();

        var targetPath = string.IsNullOrEmpty(subPath)
            ? basePath
            : Path.Combine(basePath, subPath);

        if (!Directory.Exists(targetPath))
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Directory not found: {targetPath}"));

        // Ensure the target is within the library path (prevent directory traversal)
        var fullTarget = Path.GetFullPath(targetPath);
        var fullBase = Path.GetFullPath(basePath);
        if (!fullTarget.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            return new SidecarResponse(req.Id, Error: new SidecarError("INVALID_PATH", "Path must be within library directory"));

        var entries = new List<object>();

        // Folders first
        foreach (var dir in Directory.EnumerateDirectories(targetPath).OrderBy(d => d))
        {
            var di = new DirectoryInfo(dir);
            entries.Add(new
            {
                name = di.Name,
                path = Path.GetRelativePath(basePath, dir),
                type = "folder",
                size = 0L,
                lastModified = di.LastWriteTimeUtc.ToString("o")
            });
        }

        // Then files
        foreach (var file in Directory.EnumerateFiles(targetPath).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
            var ext = fi.Extension.ToLowerInvariant();
            var fileType = ext switch
            {
                ".uasset" => "uasset",
                ".umap" => "umap",
                ".verse" => "verse",
                ".uexp" => "uexp",
                _ => "other"
            };

            entries.Add(new
            {
                name = fi.Name,
                path = Path.GetRelativePath(basePath, file),
                type = fileType,
                size = fi.Length,
                lastModified = fi.LastWriteTimeUtc.ToString("o")
            });
        }

        return new SidecarResponse(req.Id, new { entries });
    }

    private static SidecarResponse HandleSearchLibraryIndex(SidecarRequest req)
    {
        var lm = _sidecarLibraries!;
        var lib = lm.GetActiveLibrary();
        if (lib == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NO_LIBRARY", "No active library"));

        var query = req.Params?.GetProperty("query").GetString()
            ?? throw new ArgumentException("Missing 'query' parameter");

        var indexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wellversed", $"library-index-{lib.Id}.json");

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var indexer = new LibraryIndexer(loggerFactory.CreateLogger<LibraryIndexer>());
        var index = indexer.LoadIndex(indexPath);
        if (index == null)
            return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INDEXED", "Library not indexed yet. Call index-library first."));

        var result = indexer.Search(query);

        return new SidecarResponse(req.Id, new
        {
            query = result.Query,
            verseFiles = result.VerseFiles.Select(h => new
            {
                name = h.Item.Name,
                filePath = h.Item.FilePath,
                lineCount = h.Item.LineCount,
                classes = h.Item.Classes,
                functions = h.Item.Functions,
                deviceReferences = h.Item.DeviceReferences,
                imports = h.Item.Imports,
                summary = h.Item.Summary,
                projectName = h.ProjectName,
                score = h.Score
            }).ToList(),
            assets = result.Assets.Select(h => new
            {
                name = h.Item.Name,
                filePath = h.Item.FilePath,
                assetClass = h.Item.AssetClass,
                fileSize = h.Item.FileSize,
                projectName = h.ProjectName,
                score = h.Score
            }).ToList(),
            deviceTypes = result.DeviceTypes.Select(h => new
            {
                className = h.Item.ClassName,
                displayName = h.Item.DisplayName,
                count = h.Item.Count,
                projectName = h.ProjectName,
                score = h.Score
            }).ToList()
        });
    }

    private static string? FindConfigFile()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var configPath = Path.Combine(dir, "forge.config.json");
            if (File.Exists(configPath))
                return configPath;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

// ========= CUE4Parse Preview Handlers =========
// (handlers follow, then class closes)

static SidecarResponse HandlePreviewInit(SidecarRequest req)
{
    var path = req.Params?.GetProperty("fortnitePath").GetString() ?? throw new ArgumentException("Missing 'fortnitePath' parameter");
    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    _previewService?.Dispose();
    _previewService = new AssetPreviewService(loggerFactory.CreateLogger<AssetPreviewService>());
    var task = _previewService.InitializeAsync(path); task.Wait();
    if (task.Result)
    {
        // Create mesh export service alongside preview service
        _meshExportService = new MeshExportService(_previewService, loggerFactory.CreateLogger<MeshExportService>());
        return new SidecarResponse(req.Id, new { initialized = true, fileCount = _previewService.GetFileCount(), gamePath = _previewService.GamePath });
    }
    return new SidecarResponse(req.Id, Error: new SidecarError("INIT_FAILED", $"Could not find Fortnite PAK files at: {path}"));
}

static SidecarResponse HandlePreviewStatus(SidecarRequest req) =>
    new(req.Id, new { initialized = _previewService?.IsInitialized ?? false, fileCount = _previewService?.GetFileCount() ?? 0, gamePath = _previewService?.GamePath });

static SidecarResponse HandlePreviewSearch(SidecarRequest req)
{
    if (_previewService == null || !_previewService.IsInitialized) return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INITIALIZED", "Preview service not initialized."));
    var query = req.Params?.GetProperty("query").GetString() ?? throw new ArgumentException("Missing 'query'");
    var limit = 50; if (req.Params?.TryGetProperty("limit", out var lEl) == true) limit = lEl.GetInt32();
    var results = _previewService.SearchAssets(query, limit);
    return new SidecarResponse(req.Id, new { query, results, count = results.Count });
}

static SidecarResponse HandlePreviewTexture(SidecarRequest req)
{
    if (_previewService == null || !_previewService.IsInitialized) return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INITIALIZED", "Preview service not initialized."));
    var assetPath = req.Params?.GetProperty("assetPath").GetString() ?? throw new ArgumentException("Missing 'assetPath'");
    var pngBytes = _previewService.ExtractTexture(assetPath);
    if (pngBytes == null) return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Could not extract texture: {assetPath}"));
    return new SidecarResponse(req.Id, new { assetPath, dataUrl = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}", size = pngBytes.Length });
}

static SidecarResponse HandlePreviewMeshInfo(SidecarRequest req)
{
    if (_previewService == null || !_previewService.IsInitialized) return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INITIALIZED", "Preview service not initialized."));
    var assetPath = req.Params?.GetProperty("assetPath").GetString() ?? throw new ArgumentException("Missing 'assetPath'");
    var info = _previewService.GetMeshInfo(assetPath);
    if (info == null) return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Could not read mesh: {assetPath}"));
    return new SidecarResponse(req.Id, new { assetPath, vertexCount = info.VertexCount, triangleCount = info.TriangleCount, lodCount = info.LODCount, materialCount = info.MaterialCount });
}

static SidecarResponse HandlePreviewExportMesh(SidecarRequest req)
{
    if (_meshExportService == null) return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INITIALIZED", "Preview service not initialized. Call preview-init first."));
    var deviceClass = req.Params?.GetProperty("deviceClass").GetString() ?? throw new ArgumentException("Missing 'deviceClass'");
    var result = _meshExportService.GetOrExportMesh(deviceClass);
    if (result == null) return new SidecarResponse(req.Id, new { deviceClass, found = false });
    return new SidecarResponse(req.Id, new { deviceClass, found = true, glbBase64 = Convert.ToBase64String(result.GlbData), vertexCount = result.VertexCount, cached = result.Cached, assetPath = result.AssetPath, sizeBytes = result.GlbData.Length });
}

static SidecarResponse HandlePreviewExportMeshBatch(SidecarRequest req)
{
    if (_meshExportService == null) return new SidecarResponse(req.Id, Error: new SidecarError("NOT_INITIALIZED", "Preview service not initialized. Call preview-init first."));
    var classesElement = req.Params?.GetProperty("deviceClasses") ?? throw new ArgumentException("Missing 'deviceClasses'");
    var deviceClasses = classesElement.EnumerateArray().Select(e => e.GetString()!).Distinct().ToList();
    var results = new List<object>();
    var exportedCount = 0;
    foreach (var deviceClass in deviceClasses)
    {
        try
        {
            var result = _meshExportService.GetOrExportMesh(deviceClass);
            if (result != null)
            {
                results.Add(new { deviceClass, found = true, glbBase64 = Convert.ToBase64String(result.GlbData), vertexCount = result.VertexCount, cached = result.Cached, sizeBytes = result.GlbData.Length });
                exportedCount++;
            }
            else
                results.Add(new { deviceClass, found = false, error = "No mesh found" });
        }
        catch (Exception ex)
        {
            results.Add(new { deviceClass, found = false, error = ex.Message });
        }
    }
    return new SidecarResponse(req.Id, new { results, total = deviceClasses.Count, exported = exportedCount });
}

static SidecarResponse HandleAnalyzeLevelSystems(SidecarRequest req)
{
    var result = BuildActiveProjectServices(out var errorResponse, req.Id);
    if (result == null) return errorResponse!;
    var (cfg, _, deviceService, _) = result.Value;

    var levelPath = req.Params?.GetProperty("levelPath").GetString() ?? throw new ArgumentException("Missing 'levelPath'");
    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    var extractor = new SystemExtractor(cfg, deviceService, loggerFactory.CreateLogger<SystemExtractor>());
    var analysis = extractor.AnalyzeLevel(levelPath);
    return new SidecarResponse(req.Id, new
    {
        levelPath = Path.GetFileName(analysis.LevelPath),
        totalDevices = analysis.TotalDevices,
        systemsFound = analysis.Systems.Count,
        systems = analysis.Systems.Select(s => new
        {
            s.Name, s.Category, s.DetectionMethod, s.Confidence, s.DeviceCount,
            devices = s.Devices.Select(d => new { d.Role, d.DeviceClass, d.DeviceType, d.Label, offset = d.Offset.ToString(), propertyCount = d.Properties.Count }),
            wiring = s.Wiring.Select(w => new { connection = $"{w.SourceRole}.{w.OutputEvent} → {w.TargetRole}.{w.InputAction}", w.Channel })
        }),
        errors = analysis.Errors
    });
}

static SidecarResponse HandleAnalyzeProjectSystems(SidecarRequest req)
{
    var result = BuildActiveProjectServices(out var errorResponse, req.Id);
    if (result == null) return errorResponse!;
    var (cfg, _, deviceService, _) = result.Value;

    var projectPath = cfg.ProjectPath;
    if (req.Params?.TryGetProperty("projectPath", out var ppEl) == true)
        projectPath = ppEl.GetString() ?? projectPath;

    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    var extractor = new SystemExtractor(cfg, deviceService, loggerFactory.CreateLogger<SystemExtractor>());
    var analysis = extractor.AnalyzeProject(projectPath);
    return new SidecarResponse(req.Id, new
    {
        projectPath = Path.GetFileName(analysis.ProjectPath),
        levelsScanned = analysis.LevelCount,
        totalSystems = analysis.Systems.Count,
        uniquePatterns = analysis.UniqueSystems.Count,
        systems = analysis.UniqueSystems.Select(s => new
        {
            s.Name, s.Category, s.DetectionMethod, s.Confidence, s.DeviceCount, s.Frequency,
            sourceLevel = Path.GetFileName(s.SourceLevel ?? ""),
            deviceTypes = s.Devices.Select(d => d.DeviceType).Distinct(),
            wiringConnections = s.Wiring.Count
        }),
        errors = analysis.Errors
    });
}

// ========= Device Encyclopedia Handlers =========

static SidecarResponse HandleEncyclopediaSearch(SidecarRequest req)
{
    var query = req.Params?.GetProperty("query").GetString()
        ?? throw new ArgumentException("Missing 'query' parameter");

    var encyclopedia = BuildEncyclopediaService();
    var results = encyclopedia.SearchDevices(query);

    return new SidecarResponse(req.Id, new
    {
        query,
        resultCount = results.Count,
        results = results.Select(r => new
        {
            r.DeviceName,
            r.DisplayName,
            r.ParentClass,
            r.PropertyCount,
            r.EventCount,
            r.FunctionCount,
            r.HasCommonConfigs,
            r.UsageCount,
            matchedProperties = r.MatchedProperties.Count > 0 ? r.MatchedProperties : null,
            matchedEvents = r.MatchedEvents.Count > 0 ? r.MatchedEvents : null,
            r.MatchContext
        })
    });
}

static SidecarResponse HandleEncyclopediaDeviceReference(SidecarRequest req)
{
    var deviceClass = req.Params?.GetProperty("deviceClass").GetString()
        ?? throw new ArgumentException("Missing 'deviceClass' parameter");

    var encyclopedia = BuildEncyclopediaService();
    var reference = encyclopedia.GetDeviceReference(deviceClass);

    if (reference == null)
    {
        var suggestions = encyclopedia.SearchDevices(deviceClass);
        return new SidecarResponse(req.Id, new
        {
            error = $"No reference found for '{deviceClass}'.",
            suggestions = suggestions.Take(5).Select(s => new { s.DeviceName, s.DisplayName })
        });
    }

    return new SidecarResponse(req.Id, new
    {
        reference.DeviceName,
        reference.DisplayName,
        reference.ParentClass,
        reference.Description,
        reference.SourceFile,
        reference.TotalUsageCount,
        reference.ProjectsUsedIn,
        properties = reference.Properties.Select(p => new
        {
            p.Name,
            p.Type,
            p.DefaultValue,
            p.IsEditable,
            p.Description,
            usagePercent = p.UsagePercent > 0 ? p.UsagePercent : (double?)null,
            commonValues = p.CommonValues.Count > 0 ? p.CommonValues : null,
            relatedProperties = p.RelatedProperties.Count > 0 ? p.RelatedProperties : null
        }),
        reference.Events,
        reference.Functions,
        commonConfigurations = reference.CommonConfigurations.Count > 0
            ? reference.CommonConfigurations.Select(c => new
            {
                c.Name,
                c.Description,
                c.Properties,
                c.Tags
            })
            : null
    });
}

static SidecarResponse HandleEncyclopediaCommonConfigs(SidecarRequest req)
{
    var deviceClass = req.Params?.GetProperty("deviceClass").GetString()
        ?? throw new ArgumentException("Missing 'deviceClass' parameter");

    var encyclopedia = BuildEncyclopediaService();
    var configs = encyclopedia.GetCommonConfigurations(deviceClass);

    if (configs.Count == 0)
    {
        var allDevices = encyclopedia.ListAllDevices();
        var withConfigs = allDevices.Where(d => d.HasCommonConfigs).Select(d => d.DisplayName).ToList();
        return new SidecarResponse(req.Id, new
        {
            error = $"No common configurations found for '{deviceClass}'.",
            devicesWithConfigs = withConfigs
        });
    }

    return new SidecarResponse(req.Id, new
    {
        deviceClass,
        configCount = configs.Count,
        configurations = configs.Select(c => new
        {
            c.Name,
            c.Description,
            c.Properties,
            c.Tags
        })
    });
}

static SidecarResponse HandleEncyclopediaListDevices(SidecarRequest req)
{
    var encyclopedia = BuildEncyclopediaService();
    var devices = encyclopedia.ListAllDevices();

    return new SidecarResponse(req.Id, new
    {
        deviceCount = devices.Count,
        devices = devices.Select(d => new
        {
            d.Name,
            d.DisplayName,
            d.ParentClass,
            d.PropertyCount,
            d.EventCount,
            d.FunctionCount,
            d.UsageCount,
            d.HasCommonConfigs
        })
    });
}

static DeviceEncyclopedia BuildEncyclopediaService()
{
    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    var indexer = new LibraryIndexer(loggerFactory.CreateLogger<LibraryIndexer>());

    // Try to load active library index for usage stats
    var lm = _sidecarLibraries;
    if (lm != null)
    {
        var lib = lm.GetActiveLibrary();
        if (lib != null)
        {
            var indexPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".wellversed", $"library-index-{lib.Id}.json");
            indexer.LoadIndex(indexPath);
        }
    }

    // Build digest service from active project
    DigestService digestService;
    var pm = _sidecarProjects;
    var active = pm?.GetActiveProject();
    if (active != null)
    {
        var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
        digestService = new DigestService(cfg, loggerFactory.CreateLogger<DigestService>());
    }
    else
    {
        digestService = new DigestService(new WellVersedConfig(), loggerFactory.CreateLogger<DigestService>());
    }

    var encyclopedia = new DeviceEncyclopedia(digestService, indexer, loggerFactory.CreateLogger<DeviceEncyclopedia>());
    return encyclopedia;
}

// ========= Project Diff / Snapshot Handlers =========

static SidecarResponse HandleTakeSnapshot(SidecarRequest req)
{
    var pm = _sidecarProjects!;
    var active = pm.GetActiveProject();
    if (active == null)
        return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

    string? description = null;
    if (req.Params?.TryGetProperty("description", out var descEl) == true)
        description = descEl.GetString();

    var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
    var fileAccess = new SafeFileAccess(cfg, detector, loggerFactory.CreateLogger<SafeFileAccess>());
    var diffService = new ProjectDiffService(cfg, fileAccess, loggerFactory.CreateLogger<ProjectDiffService>());

    var snapshot = diffService.TakeSnapshot(active.ProjectPath, description);

    return new SidecarResponse(req.Id, new
    {
        id = snapshot.Id,
        description = snapshot.Description,
        timestamp = snapshot.Timestamp,
        projectName = snapshot.ProjectName,
        fileCount = snapshot.Files.Count,
        uassetCount = snapshot.UassetCount,
        verseCount = snapshot.VerseCount,
        snapshotPath = snapshot.SnapshotPath,
    });
}

static SidecarResponse HandleListSnapshots(SidecarRequest req)
{
    var pm = _sidecarProjects!;
    var active = pm.GetActiveProject();
    if (active == null)
        return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

    var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
    var fileAccess = new SafeFileAccess(cfg, detector, loggerFactory.CreateLogger<SafeFileAccess>());
    var diffService = new ProjectDiffService(cfg, fileAccess, loggerFactory.CreateLogger<ProjectDiffService>());

    var snapshots = diffService.ListSnapshots(active.ProjectPath);

    return new SidecarResponse(req.Id, new
    {
        count = snapshots.Count,
        snapshots = snapshots.Select(s => new
        {
            s.Id,
            s.Description,
            s.Timestamp,
            s.ProjectName,
            s.FileCount,
            s.UassetCount,
            s.VerseCount,
            s.SnapshotPath,
        })
    });
}

static SidecarResponse HandleCompareSnapshot(SidecarRequest req)
{
    var pm = _sidecarProjects!;
    var active = pm.GetActiveProject();
    if (active == null)
        return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

    var snapshotId = req.Params?.GetProperty("snapshotId").GetString()
        ?? throw new ArgumentException("Missing 'snapshotId' parameter");

    var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
    var fileAccess = new SafeFileAccess(cfg, detector, loggerFactory.CreateLogger<SafeFileAccess>());
    var diffService = new ProjectDiffService(cfg, fileAccess, loggerFactory.CreateLogger<ProjectDiffService>());

    // Resolve snapshot ID to path
    string? snapshotPath = null;
    if (File.Exists(snapshotId))
    {
        snapshotPath = snapshotId;
    }
    else
    {
        var snapshots = diffService.ListSnapshots(active.ProjectPath);
        var match = snapshots.FirstOrDefault(s =>
            s.Id.Equals(snapshotId, StringComparison.OrdinalIgnoreCase));
        snapshotPath = match?.SnapshotPath;
    }

    if (snapshotPath == null)
        return new SidecarResponse(req.Id, Error: new SidecarError("NOT_FOUND", $"Snapshot not found: {snapshotId}"));

    var diff = diffService.CompareToSnapshot(active.ProjectPath, snapshotPath);

    return new SidecarResponse(req.Id, new
    {
        diff.Description,
        olderTimestamp = diff.OlderTimestamp,
        newerTimestamp = diff.NewerTimestamp,
        summary = new
        {
            added = diff.AddedCount,
            modified = diff.ModifiedCount,
            deleted = diff.DeletedCount,
            totalPropertyChanges = diff.TotalPropertyChanges,
        },
        changes = diff.Changes.Select(c => new
        {
            filePath = c.FilePath,
            type = c.Type.ToString(),
            c.ActorClass,
            c.ActorName,
            oldSize = c.OldSize,
            newSize = c.NewSize,
            linesAdded = c.LinesAdded,
            linesRemoved = c.LinesRemoved,
            propertyChanges = c.PropertyChanges?.Select(p => new
            {
                p.ActorName,
                p.PropertyName,
                p.OldValue,
                p.NewValue
            })
        })
    });
}

// ========= File Watcher Integration Handlers =========

static SidecarResponse HandleWatchProject(SidecarRequest req)
{
    var pm = _sidecarProjects!;
    var active = pm.GetActiveProject();
    if (active == null)
        return new SidecarResponse(req.Id, Error: new SidecarError("NO_PROJECT", "No active project"));

    // The Rust side handles the actual file watching.
    // This handler acknowledges the intent and takes an initial snapshot.
    var cfg = new WellVersedConfig { ProjectPath = active.ProjectPath };
    var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
    var detector = new UefnDetector(cfg, loggerFactory.CreateLogger<UefnDetector>());
    var fileAccess = new SafeFileAccess(cfg, detector, loggerFactory.CreateLogger<SafeFileAccess>());
    var diffService = new ProjectDiffService(cfg, fileAccess, loggerFactory.CreateLogger<ProjectDiffService>());

    try
    {
        var snapshot = diffService.TakeSnapshot(active.ProjectPath, "Watch started — baseline snapshot");
        return new SidecarResponse(req.Id, new
        {
            watching = true,
            projectPath = active.ProjectPath,
            baselineSnapshot = new
            {
                id = snapshot.Id,
                description = snapshot.Description,
                timestamp = snapshot.Timestamp,
                fileCount = snapshot.Files.Count,
            }
        });
    }
    catch (Exception ex)
    {
        // Still report watching even if baseline snapshot fails
        return new SidecarResponse(req.Id, new
        {
            watching = true,
            projectPath = active.ProjectPath,
            baselineSnapshotError = ex.Message
        });
    }
}

static SidecarResponse HandleUnwatchProject(SidecarRequest req)
{
    return new SidecarResponse(req.Id, new { watching = false });
}

// ========= UEFN Bridge Handlers =========

private static UefnBridge EnsureBridge()
{
    if (_uefnBridge == null)
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        _uefnBridge = new UefnBridge(loggerFactory.CreateLogger<UefnBridge>());
    }
    return _uefnBridge;
}

private static SidecarResponse HandleBridgeConnect(SidecarRequest req)
{
    var bridge = EnsureBridge();
    int port = 9220;
    if (req.Params.HasValue)
    {
        try
        {
            if (req.Params.Value.TryGetProperty("port", out var portEl))
                port = portEl.GetInt32();
        }
        catch { }
    }

    var result = bridge.Connect(port).GetAwaiter().GetResult();
    return new SidecarResponse(req.Id, new
    {
        connected = result.IsOk,
        status = result.Status,
        error = result.Error,
        data = result.Data
    });
}

private static SidecarResponse HandleBridgeStatus(SidecarRequest req)
{
    var bridge = EnsureBridge();
    var connected = bridge.IsConnected().GetAwaiter().GetResult();
    if (!connected)
    {
        return new SidecarResponse(req.Id, new
        {
            connected = false,
            message = "Bridge not connected"
        });
    }

    var status = bridge.SendCommand("status").GetAwaiter().GetResult();
    return new SidecarResponse(req.Id, new
    {
        connected = true,
        bridgeStatus = status.Status,
        data = status.Data,
        error = status.Error
    });
}

private static SidecarResponse HandleBridgeCommand(SidecarRequest req)
{
    var bridge = EnsureBridge();
    if (!bridge.IsConnected().GetAwaiter().GetResult())
    {
        return new SidecarResponse(req.Id, Error: new SidecarError("NOT_CONNECTED", "UEFN bridge not connected. Call bridge-connect first."));
    }

    string command = "status";
    object? @params = null;

    if (req.Params.HasValue)
    {
        try
        {
            if (req.Params.Value.TryGetProperty("command", out var cmdEl))
                command = cmdEl.GetString() ?? "status";
            if (req.Params.Value.TryGetProperty("params", out var paramsEl))
                @params = paramsEl;
        }
        catch { }
    }

    var result = bridge.SendCommand(command, @params).GetAwaiter().GetResult();
    return new SidecarResponse(req.Id, new
    {
        success = result.IsOk,
        status = result.Status,
        data = result.Data,
        error = result.Error
    });
}

// ========= Device Behavior Simulator Handlers =========

static SidecarResponse HandleSimulateGameLoop(SidecarRequest req)
{
    var svcResult = BuildActiveProjectServices(out var errorResponse, req.Id);
    if (svcResult == null) return errorResponse!;
    var (cfg, _, deviceService, fileAccess) = svcResult.Value;

    try
    {
        var levelPath = req.Params?.GetProperty("levelPath").GetString()
            ?? throw new ArgumentException("Missing 'levelPath'");

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var digestService = new DigestService(cfg, loggerFactory.CreateLogger<DigestService>());
        var simulator = new DeviceSimulator(cfg, deviceService, digestService, loggerFactory.CreateLogger<DeviceSimulator>());

        var simResult = simulator.SimulateGameLoop(levelPath);

        return new SidecarResponse(req.Id, new
        {
            initialTrigger = simResult.InitialTrigger,
            stepCount = simResult.Steps.Count,
            totalSimulatedTime = simResult.TotalSimulatedTime,
            reachesEndGame = simResult.ReachesEndGame,
            warnings = simResult.Warnings,
            dfa = simResult.DFA == null ? null : new
            {
                nodes = simResult.DFA.Nodes.Select(n => new
                {
                    n.DeviceName, n.DeviceClass, n.DeviceType,
                    currentPhase = n.CurrentPhase.ToString(),
                    n.AvailableEvents, n.AvailableActions, n.X, n.Y
                }),
                edges = simResult.DFA.Edges.Select(e => new
                {
                    e.SourceDevice, e.Event, e.TargetDevice, e.Action,
                    resultingPhase = e.ResultingPhase.ToString(),
                    e.IsConditional, e.Condition
                }),
                simResult.DFA.StateHash,
                historyCount = simResult.DFA.History.Count
            },
            steps = simResult.Steps.Select(s => new
            {
                s.StepNumber, s.SimulatedTime, triggerDevice = s.TriggerDevice,
                @event = s.Event, targetDevice = s.TargetDevice, action = s.Action,
                oldPhase = s.OldPhase.ToString(), newPhase = s.NewPhase.ToString(),
                s.Description, s.VerseHandlersCalled, s.IsConditional, s.Condition
            }),
            finalStates = simResult.FinalStates.ToDictionary(
                kvp => kvp.Key, kvp => kvp.Value.ToString())
        });
    }
    finally
    {
        fileAccess.Dispose();
    }
}

static SidecarResponse HandleSimulateEvent(SidecarRequest req)
{
    var svcResult = BuildActiveProjectServices(out var errorResponse, req.Id);
    if (svcResult == null) return errorResponse!;
    var (cfg, _, deviceService, fileAccess) = svcResult.Value;

    try
    {
        var levelPath = req.Params?.GetProperty("levelPath").GetString()
            ?? throw new ArgumentException("Missing 'levelPath'");
        var deviceName = req.Params?.GetProperty("deviceName").GetString()
            ?? throw new ArgumentException("Missing 'deviceName'");
        var eventName = req.Params?.GetProperty("eventName").GetString()
            ?? throw new ArgumentException("Missing 'eventName'");

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var digestService = new DigestService(cfg, loggerFactory.CreateLogger<DigestService>());
        var simulator = new DeviceSimulator(cfg, deviceService, digestService, loggerFactory.CreateLogger<DeviceSimulator>());

        var world = simulator.BuildSimulation(levelPath);
        var simResult = simulator.SimulateEvent(world, deviceName, eventName);

        return new SidecarResponse(req.Id, new
        {
            initialTrigger = simResult.InitialTrigger,
            stepCount = simResult.Steps.Count,
            totalSimulatedTime = simResult.TotalSimulatedTime,
            reachesEndGame = simResult.ReachesEndGame,
            warnings = simResult.Warnings,
            steps = simResult.Steps.Select(s => new
            {
                s.StepNumber, s.SimulatedTime, triggerDevice = s.TriggerDevice,
                @event = s.Event, targetDevice = s.TargetDevice, action = s.Action,
                oldPhase = s.OldPhase.ToString(), newPhase = s.NewPhase.ToString(),
                s.Description, s.VerseHandlersCalled, s.IsConditional, s.Condition
            }),
            finalStates = simResult.FinalStates.ToDictionary(
                kvp => kvp.Key, kvp => kvp.Value.ToString())
        });
    }
    finally
    {
        fileAccess.Dispose();
    }
}

}

internal record ServiceBundle(
    AssetService Asset,
    DeviceService Device,
    AuditService Audit,
    ModificationService Modification,
    BuildService Build,
    BackupService Backup,
    DigestService Digest,
    ActorPlacementService Placement,
    SafeFileAccess FileAccess,
    AssetCatalog Catalog,
    MapGenerator MapGen) : IDisposable
{
    public void Dispose() => FileAccess.Dispose();
}

// ========= Sidecar Protocol Types =========

internal record SidecarRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

internal record SidecarResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] SidecarError? Error = null);

internal record SidecarError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] object? Details = null);

// ========= Sidecar Project Manager (simple file-backed project list) =========

internal class SidecarProjectManager
{
    private readonly string _storagePath;
    private SidecarProjectStore _store;
    private static readonly JsonSerializerOptions StoreJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SidecarProjectManager()
    {
        _storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wellversed", "projects.json");
        _store = Load();
    }

    public List<SidecarProjectEntry> ListProjects() => _store.Projects;

    public SidecarProjectEntry? GetActiveProject()
        => _store.Projects.FirstOrDefault(p => p.Id == _store.ActiveProjectId);

    public SidecarProjectEntry AddProject(string projectPath, string type)
    {
        var fullPath = Path.GetFullPath(projectPath);

        // Check if already added
        var existing = _store.Projects.FirstOrDefault(p =>
            string.Equals(Path.GetFullPath(p.ProjectPath), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var config = new WellVersedConfig { ProjectPath = fullPath };
        var entry = new SidecarProjectEntry
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ProjectPath = fullPath,
            Name = config.ProjectName,
            Type = type,
            IsUefnProject = config.IsUefnProject,
            HasUrc = config.HasUrc,
            ContentPath = config.ContentPath,
            AddedAt = DateTime.UtcNow
        };

        // Count assets
        if (Directory.Exists(entry.ContentPath))
        {
            try
            {
                entry.AssetCount = Directory.EnumerateFiles(entry.ContentPath, "*.uasset", SearchOption.AllDirectories)
                    .Count(f => !f.Contains("__External"));
                entry.ExternalActorCount = Directory.EnumerateFiles(entry.ContentPath, "*.uasset", SearchOption.AllDirectories)
                    .Count(f => f.Contains("__ExternalActors__"));
                entry.VerseFileCount = Directory.EnumerateFiles(entry.ContentPath, "*.verse", SearchOption.AllDirectories).Count();
                entry.LevelCount = Directory.EnumerateFiles(entry.ContentPath, "*.umap", SearchOption.AllDirectories).Count();
            }
            catch { }
        }

        _store.Projects.Add(entry);

        if (_store.Projects.Count == 1)
            _store.ActiveProjectId = entry.Id;

        Save();
        return entry;
    }

    public bool RemoveProject(string id)
    {
        var removed = _store.Projects.RemoveAll(p => p.Id == id) > 0;
        if (removed && _store.ActiveProjectId == id)
            _store.ActiveProjectId = _store.Projects.FirstOrDefault()?.Id;
        if (removed) Save();
        return removed;
    }

    public SidecarProjectEntry? SetActive(string id)
    {
        var project = _store.Projects.FirstOrDefault(p => p.Id == id);
        if (project != null)
        {
            _store.ActiveProjectId = project.Id;
            Save();
        }
        return project;
    }

    public List<SidecarDiscoveredProject> ScanDirectory(string searchPath)
    {
        var projectPaths = WellVersedConfig.DiscoverProjects(searchPath);
        return projectPaths.Select(p =>
        {
            var cfg = new WellVersedConfig { ProjectPath = p };
            var alreadyAdded = _store.Projects.Any(ex =>
                string.Equals(Path.GetFullPath(ex.ProjectPath), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase));

            int assets = 0, extActors = 0, verse = 0, levels = 0;
            if (Directory.Exists(cfg.ContentPath))
            {
                try
                {
                    assets = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
                        .Count(f => !f.Contains("__External"));
                    extActors = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
                        .Count(f => f.Contains("__ExternalActors__"));
                    verse = Directory.EnumerateFiles(cfg.ContentPath, "*.verse", SearchOption.AllDirectories).Count();
                    levels = Directory.EnumerateFiles(cfg.ContentPath, "*.umap", SearchOption.AllDirectories).Count();
                }
                catch { }
            }

            return new SidecarDiscoveredProject
            {
                ProjectPath = p,
                ProjectName = cfg.ProjectName,
                IsUefnProject = cfg.IsUefnProject,
                HasUrc = cfg.HasUrc,
                AssetCount = assets,
                ExternalActorCount = extActors,
                VerseFileCount = verse,
                LevelCount = levels,
                AlreadyAdded = alreadyAdded
            };
        }).ToList();
    }

    private SidecarProjectStore Load()
    {
        if (File.Exists(_storagePath))
        {
            try
            {
                var json = File.ReadAllText(_storagePath);
                return JsonSerializer.Deserialize<SidecarProjectStore>(json, StoreJsonOpts) ?? new SidecarProjectStore();
            }
            catch { }
        }
        return new SidecarProjectStore();
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_storagePath, JsonSerializer.Serialize(_store, StoreJsonOpts));
    }
}

internal class SidecarProjectStore
{
    public string? ActiveProjectId { get; set; }
    public List<SidecarProjectEntry> Projects { get; set; } = new();
}

internal class SidecarProjectEntry
{
    public string Id { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "MyProject";
    public bool IsUefnProject { get; set; }
    public bool HasUrc { get; set; }
    public string ContentPath { get; set; } = "";
    public int AssetCount { get; set; }
    public int ExternalActorCount { get; set; }
    public int VerseFileCount { get; set; }
    public int LevelCount { get; set; }
    public DateTime AddedAt { get; set; }
}

internal class SidecarDiscoveredProject
{
    public string ProjectPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public bool IsUefnProject { get; set; }
    public bool HasUrc { get; set; }
    public int AssetCount { get; set; }
    public int ExternalActorCount { get; set; }
    public int VerseFileCount { get; set; }
    public int LevelCount { get; set; }
    public bool AlreadyAdded { get; set; }
}

// ========= Sidecar Library Manager (separate from projects — read-only reference collections) =========

internal class SidecarLibraryManager
{
    private readonly string _storagePath;
    private SidecarLibraryStore _store;
    private static readonly JsonSerializerOptions StoreJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SidecarLibraryManager()
    {
        _storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wellversed", "libraries.json");
        _store = Load();
    }

    public List<SidecarLibraryEntry> ListLibraries() => _store.Libraries;

    public SidecarLibraryEntry? GetActiveLibrary()
        => _store.Libraries.FirstOrDefault(l => l.Id == _store.ActiveLibraryId);

    public SidecarLibraryEntry AddLibrary(string path)
    {
        var fullPath = Path.GetFullPath(path);

        // Check if already added
        var existing = _store.Libraries.FirstOrDefault(l =>
            string.Equals(Path.GetFullPath(l.Path), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var entry = new SidecarLibraryEntry
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Path = fullPath,
            Name = Path.GetFileName(fullPath),
            AddedAt = DateTime.UtcNow
        };

        // Count .verse and .uasset files for stats
        if (Directory.Exists(fullPath))
        {
            try
            {
                entry.VerseFileCount = Directory.EnumerateFiles(fullPath, "*.verse", SearchOption.AllDirectories).Count();
                entry.AssetCount = Directory.EnumerateFiles(fullPath, "*.uasset", SearchOption.AllDirectories).Count();
            }
            catch { }
        }

        _store.Libraries.Add(entry);

        if (_store.Libraries.Count == 1)
            _store.ActiveLibraryId = entry.Id;

        Save();
        return entry;
    }

    public bool RemoveLibrary(string id)
    {
        var removed = _store.Libraries.RemoveAll(l => l.Id == id) > 0;
        if (removed && _store.ActiveLibraryId == id)
            _store.ActiveLibraryId = _store.Libraries.FirstOrDefault()?.Id;
        if (removed) Save();
        return removed;
    }

    public SidecarLibraryEntry? SetActive(string id)
    {
        var library = _store.Libraries.FirstOrDefault(l => l.Id == id);
        if (library != null)
        {
            _store.ActiveLibraryId = library.Id;
            Save();
        }
        return library;
    }

    public void UpdateStats(string id, int verseFileCount, int assetCount, DateTime indexedAt)
    {
        var library = _store.Libraries.FirstOrDefault(l => l.Id == id);
        if (library != null)
        {
            library.VerseFileCount = verseFileCount;
            library.AssetCount = assetCount;
            library.IndexedAt = indexedAt;
            Save();
        }
    }

    private SidecarLibraryStore Load()
    {
        if (File.Exists(_storagePath))
        {
            try
            {
                var json = File.ReadAllText(_storagePath);
                return JsonSerializer.Deserialize<SidecarLibraryStore>(json, StoreJsonOpts) ?? new SidecarLibraryStore();
            }
            catch { }
        }
        return new SidecarLibraryStore();
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_storagePath, JsonSerializer.Serialize(_store, StoreJsonOpts));
    }
}

internal class SidecarLibraryStore
{
    public string? ActiveLibraryId { get; set; }
    public List<SidecarLibraryEntry> Libraries { get; set; } = new();
}

internal class SidecarLibraryEntry
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int VerseFileCount { get; set; }
    public int AssetCount { get; set; }
    public DateTime? IndexedAt { get; set; }
    public DateTime AddedAt { get; set; }
}
