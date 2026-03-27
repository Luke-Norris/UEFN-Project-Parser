using System.Text.Json;
using WellVersed.Core.Models;
using WellVersed.Core.Services;
using WellVersed.Core.Services.VerseGeneration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the ProjectAssembler — verifies project folder structure creation,
/// .uefnproject file validity, verse file generation, and device manifest output.
///
/// Each test creates a temp directory and cleans it up after.
/// </summary>
public class ProjectAssemblerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "WellVersedTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* cleanup best-effort */ }
        }
    }

    private ProjectAssembler CreateAssembler()
    {
        var config = new WellVersed.Core.Config.WellVersedConfig { ProjectPath = "." };
        var verseDeviceGen = new VerseDeviceGenerator(config, NullLogger<VerseDeviceGenerator>.Instance);
        var verseUIGen = new VerseUIGenerator(config, NullLogger<VerseUIGenerator>.Instance);
        return new ProjectAssembler(verseDeviceGen, verseUIGen, NullLogger<ProjectAssembler>.Instance);
    }

    private GameDesigner CreateDesigner()
    {
        return new GameDesigner(NullLogger<GameDesigner>.Instance);
    }

    // =====================================================================
    //  AssembleProject — folder structure
    // =====================================================================

    [Fact]
    public void AssembleProject_CreatesFolderStructure()
    {
        var outputDir = CreateTempDir();
        var projectDir = Path.Combine(outputDir, "TestProject");
        var assembler = CreateAssembler();
        var designer = CreateDesigner();

        var design = designer.DesignGame("team deathmatch for 8 players");
        var plan = designer.DesignToDevicePlan(design);

        var result = assembler.AssembleProject(design, plan, projectDir);

        Assert.True(Directory.Exists(projectDir), "Project directory should exist");
        Assert.True(Directory.Exists(Path.Combine(projectDir, "Config")), "Config directory should exist");
        Assert.True(Directory.Exists(Path.Combine(projectDir, "Content")), "Content directory should exist");
    }

    [Fact]
    public void AssembleProject_CreatesUefnProjectFile()
    {
        var outputDir = CreateTempDir();
        var projectDir = Path.Combine(outputDir, "TestProject");
        var assembler = CreateAssembler();
        var designer = CreateDesigner();

        var design = designer.DesignGame("team deathmatch");
        var plan = designer.DesignToDevicePlan(design);

        var result = assembler.AssembleProject(design, plan, projectDir);

        // Find .uefnproject file
        var uefnFiles = Directory.GetFiles(projectDir, "*.uefnproject");
        Assert.True(uefnFiles.Length >= 1, "Should have a .uefnproject file");

        // Verify it's valid JSON
        var json = File.ReadAllText(uefnFiles[0]);
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void AssembleProject_GeneratesVerseFiles()
    {
        var outputDir = CreateTempDir();
        var projectDir = Path.Combine(outputDir, "TestProject");
        var assembler = CreateAssembler();
        var designer = CreateDesigner();

        var design = designer.DesignGame("team deathmatch for 8 players");
        var plan = designer.DesignToDevicePlan(design);

        var result = assembler.AssembleProject(design, plan, projectDir);

        var verseFiles = Directory.GetFiles(Path.Combine(projectDir, "Content"), "*.verse", SearchOption.AllDirectories);
        Assert.True(verseFiles.Length >= 1, $"Should have verse files, found {verseFiles.Length}");
    }

    [Fact]
    public void AssembleProject_CreatesDeviceManifest()
    {
        var outputDir = CreateTempDir();
        var projectDir = Path.Combine(outputDir, "TestProject");
        var assembler = CreateAssembler();
        var designer = CreateDesigner();

        var design = designer.DesignGame("team deathmatch");
        var plan = designer.DesignToDevicePlan(design);

        var result = assembler.AssembleProject(design, plan, projectDir);

        var manifestPath = Path.Combine(projectDir, "Content", "device_manifest.json");
        Assert.True(File.Exists(manifestPath), "Device manifest should exist");

        // Verify it's valid JSON
        var json = File.ReadAllText(manifestPath);
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void AssembleProject_ResultContainsGeneratedFiles()
    {
        var outputDir = CreateTempDir();
        var projectDir = Path.Combine(outputDir, "TestProject");
        var assembler = CreateAssembler();
        var designer = CreateDesigner();

        var design = designer.DesignGame("free for all");
        var plan = designer.DesignToDevicePlan(design);

        var result = assembler.AssembleProject(design, plan, projectDir);

        Assert.True(result.GeneratedFiles.Count >= 3,
            $"Should have generated multiple files, got {result.GeneratedFiles.Count}");
    }

    [Fact]
    public void AssembleProject_TycoonIncludesEconomyVerse()
    {
        var outputDir = CreateTempDir();
        var projectDir = Path.Combine(outputDir, "TestProject");
        var assembler = CreateAssembler();
        var designer = CreateDesigner();

        var design = designer.DesignGame("tycoon with upgrades");
        var plan = designer.DesignToDevicePlan(design);

        var result = assembler.AssembleProject(design, plan, projectDir);

        // Tycoon should generate an economy verse file
        var hasEconomyFile = result.GeneratedFiles.Any(f =>
            f.Contains("economy", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasEconomyFile, "Tycoon project should include economy verse file");
    }

    [Fact]
    public void AssembleProject_DefaultEngineIniExists()
    {
        var outputDir = CreateTempDir();
        var projectDir = Path.Combine(outputDir, "TestProject");
        var assembler = CreateAssembler();
        var designer = CreateDesigner();

        var design = designer.DesignGame("team deathmatch");
        var plan = designer.DesignToDevicePlan(design);

        assembler.AssembleProject(design, plan, projectDir);

        var iniPath = Path.Combine(projectDir, "Config", "DefaultEngine.ini");
        Assert.True(File.Exists(iniPath), "DefaultEngine.ini should exist");
    }
}
