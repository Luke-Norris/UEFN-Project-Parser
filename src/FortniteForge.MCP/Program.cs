using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using FortniteForge.Core.Services.MapGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortniteForge.MCP;

/// <summary>
/// FortniteForge MCP Server — "The Model Context Protocol for Master Control of your Creative Projects"
///
/// This server exposes FortniteForge's capabilities as MCP tools that Claude Code
/// can call directly. No copy-paste, no manual CLI — Claude talks to your UEFN
/// project natively.
///
/// Setup in Claude Code:
///   claude mcp add fortniteforge -- dotnet run --project path/to/FortniteForge.MCP
///
/// Or in .claude/settings.json:
///   "mcpServers": {
///     "fortniteforge": {
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
            : Environment.GetEnvironmentVariable("FORTNITEFORGE_CONFIG")
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
            var projectPath = Environment.GetEnvironmentVariable("FORTNITEFORGE_PROJECT");
            if (!string.IsNullOrEmpty(projectPath))
                config.ProjectPath = projectPath;
        }

        // Register services
        builder.Services.AddSingleton(config);
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

        // Register MCP server with stdio transport
        builder.Services
            .AddMcpServer()
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
