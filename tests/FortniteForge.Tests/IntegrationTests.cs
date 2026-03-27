using WellVersed.Core.Config;
using WellVersed.Core.Services;
using WellVersed.Core.Safety;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Integration tests that run against the real UEFN_AutoMation_Test project.
/// These tests validate that WellVersed can parse real UEFN assets.
/// Skipped if the project doesn't exist on the current machine.
/// </summary>
public class IntegrationTests : IDisposable
{
    private const string ProjectPath = @"C:\Users\Luke\Documents\Fortnite Projects\UEFN_AutoMation_Test";
    private const string LevelPath = ProjectPath + @"\Content\UEFN_Automation_Test.umap";

    private static bool ProjectExists => Directory.Exists(ProjectPath) && File.Exists(LevelPath);

    private SafeFileAccess? _fileAccess;

    private WellVersedConfig CreateConfig() => new()
    {
        ProjectPath = ProjectPath,
        ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" }
    };

    private (AssetGuard Guard, SafeFileAccess FileAccess) CreateGuardAndFileAccess(WellVersedConfig config)
    {
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, NullLogger<SafeFileAccess>.Instance);
        _fileAccess = fileAccess;
        var guard = new AssetGuard(config, detector, NullLogger<AssetGuard>.Instance);
        return (guard, fileAccess);
    }

    public void Dispose()
    {
        _fileAccess?.Dispose();
    }

    // ===== Asset Service =====

    [SkippableFact]
    public void AssetService_ListAssets_FindsRealAssets()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var (guard, fileAccess) = CreateGuardAndFileAccess(config);
        var service = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);

        var assets = service.ListAssets();

        Assert.NotEmpty(assets);
        Assert.Contains(assets, a => a.Name == "UEFN_Automation_Test");
    }

    [SkippableFact]
    public void AssetService_InspectLevel_ParsesExports()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var (guard, fileAccess) = CreateGuardAndFileAccess(config);
        var service = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);

        var detail = service.InspectAsset(LevelPath);

        Assert.NotNull(detail);
        Assert.True(detail.ExportCount > 0, "Level should have exports");
        Assert.True(detail.ImportCount > 0, "Level should have imports");
        Assert.NotEmpty(detail.Exports);
    }

    [SkippableFact]
    public void AssetService_GetSummary_ReturnsInfo()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var (guard, fileAccess) = CreateGuardAndFileAccess(config);
        var service = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);

        var summary = service.GetAssetSummary(LevelPath);

        Assert.Equal("UEFN_Automation_Test", summary.Name);
        Assert.True(summary.FileSize > 0);
        Assert.NotEmpty(summary.Summary);
    }

    // ===== Level Analytics =====

    [SkippableFact]
    public void Analytics_AnalyzeLevel_ReturnsResults()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var service = new LevelAnalyticsService(config, NullLogger<LevelAnalyticsService>.Instance);

        var analytics = service.AnalyzeLevel(LevelPath);

        Assert.NotNull(analytics);
        Assert.Equal("UEFN_Automation_Test", analytics.LevelName);
        Assert.True(analytics.TotalExports > 0);
        Assert.True(analytics.FileSizeBytes > 0);
        Assert.NotNull(analytics.Performance);
        Assert.NotEmpty(analytics.Performance.Rating);
    }

    [SkippableFact]
    public void Analytics_AnalyzeLevel_DetectsExternalActors()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var service = new LevelAnalyticsService(config, NullLogger<LevelAnalyticsService>.Instance);

        var analytics = service.AnalyzeLevel(LevelPath);

        // The project has 205 external actors
        Assert.True(analytics.TotalActors > 0,
            "Should detect actors (either in-level or external)");
    }

    [SkippableFact]
    public void Analytics_AnalyzeProject_FindsLevel()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var service = new LevelAnalyticsService(config, NullLogger<LevelAnalyticsService>.Instance);

        var results = service.AnalyzeProject();

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.LevelName == "UEFN_Automation_Test");
    }

    // ===== Device Service =====

    [SkippableFact]
    public void DeviceService_FindLevels_FindsUmap()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var (guard, fileAccess) = CreateGuardAndFileAccess(config);
        var assetService = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);
        var digestService = new DigestService(config, NullLogger<DigestService>.Instance);
        var deviceService = new DeviceService(config, assetService, digestService, NullLogger<DeviceService>.Instance);

        var levels = deviceService.FindLevels();

        Assert.NotEmpty(levels);
        Assert.Contains(levels, l => l.Contains("UEFN_Automation_Test.umap"));
    }

    // ===== Asset Guard =====

    [SkippableFact]
    public void AssetGuard_CheckPath_LevelIsModifiable()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var (guard, _) = CreateGuardAndFileAccess(config);

        var result = guard.CheckPath(LevelPath);

        Assert.True(result.IsModifiable,
            $"Level should be modifiable. Reasons: {string.Join(", ", result.Reason)}");
    }

    [SkippableFact]
    public void AssetGuard_CheckCooked_LevelNotCooked()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var config = CreateConfig();
        var (guard, fileAccess) = CreateGuardAndFileAccess(config);

        var result = guard.CheckIfCooked(LevelPath, fileAccess);

        Assert.False(result.IsCooked,
            $"User level should not be cooked. Reasons: {string.Join(", ", result.Reason)}");
    }

    // ===== Config =====

    [SkippableFact]
    public void Config_Load_RealConfig()
    {
        var configPath = Path.Combine(ProjectPath, "forge.config.json");
        Skip.IfNot(File.Exists(configPath), "forge.config.json not found");

        var config = WellVersedConfig.Load(configPath);

        Assert.Equal(ProjectPath, config.ProjectPath);
        Assert.True(Directory.Exists(config.ContentPath));
    }
}
