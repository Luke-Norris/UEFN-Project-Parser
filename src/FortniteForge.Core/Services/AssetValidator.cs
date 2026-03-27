using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Services;

/// <summary>
/// Validates .uasset files after write operations by re-reading and comparing
/// structural integrity against the pre-write state.
///
/// This catches silent corruption from UAssetAPI serialization bugs,
/// truncated writes, and broken export/import references.
/// </summary>
public class AssetValidator
{
    private readonly ILogger<AssetValidator> _logger;

    public AssetValidator(ILogger<AssetValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Captures a snapshot of asset structure before writing.
    /// Call this BEFORE asset.Write().
    /// </summary>
    public AssetSnapshot CaptureSnapshot(UAsset asset, string context)
    {
        return new AssetSnapshot
        {
            Context = context,
            ExportCount = asset.Exports.Count,
            ImportCount = asset.Imports.Count,
            NameMapCount = asset.GetNameMapIndexList().Count,
            ExportClassTypes = asset.Exports
                .Select(e => e.GetExportClassType()?.ToString() ?? "")
                .ToList(),
            ExportNames = asset.Exports
                .Select(e => e.ObjectName?.ToString() ?? "")
                .ToList(),
            HasLevelExport = asset.Exports.Any(e => e is LevelExport),
            LevelActorCount = asset.Exports.OfType<LevelExport>()
                .FirstOrDefault()?.Actors.Count ?? 0
        };
    }

    /// <summary>
    /// Validates a written .uasset file against a pre-write snapshot.
    /// Re-reads the file from disk and compares structural integrity.
    /// </summary>
    public ValidationResult Validate(string writtenPath, AssetSnapshot before)
    {
        var result = new ValidationResult { FilePath = writtenPath, Context = before.Context };

        // Check file exists and has content
        if (!File.Exists(writtenPath))
        {
            result.AddError("Written file does not exist");
            return result;
        }

        var fileInfo = new FileInfo(writtenPath);
        result.FileSizeBytes = fileInfo.Length;

        if (fileInfo.Length == 0)
        {
            result.AddError("Written file is empty (0 bytes)");
            return result;
        }

        if (fileInfo.Length < 100)
        {
            result.AddError($"Written file is suspiciously small ({fileInfo.Length} bytes)");
            return result;
        }

        // Check paired .uexp exists if it should
        var uexpPath = Path.ChangeExtension(writtenPath, ".uexp");
        var originalUexp = Path.ChangeExtension(writtenPath, ".uexp");
        if (File.Exists(uexpPath))
        {
            var uexpInfo = new FileInfo(uexpPath);
            if (uexpInfo.Length == 0)
            {
                result.AddError("Paired .uexp file is empty (0 bytes)");
                return result;
            }
        }

        // Re-read the written file
        UAsset reread;
        try
        {
            reread = new UAsset(writtenPath, EngineVersion.VER_UE5_4);
        }
        catch (Exception ex)
        {
            result.AddError($"Failed to re-read written file: {ex.Message}");
            return result;
        }

        var after = CaptureSnapshot(reread, "post-write");

        // Compare export count
        if (after.ExportCount != before.ExportCount)
        {
            // Allow increase (clone/add operations) but not decrease unless remove
            if (after.ExportCount < before.ExportCount &&
                !before.Context.Contains("Remove", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    $"Export count decreased: {before.ExportCount} → {after.ExportCount}");
            }
            else
            {
                result.AddInfo(
                    $"Export count changed: {before.ExportCount} → {after.ExportCount}");
            }
        }

        // Compare import count — should never decrease
        if (after.ImportCount < before.ImportCount)
        {
            result.AddWarning(
                $"Import count decreased: {before.ImportCount} → {after.ImportCount}");
        }

        // Verify all original export class types survived
        for (int i = 0; i < Math.Min(before.ExportClassTypes.Count, after.ExportClassTypes.Count); i++)
        {
            if (before.ExportClassTypes[i] != after.ExportClassTypes[i])
            {
                result.AddError(
                    $"Export[{i}] class type changed: '{before.ExportClassTypes[i]}' → '{after.ExportClassTypes[i]}'");
            }
        }

        // Verify LevelExport integrity
        if (before.HasLevelExport)
        {
            if (!after.HasLevelExport)
            {
                result.AddError("LevelExport missing after write");
            }
            else if (after.LevelActorCount < before.LevelActorCount &&
                     !before.Context.Contains("Remove", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    $"Level actor count decreased unexpectedly: {before.LevelActorCount} → {after.LevelActorCount}");
            }
        }

        // Verify export indices are valid (no orphaned OuterIndex references)
        for (int i = 0; i < reread.Exports.Count; i++)
        {
            var export = reread.Exports[i];
            if (export.OuterIndex.IsExport())
            {
                int outerIdx = export.OuterIndex.Index - 1;
                if (outerIdx < 0 || outerIdx >= reread.Exports.Count)
                {
                    result.AddError(
                        $"Export[{i}] '{export.ObjectName}' has invalid OuterIndex → {outerIdx}");
                }
            }
        }

        // Verify NormalExport data isn't empty for exports that had data
        for (int i = 0; i < Math.Min(before.ExportCount, reread.Exports.Count); i++)
        {
            if (reread.Exports[i] is NormalExport ne && ne.Data.Count == 0)
            {
                var originalName = i < before.ExportNames.Count ? before.ExportNames[i] : "?";
                // Only warn — some exports legitimately have no data
                result.AddWarning(
                    $"Export[{i}] '{originalName}' has 0 properties after write");
            }
        }

        result.IsValid = !result.Errors.Any();

        if (result.IsValid)
        {
            _logger.LogInformation(
                "Asset validation PASSED for {Path} ({Context}): {Exports} exports, {Size} bytes",
                writtenPath, before.Context, after.ExportCount, result.FileSizeBytes);
        }
        else
        {
            _logger.LogError(
                "Asset validation FAILED for {Path} ({Context}): {Errors}",
                writtenPath, before.Context, string.Join("; ", result.Errors));
        }

        return result;
    }
}

public class AssetSnapshot
{
    public string Context { get; set; } = "";
    public int ExportCount { get; set; }
    public int ImportCount { get; set; }
    public int NameMapCount { get; set; }
    public List<string> ExportClassTypes { get; set; } = new();
    public List<string> ExportNames { get; set; } = new();
    public bool HasLevelExport { get; set; }
    public int LevelActorCount { get; set; }
}

public class ValidationResult
{
    public string FilePath { get; set; } = "";
    public string Context { get; set; } = "";
    public bool IsValid { get; set; }
    public long FileSizeBytes { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Info { get; set; } = new();

    public void AddError(string msg) => Errors.Add(msg);
    public void AddWarning(string msg) => Warnings.Add(msg);
    public void AddInfo(string msg) => Info.Add(msg);

    public override string ToString()
    {
        var status = IsValid ? "VALID" : "INVALID";
        var details = new List<string>();
        if (Errors.Any()) details.Add($"{Errors.Count} error(s)");
        if (Warnings.Any()) details.Add($"{Warnings.Count} warning(s)");
        return $"[{status}] {FilePath} — {string.Join(", ", details)}";
    }
}
