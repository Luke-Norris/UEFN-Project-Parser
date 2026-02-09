using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using Microsoft.Extensions.Logging;

namespace FortniteForge.CLI;

/// <summary>
/// FortniteForge CLI — manual interface for testing, debugging, and standalone use.
///
/// Usage:
///   fortniteforge list [--folder <path>] [--class <type>]
///   fortniteforge inspect <asset-path>
///   fortniteforge devices <level-path>
///   fortniteforge device <device-name> [--level <path>]
///   fortniteforge audit [--level <path>]
///   fortniteforge build
///   fortniteforge build-log
///   fortniteforge backups [--asset <path>]
///   fortniteforge schema <device-type>
///   fortniteforge init
/// </summary>
public class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("FortniteForge — AI-powered UEFN project tools")
        {
            BuildListCommand(),
            BuildInspectCommand(),
            BuildDevicesCommand(),
            BuildDeviceCommand(),
            BuildAuditCommand(),
            BuildBuildCommand(),
            BuildBuildLogCommand(),
            BuildBackupsCommand(),
            BuildSchemaCommand(),
            BuildInitCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    private static (ForgeConfig Config, ServiceBundle Services) LoadServices(string? configPath = null)
    {
        configPath ??= FindConfigFile();
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

        var guard = new AssetGuard(config, loggerFactory.CreateLogger<AssetGuard>());
        var assetService = new AssetService(config, guard, loggerFactory.CreateLogger<AssetService>());
        var backupService = new BackupService(config, loggerFactory.CreateLogger<BackupService>());
        var digestService = new DigestService(config, loggerFactory.CreateLogger<DigestService>());
        var deviceService = new DeviceService(config, assetService, digestService, loggerFactory.CreateLogger<DeviceService>());
        var auditService = new AuditService(config, deviceService, assetService, digestService, guard, loggerFactory.CreateLogger<AuditService>());
        var placementService = new ActorPlacementService(config, guard, backupService, loggerFactory.CreateLogger<ActorPlacementService>());
        var modService = new ModificationService(config, assetService, backupService, guard, digestService, placementService, loggerFactory.CreateLogger<ModificationService>());
        var buildService = new BuildService(config, loggerFactory.CreateLogger<BuildService>());

        return (config, new ServiceBundle(assetService, deviceService, auditService, modService, buildService, backupService, digestService, placementService));
    }

    private static Command BuildListCommand()
    {
        var folderOption = new Option<string?>("--folder", "Subfolder within Content/ to search");
        var classOption = new Option<string?>("--class", "Filter by asset class");
        var nameOption = new Option<string?>("--name", "Search by name");

        var cmd = new Command("list", "List project assets") { folderOption, classOption, nameOption };
        cmd.SetHandler((string? folder, string? assetClass, string? name) =>
        {
            var (_, services) = LoadServices();
            var assets = services.Asset.ListAssets(folder, assetClass, name);
            Console.WriteLine(JsonSerializer.Serialize(assets, JsonOpts));
        }, folderOption, classOption, nameOption);

        return cmd;
    }

    private static Command BuildInspectCommand()
    {
        var pathArg = new Argument<string>("asset-path", "Path to the asset file");
        var cmd = new Command("inspect", "Inspect an asset in detail") { pathArg };
        cmd.SetHandler((string path) =>
        {
            var (_, services) = LoadServices();
            var detail = services.Asset.InspectAsset(path);
            Console.WriteLine(JsonSerializer.Serialize(detail, JsonOpts));
        }, pathArg);

        return cmd;
    }

    private static Command BuildDevicesCommand()
    {
        var levelArg = new Argument<string>("level-path", "Path to the .umap level file");
        var cmd = new Command("devices", "List devices in a level") { levelArg };
        cmd.SetHandler((string level) =>
        {
            var (_, services) = LoadServices();
            var devices = services.Device.ListDevicesInLevel(level);
            Console.WriteLine(JsonSerializer.Serialize(devices, JsonOpts));
        }, levelArg);

        return cmd;
    }

    private static Command BuildDeviceCommand()
    {
        var nameArg = new Argument<string>("device-name", "Actor name of the device");
        var levelOption = new Option<string?>("--level", "Level path (searches all levels if not specified)");
        var cmd = new Command("device", "Inspect a specific device") { nameArg, levelOption };
        cmd.SetHandler((string name, string? level) =>
        {
            var (_, services) = LoadServices();
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
        }, nameArg, levelOption);

        return cmd;
    }

    private static Command BuildAuditCommand()
    {
        var levelOption = new Option<string?>("--level", "Audit a specific level (audits entire project if not specified)");
        var cmd = new Command("audit", "Audit project or level for issues") { levelOption };
        cmd.SetHandler((string? level) =>
        {
            var (_, services) = LoadServices();
            var result = level != null
                ? services.Audit.AuditLevel(level)
                : services.Audit.AuditProject();
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
        }, levelOption);

        return cmd;
    }

    private static Command BuildBuildCommand()
    {
        var cmd = new Command("build", "Trigger a UEFN build");
        cmd.SetHandler(async () =>
        {
            var (_, services) = LoadServices();
            Console.WriteLine("Starting build...");
            var result = await services.Build.TriggerBuildAsync();
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
        });

        return cmd;
    }

    private static Command BuildBuildLogCommand()
    {
        var cmd = new Command("build-log", "Read the latest build log");
        cmd.SetHandler(() =>
        {
            var (_, services) = LoadServices();
            Console.WriteLine(services.Build.GetBuildSummary());
        });

        return cmd;
    }

    private static Command BuildBackupsCommand()
    {
        var assetOption = new Option<string?>("--asset", "Filter backups for a specific asset");
        var cmd = new Command("backups", "List available backups") { assetOption };
        cmd.SetHandler((string? asset) =>
        {
            var (_, services) = LoadServices();
            var backups = services.Backup.ListBackups(asset);
            Console.WriteLine(JsonSerializer.Serialize(backups, JsonOpts));
        }, assetOption);

        return cmd;
    }

    private static Command BuildSchemaCommand()
    {
        var typeArg = new Argument<string>("device-type", "Device class name to look up");
        var cmd = new Command("schema", "Get device schema from digest files") { typeArg };
        cmd.SetHandler((string deviceType) =>
        {
            var (_, services) = LoadServices();
            services.Digest.LoadDigests();
            var schema = services.Digest.GetDeviceSchema(deviceType);
            Console.WriteLine(schema != null
                ? JsonSerializer.Serialize(schema, JsonOpts)
                : $"No schema found for '{deviceType}'.");
        }, typeArg);

        return cmd;
    }

    private static Command BuildInitCommand()
    {
        var projectPathArg = new Argument<string>("project-path", "Path to your UEFN project root");
        var cmd = new Command("init", "Create a forge.config.json in the current directory") { projectPathArg };
        cmd.SetHandler((string projectPath) =>
        {
            var config = new ForgeConfig
            {
                ProjectPath = Path.GetFullPath(projectPath),
                ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" }
            };

            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "forge.config.json");
            config.Save(configPath);
            Console.WriteLine($"Created: {configPath}");
            Console.WriteLine($"Project path: {config.ProjectPath}");
            Console.WriteLine("Edit this file to configure build commands, modifiable folders, etc.");
        }, projectPathArg);

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
    ActorPlacementService Placement);
