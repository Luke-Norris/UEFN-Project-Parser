using WellVersed.Core.Config;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WellVersedConfig _config;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ForgeBackupTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var contentDir = Path.Combine(_tempDir, "Content");
        Directory.CreateDirectory(contentDir);

        _config = new WellVersedConfig
        {
            ProjectPath = _tempDir,
            BackupDirectory = ".wellversed/backups",
            MaxBackupsPerFile = 3
        };
        _service = new BackupService(_config, NullLogger<BackupService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void CreateBackup_CopiesFile()
    {
        var assetPath = CreateTestAsset("TestLevel.umap", "test content");

        var backupPath = _service.CreateBackup(assetPath);

        Assert.True(File.Exists(backupPath));
        Assert.Equal("test content", File.ReadAllText(backupPath));
    }

    [Fact]
    public void CreateBackup_IncludesTimestamp()
    {
        var assetPath = CreateTestAsset("TestAsset.uasset", "data");

        var backupPath = _service.CreateBackup(assetPath);
        var backupName = Path.GetFileNameWithoutExtension(backupPath);

        // Should contain original name + timestamp pattern
        Assert.StartsWith("TestAsset_", backupName);
        Assert.Matches(@"TestAsset_\d{8}_\d{6}", backupName);
    }

    [Fact]
    public void CreateBackup_NonExistentFile_Throws()
    {
        var fakePath = Path.Combine(_config.ContentPath, "nonexistent.uasset");

        Assert.Throws<FileNotFoundException>(() => _service.CreateBackup(fakePath));
    }

    [Fact]
    public void CreateBackup_AlsoBackupsPairedUexp()
    {
        var assetPath = CreateTestAsset("TestAsset.uasset", "uasset data");
        var uexpPath = Path.ChangeExtension(assetPath, ".uexp");
        File.WriteAllText(uexpPath, "uexp data");

        var backupPath = _service.CreateBackup(assetPath);

        // Check that the .uexp backup exists too
        var backupDir = Path.GetDirectoryName(backupPath)!;
        var uexpBackups = Directory.GetFiles(backupDir, "*.uexp");
        Assert.NotEmpty(uexpBackups);
    }

    [Fact]
    public void CreateBackup_PrunesOldBackups()
    {
        var assetPath = CreateTestAsset("Prunable.uasset", "v1");

        // Pre-create old backup files with distinct timestamps to simulate history
        var backupDir = Path.Combine(_tempDir, ".wellversed", "backups");
        Directory.CreateDirectory(backupDir);
        for (int i = 0; i < 4; i++)
        {
            var fakeTimestamp = $"20250101_12000{i}";
            File.WriteAllText(Path.Combine(backupDir, $"Prunable_{fakeTimestamp}.uasset"), $"old_v{i}");
        }

        // Now create one real backup — this should trigger pruning
        _service.CreateBackup(assetPath);

        var backups = Directory.GetFiles(backupDir, "Prunable_*.uasset", SearchOption.AllDirectories);

        Assert.True(backups.Length <= _config.MaxBackupsPerFile,
            $"Expected at most {_config.MaxBackupsPerFile} backups, found {backups.Length}");
    }

    [Fact]
    public void ListBackups_ReturnsEmpty_WhenNoBackupsExist()
    {
        var result = _service.ListBackups();
        Assert.Empty(result);
    }

    [Fact]
    public void ListBackups_ReturnsCreatedBackups()
    {
        var assetPath = CreateTestAsset("Listed.uasset", "data");
        _service.CreateBackup(assetPath);

        var backups = _service.ListBackups();

        Assert.NotEmpty(backups);
        Assert.All(backups, b => Assert.True(File.Exists(b.BackupPath)));
    }

    [Fact]
    public void RestoreBackup_RestoresFile()
    {
        var assetPath = CreateTestAsset("Restorable.uasset", "original");
        var backupPath = _service.CreateBackup(assetPath);

        // Modify the original
        File.WriteAllText(assetPath, "modified");
        Assert.Equal("modified", File.ReadAllText(assetPath));

        // Wait so the pre-restore backup gets a different timestamp
        Thread.Sleep(1100);

        // Restore (internally creates a pre-restore backup first)
        _service.RestoreBackup(backupPath, assetPath);

        Assert.Equal("original", File.ReadAllText(assetPath));
    }

    [Fact]
    public void RestoreBackup_NonExistentBackup_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _service.RestoreBackup(@"C:\nonexistent_backup.uasset", @"C:\target.uasset"));
    }

    private string CreateTestAsset(string fileName, string content)
    {
        var path = Path.Combine(_config.ContentPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
