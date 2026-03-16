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
        var configPath = args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a))
            ?? Environment.GetEnvironmentVariable("FORTNITEFORGE_CONFIG")
            ?? FindConfigFile();

        var builder = WebApplication.CreateBuilder();

        // Load initial config (can be overridden by project switching)
        ForgeConfig config;
        if (configPath != null && File.Exists(configPath))
            config = ForgeConfig.Load(configPath);
        else
            config = new ForgeConfig();

        // Project manager — persistent across sessions
        var projectManager = new ProjectManager();

        // If we have a config with a valid project, auto-add it
        if (!string.IsNullOrEmpty(config.ProjectPath) && Directory.Exists(config.ProjectPath))
        {
            var type = config.ReadOnly ? ProjectType.Library : ProjectType.MyProject;
            var entry = projectManager.AddProject(config.ProjectPath, type);
            projectManager.SetActive(entry.Id);
        }

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(projectManager);
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

        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();

        try { app.Services.GetRequiredService<DigestService>().LoadDigests(); } catch { }

        app.UseStaticFiles();

        var api = app.MapGroup("/api");

        // ========= Projects =========
        api.MapGet("/projects", (ProjectManager pm) =>
            Results.Ok(new
            {
                ActiveProjectId = pm.GetActiveProject()?.Id,
                Projects = pm.ListProjects(),
                TypeDescriptions = new
                {
                    MyProject = ProjectTypeDescriptions.GetDescription(ProjectType.MyProject),
                    Library = ProjectTypeDescriptions.GetDescription(ProjectType.Library)
                }
            }));

        api.MapPost("/projects/add", async (ProjectManager pm, HttpContext ctx) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<AddProjectRequest>();
            if (body == null || string.IsNullOrEmpty(body.Path))
                return Results.BadRequest("path is required");
            if (!Directory.Exists(body.Path))
                return Results.BadRequest($"Directory not found: {body.Path}");

            var type = body.Type?.ToLowerInvariant() == "library" ? ProjectType.Library : ProjectType.MyProject;
            var entry = pm.AddProject(body.Path, type, body.Name);
            return Results.Ok(entry);
        });

        api.MapPost("/projects/remove", async (ProjectManager pm, HttpContext ctx) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IdRequest>();
            if (body == null || string.IsNullOrEmpty(body.Id)) return Results.BadRequest("id required");
            return pm.RemoveProject(body.Id) ? Results.Ok(new { removed = true }) : Results.NotFound();
        });

        api.MapPost("/projects/activate", async (ProjectManager pm, HttpContext ctx) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IdRequest>();
            if (body == null || string.IsNullOrEmpty(body.Id)) return Results.BadRequest("id required");
            var project = pm.SetActive(body.Id);
            return project != null ? Results.Ok(project) : Results.NotFound();
        });

        api.MapGet("/projects/scan", (ProjectManager pm, string path) =>
        {
            if (!Directory.Exists(path)) return Results.BadRequest("Directory not found");
            return Results.Ok(pm.ScanDirectory(path));
        });

        // ========= Native Folder Picker =========
        api.MapGet("/browse/pick-folder", async () =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = 'Select a UEFN project or folder'; $f.ShowNewFolderButton = $false; if ($f.ShowDialog() -eq 'OK') { $f.SelectedPath } else { '' }\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = System.Diagnostics.Process.Start(psi)!;
                var path = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                await proc.WaitForExitAsync();

                if (string.IsNullOrEmpty(path))
                    return Results.Ok(new { path = "", cancelled = true });

                var cfg = new ForgeConfig { ProjectPath = path };
                return Results.Ok(new { path, cancelled = false, isUefnProject = cfg.IsUefnProject, projectName = cfg.ProjectName });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ========= Directory Browser =========
        api.MapGet("/browse", (string? path) =>
        {
            // If no path, return drive roots + common locations
            if (string.IsNullOrEmpty(path))
            {
                var roots = new List<object>();
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    roots.Add(new { Name = drive.Name.TrimEnd('\\'), Path = drive.RootDirectory.FullName, Type = "drive" });

                // Common UEFN locations
                var fortniteProjects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Fortnite Projects");
                if (Directory.Exists(fortniteProjects))
                    roots.Add(new { Name = "Fortnite Projects", Path = fortniteProjects, Type = "shortcut" });

                var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                roots.Add(new { Name = "Home", Path = userHome, Type = "shortcut" });

                return Results.Ok(new { Current = "", Entries = roots, IsUefnProject = false });
            }

            if (!Directory.Exists(path))
                return Results.BadRequest("Directory not found");

            var entries = new List<object>();

            // Parent directory
            var parent = Directory.GetParent(path);
            if (parent != null)
                entries.Add(new { Name = "..", Path = parent.FullName, Type = "parent" });

            // Subdirectories
            try
            {
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('.') && name != ".urc") continue; // Skip hidden except .urc
                    entries.Add(new { Name = name, Path = dir, Type = "directory" });
                }
            }
            catch (UnauthorizedAccessException) { }

            // Check if this directory IS a UEFN project
            var isProject = Directory.GetFiles(path, "*.uefnproject").Length > 0
                         || Directory.GetFiles(path, "*.uproject").Length > 0;

            // Check for nearby .uefnproject files (one level down)
            var nestedProject = !isProject && Directory.GetDirectories(path)
                .Any(d => Directory.GetFiles(d, "*.uefnproject").Length > 0);

            return Results.Ok(new { Current = path, Entries = entries, IsUefnProject = isProject, HasNestedProjects = nestedProject });
        });

        // ========= Status (uses active project) =========
        api.MapGet("/status", (ProjectManager pm, UefnDetector detector) =>
        {
            var active = pm.GetActiveProject();
            if (active == null)
                return Results.Ok(new { projectName = "No Project", mode = "None", assetCount = 0, verseCount = 0 });

            var cfg = pm.BuildConfig(active);
            var status = detector.GetStatus();
            int assetCount = 0, verseCount = 0, defCount = 0;
            if (Directory.Exists(cfg.ContentPath))
            {
                try { assetCount = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories).Count() + Directory.EnumerateFiles(cfg.ContentPath, "*.umap", SearchOption.AllDirectories).Count(); } catch { }
                try { defCount = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories).Count(f => !f.Contains("__External")); } catch { }
                try { verseCount = Directory.EnumerateFiles(cfg.ContentPath, "*.verse", SearchOption.AllDirectories).Count(); } catch { }
            }
            return Results.Ok(new
            {
                active.Id, ProjectName = active.Name, active.Type,
                status.IsUefnRunning, status.UefnPid, status.HasUrc, status.UrcActive,
                status.Mode, status.ModeReason, status.StagedFileCount,
                active.IsUefnProject, ContentPath = cfg.ContentPath, ProjectPath = active.ProjectPath,
                AssetCount = assetCount, DefinitionCount = defCount, VerseCount = verseCount,
                ReadOnly = cfg.ReadOnly
            });
        });

        // ========= Levels (uses active or explicit project) =========
        api.MapGet("/levels", (ProjectManager pm, string? projectId) =>
        {
            var project = projectId != null ? pm.GetProject(projectId) : pm.GetActiveProject();
            if (project == null) return Results.BadRequest("No active project");
            var cfg = pm.BuildConfig(project);
            if (!Directory.Exists(cfg.ContentPath)) return Results.Ok(Array.Empty<object>());

            var umaps = Directory.EnumerateFiles(cfg.ContentPath, "*.umap", SearchOption.AllDirectories).ToList();
            return Results.Ok(umaps.Select(l => new
            {
                FilePath = l,
                RelativePath = Path.GetRelativePath(cfg.ContentPath, l),
                Name = Path.GetFileNameWithoutExtension(l),
                ProjectId = project.Id,
                ProjectName = project.Name
            }));
        });

        // ========= Devices (full classification) =========
        api.MapGet("/levels/devices-full", (ProjectManager pm, string path) =>
        {
            try
            {
                var project = pm.ListProjects().FirstOrDefault(p => path.StartsWith(p.ProjectPath, StringComparison.OrdinalIgnoreCase));
                var cfg = project != null ? pm.BuildConfig(project) : new ForgeConfig();
                var contents = DeviceClassifier.ClassifyLevel(path, cfg);
                return Results.Ok(new
                {
                    contents.LevelName, contents.TotalActorFiles, contents.ParseErrors,
                    DeviceCount = contents.Devices.Count, StaticActorCount = contents.StaticActors.Count,
                    Devices = contents.Devices.Select(d => new
                    {
                        d.ClassName, d.DisplayName, d.IsDevice, d.FilePath,
                        d.X, d.Y, d.Z, d.RotationYaw, d.HasPosition, d.TotalPropertyCount,
                        Properties = d.Properties, ComponentCount = d.Components.Count
                    }),
                    StaticActorBreakdown = contents.StaticActors
                        .GroupBy(a => a.ClassName)
                        .Select(g => new { ClassName = g.Key, Count = g.Count(), DisplayName = DeviceClassifier.CleanActorName(g.Key, g.Key) })
                        .OrderByDescending(g => g.Count)
                });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // All instances of a specific class in a level
        api.MapGet("/levels/devices-by-class", (ProjectManager pm, string levelPath, string className) =>
        {
            try
            {
                var project = pm.ListProjects().FirstOrDefault(p => levelPath.StartsWith(p.ProjectPath, StringComparison.OrdinalIgnoreCase));
                var cfg = project != null ? pm.BuildConfig(project) : new ForgeConfig();
                var contents = DeviceClassifier.ClassifyLevel(levelPath, cfg);

                var allActors = contents.Devices.Concat(contents.StaticActors);
                var matches = allActors.Where(a => a.ClassName == className).ToList();

                return Results.Ok(new
                {
                    ClassName = className,
                    DisplayName = matches.FirstOrDefault()?.DisplayName ?? DeviceClassifier.CleanActorName(className, className),
                    IsDevice = DeviceClassifier.IsDevice(className),
                    Count = matches.Count,
                    Instances = matches.Select((m, i) => new
                    {
                        m.DisplayName,
                        // Use ActorLabel property if available, otherwise generate numbered name
                        Label = m.Properties.FirstOrDefault(p => p.Name == "ActorLabel")?.Value
                            ?? m.Properties.FirstOrDefault(p => p.Name == "Label")?.Value
                            ?? $"{m.DisplayName} #{i + 1}",
                        m.FilePath,
                        m.X, m.Y, m.Z, m.HasPosition, m.RotationYaw,
                        m.TotalPropertyCount,
                        EditableCount = m.Properties.Count(p => p.IsEditable),
                        // Include a few key property previews
                        KeyProperties = m.Properties
                            .Where(p => p.IsEditable && !new[] { "CullDistance", "DataVersion", "LDMaxDrawDistance", "CachedMaxDrawDistance" }.Contains(p.Name))
                            .Take(4)
                            .Select(p => new { p.Name, p.Value })
                    })
                });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        api.MapGet("/device/inspect", (string path) =>
        {
            try
            {
                var detail = DeviceClassifier.ParseExternalActor(path);
                return detail != null ? Results.Ok(detail) : Results.NotFound("Could not parse actor");
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // ========= Assets =========
        // User-created definitions (Content/ minus External*)
        api.MapGet("/assets", (ProjectManager pm, string? projectId) =>
        {
            var project = projectId != null ? pm.GetProject(projectId) : pm.GetActiveProject();
            if (project == null) return Results.BadRequest("No active project");
            var cfg = pm.BuildConfig(project);
            if (!Directory.Exists(cfg.ContentPath)) return Results.Ok(Array.Empty<object>());

            var results = new List<object>();
            var files = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
                .Where(f => !f.Contains("__External")).ToList();

            foreach (var file in files)
            {
                var fi = new FileInfo(file);
                string assetClass = "Unknown";
                bool hasThumbnail = false;
                try
                {
                    var asset = new UAssetAPI.UAsset(file, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                    assetClass = asset.Exports.FirstOrDefault()?.GetExportClassType()?.ToString() ?? "Unknown";
                    hasThumbnail = asset.Thumbnails?.Values.Any(t =>
                        (t.CompressedImageData?.Length ?? 0) > 0 || (t.ImageData?.Length ?? 0) > 0) ?? false;
                }
                catch { }

                results.Add(new
                {
                    FilePath = file,
                    RelativePath = Path.GetRelativePath(cfg.ContentPath, file),
                    Name = Path.GetFileNameWithoutExtension(file),
                    AssetClass = assetClass,
                    FileSize = fi.Length,
                    LastModified = fi.LastWriteTime,
                    HasThumbnail = hasThumbnail,
                    Source = "User"
                });
            }
            return Results.Ok(results.OrderBy(r => ((dynamic)r).AssetClass).ThenBy(r => ((dynamic)r).Name));
        });

        // Epic-referenced asset types used in levels (from external actors)
        api.MapGet("/assets/epic", (ProjectManager pm, string? levelPath) =>
        {
            var project = pm.GetActiveProject();
            if (project == null) return Results.BadRequest("No active project");
            var cfg = pm.BuildConfig(project);
            if (!Directory.Exists(cfg.ContentPath)) return Results.Ok(Array.Empty<object>());

            // Find all external actor directories
            var extDirs = Directory.EnumerateDirectories(cfg.ContentPath, "__ExternalActors__", SearchOption.AllDirectories).ToList();
            var classCounts = new Dictionary<string, (int Count, List<string> SamplePaths)>();

            foreach (var extDir in extDirs)
            {
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
                            if (entry.SamplePaths.Count < 3) // Keep a few sample file paths
                                entry.SamplePaths.Add(file);
                            break; // Only count primary export per file
                        }
                    }
                    catch { }
                }
            }

            var results = classCounts.Select(kv => new
            {
                ClassName = kv.Key,
                DisplayName = DeviceClassifier.CleanActorName(kv.Key, kv.Key),
                Count = kv.Value.Count,
                IsDevice = DeviceClassifier.IsDevice(kv.Key),
                SamplePaths = kv.Value.SamplePaths,
                Source = "Epic"
            }).OrderByDescending(r => r.Count).ToList();

            return Results.Ok(results);
        });

        api.MapGet("/assets/inspect", (ProjectManager pm, ILoggerFactory lf, string path) =>
        {
            try
            {
                var project = pm.GetActiveProject();
                var cfg = project != null ? pm.BuildConfig(project) : new ForgeConfig();
                var (assetSvc, _, _, _, fileAccess) = BuildProjectServices(cfg, lf);
                var result = assetSvc.InspectAsset(path);
                fileAccess.Dispose();
                return Results.Ok(result);
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // ========= Audit (builds services from active project) =========
        api.MapGet("/audit", (ProjectManager pm, ILoggerFactory lf) =>
        {
            var project = pm.GetActiveProject();
            if (project == null) return Results.BadRequest("No active project");
            try
            {
                var (_, _, audit, _, _) = BuildProjectServices(pm.BuildConfig(project), lf);
                return Results.Ok(audit.AuditProject());
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        api.MapGet("/audit/level", (ProjectManager pm, ILoggerFactory lf, string path) =>
        {
            var project = pm.GetActiveProject();
            if (project == null) return Results.BadRequest("No active project");
            try
            {
                var (_, _, audit, _, _) = BuildProjectServices(pm.BuildConfig(project), lf);
                return Results.Ok(audit.AuditLevel(path));
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // ========= Pending Changes (in-memory queue) =========
        var pendingChanges = new List<PendingChange>();

        api.MapGet("/changes", () => Results.Ok(pendingChanges));

        api.MapPost("/changes/add", async (HttpContext ctx) =>
        {
            var change = await ctx.Request.ReadFromJsonAsync<PendingChange>();
            if (change == null) return Results.BadRequest("Invalid change");

            // Check for existing change on same property
            var existing = pendingChanges.FindIndex(c =>
                c.FilePath == change.FilePath && c.PropertyName == change.PropertyName && c.ExportName == change.ExportName);
            if (existing >= 0)
                pendingChanges[existing] = change;
            else
                pendingChanges.Add(change);

            return Results.Ok(new { count = pendingChanges.Count, change });
        });

        api.MapPost("/changes/remove", async (HttpContext ctx) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<PendingChange>();
            if (body == null) return Results.BadRequest();
            pendingChanges.RemoveAll(c => c.FilePath == body.FilePath && c.PropertyName == body.PropertyName && c.ExportName == body.ExportName);
            return Results.Ok(new { count = pendingChanges.Count });
        });

        api.MapPost("/changes/clear", () =>
        {
            pendingChanges.Clear();
            return Results.Ok(new { count = 0 });
        });

        api.MapPost("/changes/apply", (ProjectManager pm, ILoggerFactory lf) =>
        {
            if (pendingChanges.Count == 0)
                return Results.Ok(new { applied = 0, message = "No pending changes" });

            var project = pm.GetActiveProject();
            if (project == null) return Results.BadRequest("No active project");
            if (project.Type == ProjectType.Library)
                return Results.BadRequest("Cannot modify a Library project. Change project type to 'My Project' first.");

            var cfg = pm.BuildConfig(project);
            var (assetSvc, _, _, _, fileAccess) = BuildProjectServices(cfg, lf);

            var results = new List<object>();
            foreach (var change in pendingChanges.ToList())
            {
                try
                {
                    var (asset, writePath) = fileAccess.OpenForWrite(change.FilePath);
                    var export = asset.Exports
                        .OfType<UAssetAPI.ExportTypes.NormalExport>()
                        .FirstOrDefault(e => e.ObjectName?.ToString() == change.ExportName);

                    if (export == null)
                    {
                        results.Add(new { change.PropertyName, success = false, error = $"Export '{change.ExportName}' not found" });
                        continue;
                    }

                    var prop = export.Data.FirstOrDefault(p => p.Name?.ToString() == change.PropertyName);
                    if (prop == null)
                    {
                        results.Add(new { change.PropertyName, success = false, error = $"Property '{change.PropertyName}' not found" });
                        continue;
                    }

                    // Apply the value change
                    PropertyValueSetter.SetPropertyValue(asset, prop, change.NewValue);
                    asset.Write(writePath);

                    results.Add(new { change.PropertyName, success = true, writePath });
                }
                catch (Exception ex)
                {
                    results.Add(new { change.PropertyName, success = false, error = ex.Message });
                }
            }

            fileAccess.Dispose();
            var applied = results.Count(r => ((dynamic)r).success);
            pendingChanges.Clear();
            return Results.Ok(new { applied, total = results.Count, results });
        });

        // ========= Thumbnail Preview =========
        // Returns raw JPEG image data for assets that have embedded thumbnails
        api.MapGet("/assets/thumbnail", (string path) =>
        {
            try
            {
                var asset = new UAssetAPI.UAsset(path, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                if (asset.Thumbnails == null || asset.Thumbnails.Count == 0)
                    return Results.NotFound();

                var thumb = asset.Thumbnails.Values.First();

                // CompressedImageData is typically JPEG
                if (thumb.CompressedImageData != null && thumb.CompressedImageData.Length > 0)
                    return Results.File(thumb.CompressedImageData, "image/jpeg");

                // ImageData is raw BGRA8 — less common but possible
                if (thumb.ImageData != null && thumb.ImageData.Length > 0)
                    return Results.File(thumb.ImageData, "application/octet-stream");

                return Results.NotFound();
            }
            catch { return Results.NotFound(); }
        });

        // Check which assets in a list have thumbnails (batch check)
        api.MapGet("/assets/thumbnails/check", (ProjectManager pm) =>
        {
            var project = pm.GetActiveProject();
            if (project == null) return Results.Ok(Array.Empty<object>());
            var cfg = pm.BuildConfig(project);
            if (!Directory.Exists(cfg.ContentPath)) return Results.Ok(Array.Empty<object>());

            var results = new List<object>();
            var files = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
                .Where(f => !f.Contains("__External")).ToList();

            foreach (var file in files)
            {
                try
                {
                    var asset = new UAssetAPI.UAsset(file, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                    if (asset.Thumbnails != null && asset.Thumbnails.Count > 0)
                    {
                        var thumb = asset.Thumbnails.Values.First();
                        if ((thumb.CompressedImageData?.Length ?? 0) > 0 || (thumb.ImageData?.Length ?? 0) > 0)
                        {
                            results.Add(new
                            {
                                FilePath = file,
                                Name = Path.GetFileNameWithoutExtension(file),
                                Width = Math.Abs(thumb.Width),
                                Height = Math.Abs(thumb.Height)
                            });
                        }
                    }
                }
                catch { }
            }
            return Results.Ok(results);
        });

        // ========= Staged =========
        api.MapGet("/staged", (SafeFileAccess fa) => Results.Ok(fa.ListStagedFiles()));
        api.MapPost("/staged/apply", (SafeFileAccess fa, BackupService b) => Results.Ok(fa.ApplyAllStaged(b)));
        api.MapPost("/staged/discard", (SafeFileAccess fa) => { fa.DiscardAllStaged(); return Results.Ok(new { message = "Discarded." }); });

        app.MapFallbackToFile("index.html");

        var url = "http://0.0.0.0:5120";
        var activeProject = projectManager.GetActiveProject();
        Console.Error.WriteLine($"\n  FortniteForge Web Dashboard");
        Console.Error.WriteLine($"  Project: {activeProject?.Name ?? "None"}");
        Console.Error.WriteLine($"  Projects: {projectManager.ListProjects().Count} loaded");
        Console.Error.WriteLine($"  Open: http://localhost:5120\n");

        app.Run(url);
    }

    /// <summary>
    /// Builds a fresh set of services for a specific project config.
    /// Used when the active project changes — avoids stale DI singletons.
    /// </summary>
    private static (AssetService Asset, DeviceService Device, AuditService Audit, DigestService Digest, SafeFileAccess FileAccess) BuildProjectServices(ForgeConfig config, ILoggerFactory lf)
    {
        var detector = new UefnDetector(config, lf.CreateLogger<UefnDetector>());
        var fileAccess = new SafeFileAccess(config, detector, lf.CreateLogger<SafeFileAccess>());
        var guard = new AssetGuard(config, detector, lf.CreateLogger<AssetGuard>());
        var assetService = new AssetService(config, guard, fileAccess, lf.CreateLogger<AssetService>());
        var digestService = new DigestService(config, lf.CreateLogger<DigestService>());
        var deviceService = new DeviceService(config, assetService, digestService, lf.CreateLogger<DeviceService>());
        var auditService = new AuditService(config, deviceService, assetService, digestService, guard, lf.CreateLogger<AuditService>());
        return (assetService, deviceService, auditService, digestService, fileAccess);
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

// Request models
public record AddProjectRequest(string Path, string? Type, string? Name);
public record IdRequest(string Id);

public class PendingChange
{
    public string FilePath { get; set; } = "";
    public string ExportName { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public string PropertyType { get; set; } = "";
    public string DeviceName { get; set; } = "";
}

public static class PropertyValueSetter
{
    public static void SetPropertyValue(UAssetAPI.UAsset asset, UAssetAPI.PropertyTypes.Objects.PropertyData prop, string newValue)
    {
        switch (prop)
        {
            case UAssetAPI.PropertyTypes.Objects.BoolPropertyData b:
                b.Value = bool.Parse(newValue); break;
            case UAssetAPI.PropertyTypes.Objects.IntPropertyData i:
                i.Value = int.Parse(newValue); break;
            case UAssetAPI.PropertyTypes.Objects.FloatPropertyData f:
                f.Value = float.Parse(newValue); break;
            case UAssetAPI.PropertyTypes.Objects.DoublePropertyData d:
                d.Value = double.Parse(newValue); break;
            case UAssetAPI.PropertyTypes.Objects.StrPropertyData s:
                s.Value = UAssetAPI.UnrealTypes.FString.FromString(newValue); break;
            case UAssetAPI.PropertyTypes.Objects.NamePropertyData n:
                n.Value = UAssetAPI.UnrealTypes.FName.FromString(asset, newValue); break;
            case UAssetAPI.PropertyTypes.Objects.EnumPropertyData e:
                e.Value = UAssetAPI.UnrealTypes.FName.FromString(asset, newValue); break;
            case UAssetAPI.PropertyTypes.Objects.BytePropertyData bp:
                if (byte.TryParse(newValue, out var bv)) bp.Value = bv;
                else bp.EnumValue = UAssetAPI.UnrealTypes.FName.FromString(asset, newValue);
                break;
            default:
                throw new NotSupportedException($"Cannot set value on {prop.GetType().Name}. Supported: Bool, Int, Float, Double, String, Name, Enum, Byte.");
        }
    }
}
