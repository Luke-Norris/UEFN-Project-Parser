using FortniteForge.Core.Config;
using Microsoft.Extensions.Logging;
using UAssetAPI;

namespace FortniteForge.Core.Safety;

/// <summary>
/// Central safety gate for all asset operations.
/// Determines whether an asset is safe to modify based on:
///   1. Cooked data flags in the .uasset binary header
///   2. File path (must be within configured modifiable folders)
///   3. Configuration allowlist/blocklist
/// </summary>
public class AssetGuard
{
    private readonly ForgeConfig _config;
    private readonly ILogger<AssetGuard> _logger;

    // Package flags that indicate cooked/engine data
    // PKG_FilterEditorOnly = 0x80000000
    // PKG_Cooked (UE5) is typically indicated by the cooked flag in the summary
    private const uint PKG_FilterEditorOnly = 0x80000000;

    public AssetGuard(ForgeConfig config, ILogger<AssetGuard> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether an asset file is cooked (contains Epic's compiled data).
    /// Cooked assets must NEVER be modified.
    /// </summary>
    public CookedCheckResult CheckIfCooked(string assetPath)
    {
        try
        {
            // UAssetAPI can detect cooked status from the package summary
            var asset = new UAsset(assetPath, EngineVersion.VER_FORTNITE_LATEST);

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
            // Cooked assets typically have different internal structure
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
    /// Full safety check — combines cooked detection + path validation.
    /// This is the primary gate before any write operation.
    /// </summary>
    public SafetyCheckResult CanModify(string assetPath)
    {
        var result = new SafetyCheckResult { AssetPath = assetPath };

        // Check 1: Path validation
        var pathCheck = CheckPath(assetPath);
        if (!pathCheck.IsModifiable)
        {
            result.IsAllowed = false;
            result.Reasons.Add($"PATH BLOCKED: {pathCheck.Reason}");
            return result;
        }

        // Check 2: Cooked status
        var cookedCheck = CheckIfCooked(assetPath);
        if (cookedCheck.IsCooked)
        {
            result.IsAllowed = false;
            result.Reasons.Add($"COOKED ASSET: {cookedCheck.Reason}");
            return result;
        }

        result.IsAllowed = true;
        result.Reasons.Add("Asset passed all safety checks.");
        return result;
    }

    /// <summary>
    /// Checks for cooked serialization markers beyond just flags.
    /// </summary>
    private bool HasCookedSerializationMarkers(UAsset asset)
    {
        // Additional heuristic checks for cooked assets:
        // - Cooked assets often have specific name map patterns
        // - Cooked assets may have bulk data references
        // - The presence of certain export types indicates cooking

        // For UEFN specifically, user-created assets should NOT have:
        // - Shader bytecode
        // - Cooked texture data
        // - Stripped editor-only properties

        // This is a conservative check — if unsure, we err on the side of caution
        try
        {
            foreach (var export in asset.Exports)
            {
                if (export is NormalExport normalExport)
                {
                    // Check for cooked-only export class names
                    var className = export.GetExportClassType()?.ToString() ?? "";
                    if (IsCookedOnlyClass(className))
                    {
                        return true;
                    }
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
        // Classes that only exist in cooked assets
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
    public uint PackageFlags { get; set; }
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
}
