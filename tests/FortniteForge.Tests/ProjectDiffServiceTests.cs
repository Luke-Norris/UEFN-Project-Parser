using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the ProjectDiffService — verifies snapshot creation, listing,
/// comparison, and change detection.
///
/// Uses a temporary project structure to test without needing real UEFN projects.
/// Sandbox tests validate against real project files.
/// </summary>
public class ProjectDiffServiceTests : IDisposable
{
    private static readonly string? SandboxPath = Environment.GetEnvironmentVariable("WELLVERSED_SANDBOX");
    private static bool SandboxExists => !string.IsNullOrEmpty(SandboxPath) && Directory.Exists(SandboxPath);

    private readonly List<string> _tempDirs = new();
    private SafeFileAccess? _fileAccess;

    public void Dispose()
    {
        _fileAccess?.Dispose();
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private (ProjectDiffService Service, string ProjectPath) CreateServiceWithTempProject()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "WVDiffTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(projectDir);
        _tempDirs.Add(projectDir);

        var contentDir = Path.Combine(projectDir, "Content");
        Directory.CreateDirectory(contentDir);

        // Create a fake .uefnproject so config resolves
        File.WriteAllText(Path.Combine(projectDir, "Test.uefnproject"), "{}");

        // Create a couple of fake .verse files for snapshot testing
        File.WriteAllText(Path.Combine(contentDir, "game_logic.verse"),
            "using { /Verse.org/Simulation }\n\ntest_device := class(creative_device):\n    OnBegin<override>()<suspends>:void =\n        Print(\"Hello\")\n");
        File.WriteAllText(Path.Combine(contentDir, "utils.verse"),
            "using { /Verse.org/Simulation }\n\nhelper_function():int = 42\n");

        var config = new WellVersedConfig { ProjectPath = projectDir };
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, NullLogger<SafeFileAccess>.Instance);
        _fileAccess = fileAccess;
        var service = new ProjectDiffService(config, fileAccess, NullLogger<ProjectDiffService>.Instance);

        return (service, projectDir);
    }

    // =====================================================================
    //  TakeSnapshot
    // =====================================================================

    [Fact]
    public void TakeSnapshot_CreatesSnapshotFile()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        var snapshot = service.TakeSnapshot(projectPath, "Test snapshot");

        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrEmpty(snapshot.Id));
        Assert.Equal("Test snapshot", snapshot.Description);
        Assert.True(snapshot.Files.Count >= 2, $"Expected >= 2 files (verse), got {snapshot.Files.Count}");
        Assert.NotNull(snapshot.SnapshotPath);
        Assert.True(File.Exists(snapshot.SnapshotPath), "Snapshot file should exist on disk");
    }

    [Fact]
    public void TakeSnapshot_RecordsVerseFileCount()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        var snapshot = service.TakeSnapshot(projectPath);

        Assert.Equal(2, snapshot.VerseCount);
    }

    [Fact]
    public void TakeSnapshot_ComputesFileHashes()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        var snapshot = service.TakeSnapshot(projectPath);

        foreach (var file in snapshot.Files)
        {
            Assert.False(string.IsNullOrEmpty(file.Hash), $"File {file.RelativePath} missing hash");
            Assert.True(file.Hash.Length == 64, $"SHA-256 hash should be 64 hex chars, got {file.Hash.Length}");
            Assert.True(file.FileSize > 0, $"File {file.RelativePath} has 0 size");
        }
    }

    [Fact]
    public void TakeSnapshot_RecordsLineCountForVerse()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        var snapshot = service.TakeSnapshot(projectPath);

        var verseFiles = snapshot.Files.Where(f => f.Extension == ".verse").ToList();
        Assert.All(verseFiles, f =>
            Assert.True(f.LineCount > 0, $"Verse file {f.RelativePath} should have line count"));
    }

    // =====================================================================
    //  ListSnapshots
    // =====================================================================

    [Fact]
    public void ListSnapshots_FindsCreatedSnapshots()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        // Take two snapshots
        service.TakeSnapshot(projectPath, "First");
        Thread.Sleep(10); // ensure different timestamp
        service.TakeSnapshot(projectPath, "Second");

        var snapshots = service.ListSnapshots(projectPath);

        Assert.True(snapshots.Count >= 2, $"Expected >= 2 snapshots, got {snapshots.Count}");
        Assert.Equal("Second", snapshots[0].Description); // Newest first
        Assert.Equal("First", snapshots[1].Description);
    }

    [Fact]
    public void ListSnapshots_ReturnsEmptyWhenNoSnapshots()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        var snapshots = service.ListSnapshots(projectPath);

        Assert.Empty(snapshots);
    }

    // =====================================================================
    //  CompareToSnapshot — detects changes
    // =====================================================================

    [Fact]
    public void CompareToSnapshot_DetectsAddedFile()
    {
        var (service, projectPath) = CreateServiceWithTempProject();
        var contentPath = Path.Combine(projectPath, "Content");

        // Take baseline snapshot
        var snapshot = service.TakeSnapshot(projectPath, "Baseline");

        // Add a new file
        File.WriteAllText(Path.Combine(contentPath, "new_file.verse"), "# New file\n");

        // Compare
        var diff = service.CompareToSnapshot(projectPath, snapshot.SnapshotPath!);

        Assert.True(diff.AddedCount >= 1, $"Expected >= 1 added file, got {diff.AddedCount}");
        Assert.Contains(diff.Changes, c => c.Type == DiffType.Added && c.FilePath.Contains("new_file"));
    }

    [Fact]
    public void CompareToSnapshot_DetectsModifiedFile()
    {
        var (service, projectPath) = CreateServiceWithTempProject();
        var contentPath = Path.Combine(projectPath, "Content");

        // Take baseline snapshot
        var snapshot = service.TakeSnapshot(projectPath, "Baseline");

        // Modify a file
        var versePath = Path.Combine(contentPath, "game_logic.verse");
        File.AppendAllText(versePath, "\n# Added a comment\n");

        // Compare
        var diff = service.CompareToSnapshot(projectPath, snapshot.SnapshotPath!);

        Assert.True(diff.ModifiedCount >= 1, $"Expected >= 1 modified file, got {diff.ModifiedCount}");
        Assert.Contains(diff.Changes, c => c.Type == DiffType.Modified);
    }

    [Fact]
    public void CompareToSnapshot_DetectsDeletedFile()
    {
        var (service, projectPath) = CreateServiceWithTempProject();
        var contentPath = Path.Combine(projectPath, "Content");

        // Take baseline snapshot
        var snapshot = service.TakeSnapshot(projectPath, "Baseline");

        // Delete a file
        File.Delete(Path.Combine(contentPath, "utils.verse"));

        // Compare
        var diff = service.CompareToSnapshot(projectPath, snapshot.SnapshotPath!);

        Assert.True(diff.DeletedCount >= 1, $"Expected >= 1 deleted file, got {diff.DeletedCount}");
        Assert.Contains(diff.Changes, c => c.Type == DiffType.Deleted);
    }

    [Fact]
    public void CompareToSnapshot_NoChangesWhenNothingModified()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        var snapshot = service.TakeSnapshot(projectPath, "Baseline");
        var diff = service.CompareToSnapshot(projectPath, snapshot.SnapshotPath!);

        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.ModifiedCount);
        Assert.Equal(0, diff.DeletedCount);
        Assert.Contains("No changes", diff.Description);
    }

    // =====================================================================
    //  CompareSnapshots
    // =====================================================================

    [Fact]
    public void CompareSnapshots_DetectsChanges()
    {
        var (service, projectPath) = CreateServiceWithTempProject();
        var contentPath = Path.Combine(projectPath, "Content");

        var snapshot1 = service.TakeSnapshot(projectPath, "Before");

        // Modify a file
        File.AppendAllText(Path.Combine(contentPath, "game_logic.verse"), "\n# Changed\n");
        Thread.Sleep(10);

        var snapshot2 = service.TakeSnapshot(projectPath, "After");

        var diff = service.CompareSnapshots(snapshot1.SnapshotPath!, snapshot2.SnapshotPath!);

        Assert.True(diff.ModifiedCount >= 1, "Should detect modified file between snapshots");
    }

    // =====================================================================
    //  AutoSnapshot
    // =====================================================================

    [Fact]
    public void AutoSnapshot_CreatesSnapshotWithStandardDescription()
    {
        var (service, projectPath) = CreateServiceWithTempProject();

        var snapshot = service.AutoSnapshot(projectPath);

        Assert.Contains("Auto-snapshot", snapshot.Description);
    }

    // =====================================================================
    //  Diff description
    // =====================================================================

    [Fact]
    public void ProjectDiff_DescriptionFormatsCorrectly()
    {
        var diff = new ProjectDiff
        {
            Changes = new List<FileDiff>
            {
                new() { Type = DiffType.Added, FilePath = "a.verse" },
                new() { Type = DiffType.Added, FilePath = "b.verse" },
                new() { Type = DiffType.Modified, FilePath = "c.verse" },
                new() { Type = DiffType.Deleted, FilePath = "d.verse" }
            },
            Description = "2 files added, 1 file modified, 1 file deleted"
        };

        Assert.Equal(2, diff.AddedCount);
        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(1, diff.DeletedCount);
    }

    // =====================================================================
    //  Sandbox-dependent snapshot test
    // =====================================================================

    [SkippableFact]
    public void TakeSnapshot_RealProject_CapturesUassets()
    {
        Skip.IfNot(SandboxExists, "WELLVERSED_SANDBOX not set");

        var projects = WellVersedConfig.DiscoverProjects(SandboxPath!);
        Skip.If(projects.Count == 0, "No UEFN projects found");

        var projectPath = projects.First();
        var (service, _) = CreateServiceForRealProject(projectPath);

        var snapshot = service.TakeSnapshot(projectPath, "Sandbox test");

        Assert.True(snapshot.Files.Count > 0, "Real project should have files");
        Assert.True(snapshot.UassetCount > 0, "Real project should have .uasset files");

        // Clean up the snapshot we created
        if (snapshot.SnapshotPath != null && File.Exists(snapshot.SnapshotPath))
            File.Delete(snapshot.SnapshotPath);
        var snapshotDir = Path.Combine(projectPath, ".wellversed", "snapshots");
        if (Directory.Exists(snapshotDir) && !Directory.EnumerateFileSystemEntries(snapshotDir).Any())
            Directory.Delete(snapshotDir, true);
        var wellversedDir = Path.Combine(projectPath, ".wellversed");
        if (Directory.Exists(wellversedDir) && !Directory.EnumerateFileSystemEntries(wellversedDir).Any())
            Directory.Delete(wellversedDir, true);
    }

    private (ProjectDiffService Service, string ProjectPath) CreateServiceForRealProject(string projectPath)
    {
        var config = new WellVersedConfig
        {
            ProjectPath = projectPath,
            ReadOnly = true
        };
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, NullLogger<SafeFileAccess>.Instance);
        _fileAccess = fileAccess;
        var service = new ProjectDiffService(config, fileAccess, NullLogger<ProjectDiffService>.Instance);
        return (service, projectPath);
    }
}
