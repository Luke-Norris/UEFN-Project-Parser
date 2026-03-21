using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using FortniteForge.Core.Services.MapGeneration;
using FortniteForge.Core.Services.VerseGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortniteForge.MCP;

/// <summary>
/// WellVersed MCP Server — AI-powered UEFN project management
///
/// This server exposes WellVersed's capabilities as MCP tools that Claude Code
/// can call directly. No copy-paste, no manual CLI — Claude talks to your UEFN
/// project natively.
///
/// All file modifications are staged for review. The user must approve changes
/// through the WellVersed app before they are applied to project source files.
///
/// Setup in Claude Code:
///   claude mcp add wellversed -- dotnet run --project path/to/FortniteForge.MCP
///
/// Or in .claude/settings.json:
///   "mcpServers": {
///     "wellversed": {
///       "command": "dotnet",
///       "args": ["run", "--project", "path/to/FortniteForge.MCP"]
///     }
///   }
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // Determine config path — first arg, env var, or default
        var configPath = args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable("WELLVERSED_CONFIG")
              ?? Environment.GetEnvironmentVariable("FORTNITEFORGE_CONFIG")
              ?? FindConfigFile();

        var builder = Host.CreateApplicationBuilder(args);

        // Suppress noisy console logging (MCP uses stdin/stdout)
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Load config
        ForgeConfig config;
        if (configPath != null && File.Exists(configPath))
        {
            config = ForgeConfig.Load(configPath);
        }
        else
        {
            config = new ForgeConfig();
            // Try to find project path from environment
            var projectPath = Environment.GetEnvironmentVariable("WELLVERSED_PROJECT")
                ?? Environment.GetEnvironmentVariable("FORTNITEFORGE_PROJECT");
            if (!string.IsNullOrEmpty(projectPath))
                config.ProjectPath = projectPath;
        }

        // Register services
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<UefnDetector>();
        builder.Services.AddSingleton<SafeFileAccess>();
        builder.Services.AddSingleton<AssetGuard>();
        builder.Services.AddSingleton<AssetService>();
        builder.Services.AddSingleton<BackupService>();
        builder.Services.AddSingleton<DigestService>();
        builder.Services.AddSingleton<DeviceService>();
        builder.Services.AddSingleton<AuditService>();
        builder.Services.AddSingleton<ActorPlacementService>();
        builder.Services.AddSingleton<ModificationService>();
        builder.Services.AddSingleton<BuildService>();
        builder.Services.AddSingleton<AssetCatalog>();
        builder.Services.AddSingleton<MapGenerator>();
        builder.Services.AddSingleton<VerseUIGenerator>();
        builder.Services.AddSingleton<VerseDeviceGenerator>();
        builder.Services.AddSingleton<LevelAnalyticsService>();
        builder.Services.AddSingleton<VerseReferenceService>();
        builder.Services.AddSingleton<LibraryIndexer>();

        // Register MCP server with stdio transport
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "WellVersed",
                    Version = "1.0.0"
                };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();

        // Pre-load digest files if project is configured
        if (!string.IsNullOrEmpty(config.ProjectPath) && Directory.Exists(config.ProjectPath))
        {
            var digestService = app.Services.GetRequiredService<DigestService>();
            try
            {
                digestService.LoadDigests();
            }
            catch
            {
                // Non-fatal — digest loading can happen later
            }
        }

        await app.RunAsync();
    }

    private static string? FindConfigFile()
    {
        // Look for forge.config.json in current directory and parent directories
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
