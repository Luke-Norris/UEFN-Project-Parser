using WellVersed.Core.Config;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Safety;

/// <summary>
/// Central safety gate for all asset operations.
/// Determines whether an asset is safe to modify based on:
///   1. Operation mode (ReadOnly / Staged / Direct)
///   2. Cooked data flags in the .uasset binary header
///   3. File path (must be within configured modifiable folders)
///   4. Configuration allowlist/blocklist
/// </summary>
public class AssetGuard
{
    private readonly WellVersedConfig _config;
    private readonly UefnDetector _detector;
    private readonly ILogger<AssetGuard> _logger;

    // Package flags that indicate cooked/engine data
    private const EPackageFlags PKG_FilterEditorOnly = EPackageFlags.PKG_FilterEditorOnly;

    public AssetGuard(WellVersedConfig config, UefnDetector detector, ILogger<AssetGuard> logger)
    {
        _config = config;
        _detector = detector;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether an asset file is cooked (contains Epic's compiled data).
    /// Cooked assets must NEVER be modified.
    /// Uses copy-on-read via SafeFileAccess to avoid locking conflicts.
    /// </summary>
    public CookedCheckResult CheckIfCooked(UAsset asset)
    {
        try
        {
            bool isCooked = false;
            string reason = "";

            // Check 1: UsesEventDrivenLoader / cooked flag in the summary
            if (asset.UsesEventDrivenLoader)
            {
                isCooked = true;
                reason = "Asset uses EventDrivenLoader (cooked format).";
            }

            // Check 2: Package flags
            if ((asset.PackageFlags & PKG_FilterEditorOnly) != 0)
            {
                isCooked = true;
                reason = "Asset has PKG_FilterEditorOnly flag set (editor-only data stripped).";
            }

            // Check 3: Check if asset has cooked serialization format
            if (asset.PackageFlags != 0 && HasCookedSerializationMarkers(asset))
            {
                isCooked = true;
                reason = "Asset has cooked serialization markers.";
            }

            return new CookedCheckResult
            {
                IsCooked = isCooked,
                Reason = isCooked ? reason : "Asset is uncooked (user-created).",
                PackageFlags = asset.PackageFlags
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check cooked status. Treating as cooked (safe default).");
            return new CookedCheckResult
            {
                IsCooked = true,
                Reason = $"Could not determine cooked status: {ex.Message}. Defaulting to read-only for safety."
            };
        }
    }

    /// <summary>
    /// Overload that opens the file from path. Prefer the UAsset overload when
    /// the asset is already loaded to avoid double-parsing.
    /// </summary>
    public CookedCheckResult CheckIfCooked(string assetPath, SafeFileAccess fileAccess)
    {
        try
        {
            var asset = fileAccess.OpenForRead(assetPath);
            return CheckIfCooked(asset);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check cooked status for {Path}. Treating as cooked (safe default).", assetPath);
            return new CookedCheckResult
            {
                IsCooked = true,
                Reason = $"Could not determine cooked status: {ex.Message}. Defaulting to read-only for safety."
            };
        }
    }

    /// <summary>
    /// Determines if a file path is within the user's modifiable directories.
    /// </summary>
    public PathCheckResult CheckPath(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        var contentPath = Path.GetFullPath(_config.ContentPath);

        // Must be within the project's Content directory
        if (!fullPath.StartsWith(contentPath, StringComparison.OrdinalIgnoreCase))
        {
            return new PathCheckResult
            {
                IsModifiable = false,
                Reason = $"File is outside the project Content directory: {fullPath}"
            };
        }

        var relativePath = Path.GetRelativePath(contentPath, fullPath);

        // Check explicit read-only folders
        foreach (var readOnlyFolder in _config.ReadOnlyFolders)
        {
            if (relativePath.StartsWith(readOnlyFolder, StringComparison.OrdinalIgnoreCase))
            {
                return new PathCheckResult
                {
                    IsModifiable = false,
                    Reason = $"File is in read-only folder: {readOnlyFolder}"
                };
            }
        }

        // If modifiable folders are specified, file must be in one of them
        if (_config.ModifiableFolders.Count > 0)
        {
            var inModifiable = _config.ModifiableFolders.Any(folder =>
                relativePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase));

            if (!inModifiable)
            {
                return new PathCheckResult
                {
                    IsModifiable = false,
                    Reason = $"File is not in any configured modifiable folder. Relative path: {relativePath}"
                };
            }
        }

        return new PathCheckResult
        {
            IsModifiable = true,
            Reason = "File is in a modifiable location."
        };
    }

    /// <summary>
    /// Full safety check — combines mode check + cooked detection + path validation.
    /// This is the primary gate before any write operation.
    /// </summary>
    public SafetyCheckResult CanModify(string assetPath, SafeFileAccess? fileAccess = null)
    {
        var result = new SafetyCheckResult { AssetPath = assetPath };

        // Check 0: Read-only mode
        if (_config.ReadOnly)
        {
            result.IsAllowed = false;
            result.Reasons.Add("BLOCKED: Read-only mode is enabled in config.");
            return result;
        }

        // Check 1: Path validation
        var pathCheck = CheckPath(assetPath);
        if (!pathCheck.IsModifiable)
        {
            result.IsAllowed = false;
            result.Reasons.Add($"PATH BLOCKED: {pathCheck.Reason}");
            return result;
        }

        // Check 2: Cooked status (use SafeFileAccess if available)
        CookedCheckResult cookedCheck;
        if (fileAccess != null)
        {
            cookedCheck = CheckIfCooked(assetPath, fileAccess);
        }
        else
        {
            // Fallback: open directly (legacy path)
            try
            {
                var asset = new UAsset(assetPath, EngineVersion.VER_UE5_4);
                cookedCheck = CheckIfCooked(asset);
            }
            catch (Exception ex)
            {
                cookedCheck = new CookedCheckResult
                {
                    IsCooked = true,
                    Reason = $"Could not open asset: {ex.Message}"
                };
            }
        }

        if (cookedCheck.IsCooked)
        {
            result.IsAllowed = false;
            result.Reasons.Add($"COOKED ASSET: {cookedCheck.Reason}");
            return result;
        }

        // Check 3: UEFN status — inform about operation mode
        var status = _detector.GetStatus();
        result.OperationMode = status.Mode;

        if (status.Mode == OperationMode.Staged)
        {
            result.IsAllowed = true;
            result.Reasons.Add($"STAGED MODE: {status.ModeReason}. Writes go to staging directory.");
        }
        else
        {
            result.IsAllowed = true;
            result.Reasons.Add("Asset passed all safety checks.");
        }

        return result;
    }

    /// <summary>
    /// Checks for cooked serialization markers beyond just flags.
    /// </summary>
    private bool HasCookedSerializationMarkers(UAsset asset)
    {
        try
        {
            foreach (var export in asset.Exports)
            {
                if (export is NormalExport)
                {
                    var className = export.GetExportClassType()?.ToString() ?? "";
                    if (IsCookedOnlyClass(className))
                        return true;
                }
            }
        }
        catch
        {
            // If we can't check, don't flag it — the other checks should catch it
        }

        return false;
    }

    private static bool IsCookedOnlyClass(string className)
    {
        var cookedOnlyClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShaderResource",
            "CookedMetaData",
            "BulkDataStreamingInfo"
        };

        return cookedOnlyClasses.Contains(className);
    }
}

public class CookedCheckResult
{
    public bool IsCooked { get; set; }
    public string Reason { get; set; } = "";
    public EPackageFlags PackageFlags { get; set; }
}

public class PathCheckResult
{
    public bool IsModifiable { get; set; }
    public string Reason { get; set; } = "";
}

public class SafetyCheckResult
{
    public string AssetPath { get; set; } = "";
    public bool IsAllowed { get; set; }
    public List<string> Reasons { get; set; } = new();
    public OperationMode OperationMode { get; set; }
}
