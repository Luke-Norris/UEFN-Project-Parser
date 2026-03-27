using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Safety;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Services;

/// <summary>
/// Core service for reading and inspecting .uasset and .umap files.
/// All read operations go through here. Uses SafeFileAccess for copy-on-read
/// to prevent file locking conflicts with UEFN.
/// </summary>
public class AssetService
{
    private readonly WellVersedConfig _config;
    private readonly AssetGuard _guard;
    private readonly SafeFileAccess _fileAccess;
    private readonly ILogger<AssetService> _logger;

    public AssetService(WellVersedConfig config, AssetGuard guard, SafeFileAccess fileAccess, ILogger<AssetService> logger)
    {
        _config = config;
        _guard = guard;
        _fileAccess = fileAccess;
        _logger = logger;
    }

    /// <summary>
    /// Lists all assets in the project's Content directory with optional filtering.
    /// Returns lightweight summaries — use InspectAsset for details.
    /// </summary>
    public List<AssetInfo> ListAssets(string? subfolder = null, string? filterByClass = null, string? searchName = null)
    {
        var contentPath = _config.ContentPath;
        var searchPath = string.IsNullOrEmpty(subfolder)
            ? contentPath
            : Path.Combine(contentPath, subfolder);

        if (!Directory.Exists(searchPath))
        {
            _logger.LogWarning("Search path does not exist: {Path}", searchPath);
            return new List<AssetInfo>();
        }

        var assetFiles = Directory.EnumerateFiles(searchPath, "*.uasset", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(searchPath, "*.umap", SearchOption.AllDirectories));

        var results = new List<AssetInfo>();

        foreach (var filePath in assetFiles)
        {
            try
            {
                var info = GetAssetSummary(filePath);

                // Apply filters
                if (!string.IsNullOrEmpty(filterByClass) &&
                    !info.AssetClass.Contains(filterByClass, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(searchName) &&
                    !info.Name.Contains(searchName, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(info);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping unparseable asset: {Path}", filePath);
                results.Add(new AssetInfo
                {
                    FilePath = filePath,
                    RelativePath = Path.GetRelativePath(contentPath, filePath),
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    AssetClass = "Unknown (parse error)",
                    FileSize = new FileInfo(filePath).Length,
                    LastModified = File.GetLastWriteTime(filePath),
                    Summary = $"Could not parse: {ex.Message}"
                });
            }
        }

        return results.OrderBy(a => a.RelativePath).ToList();
    }

    /// <summary>
    /// Gets a lightweight summary of an asset without full property loading.
    /// Uses copy-on-read for safe access.
    /// </summary>
    public AssetInfo GetAssetSummary(string assetPath)
    {
        var fileInfo = new FileInfo(assetPath);
        var asset = _fileAccess.OpenForRead(assetPath);
        var cookedCheck = _guard.CheckIfCooked(asset);
        var pathCheck = _guard.CheckPath(assetPath);

        var primaryExport = asset.Exports.FirstOrDefault();
        var assetClass = primaryExport?.GetExportClassType()?.ToString() ?? "Unknown";

        return new AssetInfo
        {
            FilePath = assetPath,
            RelativePath = Path.GetRelativePath(_config.ContentPath, assetPath),
            Name = Path.GetFileNameWithoutExtension(assetPath),
            AssetClass = assetClass,
            FileSize = fileInfo.Length,
            IsCooked = cookedCheck.IsCooked,
            IsModifiable = pathCheck.IsModifiable && !cookedCheck.IsCooked,
            ExportCount = asset.Exports.Count,
            ImportCount = asset.Imports.Count,
            LastModified = fileInfo.LastWriteTime,
            EngineVersion = asset.ObjectVersionUE5.ToString(),
            Summary = BuildAssetSummary(asset, assetClass)
        };
    }

    /// <summary>
    /// Gets full details of an asset including all exports and properties.
    /// This is the "drill-down" operation. Uses copy-on-read.
    /// </summary>
    public AssetDetail InspectAsset(string assetPath)
    {
        var asset = _fileAccess.OpenForRead(assetPath);
        var summary = GetAssetSummary(assetPath);

        var detail = new AssetDetail
        {
            FilePath = summary.FilePath,
            RelativePath = summary.RelativePath,
            Name = summary.Name,
            AssetClass = summary.AssetClass,
            FileSize = summary.FileSize,
            IsCooked = summary.IsCooked,
            IsModifiable = summary.IsModifiable,
            ExportCount = summary.ExportCount,
            ImportCount = summary.ImportCount,
            LastModified = summary.LastModified,
            EngineVersion = summary.EngineVersion,
            Summary = summary.Summary,
            PackageFlags = asset.PackageFlags
        };

        // Parse exports
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            var export = asset.Exports[i];
            var exportInfo = new ExportInfo
            {
                Index = i,
                ObjectName = export.ObjectName?.ToString() ?? $"Export_{i}",
                ClassName = export.GetExportClassType()?.ToString() ?? "Unknown",
                SerialSize = export.SerialSize
            };

            if (export is NormalExport normalExport)
            {
                exportInfo.Properties = ExtractProperties(normalExport.Data);
            }

            detail.Exports.Add(exportInfo);
        }

        // Parse imports
        for (int i = 0; i < asset.Imports.Count; i++)
        {
            var import = asset.Imports[i];
            detail.Imports.Add(new ImportInfo
            {
                Index = i,
                ObjectName = import.ObjectName?.ToString() ?? "",
                ClassName = import.ClassName?.ToString() ?? "",
                PackageName = import.OuterIndex.IsImport()
                    ? import.OuterIndex.ToImport(asset).ObjectName?.ToString() ?? ""
                    : ""
            });
        }

        return detail;
    }

    /// <summary>
    /// Searches for assets matching a query across the project.
    /// </summary>
    public List<AssetInfo> SearchAssets(string query)
    {
        return ListAssets()
            .Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.AssetClass.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Opens a UAsset for reading via copy-on-read.
    /// The caller gets a parsed copy that is safe to inspect without affecting the source.
    /// </summary>
    public UAsset OpenAsset(string assetPath)
    {
        return _fileAccess.OpenForRead(assetPath);
    }

    /// <summary>
    /// Opens a UAsset for modification via SafeFileAccess.
    /// Returns the parsed asset and the write path (staging or direct).
    /// </summary>
    public (UAsset Asset, string WritePath) OpenAssetForWrite(string assetPath)
    {
        return _fileAccess.OpenForWrite(assetPath);
    }

    /// <summary>
    /// Extracts properties from an export's property list into our simplified model.
    /// </summary>
    private List<PropertyInfo> ExtractProperties(List<UAssetAPI.PropertyTypes.Objects.PropertyData> properties)
    {
        var result = new List<PropertyInfo>();

        foreach (var prop in properties)
        {
            result.Add(ConvertProperty(prop));
        }

        return result;
    }

    private PropertyInfo ConvertProperty(PropertyData prop)
    {
        var info = new PropertyInfo
        {
            Name = prop.Name?.ToString() ?? "Unknown",
            Type = prop.PropertyType?.ToString() ?? prop.GetType().Name,
            ArrayIndex = prop.ArrayIndex
        };

        info.Value = prop switch
        {
            BoolPropertyData boolProp => boolProp.Value.ToString(),
            IntPropertyData intProp => intProp.Value.ToString(),
            FloatPropertyData floatProp => floatProp.Value.ToString(),
            StrPropertyData strProp => strProp.Value?.ToString() ?? "null",
            NamePropertyData nameProp => nameProp.Value?.ToString() ?? "null",
            TextPropertyData textProp => textProp.Value?.ToString() ?? "null",
            ObjectPropertyData objProp => objProp.Value?.ToString() ?? "null",
            SoftObjectPropertyData softProp => softProp.Value.ToString() ?? "null",
            EnumPropertyData enumProp => enumProp.Value?.ToString() ?? "null",
            BytePropertyData byteProp => byteProp.EnumValue?.ToString() ?? byteProp.Value.ToString(),
            StructPropertyData structProp => FormatStructValue(structProp),
            SetPropertyData setProp => $"[Set: {setProp.Value?.Length ?? 0} elements]",
            ArrayPropertyData arrayProp => $"[Array: {arrayProp.Value?.Length ?? 0} elements]",
            MapPropertyData mapProp => $"[Map: {mapProp.Value?.Count ?? 0} entries]",
            _ => SafeToString(prop)
        };

        return info;
    }

    private string FormatStructValue(StructPropertyData structProp)
    {
        if (structProp.Value == null || structProp.Value.Count == 0)
            return "{}";

        var structType = structProp.StructType?.ToString() ?? "";

        if (structType is "Vector" or "Rotator" && structProp.Value.Count >= 3)
        {
            var values = structProp.Value.Select(p => ConvertProperty(p).Value).ToList();
            return $"({string.Join(", ", values)})";
        }

        var props = structProp.Value.Select(p =>
        {
            var converted = ConvertProperty(p);
            return $"{converted.Name}={converted.Value}";
        });

        return $"{{{string.Join(", ", props)}}}";
    }

    private string BuildAssetSummary(UAsset asset, string assetClass)
    {
        var exportTypes = asset.Exports
            .Select(e => e.GetExportClassType()?.ToString() ?? "Unknown")
            .GroupBy(t => t)
            .Select(g => $"{g.Count()} {g.Key}")
            .ToList();

        return $"{assetClass} with {asset.Exports.Count} exports ({string.Join(", ", exportTypes)})";
    }

    private static string SafeToString(PropertyData prop)
    {
        try { return prop.ToString() ?? prop.GetType().Name; }
        catch { return $"[{prop.GetType().Name}]"; }
    }
}
