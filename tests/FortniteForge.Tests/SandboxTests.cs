using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortniteForge.Tests;

/// <summary>
/// Tests that run against a UEFN map collection (sandbox).
/// Set FORTNITEFORGE_SANDBOX env var to the path containing UEFN projects.
/// Skipped automatically if the env var is not set or the path doesn't exist.
///
/// These tests discover real UEFN projects dynamically and validate
/// FortniteForge's ability to parse any UEFN project structure.
/// </summary>
public class SandboxTests : IDisposable
{
    private static readonly string? SandboxPath = Environment.GetEnvironmentVariable("FORTNITEFORGE_SANDBOX");
    private static bool SandboxExists => !string.IsNullOrEmpty(SandboxPath) && Directory.Exists(SandboxPath);

    private SafeFileAccess? _fileAccess;

    private (ForgeConfig Config, AssetGuard Guard, SafeFileAccess FileAccess, UefnDetector Detector) CreateServices(string projectPath)
    {
        var config = new ForgeConfig
        {
            ProjectPath = projectPath,
            ReadOnly = true,
            ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" }
        };
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, NullLogger<SafeFileAccess>.Instance);
        _fileAccess = fileAccess;
        var guard = new AssetGuard(config, detector, NullLogger<AssetGuard>.Instance);
        return (config, guard, fileAccess, detector);
    }

    public void Dispose()
    {
        _fileAccess?.Dispose();
    }

    // ===== Project Discovery =====

    [SkippableFact]
    public void DiscoverProjects_FindsMultipleProjects()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);

        Assert.NotEmpty(projects);
        Assert.True(projects.Count >= 2, $"Expected multiple projects, found {projects.Count}");
    }

    [SkippableFact]
    public void DiscoverProjects_AllHaveUefnProjectFile()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);

        foreach (var projectPath in projects)
        {
            var hasProjectFile = Directory.GetFiles(projectPath, "*.uefnproject").Length > 0
                              || Directory.GetFiles(projectPath, "*.uproject").Length > 0;
            Assert.True(hasProjectFile, $"No project file in: {projectPath}");
        }
    }

    // ===== Config / ContentPath Resolution =====

    [SkippableFact]
    public void ContentPath_ResolvesForAllDiscoveredProjects()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        int resolved = 0;

        foreach (var projectPath in projects)
        {
            var config = new ForgeConfig { ProjectPath = projectPath };
            var contentPath = config.ContentPath;

            if (Directory.Exists(contentPath))
            {
                resolved++;
                // Content should contain .uasset or .umap files
                var hasAssets = Directory.EnumerateFiles(contentPath, "*.uasset", SearchOption.AllDirectories).Any()
                             || Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories).Any();
                Assert.True(hasAssets, $"Content dir exists but has no assets: {contentPath}");
            }
        }

        Assert.True(resolved > 0, "No projects had resolvable Content paths");
    }

    [SkippableFact]
    public void ContentPath_AlwaysUnderPlugins()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);

        foreach (var projectPath in projects)
        {
            var config = new ForgeConfig { ProjectPath = projectPath };
            var contentPath = config.ContentPath;

            if (Directory.Exists(contentPath))
            {
                // All UEFN projects use Plugins/<Name>/Content/
                Assert.Contains("Plugins", contentPath, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [SkippableFact]
    public void ProjectName_DetectedForAllProjects()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);

        foreach (var projectPath in projects)
        {
            var config = new ForgeConfig { ProjectPath = projectPath };
            Assert.NotEmpty(config.ProjectName);
            Assert.NotEqual("Unknown", config.ProjectName);
        }
    }

    // ===== Asset Parsing =====

    [SkippableFact]
    public void AssetService_CanListAssetsFromAnyProject()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        int projectsWithAssets = 0;

        foreach (var projectPath in projects.Take(5)) // Test first 5 to keep it fast
        {
            var (config, guard, fileAccess, _) = CreateServices(projectPath);
            if (!Directory.Exists(config.ContentPath)) continue;

            var assetService = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);
            var assets = assetService.ListAssets();

            if (assets.Count > 0)
            {
                projectsWithAssets++;
                // Every asset should have a name and class
                foreach (var asset in assets.Take(10))
                {
                    Assert.NotEmpty(asset.Name);
                    Assert.NotEmpty(asset.AssetClass);
                    Assert.True(asset.FileSize > 0);
                }
            }

            fileAccess.Dispose();
            _fileAccess = null;
        }

        Assert.True(projectsWithAssets > 0, "No projects had parseable assets");
    }

    // ===== Property Extraction (UAssetAPI ScriptSerializationOffset fix) =====

    [SkippableFact]
    public void PropertyExtraction_ExternalActorsHaveProperties()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        int actorsWithProperties = 0;
        int totalActorsTested = 0;

        foreach (var projectPath in projects.Take(5))
        {
            var (config, guard, fileAccess, _) = CreateServices(projectPath);
            if (!Directory.Exists(config.ContentPath)) continue;

            // Find external actors directories
            var externalActorDirs = Directory.EnumerateDirectories(config.ContentPath, "__ExternalActors__", SearchOption.AllDirectories);

            foreach (var extActorDir in externalActorDirs)
            {
                var actorFiles = Directory.EnumerateFiles(extActorDir, "*.uasset", SearchOption.AllDirectories).Take(10);

                foreach (var actorFile in actorFiles)
                {
                    totalActorsTested++;
                    try
                    {
                        var assetService = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);
                        var detail = assetService.InspectAsset(actorFile);

                        var totalProps = detail.Exports.Sum(e => e.Properties.Count);
                        if (totalProps > 0)
                            actorsWithProperties++;
                    }
                    catch
                    {
                        // Some actors may fail to parse — that's OK for this test
                    }
                }
            }

            fileAccess.Dispose();
            _fileAccess = null;
        }

        // At least some external actors should have extractable properties
        Assert.True(totalActorsTested > 0, "No external actors found to test");
        Assert.True(actorsWithProperties > 0,
            $"No properties extracted from any of {totalActorsTested} external actors. " +
            "The ScriptSerializationOffset fix may not be working.");
    }

    [SkippableFact]
    public void PropertyExtraction_DeviceActorsHaveConfigurableProperties()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        var deviceKeywords = new[] { "Device_", "Spawner", "Trigger", "Timer", "Button", "Barrier" };
        int devicesFound = 0;
        int devicesWithProperties = 0;

        foreach (var projectPath in projects.Take(5))
        {
            var (config, guard, fileAccess, _) = CreateServices(projectPath);
            if (!Directory.Exists(config.ContentPath)) continue;

            var externalActorDirs = Directory.EnumerateDirectories(config.ContentPath, "__ExternalActors__", SearchOption.AllDirectories);

            foreach (var extActorDir in externalActorDirs)
            {
                var actorFiles = Directory.EnumerateFiles(extActorDir, "*.uasset", SearchOption.AllDirectories).Take(50);

                foreach (var actorFile in actorFiles)
                {
                    try
                    {
                        var assetService = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);
                        var detail = assetService.InspectAsset(actorFile);

                        // Check if any export class name matches device patterns
                        var deviceExport = detail.Exports.FirstOrDefault(e =>
                            deviceKeywords.Any(kw => e.ClassName.Contains(kw, StringComparison.OrdinalIgnoreCase)));

                        if (deviceExport != null)
                        {
                            devicesFound++;
                            if (deviceExport.Properties.Count > 0)
                                devicesWithProperties++;
                        }
                    }
                    catch { }
                }
            }

            fileAccess.Dispose();
            _fileAccess = null;

            if (devicesFound >= 5) break; // Enough devices tested
        }

        if (devicesFound > 0)
        {
            Assert.True(devicesWithProperties > 0,
                $"Found {devicesFound} devices but none had extractable properties");
        }
    }

    // ===== SafeFileAccess =====

    [SkippableFact]
    public void SafeFileAccess_CopyOnRead_DoesNotModifySource()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        Skip.If(projects.Count == 0, "No projects found");

        var (config, guard, fileAccess, _) = CreateServices(projects[0]);
        if (!Directory.Exists(config.ContentPath)) return;

        var firstAsset = Directory.EnumerateFiles(config.ContentPath, "*.uasset", SearchOption.AllDirectories).FirstOrDefault();
        Skip.If(firstAsset == null, "No assets found");

        var originalModTime = File.GetLastWriteTimeUtc(firstAsset);
        var originalSize = new FileInfo(firstAsset).Length;

        // Read via SafeFileAccess
        var asset = fileAccess.OpenForRead(firstAsset);
        Assert.NotNull(asset);
        Assert.True(asset.Exports.Count >= 0); // Just verify it parsed

        // Verify source file was NOT modified
        Assert.Equal(originalModTime, File.GetLastWriteTimeUtc(firstAsset));
        Assert.Equal(originalSize, new FileInfo(firstAsset).Length);
    }

    [SkippableFact]
    public void SafeFileAccess_ReadOnlyMode_BlocksWrites()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        Skip.If(projects.Count == 0, "No projects found");

        var (config, _, fileAccess, _) = CreateServices(projects[0]);
        Assert.True(config.ReadOnly);

        var firstAsset = Directory.EnumerateFiles(config.ContentPath, "*.uasset", SearchOption.AllDirectories).FirstOrDefault();
        Skip.If(firstAsset == null, "No assets found");

        Assert.Throws<InvalidOperationException>(() => fileAccess.OpenForWrite(firstAsset));
    }

    // ===== UefnDetector =====

    [SkippableFact]
    public void UefnDetector_DetectsUrcWhenPresent()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        var withUrc = projects.Where(p => Directory.Exists(Path.Combine(p, ".urc"))).ToList();

        if (withUrc.Count > 0)
        {
            var config = new ForgeConfig { ProjectPath = withUrc[0] };
            Assert.True(config.HasUrc, $"HasUrc should be true for {withUrc[0]}");
        }

        var withoutUrc = projects.Where(p => !Directory.Exists(Path.Combine(p, ".urc"))).ToList();
        if (withoutUrc.Count > 0)
        {
            var config = new ForgeConfig { ProjectPath = withoutUrc[0] };
            Assert.False(config.HasUrc, $"HasUrc should be false for {withoutUrc[0]}");
        }
    }

    // ===== AssetGuard =====

    [SkippableFact]
    public void AssetGuard_ReadOnlyConfig_BlocksAllModifications()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        Skip.If(projects.Count == 0, "No projects found");

        var (config, guard, fileAccess, _) = CreateServices(projects[0]);
        if (!Directory.Exists(config.ContentPath)) return;

        var firstAsset = Directory.EnumerateFiles(config.ContentPath, "*.uasset", SearchOption.AllDirectories).FirstOrDefault();
        Skip.If(firstAsset == null, "No assets found");

        var result = guard.CanModify(firstAsset, fileAccess);
        Assert.False(result.IsAllowed, "ReadOnly config should block all modifications");
        Assert.Contains("read-only", result.Reasons[0], StringComparison.OrdinalIgnoreCase);
    }

    // ===== Spatial / Transform Extraction =====

    [SkippableFact]
    public void TransformExtraction_SomeActorsHaveRelativeLocation()
    {
        Skip.IfNot(SandboxExists, "FORTNITEFORGE_SANDBOX not set");

        var projects = ForgeConfig.DiscoverProjects(SandboxPath!);
        int actorsWithLocation = 0;
        int totalChecked = 0;

        foreach (var projectPath in projects.Take(3))
        {
            var (config, guard, fileAccess, _) = CreateServices(projectPath);
            if (!Directory.Exists(config.ContentPath)) continue;

            var externalActorDirs = Directory.EnumerateDirectories(config.ContentPath, "__ExternalActors__", SearchOption.AllDirectories);

            foreach (var extActorDir in externalActorDirs)
            {
                foreach (var actorFile in Directory.EnumerateFiles(extActorDir, "*.uasset", SearchOption.AllDirectories).Take(20))
                {
                    totalChecked++;
                    try
                    {
                        var assetService = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);
                        var detail = assetService.InspectAsset(actorFile);

                        var hasLocation = detail.Exports.Any(e =>
                            e.Properties.Any(p => p.Name == "RelativeLocation"));

                        if (hasLocation) actorsWithLocation++;
                    }
                    catch { }
                }
            }

            fileAccess.Dispose();
            _fileAccess = null;
        }

        if (totalChecked > 0)
        {
            Assert.True(actorsWithLocation > 0,
                $"No actors with RelativeLocation found out of {totalChecked} checked");
        }
    }
}
