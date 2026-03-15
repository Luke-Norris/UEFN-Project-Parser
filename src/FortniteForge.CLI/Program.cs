using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using FortniteForge.Core.Services.MapGeneration;
using Microsoft.Extensions.Logging;

namespace FortniteForge.CLI;

/// <summary>
/// FortniteForge CLI — manual interface for testing, debugging, and standalone use.
///
/// Usage:
///   fortniteforge status
///   fortniteforge list [--folder path] [--class type]
///   fortniteforge inspect asset-path
///   fortniteforge devices level-path
///   fortniteforge device device-name [--level path]
///   fortniteforge audit [--level path]
///   fortniteforge staged [--apply | --discard]
///   fortniteforge build
///   fortniteforge build-log
///   fortniteforge backups [--asset path]
///   fortniteforge schema device-type
///   fortniteforge init project-path
/// </summary>
public class Program
{
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
        var rootCommand = new RootCommand("FortniteForge — AI-powered UEFN project tools")
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
            BuildInitCommand()
        };
        rootCommand.AddGlobalOption(ConfigOption);

        var result = await rootCommand.InvokeAsync(args);
        _lastServices?.Dispose();
        return result;
    }

    // Track the last created service bundle for cleanup
    private static ServiceBundle? _lastServices;

    private static (ForgeConfig Config, ServiceBundle Services, UefnDetector Detector) LoadServices(string? configPath = null)
    {
        // Priority: explicit arg > env var > walk parent dirs
        configPath ??= Environment.GetEnvironmentVariable("FORTNITEFORGE_CONFIG")
                       ?? FindConfigFile();
        ForgeConfig config;

        if (configPath != null && File.Exists(configPath))
        {
            config = ForgeConfig.Load(configPath);
        }
        else
        {
            Console.Error.WriteLine("Warning: No forge.config.json found. Run 'fortniteforge init' to create one.");
            config = new ForgeConfig();
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
        var placementService = new ActorPlacementService(config, guard, fileAccess, backupService, loggerFactory.CreateLogger<ActorPlacementService>());
        var modService = new ModificationService(config, assetService, backupService, guard, fileAccess, digestService, placementService, loggerFactory.CreateLogger<ModificationService>());
        var buildService = new BuildService(config, loggerFactory.CreateLogger<BuildService>());
        var catalog = new AssetCatalog(config, loggerFactory.CreateLogger<AssetCatalog>());
        var mapGenerator = new MapGenerator(config, catalog, placementService, backupService, loggerFactory.CreateLogger<MapGenerator>());

        var bundle = new ServiceBundle(assetService, deviceService, auditService, modService, buildService, backupService, digestService, placementService, fileAccess, catalog, mapGenerator);
        _lastServices = bundle;
        return (config, bundle, detector);
    }

    // ========= Status Line =========

    private static void PrintStatusLine(ForgeConfig config, UefnDetector detector)
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
            Console.Error.WriteLine($"{bold}{cyan}  FortniteForge{reset}  {status.ProjectName} {dim}({projectType}){reset}");
        }
        else
        {
            Console.Error.WriteLine($"{bold}{cyan}  FortniteForge{reset}  {yellow}No project configured{reset}");
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
            var config = new ForgeConfig
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
