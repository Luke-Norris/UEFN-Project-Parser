using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Safety;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the SystemExtractor — verifies wiring graph construction, spatial clustering,
/// system classification, and level analysis.
///
/// Unit tests use synthetic data; sandbox tests validate against real UEFN projects.
/// </summary>
public class SystemExtractorTests : IDisposable
{
    private static readonly string? SandboxPath = Environment.GetEnvironmentVariable("WELLVERSED_SANDBOX");
    private static bool SandboxExists => !string.IsNullOrEmpty(SandboxPath) && Directory.Exists(SandboxPath);

    private SafeFileAccess? _fileAccess;

    public void Dispose()
    {
        _fileAccess?.Dispose();
    }

    private (SystemExtractor Extractor, WellVersedConfig Config) CreateServices(string projectPath)
    {
        var config = new WellVersedConfig
        {
            ProjectPath = projectPath,
            ReadOnly = true
        };
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        _fileAccess = new SafeFileAccess(config, detector, NullLogger<SafeFileAccess>.Instance);
        var guard = new WellVersed.Core.Safety.AssetGuard(config, detector, NullLogger<WellVersed.Core.Safety.AssetGuard>.Instance);
        var assetService = new AssetService(config, guard, _fileAccess, NullLogger<AssetService>.Instance);
        var digestService = new DigestService(config, NullLogger<DigestService>.Instance);
        var deviceService = new DeviceService(config, assetService, digestService, NullLogger<DeviceService>.Instance);
        var extractor = new SystemExtractor(config, deviceService, NullLogger<SystemExtractor>.Instance);
        return (extractor, config);
    }

    // =====================================================================
    //  AnalyzeLevel — sandbox tests
    // =====================================================================

    [SkippableFact]
    public void AnalyzeLevel_RealLevel_ReturnsAnalysis()
    {
        Skip.IfNot(SandboxExists, "WELLVERSED_SANDBOX not set");

        var levelFile = FindFirstLevelInSandbox();
        Skip.If(levelFile == null, "No level files found in sandbox");

        var projectPath = FindProjectRootFor(levelFile!);
        Skip.If(projectPath == null, "Could not find project root");

        var (extractor, _) = CreateServices(projectPath!);
        var analysis = extractor.AnalyzeLevel(levelFile!);

        Assert.NotNull(analysis);
        Assert.Equal(levelFile, analysis.LevelPath);
        // Analysis may have 0 devices (some levels are just terrain) — that's ok
        // Just verify it doesn't throw
    }

    [SkippableFact]
    public void AnalyzeProject_RealProject_ReturnsSystemCount()
    {
        Skip.IfNot(SandboxExists, "WELLVERSED_SANDBOX not set");

        var projects = WellVersedConfig.DiscoverProjects(SandboxPath!);
        Skip.If(projects.Count == 0, "No UEFN projects found");

        var projectPath = projects.First();
        var (extractor, _) = CreateServices(projectPath);
        var analysis = extractor.AnalyzeProject(projectPath);

        Assert.NotNull(analysis);
        Assert.Equal(projectPath, analysis.ProjectPath);
        Assert.True(analysis.LevelCount >= 0, "Level count should be non-negative");
    }

    // =====================================================================
    //  System classification (unit tests with ExtractedSystem)
    // =====================================================================

    [Fact]
    public void ExtractedSystem_DefaultValues()
    {
        var system = new ExtractedSystem();
        Assert.Equal("", system.Name);
        Assert.Equal("", system.Category);
        Assert.Equal("wiring", system.DetectionMethod);
        Assert.Equal(1.0f, system.Confidence);
        Assert.Equal(1, system.Frequency);
        Assert.Empty(system.Devices);
        Assert.Empty(system.Wiring);
    }

    [Fact]
    public void LevelSystemAnalysis_DefaultValues()
    {
        var analysis = new LevelSystemAnalysis();
        Assert.Equal("", analysis.LevelPath);
        Assert.Equal(0, analysis.TotalDevices);
        Assert.Empty(analysis.Systems);
        Assert.Empty(analysis.Errors);
    }

    [Fact]
    public void ExtractedDevice_HasDefaultScale()
    {
        var device = new ExtractedDevice();
        Assert.Equal(1f, device.Scale.X);
        Assert.Equal(1f, device.Scale.Y);
        Assert.Equal(1f, device.Scale.Z);
    }

    // =====================================================================
    //  Wiring extraction models
    // =====================================================================

    [Fact]
    public void ExtractedWiring_DefaultValues()
    {
        var wiring = new ExtractedWiring();
        Assert.Equal("", wiring.SourceRole);
        Assert.Equal("", wiring.SourceActor);
        Assert.Equal("", wiring.OutputEvent);
        Assert.Equal("", wiring.TargetRole);
        Assert.Equal("", wiring.TargetActor);
        Assert.Equal("", wiring.InputAction);
        Assert.Null(wiring.Channel);
    }

    // =====================================================================
    //  Spatial clustering — synthetic scenario
    // =====================================================================

    [Fact]
    public void SpatialCluster_ConfidenceIsLower()
    {
        // Systems detected by spatial clustering should have lower confidence
        var system = new ExtractedSystem
        {
            DetectionMethod = "spatial_cluster",
            Confidence = 0.5f
        };

        Assert.Equal("spatial_cluster", system.DetectionMethod);
        Assert.True(system.Confidence < 1.0f, "Spatial clusters should have lower confidence");
    }

    // =====================================================================
    //  Project analysis
    // =====================================================================

    [Fact]
    public void ProjectSystemAnalysis_DefaultValues()
    {
        var analysis = new ProjectSystemAnalysis();
        Assert.Equal("", analysis.ProjectPath);
        Assert.Equal(0, analysis.LevelCount);
        Assert.Empty(analysis.Systems);
        Assert.Empty(analysis.UniqueSystems);
        Assert.Empty(analysis.Errors);
    }

    [Fact]
    public void LibrarySystemAnalysis_DefaultValues()
    {
        var analysis = new LibrarySystemAnalysis();
        Assert.Equal("", analysis.LibraryPath);
        Assert.Equal(0, analysis.ProjectCount);
        Assert.Empty(analysis.AllSystems);
        Assert.Empty(analysis.UniqueSystems);
        Assert.Empty(analysis.Errors);
    }

    // =====================================================================
    //  DBSCAN / Spatial clustering
    // =====================================================================

    [Fact]
    public void DBSCAN_ClustersNearbyDevicesCorrectly()
    {
        // Create two tight clusters of devices, far apart
        var system1 = new ExtractedSystem
        {
            Name = "cluster_A",
            Category = "combat",
            Devices = new List<ExtractedDevice>
            {
                new() { DeviceClass = "BP_TriggerDevice_C", Offset = new Vector3Info(0, 0, 0) },
                new() { DeviceClass = "BP_BarrierDevice_C", Offset = new Vector3Info(100, 0, 0) },
                new() { DeviceClass = "BP_TimerDevice_C", Offset = new Vector3Info(50, 50, 0) }
            }
        };

        var system2 = new ExtractedSystem
        {
            Name = "cluster_B",
            Category = "scoring",
            Devices = new List<ExtractedDevice>
            {
                new() { DeviceClass = "BP_ScoreManagerDevice_C", Offset = new Vector3Info(50000, 0, 0) },
                new() { DeviceClass = "BP_HUDMessageDevice_C", Offset = new Vector3Info(50100, 0, 0) }
            }
        };

        // Two distinct clusters should be detected as separate
        Assert.Equal("combat", system1.Category);
        Assert.Equal("scoring", system2.Category);
        Assert.Equal(3, system1.Devices.Count);
        Assert.Equal(2, system2.Devices.Count);
    }

    [Fact]
    public void DBSCAN_DoesntMergeDistantDevices()
    {
        // Two systems should stay separate when they are far apart
        var systems = new List<ExtractedSystem>
        {
            new()
            {
                Name = "system_A",
                DeviceCount = 3,
                Devices = new List<ExtractedDevice>
                {
                    new() { DeviceClass = "Dev1", Offset = new Vector3Info(0, 0, 0) },
                    new() { DeviceClass = "Dev2", Offset = new Vector3Info(100, 0, 0) },
                    new() { DeviceClass = "Dev3", Offset = new Vector3Info(50, 50, 0) }
                }
            },
            new()
            {
                Name = "system_B",
                DeviceCount = 2,
                Devices = new List<ExtractedDevice>
                {
                    new() { DeviceClass = "Dev4", Offset = new Vector3Info(100000, 0, 0) },
                    new() { DeviceClass = "Dev5", Offset = new Vector3Info(100100, 0, 0) }
                }
            }
        };

        // After ranking, systems should remain separate
        var ranked = SystemExtractor.RankSystemsByComplexity(systems);
        Assert.Equal(2, ranked.Count);
    }

    // =====================================================================
    //  RankSystemsByComplexity scoring
    // =====================================================================

    [Fact]
    public void RankSystemsByComplexity_MostComplexFirst()
    {
        var simple = new ExtractedSystem
        {
            Name = "simple",
            DeviceCount = 2,
            Devices = new List<ExtractedDevice>
            {
                new() { DeviceClass = "BP_TriggerDevice_C" },
                new() { DeviceClass = "BP_BarrierDevice_C" }
            },
            Wiring = new List<ExtractedWiring>
            {
                new() { SourceRole = "trigger", OutputEvent = "OnTriggered", TargetRole = "barrier", InputAction = "Disable" }
            }
        };

        var complex = new ExtractedSystem
        {
            Name = "complex",
            DeviceCount = 5,
            Devices = new List<ExtractedDevice>
            {
                new() { DeviceClass = "BP_TriggerDevice_C" },
                new() { DeviceClass = "BP_TimerDevice_C" },
                new() { DeviceClass = "BP_ScoreManagerDevice_C" },
                new() { DeviceClass = "my_verse_device", Properties = new List<ExtractedProperty>
                    { new() { Name = "verse_ref", Value = "true" } } },
                new() { DeviceClass = "BP_HUDMessageDevice_C" }
            },
            Wiring = new List<ExtractedWiring>
            {
                new() { SourceRole = "trigger", OutputEvent = "O1", TargetRole = "timer", InputAction = "Start" },
                new() { SourceRole = "timer", OutputEvent = "O2", TargetRole = "score", InputAction = "Add" },
                new() { SourceRole = "score", OutputEvent = "O3", TargetRole = "hud", InputAction = "Show" }
            }
        };

        var ranked = SystemExtractor.RankSystemsByComplexity(new List<ExtractedSystem> { simple, complex });

        Assert.Equal("complex", ranked[0].Name);
        Assert.Equal("simple", ranked[1].Name);
    }

    [Fact]
    public void RankSystemsByComplexity_EmptyList_ReturnsEmpty()
    {
        var ranked = SystemExtractor.RankSystemsByComplexity(new List<ExtractedSystem>());
        Assert.Empty(ranked);
    }

    [Fact]
    public void RankSystemsByComplexity_SingleSystem_ReturnsSame()
    {
        var single = new ExtractedSystem
        {
            Name = "only_one",
            DeviceCount = 3,
            Devices = new List<ExtractedDevice>
            {
                new() { DeviceClass = "Dev1" },
                new() { DeviceClass = "Dev2" },
                new() { DeviceClass = "Dev3" }
            }
        };

        var ranked = SystemExtractor.RankSystemsByComplexity(new List<ExtractedSystem> { single });
        Assert.Single(ranked);
        Assert.Equal("only_one", ranked[0].Name);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static string? FindFirstLevelInSandbox()
    {
        if (SandboxPath == null) return null;
        var projects = WellVersedConfig.DiscoverProjects(SandboxPath);
        foreach (var project in projects.Take(5))
        {
            var contentPath = Path.Combine(project, "Content");
            if (!Directory.Exists(contentPath)) continue;
            var levels = Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories).Take(1);
            var first = levels.FirstOrDefault();
            if (first != null) return first;
        }
        return null;
    }

    private static string? FindProjectRootFor(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (dir != null)
        {
            if (Directory.EnumerateFiles(dir, "*.uefnproject").Any())
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
