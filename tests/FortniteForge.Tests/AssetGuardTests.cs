using FortniteForge.Core.Config;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortniteForge.Tests;

public class AssetGuardTests
{
    private static AssetGuard CreateGuard(ForgeConfig? config = null)
    {
        config ??= new ForgeConfig
        {
            ProjectPath = Path.Combine(Path.GetTempPath(), "TestProject"),
            ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" },
            ModifiableFolders = new List<string>()
        };
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        return new AssetGuard(config, detector, NullLogger<AssetGuard>.Instance);
    }

    [Fact]
    public void CheckPath_InsideContent_IsModifiable()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "TestProject");
        var config = new ForgeConfig { ProjectPath = projectPath };
        var guard = CreateGuard(config);

        // Create the content directory structure
        var contentPath = Path.Combine(projectPath, "Content", "MyMaps");
        Directory.CreateDirectory(contentPath);
        try
        {
            var assetPath = Path.Combine(contentPath, "TestLevel.umap");
            var result = guard.CheckPath(assetPath);

            Assert.True(result.IsModifiable);
        }
        finally
        {
            Directory.Delete(Path.Combine(projectPath, "Content"), true);
        }
    }

    [Fact]
    public void CheckPath_OutsideContent_NotModifiable()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "TestProject");
        var config = new ForgeConfig { ProjectPath = projectPath };
        var guard = CreateGuard(config);

        var outsidePath = Path.Combine(Path.GetTempPath(), "OtherProject", "test.uasset");

        var result = guard.CheckPath(outsidePath);

        Assert.False(result.IsModifiable);
        Assert.Contains("outside", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckPath_ReadOnlyFolder_NotModifiable()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "TestProject");
        var config = new ForgeConfig
        {
            ProjectPath = projectPath,
            ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" }
        };
        var guard = CreateGuard(config);

        var assetPath = Path.Combine(projectPath, "Content", "FortniteGame", "Meshes", "test.uasset");

        var result = guard.CheckPath(assetPath);

        Assert.False(result.IsModifiable);
        Assert.Contains("read-only", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckPath_EngineFolder_NotModifiable()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "TestProject");
        var config = new ForgeConfig
        {
            ProjectPath = projectPath,
            ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" }
        };
        var guard = CreateGuard(config);

        var assetPath = Path.Combine(projectPath, "Content", "Engine", "test.uasset");

        var result = guard.CheckPath(assetPath);

        Assert.False(result.IsModifiable);
    }

    [Fact]
    public void CheckPath_WithModifiableFolders_OnlyAllowsConfigured()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "TestProject");
        var config = new ForgeConfig
        {
            ProjectPath = projectPath,
            ModifiableFolders = new List<string> { "MyMaps", "MyDevices" },
            ReadOnlyFolders = new List<string>()
        };
        var guard = CreateGuard(config);

        // Asset in allowed folder
        var allowedPath = Path.Combine(projectPath, "Content", "MyMaps", "test.umap");
        Assert.True(guard.CheckPath(allowedPath).IsModifiable);

        // Asset not in any allowed folder
        var blockedPath = Path.Combine(projectPath, "Content", "OtherStuff", "test.uasset");
        Assert.False(guard.CheckPath(blockedPath).IsModifiable);
    }
}
