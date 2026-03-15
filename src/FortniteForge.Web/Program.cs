using System.Text.Json;
using System.Text.Json.Serialization;
using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using FortniteForge.Core.Services.MapGeneration;

namespace FortniteForge.Web;

public class Program
{
    public static void Main(string[] args)
    {
        // Determine config path BEFORE builder consumes args
        var configPath = args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a))
            ?? Environment.GetEnvironmentVariable("FORTNITEFORGE_CONFIG")
            ?? FindConfigFile();

        var builder = WebApplication.CreateBuilder();

        ForgeConfig config;
        if (configPath != null && File.Exists(configPath))
        {
            config = ForgeConfig.Load(configPath);
            Console.Error.WriteLine($"Loaded config: {configPath}");
        }
        else
        {
            config = new ForgeConfig();
            Console.Error.WriteLine("Warning: No config found. Pass config path as first argument.");
        }

        // Register core services
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
        builder.Services.AddSingleton<LevelAnalyticsService>();

        builder.Services.ConfigureHttpJsonOptions(opts =>
        {
            opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        var app = builder.Build();

        // Pre-load digests
        try
        {
            app.Services.GetRequiredService<DigestService>().LoadDigests();
        }
        catch { }

        app.UseStaticFiles();

        // === API Routes ===
        var api = app.MapGroup("/api");

        // --- Status ---
        api.MapGet("/status", (UefnDetector detector, ForgeConfig cfg) =>
        {
            var status = detector.GetStatus();
            var contentPath = cfg.ContentPath;
            int assetCount = 0, verseCount = 0;

            if (Directory.Exists(contentPath))
            {
                try
                {
                    assetCount = Directory.EnumerateFiles(contentPath, "*.uasset", SearchOption.AllDirectories).Count()
                               + Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories).Count();
                }
                catch { }
                try
                {
                    verseCount = Directory.EnumerateFiles(contentPath, "*.verse", SearchOption.AllDirectories).Count();
                }
                catch { }
            }

            return Results.Ok(new
            {
                status.ProjectName,
                status.IsUefnRunning,
                status.UefnPid,
                status.HasUrc,
                status.UrcActive,
                status.Mode,
                status.ModeReason,
                status.StagedFileCount,
                IsUefnProject = cfg.IsUefnProject,
                ContentPath = cfg.ContentPath,
                AssetCount = assetCount,
                VerseCount = verseCount,
                ReadOnly = cfg.ReadOnly
            });
        });

        // --- Assets ---
        api.MapGet("/assets", (AssetService assets, string? folder, string? classFilter, string? name) =>
        {
            var list = assets.ListAssets(folder, classFilter, name);
            // Return lightweight summaries only
            return Results.Ok(list.Select(a => new
            {
                a.FilePath,
                a.RelativePath,
                a.Name,
                a.AssetClass,
                a.FileSize,
                a.IsCooked,
                a.IsModifiable,
                a.ExportCount,
                a.ImportCount,
                a.LastModified,
                a.Summary
            }));
        });

        api.MapGet("/assets/inspect", (AssetService assets, string path) =>
        {
            try
            {
                var detail = assets.InspectAsset(path);
                return Results.Ok(detail);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // --- Levels ---
        api.MapGet("/levels", (DeviceService devices, ForgeConfig cfg) =>
        {
            var levels = devices.FindLevels();
            var results = new List<object>();

            foreach (var level in levels)
            {
                var relativePath = Path.GetRelativePath(cfg.ContentPath, level);
                results.Add(new
                {
                    FilePath = level,
                    RelativePath = relativePath,
                    Name = Path.GetFileNameWithoutExtension(level)
                });
            }

            return Results.Ok(results);
        });

        api.MapGet("/levels/devices", (DeviceService devices, string path) =>
        {
            try
            {
                var list = devices.ListDevicesInLevel(path);
                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        api.MapGet("/levels/spatial", (AssetService assets, ForgeConfig cfg, string path) =>
        {
            try
            {
                return Results.Ok(SpatialExtractor.ExtractActorPositions(path, cfg, assets));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        api.MapGet("/levels/actors", (AssetService assets, string path) =>
        {
            try
            {
                var detail = assets.InspectAsset(path);
                return Results.Ok(detail.Exports.Select(e => new
                {
                    e.Index,
                    e.ObjectName,
                    e.ClassName,
                    e.SerialSize,
                    PropertyCount = e.Properties.Count
                }));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // --- Audit ---
        api.MapGet("/audit", (AuditService audit) =>
        {
            try
            {
                return Results.Ok(audit.AuditProject());
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        api.MapGet("/audit/level", (AuditService audit, string path) =>
        {
            try
            {
                return Results.Ok(audit.AuditLevel(path));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // --- Staged ---
        api.MapGet("/staged", (SafeFileAccess fileAccess) =>
        {
            return Results.Ok(fileAccess.ListStagedFiles());
        });

        api.MapPost("/staged/apply", (SafeFileAccess fileAccess, BackupService backup) =>
        {
            var results = fileAccess.ApplyAllStaged(backup);
            return Results.Ok(results);
        });

        api.MapPost("/staged/discard", (SafeFileAccess fileAccess) =>
        {
            fileAccess.DiscardAllStaged();
            return Results.Ok(new { message = "All staged files discarded." });
        });

        // Fallback to index.html for SPA routing
        app.MapFallbackToFile("index.html");

        var url = "http://0.0.0.0:5120";
        Console.Error.WriteLine($"\n  FortniteForge Web Dashboard");
        Console.Error.WriteLine($"  Project: {config.ProjectName}");
        Console.Error.WriteLine($"  Open: http://localhost:5120\n");

        app.Run(url);
    }

    private static string? FindConfigFile()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var p = Path.Combine(dir, "forge.config.json");
            if (File.Exists(p)) return p;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
