using WellVersed.Core.Config;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Manages backups of asset files before modifications.
/// Every modification should create a backup first.
/// </summary>
public class BackupService
{
    private readonly WellVersedConfig _config;
    private readonly ILogger<BackupService> _logger;

    public BackupService(WellVersedConfig config, ILogger<BackupService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Creates a timestamped backup of an asset file.
    /// Returns the path to the backup file.
    /// </summary>
    public string CreateBackup(string assetPath)
    {
        if (!File.Exists(assetPath))
            throw new FileNotFoundException($"Cannot backup non-existent file: {assetPath}");

        var backupDir = GetBackupDirectory(assetPath);
        Directory.CreateDirectory(backupDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = Path.GetFileName(assetPath);
        var backupFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";
        var backupPath = Path.Combine(backupDir, backupFileName);

        File.Copy(assetPath, backupPath, overwrite: false);
        _logger.LogInformation("Backup created: {BackupPath}", backupPath);

        // Also backup the .uexp file if it exists (paired with .uasset)
        var uexpPath = Path.ChangeExtension(assetPath, ".uexp");
        if (File.Exists(uexpPath))
        {
            var uexpBackupName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}.uexp";
            var uexpBackupPath = Path.Combine(backupDir, uexpBackupName);
            File.Copy(uexpPath, uexpBackupPath, overwrite: false);
            _logger.LogInformation("Paired .uexp backup created: {BackupPath}", uexpBackupPath);
        }

        PruneOldBackups(backupDir, fileName);

        return backupPath;
    }

    /// <summary>
    /// Restores an asset from a backup file.
    /// </summary>
    public void RestoreBackup(string backupPath, string targetPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException($"Backup file not found: {backupPath}");

        // Create a safety backup of the current file before restoring
        if (File.Exists(targetPath))
        {
            var preRestoreBackup = CreateBackup(targetPath);
            _logger.LogInformation("Pre-restore backup created: {BackupPath}", preRestoreBackup);
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        _logger.LogInformation("Restored {Target} from backup {Backup}", targetPath, backupPath);

        // Also restore .uexp if present
        var uexpBackup = Path.ChangeExtension(backupPath, ".uexp");
        var uexpTarget = Path.ChangeExtension(targetPath, ".uexp");
        if (File.Exists(uexpBackup))
        {
            File.Copy(uexpBackup, uexpTarget, overwrite: true);
            _logger.LogInformation("Restored paired .uexp from backup");
        }
    }

    /// <summary>
    /// Lists all available backups for a given asset.
    /// </summary>
    public List<BackupInfo> ListBackups(string? assetPath = null)
    {
        var backupRoot = Path.Combine(_config.ProjectPath, _config.BackupDirectory);
        if (!Directory.Exists(backupRoot))
            return new List<BackupInfo>();

        var searchPattern = assetPath != null
            ? $"{Path.GetFileNameWithoutExtension(assetPath)}_*.uasset"
            : "*.uasset";

        var backupFiles = Directory.EnumerateFiles(backupRoot, searchPattern, SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(backupRoot, searchPattern.Replace(".uasset", ".umap"), SearchOption.AllDirectories));

        return backupFiles
            .Select(f => new BackupInfo
            {
                BackupPath = f,
                OriginalFileName = ExtractOriginalName(Path.GetFileName(f)),
                Timestamp = File.GetCreationTimeUtc(f),
                FileSize = new FileInfo(f).Length
            })
            .OrderByDescending(b => b.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Gets the backup directory for a specific asset.
    /// Mirrors the Content folder structure inside the backup directory.
    /// </summary>
    private string GetBackupDirectory(string assetPath)
    {
        var relativePath = Path.GetRelativePath(_config.ContentPath, assetPath);
        var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
        return Path.Combine(_config.ProjectPath, _config.BackupDirectory, relativeDir);
    }

    /// <summary>
    /// Removes old backups beyond the configured maximum.
    /// </summary>
    private void PruneOldBackups(string backupDir, string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var pattern = $"{baseName}_*{extension}";

        var backups = Directory.GetFiles(backupDir, pattern)
            .OrderByDescending(f => File.GetCreationTimeUtc(f))
            .ToList();

        if (backups.Count > _config.MaxBackupsPerFile)
        {
            foreach (var old in backups.Skip(_config.MaxBackupsPerFile))
            {
                File.Delete(old);
                _logger.LogDebug("Pruned old backup: {Path}", old);

                // Also prune paired .uexp
                var oldUexp = Path.ChangeExtension(old, ".uexp");
                if (File.Exists(oldUexp))
                    File.Delete(oldUexp);
            }
        }
    }

    private static string ExtractOriginalName(string backupFileName)
    {
        // Format: OriginalName_20240101_120000.uasset
        var lastUnderscore = backupFileName.LastIndexOf('_');
        if (lastUnderscore > 0)
        {
            var secondLastUnderscore = backupFileName.LastIndexOf('_', lastUnderscore - 1);
            if (secondLastUnderscore > 0)
            {
                return backupFileName[..secondLastUnderscore] + Path.GetExtension(backupFileName);
            }
        }
        return backupFileName;
    }
}

public class BackupInfo
{
    public string BackupPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public long FileSize { get; set; }
}
