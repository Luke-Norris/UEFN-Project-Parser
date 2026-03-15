using FortniteForge.Core.Config;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Core.Services;

/// <summary>
/// Provides safe file access for .uasset/.umap files.
///
/// Read operations: copies the file to a temp directory first, parses the copy.
/// This prevents file locking conflicts with UEFN.
///
/// Write operations: writes to a staging directory instead of the source.
/// Staged changes can be applied later when UEFN is closed.
/// </summary>
public class SafeFileAccess : IDisposable
{
    private readonly ForgeConfig _config;
    private readonly UefnDetector _detector;
    private readonly ILogger<SafeFileAccess> _logger;
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();
    private bool _disposed;

    public SafeFileAccess(ForgeConfig config, UefnDetector detector, ILogger<SafeFileAccess> logger)
    {
        _config = config;
        _detector = detector;
        _logger = logger;
        _tempDir = Path.Combine(Path.GetTempPath(), "FortniteForge", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Opens an asset safely for reading. Copies to temp first to avoid
    /// file locking conflicts with UEFN.
    /// </summary>
    public UAsset OpenForRead(string assetPath)
    {
        var tempPath = CopyToTemp(assetPath);
        return new UAsset(tempPath, EngineVersion.VER_UE5_4);
    }

    /// <summary>
    /// Opens an asset for modification. Returns the parsed UAsset and the path
    /// where the modified version should be written.
    ///
    /// In Staged/ReadOnly mode: returns a staging path.
    /// In Direct mode: returns the original path (with backup responsibility on caller).
    /// </summary>
    public (UAsset Asset, string WritePath) OpenForWrite(string assetPath)
    {
        var status = _detector.GetStatus();

        if (status.Mode == OperationMode.ReadOnly)
            throw new InvalidOperationException(
                $"Cannot open for write: read-only mode is active. Reason: {status.ModeReason}");

        // Always read from a copy
        var tempPath = CopyToTemp(assetPath);
        var asset = new UAsset(tempPath, EngineVersion.VER_UE5_4);

        // Determine write path
        string writePath;
        if (status.Mode == OperationMode.Staged)
        {
            writePath = GetStagedPath(assetPath);
            var writeDir = Path.GetDirectoryName(writePath);
            if (!string.IsNullOrEmpty(writeDir))
                Directory.CreateDirectory(writeDir);
        }
        else
        {
            // Direct mode — write to original location
            writePath = assetPath;
        }

        return (asset, writePath);
    }

    /// <summary>
    /// Returns the staging path for a given source file.
    /// Mirrors the project directory structure inside the staging directory.
    /// </summary>
    public string GetStagedPath(string originalPath)
    {
        var stagingRoot = GetStagingRoot();

        if (!string.IsNullOrEmpty(_config.ProjectPath))
        {
            var relativePath = Path.GetRelativePath(_config.ProjectPath, originalPath);
            if (!relativePath.StartsWith(".."))
                return Path.Combine(stagingRoot, relativePath);
        }

        // Fallback: use filename only
        return Path.Combine(stagingRoot, Path.GetFileName(originalPath));
    }

    /// <summary>
    /// Lists all staged files pending application.
    /// </summary>
    public List<StagedFile> ListStagedFiles()
    {
        var stagingRoot = GetStagingRoot();
        if (!Directory.Exists(stagingRoot))
            return new();

        var staged = new List<StagedFile>();
        var files = Directory.EnumerateFiles(stagingRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".uasset") || f.EndsWith(".umap"));

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(stagingRoot, file);
            var originalPath = Path.Combine(_config.ProjectPath, relativePath);

            staged.Add(new StagedFile
            {
                StagedPath = file,
                OriginalPath = originalPath,
                RelativePath = relativePath,
                Exists = File.Exists(originalPath),
                StagedAt = File.GetLastWriteTime(file),
                Size = new FileInfo(file).Length
            });
        }

        return staged.OrderBy(s => s.RelativePath).ToList();
    }

    /// <summary>
    /// Applies a staged file back to the project.
    /// Blocks if UEFN is running.
    /// </summary>
    public ApplyResult ApplyStaged(string stagedPath, BackupService? backupService = null)
    {
        var status = _detector.GetStatus();
        if (status.IsUefnRunning)
        {
            return new ApplyResult
            {
                Success = false,
                Message = $"Cannot apply: UEFN is running (PID {status.UefnPid}). Close UEFN first."
            };
        }

        if (status.Mode == OperationMode.ReadOnly)
        {
            return new ApplyResult
            {
                Success = false,
                Message = "Cannot apply: read-only mode is active."
            };
        }

        var stagingRoot = GetStagingRoot();
        var relativePath = Path.GetRelativePath(stagingRoot, stagedPath);
        var originalPath = Path.Combine(_config.ProjectPath, relativePath);

        try
        {
            // Backup original if it exists
            if (File.Exists(originalPath) && backupService != null && _config.AutoBackup)
            {
                backupService.CreateBackup(originalPath);
            }

            var dir = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.Copy(stagedPath, originalPath, overwrite: true);

            // Also copy paired .uexp if staged
            var stagedUexp = Path.ChangeExtension(stagedPath, ".uexp");
            if (File.Exists(stagedUexp))
            {
                var originalUexp = Path.ChangeExtension(originalPath, ".uexp");
                File.Copy(stagedUexp, originalUexp, overwrite: true);
            }

            // Remove staged files
            File.Delete(stagedPath);
            if (File.Exists(stagedUexp))
                File.Delete(stagedUexp);

            _logger.LogInformation("Applied staged file: {Relative}", relativePath);

            return new ApplyResult
            {
                Success = true,
                Message = $"Applied: {relativePath}",
                AppliedPath = originalPath
            };
        }
        catch (Exception ex)
        {
            return new ApplyResult
            {
                Success = false,
                Message = $"Failed to apply {relativePath}: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Applies all staged files.
    /// </summary>
    public List<ApplyResult> ApplyAllStaged(BackupService? backupService = null)
    {
        var results = new List<ApplyResult>();
        var staged = ListStagedFiles();

        foreach (var file in staged)
        {
            results.Add(ApplyStaged(file.StagedPath, backupService));
        }

        return results;
    }

    /// <summary>
    /// Discards a staged file.
    /// </summary>
    public void DiscardStaged(string stagedPath)
    {
        if (File.Exists(stagedPath))
            File.Delete(stagedPath);

        var uexp = Path.ChangeExtension(stagedPath, ".uexp");
        if (File.Exists(uexp))
            File.Delete(uexp);
    }

    /// <summary>
    /// Discards all staged files.
    /// </summary>
    public void DiscardAllStaged()
    {
        var stagingRoot = GetStagingRoot();
        if (Directory.Exists(stagingRoot))
            Directory.Delete(stagingRoot, recursive: true);
    }

    private string CopyToTemp(string assetPath)
    {
        if (!File.Exists(assetPath))
            throw new FileNotFoundException($"Asset file not found: {assetPath}");

        // Create a subdirectory structure in temp to avoid name collisions
        var hash = assetPath.GetHashCode().ToString("X8");
        var fileName = Path.GetFileName(assetPath);
        var tempSubDir = Path.Combine(_tempDir, hash);
        Directory.CreateDirectory(tempSubDir);

        var tempPath = Path.Combine(tempSubDir, fileName);
        File.Copy(assetPath, tempPath, overwrite: true);
        _tempFiles.Add(tempPath);

        // Also copy .uexp if it exists (UAssetAPI needs both)
        var uexpPath = Path.ChangeExtension(assetPath, ".uexp");
        if (File.Exists(uexpPath))
        {
            var tempUexp = Path.ChangeExtension(tempPath, ".uexp");
            File.Copy(uexpPath, tempUexp, overwrite: true);
            _tempFiles.Add(tempUexp);
        }

        _logger.LogDebug("Copied {Source} to temp: {Temp}", assetPath, tempPath);
        return tempPath;
    }

    private string GetStagingRoot()
    {
        var stagingDir = _config.StagingDirectory;
        if (Path.IsPathRooted(stagingDir))
            return stagingDir;

        return Path.Combine(_config.ProjectPath, stagingDir);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not clean up temp directory: {Dir}", _tempDir);
        }
    }
}

public class StagedFile
{
    public string StagedPath { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool Exists { get; set; }
    public DateTime StagedAt { get; set; }
    public long Size { get; set; }
}

public class ApplyResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? AppliedPath { get; set; }
}
